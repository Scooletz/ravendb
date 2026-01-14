using System;
using Sparrow.Logging;

namespace Raven.Server.Documents.Replication
{
    /// <summary>
    /// Logger that de-escalates from an initial level (WARN or INFO) to DEBUG after the first log entry.
    /// Useful for reducing log noise from recurring transient errors.
    /// </summary>
    public sealed class DeescalatingWarnToDebugLogger
    {
        private readonly RavenLogger _logger;
        private readonly LogLevel _firstOccurrenceLevel;
        // Tracks whether the first log entry has been written for this logger instance
        private bool _logged;

        /// <summary>
        /// Creates a new de-escalating logger.
        /// </summary>
        /// <param name="logger">The underlying logger to use</param>
        /// <param name="firstOccurrenceLevel">The log level for the first occurrence (WARN or INFO)</param>
        public DeescalatingWarnToDebugLogger(RavenLogger logger, LogLevel firstOccurrenceLevel)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (firstOccurrenceLevel != LogLevel.Warn && firstOccurrenceLevel != LogLevel.Info)
                throw new ArgumentOutOfRangeException(nameof(firstOccurrenceLevel), firstOccurrenceLevel, 
                    $"Only WARN or INFO levels are supported for first occurrence, received: {firstOccurrenceLevel}");
            
            _firstOccurrenceLevel = firstOccurrenceLevel;
        }

        /// <summary>
        /// Returns true if logging is enabled at the current level.
        /// For the first log entry, checks the first occurrence level; for subsequent entries, checks DEBUG level.
        /// </summary>
        public bool IsEnabled => _logged == false 
            ? (_firstOccurrenceLevel == LogLevel.Warn ? _logger.IsWarnEnabled : _logger.IsInfoEnabled)
            : _logger.IsDebugEnabled;

        /// <summary>
        /// Logs a message with an exception. First call logs at the configured level (WARN/INFO), 
        /// subsequent calls log at DEBUG level.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="exception">The exception to log</param>
        public void Log(string message, Exception exception)
        {
            if (_logged == false)
            {
                _logged = true;
                switch (_firstOccurrenceLevel)
                {
                    case LogLevel.Warn:
                        if (_logger.IsWarnEnabled)
                            _logger.Warn(message, exception);
                        break;
                    case LogLevel.Info:
                        if (_logger.IsInfoEnabled)
                            _logger.Info(message, exception);
                        break;
                }
            }
            else
            {
                if (_logger.IsDebugEnabled)
                    _logger.Debug(message, exception);
            }
        }

        public enum LogLevel
        {
            Warn,
            Info
        }
    }
}
