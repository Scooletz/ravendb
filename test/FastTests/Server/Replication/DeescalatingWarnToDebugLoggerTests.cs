using System;
using System.Collections.Generic;
using Raven.Server.Documents.Replication;
using Sparrow.Logging;
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
            var mockLogger = new MockRavenLogger();
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(mockLogger);
            var exception = new Exception("Test exception");
            var message = "Test message";

            // Act
            deescalatingLogger.Log(message, exception);

            // Assert
            Assert.Single(mockLogger.WarnCalls);
            Assert.Empty(mockLogger.DebugCalls);
            Assert.Equal(message, mockLogger.WarnCalls[0].Message);
            Assert.Equal(exception, mockLogger.WarnCalls[0].Exception);
        }

        [Fact]
        public void SecondCall_ShouldLogAtDebugLevel()
        {
            // Arrange
            var mockLogger = new MockRavenLogger();
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(mockLogger);
            var exception1 = new Exception("First exception");
            var exception2 = new Exception("Second exception");
            var message1 = "First message";
            var message2 = "Second message";

            // Act
            deescalatingLogger.Log(message1, exception1);
            deescalatingLogger.Log(message2, exception2);

            // Assert
            Assert.Single(mockLogger.WarnCalls);
            Assert.Single(mockLogger.DebugCalls);
            Assert.Equal(message1, mockLogger.WarnCalls[0].Message);
            Assert.Equal(exception1, mockLogger.WarnCalls[0].Exception);
            Assert.Equal(message2, mockLogger.DebugCalls[0].Message);
            Assert.Equal(exception2, mockLogger.DebugCalls[0].Exception);
        }

        [Fact]
        public void SubsequentCalls_ShouldAllLogAtDebugLevel()
        {
            // Arrange
            var mockLogger = new MockRavenLogger();
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(mockLogger);

            // Act
            deescalatingLogger.Log("Message 1", new Exception("Exception 1"));
            deescalatingLogger.Log("Message 2", new Exception("Exception 2"));
            deescalatingLogger.Log("Message 3", new Exception("Exception 3"));
            deescalatingLogger.Log("Message 4", new Exception("Exception 4"));

            // Assert
            Assert.Single(mockLogger.WarnCalls); // Only first call
            Assert.Equal(3, mockLogger.DebugCalls.Count); // Second, third, and fourth calls
        }

        [Fact]
        public void IsEnabled_BeforeFirstCall_ShouldCheckWarnLevel()
        {
            // Arrange
            var mockLogger = new MockRavenLogger { IsWarnEnabled = true, IsDebugEnabled = false };
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(mockLogger);

            // Act & Assert
            Assert.True(deescalatingLogger.IsEnabled);
        }

        [Fact]
        public void IsEnabled_AfterFirstCall_ShouldCheckDebugLevel()
        {
            // Arrange
            var mockLogger = new MockRavenLogger { IsWarnEnabled = true, IsDebugEnabled = false };
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(mockLogger);

            // Act
            deescalatingLogger.Log("Test", new Exception());

            // Assert - After first call, should check debug level
            Assert.False(deescalatingLogger.IsEnabled);

            // Arrange - Enable debug
            mockLogger.IsDebugEnabled = true;

            // Assert - Should now be enabled
            Assert.True(deescalatingLogger.IsEnabled);
        }

        [Fact]
        public void IsEnabled_WhenWarnDisabled_ShouldReturnFalseBeforeFirstCall()
        {
            // Arrange
            var mockLogger = new MockRavenLogger { IsWarnEnabled = false, IsDebugEnabled = true };
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(mockLogger);

            // Act & Assert
            Assert.False(deescalatingLogger.IsEnabled);
        }

        [Fact]
        public void Log_WithWarnDisabled_ShouldNotLogFirstCall()
        {
            // Arrange
            var mockLogger = new MockRavenLogger { IsWarnEnabled = false };
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(mockLogger);

            // Act
            deescalatingLogger.Log("Message", new Exception());

            // Assert
            Assert.Empty(mockLogger.WarnCalls);
            Assert.Empty(mockLogger.DebugCalls);
        }

        [Fact]
        public void Log_WithDebugDisabled_ShouldNotLogSubsequentCalls()
        {
            // Arrange
            var mockLogger = new MockRavenLogger { IsWarnEnabled = true, IsDebugEnabled = false };
            var deescalatingLogger = new DeescalatingWarnToDebugLogger(mockLogger);

            // Act
            deescalatingLogger.Log("First", new Exception());
            deescalatingLogger.Log("Second", new Exception());

            // Assert
            Assert.Single(mockLogger.WarnCalls);
            Assert.Empty(mockLogger.DebugCalls); // Debug was disabled
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DeescalatingWarnToDebugLogger(null));
        }

        private class MockRavenLogger : IRavenLogger
        {
            public List<(string Message, Exception Exception)> WarnCalls { get; } = new List<(string, Exception)>();
            public List<(string Message, Exception Exception)> DebugCalls { get; } = new List<(string, Exception)>();

            public bool IsWarnEnabled { get; set; } = true;
            public bool IsDebugEnabled { get; set; } = true;

            // IRavenLogger implementation
            public bool IsErrorEnabled => true;
            public bool IsInfoEnabled => true;
            public bool IsFatalEnabled => true;
            public bool IsTraceEnabled => true;

            public void Warn(string message, Exception exception)
            {
                WarnCalls.Add((message, exception));
            }

            public void Debug(string message, Exception exception)
            {
                DebugCalls.Add((message, exception));
            }

            // Not used in tests but required by interface
            public void Error(string message) { }
            public void Error(string message, params object[] args) { }
            public void Error(string message, Exception exception) { }
            public void Error(Exception exception, string message, params object[] args) { }
            public void Error<TArgument1, TArgument2>(string message, TArgument1 argument1, TArgument2 argument2) { }
            public void Error<TArgument1, TArgument2, TArgument3>(string message, TArgument1 argument1, TArgument2 argument2, TArgument3 argument3) { }
            public void Info(string message) { }
            public void Info(string message, params object[] args) { }
            public void Info(string message, Exception exception) { }
            public void Info(Exception exception, string message, params object[] args) { }
            public void Info<TArgument1, TArgument2>(string message, TArgument1 argument1, TArgument2 argument2) { }
            public void Info<TArgument1, TArgument2, TArgument3>(string message, TArgument1 argument1, TArgument2 argument2, TArgument3 argument3) { }
            public void Debug(string message) { }
            public void Debug(string message, params object[] args) { }
            public void Debug(Exception exception, string message, params object[] args) { }
            public void Debug<TArgument1, TArgument2>(string message, TArgument1 argument1, TArgument2 argument2) { }
            public void Debug<TArgument1, TArgument2, TArgument3>(string message, TArgument1 argument1, TArgument2 argument2, TArgument3 argument3) { }
            public void Warn(string message) { }
            public void Warn(string message, params object[] args) { }
            public void Warn(Exception exception, string message, params object[] args) { }
            public void Warn<TArgument1, TArgument2>(string message, TArgument1 argument1, TArgument2 argument2) { }
            public void Warn<TArgument1, TArgument2, TArgument3>(string message, TArgument1 argument1, TArgument2 argument2, TArgument3 argument3) { }
            public void Fatal(string message) { }
            public void Fatal(string message, params object[] args) { }
            public void Fatal(string message, Exception exception) { }
            public void Fatal(Exception exception, string message, params object[] args) { }
            public void Fatal<TArgument1, TArgument2>(string message, TArgument1 argument1, TArgument2 argument2) { }
            public void Fatal<TArgument1, TArgument2, TArgument3>(string message, TArgument1 argument1, TArgument2 argument2, TArgument3 argument3) { }
            public void Trace(string message) { }
            public void Trace(string message, params object[] args) { }
            public void Trace(string message, Exception exception) { }
            public void Trace(Exception exception, string message, params object[] args) { }
            public void Trace<TArgument1, TArgument2>(string message, TArgument1 argument1, TArgument2 argument2) { }
            public void Trace<TArgument1, TArgument2, TArgument3>(string message, TArgument1 argument1, TArgument2 argument2, TArgument3 argument3) { }
            public bool IsEnabled(Sparrow.Logging.LogLevel logLevel) => true;
            public void Log(Sparrow.Logging.LogLevel logLevel, string message) { }
            public void Log(Sparrow.Logging.LogLevel logLevel, string message, Exception exception) { }
            public IRavenLogger WithProperty(string propertyKey, object propertyValue) => this;
        }
    }
}
