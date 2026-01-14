using System;
using System.IO;
using Sparrow.Logging;

namespace Raven.Server.Documents.Replication
{
    /// <summary>
    /// Helper class to handle EndOfStreamException logging with state tracking.
    /// Logs first occurrence at a higher level (WARN/INFO), subsequent at DEBUG.
    /// </summary>
    public sealed class EndOfStreamExceptionLogger
    {
        // Tracks whether the first EndOfStreamException has been logged for this connection instance
        private bool _logged;

        /// <summary>
        /// Logs the EndOfStreamException with appropriate level based on whether it's the first occurrence.
        /// </summary>
        /// <param name="exception">The EndOfStreamException to log</param>
        /// <param name="logger">The logger to use</param>
        /// <param name="message">The message to log</param>
        /// <param name="firstOccurrenceLevel">The log level for the first occurrence (WARN or INFO)</param>
        public void Log(EndOfStreamException exception, RavenLogger logger, string message, LogLevel firstOccurrenceLevel)
        {
            if (_logged == false)
            {
                _logged = true;
                switch (firstOccurrenceLevel)
                {
                    case LogLevel.Warn:
                        if (logger.IsWarnEnabled)
                            logger.Warn(message, exception);
                        break;
                    case LogLevel.Info:
                        if (logger.IsInfoEnabled)
                            logger.Info(message, exception);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(firstOccurrenceLevel), firstOccurrenceLevel, 
                            $"Only WARN or INFO levels are supported for first occurrence, received: {firstOccurrenceLevel}");
                }
            }
            else
            {
                if (logger.IsDebugEnabled)
                    logger.Debug(message, exception);
            }
        }

        public enum LogLevel
        {
            Warn,
            Info
        }
    }
}
