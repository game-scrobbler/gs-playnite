using System;
using System.Threading.Tasks;
using Xunit;

namespace GsPlugin.Tests {
    public class GsCircuitBreakerTests {
        [Fact]
        public void StartsInClosedState() {
            var breaker = new GsCircuitBreaker();
            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }

        [Fact]
        public async Task SuccessfulCallKeepsCircuitClosed() {
            var breaker = new GsCircuitBreaker(failureThreshold: 3);
            var result = await breaker.ExecuteAsync(async () => {
                await Task.CompletedTask;
                return 42;
            });
            Assert.Equal(42, result);
            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }

        [Fact]
        public async Task OpensAfterFailureThreshold() {
            var breaker = new GsCircuitBreaker(failureThreshold: 2, retryDelay: TimeSpan.FromMilliseconds(1));

            // Trigger failures up to the threshold (maxRetries=0 means single attempt per call)
            for (int i = 0; i < 2; i++) {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    breaker.ExecuteAsync<int>(async () => {
                        await Task.CompletedTask;
                        throw new InvalidOperationException("test failure");
                    }, maxRetries: 0));
            }

            Assert.Equal(GsCircuitBreaker.CircuitState.Open, breaker.State);
        }

        [Fact]
        public async Task OpenCircuitReturnsDefaultWithoutExecuting() {
            var breaker = new GsCircuitBreaker(failureThreshold: 1, retryDelay: TimeSpan.FromMilliseconds(1));

            // Open the circuit
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(async () => {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("test failure");
                }, maxRetries: 0));

            Assert.Equal(GsCircuitBreaker.CircuitState.Open, breaker.State);

            // Now call should return default without executing
            bool executed = false;
            var result = await breaker.ExecuteAsync(async () => {
                executed = true;
                await Task.CompletedTask;
                return 99;
            }, maxRetries: 0);

            Assert.False(executed);
            Assert.Equal(0, result); // default(int)
        }

        [Fact]
        public async Task TransitionsToHalfOpenAfterTimeout() {
            var timeout = TimeSpan.FromMilliseconds(50);
            var breaker = new GsCircuitBreaker(failureThreshold: 1, timeout: timeout, retryDelay: TimeSpan.FromMilliseconds(1));

            // Open the circuit
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(async () => {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("test failure");
                }, maxRetries: 0));

            Assert.Equal(GsCircuitBreaker.CircuitState.Open, breaker.State);

            // Wait for timeout to elapse
            await Task.Delay(100);

            // Next successful call should transition to HalfOpen then Closed
            var result = await breaker.ExecuteAsync(async () => {
                await Task.CompletedTask;
                return 42;
            }, maxRetries: 0);

            Assert.Equal(42, result);
            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }

        [Fact]
        public async Task HalfOpenFailureReopensCircuit() {
            var timeout = TimeSpan.FromMilliseconds(50);
            var breaker = new GsCircuitBreaker(failureThreshold: 1, timeout: timeout, retryDelay: TimeSpan.FromMilliseconds(1));

            // Open the circuit
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(async () => {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("test failure");
                }, maxRetries: 0));

            // Wait for timeout
            await Task.Delay(100);

            // Fail in HalfOpen state - should reopen
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(async () => {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("test failure");
                }, maxRetries: 0));

            Assert.Equal(GsCircuitBreaker.CircuitState.Open, breaker.State);
        }

        [Fact]
        public void ResetClosesCircuit() {
            var breaker = new GsCircuitBreaker(failureThreshold: 1);

            // We can't easily open the circuit without async, but we can test Reset from Closed
            breaker.Reset();
            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }

        [Fact]
        public async Task RetriesOnFailureBeforeGivingUp() {
            var breaker = new GsCircuitBreaker(failureThreshold: 10);
            int attempts = 0;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(async () => {
                    attempts++;
                    await Task.CompletedTask;
                    throw new InvalidOperationException("test failure");
                }, maxRetries: 2, baseDelay: TimeSpan.FromMilliseconds(1)));

            // Should have attempted 3 times (initial + 2 retries)
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task SucceedsOnRetry() {
            var breaker = new GsCircuitBreaker(failureThreshold: 10);
            int attempts = 0;

            var result = await breaker.ExecuteAsync(async () => {
                attempts++;
                await Task.CompletedTask;
                if (attempts < 3) throw new InvalidOperationException("transient failure");
                return 42;
            }, maxRetries: 3, baseDelay: TimeSpan.FromMilliseconds(1));

            Assert.Equal(42, result);
            Assert.Equal(3, attempts);
            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }

        [Fact]
        public async Task ZeroRetries_SingleAttemptOnly() {
            var breaker = new GsCircuitBreaker(failureThreshold: 10);
            int attempts = 0;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(async () => {
                    attempts++;
                    await Task.CompletedTask;
                    throw new InvalidOperationException("fail");
                }, maxRetries: 0));

            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task FailuresBelowThreshold_StaysClosed() {
            var breaker = new GsCircuitBreaker(failureThreshold: 5);

            // 4 failures - below threshold of 5
            for (int i = 0; i < 4; i++) {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    breaker.ExecuteAsync<int>(async () => {
                        await Task.CompletedTask;
                        throw new InvalidOperationException("fail");
                    }, maxRetries: 0));
            }

            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }

        [Fact]
        public async Task SuccessResetsFailureCount() {
            var breaker = new GsCircuitBreaker(failureThreshold: 3);

            // 2 failures
            for (int i = 0; i < 2; i++) {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    breaker.ExecuteAsync<int>(async () => {
                        await Task.CompletedTask;
                        throw new InvalidOperationException("fail");
                    }, maxRetries: 0));
            }

            // 1 success should reset the counter
            await breaker.ExecuteAsync(async () => {
                await Task.CompletedTask;
                return 1;
            }, maxRetries: 0);

            // 2 more failures should not open (counter was reset)
            for (int i = 0; i < 2; i++) {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    breaker.ExecuteAsync<int>(async () => {
                        await Task.CompletedTask;
                        throw new InvalidOperationException("fail");
                    }, maxRetries: 0));
            }

            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }

        [Fact]
        public async Task ResetAfterOpen_AllowsExecution() {
            var breaker = new GsCircuitBreaker(failureThreshold: 1, retryDelay: TimeSpan.FromMilliseconds(1));

            // Open the circuit
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(async () => {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("fail");
                }, maxRetries: 0));

            Assert.Equal(GsCircuitBreaker.CircuitState.Open, breaker.State);

            // Manual reset
            breaker.Reset();
            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);

            // Should be able to execute again
            var result = await breaker.ExecuteAsync(async () => {
                await Task.CompletedTask;
                return 99;
            }, maxRetries: 0);

            Assert.Equal(99, result);
        }

        [Fact]
        public async Task VoidOverload_ExecutesSuccessfully() {
            var breaker = new GsCircuitBreaker();
            bool executed = false;

            await breaker.ExecuteAsync(async () => {
                executed = true;
                await Task.CompletedTask;
            }, maxRetries: 0);

            Assert.True(executed);
            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }

        [Fact]
        public async Task VoidOverload_ThrowsOnFailure() {
            var breaker = new GsCircuitBreaker(failureThreshold: 10);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync(async () => {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("void fail");
                }, maxRetries: 0));
        }

        [Fact]
        public async Task OpenCircuitReturnsDefault_ForStringType() {
            var breaker = new GsCircuitBreaker(failureThreshold: 1, retryDelay: TimeSpan.FromMilliseconds(1));

            // Open the circuit
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<string>(async () => {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("fail");
                }, maxRetries: 0));

            // Should return null (default for string)
            var result = await breaker.ExecuteAsync(async () => {
                await Task.CompletedTask;
                return "should not execute";
            }, maxRetries: 0);

            Assert.Null(result);
        }

        [Fact]
        public void DefaultConstructor_UsesReasonableDefaults() {
            var breaker = new GsCircuitBreaker();
            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }

        [Fact]
        public void CustomThreshold_IsRespected() {
            // Just verifying construction with custom params doesn't throw
            var breaker = new GsCircuitBreaker(
                failureThreshold: 10,
                timeout: TimeSpan.FromMinutes(5),
                retryDelay: TimeSpan.FromSeconds(10));
            Assert.Equal(GsCircuitBreaker.CircuitState.Closed, breaker.State);
        }
    }
}
