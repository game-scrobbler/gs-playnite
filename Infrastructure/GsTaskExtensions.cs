using System;
using System.Threading.Tasks;
using Playnite.SDK;

namespace GsPlugin.Infrastructure {
    /// <summary>
    /// Helpers for fire-and-forget tasks.
    /// </summary>
    internal static class GsTaskExtensions {
        private static readonly ILogger _logger = LogManager.GetLogger();

        /// <summary>
        /// Attaches a fault-only continuation that logs the task's base exception, so a
        /// fire-and-forget task can never surface later as an unobserved task exception on
        /// the finalizer thread. Intended for <c>_ = something().LogFaults("...")</c>.
        ///
        /// The original task is returned, not the continuation, so this call never changes
        /// await semantics: awaiting the result still throws when the task faults, exactly
        /// as awaiting the bare task would. Callers that must not throw need their own
        /// try/catch around the await, and the fault is logged either way.
        /// </summary>
        /// <param name="task">The task to observe.</param>
        /// <param name="label">Message logged alongside the exception.</param>
        /// <param name="asError">Log at Error level instead of the default Warn level.</param>
        /// <returns>The same task instance that was passed in.</returns>
        internal static Task LogFaults(this Task task, string label, bool asError = false) {
            if (task == null) {
                return null;
            }

            task.ContinueWith(t => {
                var ex = t.Exception?.GetBaseException();
                if (asError) {
                    _logger.Error(ex, label);
                }
                else {
                    _logger.Warn(ex, label);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);

            return task;
        }
    }
}
