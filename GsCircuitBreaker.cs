using System;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace GsPlugin {
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
        /// Executes a function with circuit breaker protection and retry logic.
        /// </summary>
        /// <typeparam name="T">Return type of the function</typeparam>
        /// <param name="func">Function to execute</param>
        /// <param name="maxRetries">Maximum number of retries (default: 3)</param>
        /// <param name="baseDelay">Base delay for exponential backoff (default: 1 second)</param>
        /// <returns>Result of the function or default(T) if all attempts fail</returns>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> func, int maxRetries = 3, TimeSpan? baseDelay = null) {
            var delay = baseDelay ?? TimeSpan.FromSeconds(1);

            for (int attempt = 0; attempt <= maxRetries; attempt++) {
                try {
                    // Check circuit breaker state
                    if (!CanExecute()) {
                        _logger.Warn($"Circuit breaker is {State}, skipping execution attempt {attempt + 1}");
                        return default(T);
                    }

                    var result = await func();
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

                    // Exponential backoff: delay = baseDelay * 2^attempt with some jitter
                    int jitter;
                    lock (_lock) {
                        jitter = _random.Next(0, 1000); // Add jitter up to 1 second
                    }
                    var waitTime = TimeSpan.FromMilliseconds(
                        delay.TotalMilliseconds * Math.Pow(2, attempt) + jitter);

                    _logger.Info($"Waiting {waitTime.TotalSeconds:F1} seconds before retry attempt {attempt + 2}");
                    await Task.Delay(waitTime);
                }
            }

            return default(T);
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
