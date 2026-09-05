using System;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace GsPlugin.Api {
    /// <summary>
    /// Circuit breaker pattern implementation for API calls with exponential backoff retry logic.
    /// Helps prevent cascading failures and provides resilience against temporary service outages.
    /// </summary>
    public class GsCircuitBreaker {
        private static readonly ILogger _logger = LogManager.GetLogger();

        // Reuse a single Random instance for jitter calculation to improve performance
        // and ensure better randomness distribution
        private static readonly Random _random = new Random();

        public enum CircuitState {
            Closed,     // Normal operation
            Open,       // Circuit breaker is open, failing fast
            HalfOpen    // Testing if service has recovered
        }

        /// <summary>
        /// Raised when the circuit breaker transitions from HalfOpen to Closed (i.e., the API has recovered).
        /// Subscribers should use this to flush any queued operations.
        /// </summary>
        public event Action OnCircuitClosed;

        private readonly int _failureThreshold;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _retryDelay;
        private int _failureCount;
        private DateTime _lastFailureTime;
        private CircuitState _state;
        private readonly object _lock = new object();

        public GsCircuitBreaker(int failureThreshold = 5, TimeSpan? timeout = null, TimeSpan? retryDelay = null) {
            _failureThreshold = failureThreshold;
            _timeout = timeout ?? TimeSpan.FromMinutes(1);
            _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
            _state = CircuitState.Closed;
        }

        public CircuitState State {
            get {
                lock (_lock) {
                    return _state;
                }
            }
        }

        /// <summary>
        /// True while the open-circuit cooldown still prevents a request. Once the cooldown
        /// expires, a caller must be allowed into ExecuteAsync to probe for recovery; merely
        /// reading State never transitions the circuit out of Open.
        /// </summary>
        internal bool IsBlocking {
            get {
                lock (_lock) {
                    return _state == CircuitState.Open && DateTime.UtcNow - _lastFailureTime < _timeout;
                }
            }
        }

        /// <summary>
        /// Executes a function with circuit breaker protection and retry logic.
        /// </summary>
        /// <typeparam name="T">Return type of the function</typeparam>
        /// <param name="func">Function to execute</param>
        /// <param name="maxRetries">Maximum number of retries (default: 3)</param>
        /// <param name="baseDelay">Base delay for exponential backoff (default: 1 second)</param>
        /// <param name="isFailure">Optional predicate to treat a non-throwing result as a failure
        /// (e.g. null return from an HTTP call that swallows errors). When supplied, matching
        /// results count toward the failure threshold and trigger retries.</param>
        /// <returns>Result of the function or default(T) if all attempts fail</returns>
        /// <param name="isPermanent">
        /// Consulted only when <paramref name="isFailure"/> reports a failure.
        /// Returns true when the last attempt failed for a reason retrying cannot
        /// change — a 4xx the server will answer identically next time. Such a
        /// result is returned immediately and, deliberately, does NOT count toward
        /// the failure threshold: a rejected request says nothing about whether the
        /// service is healthy, and letting it trip the breaker takes unrelated
        /// calls (scrobbles) down with it. Without this, a permanently-rejected
        /// library commit burned every retry and pushed the circuit open on repeat.
        /// </param>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> func, int maxRetries = 3,
            TimeSpan? baseDelay = null, Func<T, bool> isFailure = null,
            Func<bool> isPermanent = null) {
            var delay = baseDelay ?? TimeSpan.FromSeconds(1);

            for (int attempt = 0; attempt <= maxRetries; attempt++) {
                try {
                    // Check circuit breaker state
                    if (!CanExecute()) {
                        _logger.Warn($"Circuit breaker is {State}, skipping execution attempt {attempt + 1}");
                        return default(T);
                    }

                    var result = await func();

                    // If the caller supplied a failure predicate and the result indicates failure,
                    // treat it the same as a thrown exception for circuit breaker purposes.
                    if (isFailure != null && isFailure(result)) {
                        if (isPermanent != null && isPermanent()) {
                            _logger.Warn(
                                $"API call attempt {attempt + 1} was rejected permanently; not retrying");
                            // The service answered, so the circuit is healthy even though
                            // this request was refused. Resolve a HalfOpen probe rather
                            // than returning with the state machine still mid-probe.
                            OnServiceResponsive();
                            return result;
                        }

                        _logger.Warn($"API call attempt {attempt + 1} returned a failure result");
                        OnFailure();

                        if (attempt == maxRetries) {
                            _logger.Warn($"All {maxRetries + 1} attempts returned failure results, giving up");
                            return result;
                        }

                        await WaitWithBackoffAsync(delay, attempt);
                        continue;
                    }

                    OnSuccess();
                    return result;
                }
                catch (Exception ex) {
                    _logger.Warn(ex, $"API call attempt {attempt + 1} failed");
                    OnFailure();

                    // Don't retry if it's the last attempt
                    if (attempt == maxRetries) {
                        _logger.Error(ex, $"All {maxRetries + 1} attempts failed, giving up");
                        throw;
                    }

                    await WaitWithBackoffAsync(delay, attempt);
                }
            }

            return default(T);
        }

        /// <summary>
        /// Exponential backoff with jitter: delay = baseDelay * 2^attempt + random(0-1000ms).
        /// </summary>
        private async Task WaitWithBackoffAsync(TimeSpan baseDelay, int attempt) {
            int jitter;
            lock (_lock) {
                jitter = _random.Next(0, 1000);
            }
            var waitTime = TimeSpan.FromMilliseconds(
                baseDelay.TotalMilliseconds * Math.Pow(2, attempt) + jitter);
            _logger.Info($"Waiting {waitTime.TotalSeconds:F1} seconds before retry attempt {attempt + 2}");
            await Task.Delay(waitTime);
        }

        /// <summary>
        /// Executes a function that doesn't return a value with circuit breaker protection.
        /// </summary>
        public async Task ExecuteAsync(Func<Task> func, int maxRetries = 3, TimeSpan? baseDelay = null) {
            await ExecuteAsync(async () => {
                await func();
                return true; // Convert to a function that returns something
            }, maxRetries, baseDelay);
        }

        private bool CanExecute() {
            lock (_lock) {
                switch (_state) {
                    case CircuitState.Closed:
                        return true;
                    case CircuitState.Open:
                        if (DateTime.UtcNow - _lastFailureTime >= _timeout) {
                            _state = CircuitState.HalfOpen;
                            _logger.Info("Circuit breaker moving from Open to HalfOpen state");
                            return true;
                        }
                        return false;
                    case CircuitState.HalfOpen:
                        return true;
                    default:
                        return false;
                }
            }
        }

        private void OnSuccess() {
            OnServiceResponsive();
        }

        /// <summary>
        /// Records that the service answered. Resets the failure count and resolves a
        /// HalfOpen probe to Closed, firing <see cref="OnCircuitClosed"/> so recovery
        /// work (the pending-scrobble flush) runs.
        ///
        /// Called for a successful call and for a permanently rejected one. A rejection
        /// is still proof the service is reachable and deciding, so it must not leave a
        /// HalfOpen breaker unresolved: that would strand the failure count, skip the
        /// recovery event, and let the next unrelated failure re-open the circuit
        /// straight from HalfOpen.
        /// </summary>
        private void OnServiceResponsive() {
            bool recovered = false;
            lock (_lock) {
                _failureCount = 0;
                if (_state == CircuitState.HalfOpen) {
                    _state = CircuitState.Closed;
                    _logger.Info("Circuit breaker moving from HalfOpen to Closed state");
                    recovered = true;
                }
            }
            if (recovered) {
                OnCircuitClosed?.Invoke();
            }
        }

        private void OnFailure() {
            lock (_lock) {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;

                if (_state == CircuitState.HalfOpen) {
                    _state = CircuitState.Open;
                    _logger.Warn("Circuit breaker moving from HalfOpen to Open state");
                }
                else if (_failureCount >= _failureThreshold) {
                    _state = CircuitState.Open;
                    _logger.Warn($"Circuit breaker opening due to {_failureCount} consecutive failures");
                }
            }
        }

        /// <summary>
        /// Manually resets the circuit breaker to closed state.
        /// </summary>
        public void Reset() {
            lock (_lock) {
                _failureCount = 0;
                _state = CircuitState.Closed;
                _logger.Info("Circuit breaker manually reset to Closed state");
            }
        }
    }
}
