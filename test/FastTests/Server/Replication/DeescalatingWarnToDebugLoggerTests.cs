using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Raven.Server.Documents.Replication;
using Sparrow.Server.Logging;
using Xunit;
using Xunit.Abstractions;

namespace FastTests.Server.Replication
{
    public class DeescalatingWarnToDebugLoggerTests : NoDisposalNeeded
    {
        public DeescalatingWarnToDebugLoggerTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void FirstCall_ShouldLogAtWarnLevel()
        {
            // Arrange
            using var logSetup = new NLogTestSetup();
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(logSetup.RavenLogger);
            var exception = new Exception("Test exception");
            var message = "Test message";

            // Act
            deescalatingLogger.Log(message, exception);

            // Assert
            var logs = logSetup.GetLogs();
            Assert.Single(logs);
            Assert.Equal(NLog.LogLevel.Warn, logs[0].Level);
            Assert.Contains(message, logs[0].FormattedMessage);
        }

        [Fact]
        public void SecondCall_ShouldLogAtDebugLevel()
        {
            // Arrange
            using var logSetup = new NLogTestSetup();
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(logSetup.RavenLogger);
            var exception1 = new Exception("First exception");
            var exception2 = new Exception("Second exception");
            var message1 = "First message";
            var message2 = "Second message";

            // Act
            deescalatingLogger.Log(message1, exception1);
            deescalatingLogger.Log(message2, exception2);

            // Assert
            var logs = logSetup.GetLogs();
            Assert.Equal(2, logs.Count);
            Assert.Equal(NLog.LogLevel.Warn, logs[0].Level);
            Assert.Contains(message1, logs[0].FormattedMessage);
            Assert.Equal(NLog.LogLevel.Debug, logs[1].Level);
            Assert.Contains(message2, logs[1].FormattedMessage);
        }

        [Fact]
        public void SubsequentCalls_ShouldAllLogAtDebugLevel()
        {
            // Arrange
            using var logSetup = new NLogTestSetup();
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(logSetup.RavenLogger);

            // Act
            deescalatingLogger.Log("Message 1", new Exception("Exception 1"));
            deescalatingLogger.Log("Message 2", new Exception("Exception 2"));
            deescalatingLogger.Log("Message 3", new Exception("Exception 3"));
            deescalatingLogger.Log("Message 4", new Exception("Exception 4"));

            // Assert
            var logs = logSetup.GetLogs();
            Assert.Equal(4, logs.Count);
            Assert.Equal(NLog.LogLevel.Warn, logs[0].Level); // Only first call
            Assert.Equal(NLog.LogLevel.Debug, logs[1].Level);
            Assert.Equal(NLog.LogLevel.Debug, logs[2].Level);
            Assert.Equal(NLog.LogLevel.Debug, logs[3].Level);
        }

        [Fact]
        public void IsEnabled_BeforeFirstCall_ShouldCheckWarnLevel()
        {
            // Arrange
            using var logSetup = new NLogTestSetup(minLevel: NLog.LogLevel.Warn);
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(logSetup.RavenLogger);

            // Act & Assert
            Assert.True(deescalatingLogger.IsEnabled);
        }

        [Fact]
        public void IsEnabled_AfterFirstCall_ShouldCheckDebugLevel()
        {
            // Arrange
            using var logSetup = new NLogTestSetup(minLevel: NLog.LogLevel.Warn); // Warn enabled, Debug disabled
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(logSetup.RavenLogger);

            // Act
            deescalatingLogger.Log("Test", new Exception());

            // Assert - After first call, should check debug level (which is disabled)
            Assert.False(deescalatingLogger.IsEnabled);
        }

        [Fact]
        public void IsEnabled_WhenWarnDisabled_ShouldReturnFalseBeforeFirstCall()
        {
            // Arrange
            using var logSetup = new NLogTestSetup(minLevel: NLog.LogLevel.Error); // Warn disabled
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(logSetup.RavenLogger);

            // Act & Assert
            Assert.False(deescalatingLogger.IsEnabled);
        }

        [Fact]
        public void Log_WithWarnDisabled_ShouldNotLogFirstCall()
        {
            // Arrange
            using var logSetup = new NLogTestSetup(minLevel: NLog.LogLevel.Error); // Warn disabled
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(logSetup.RavenLogger);

            // Act
            deescalatingLogger.Log("Message", new Exception());

            // Assert
            var logs = logSetup.GetLogs();
            Assert.Empty(logs);
        }

        [Fact]
        public void Log_WithDebugDisabled_ShouldNotLogSubsequentCalls()
        {
            // Arrange
            using var logSetup = new NLogTestSetup(minLevel: NLog.LogLevel.Warn); // Warn enabled, Debug disabled
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(logSetup.RavenLogger);

            // Act
            deescalatingLogger.Log("First", new Exception());
            deescalatingLogger.Log("Second", new Exception());

            // Assert
            var logs = logSetup.GetLogs();
            Assert.Single(logs); // Only first call logged
            Assert.Equal(NLog.LogLevel.Warn, logs[0].Level);
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DeescalatingWarnToDebugLogger(null));
        }

        private class NLogTestSetup : IDisposable
        {
            private readonly LoggingConfiguration _previousConfiguration;
            private readonly TestTarget _testTarget;

            public RavenLogger RavenLogger { get; }

            public NLogTestSetup(NLog.LogLevel minLevel = null)
            {
                minLevel ??= NLog.LogLevel.Debug;

                // Store previous configuration to restore later
                _previousConfiguration = LogManager.Configuration;

                // Create new configuration with test target
                var configuration = new LoggingConfiguration();
                _testTarget = new TestTarget { Name = "test" };
                configuration.AddTarget(_testTarget);

                // Add logging rule
                configuration.LoggingRules.Add(new LoggingRule("*", minLevel, _testTarget));

                LogManager.Configuration = configuration;
                var nlogLogger = LogManager.GetLogger("test");
                RavenLogger = new RavenLogger(nlogLogger);
            }

            public List<LogEventInfo> GetLogs()
            {
                LogManager.Flush();
                return _testTarget.LogEvents.ToList();
            }

            public void Dispose()
            {
                LogManager.Flush();
                // Restore previous configuration
                LogManager.Configuration = _previousConfiguration;
            }
        }

        [Target("TestTarget")]
        private class TestTarget : TargetWithLayout
        {
            public List<LogEventInfo> LogEvents { get; } = new List<LogEventInfo>();

            protected override void Write(LogEventInfo logEvent)
            {
                LogEvents.Add(logEvent);
            }
        }
    }
}
