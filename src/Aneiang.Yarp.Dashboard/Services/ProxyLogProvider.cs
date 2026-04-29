using Microsoft.Extensions.Logging;
using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Services
{
    /// <summary>
    /// ILoggerProvider that captures YARP logs from Microsoft.Extensions.Logging pipeline.
    /// Works with all logging frameworks that bridge to ILogger (including Serilog via Serilog.Extensions.Logging).
    /// 捕获 YARP 日志的 ILoggerProvider，兼容所有桥接到 ILogger 的日志框架（包括 Serilog）.
    /// </summary>
    public sealed class ProxyLogProvider : ILoggerProvider
    {
        private const string CategoryPrefix = "Yarp.ReverseProxy";
        private static readonly string[] LevelCache = { "", "", "", "Error", "Critical", "", "", "Warning", "", "", "Information", "", "", "", "", "", "Debug", "Trace" };

        private readonly ProxyLogStore _store;

        /// <summary>
        /// Creates a new provider linked to the specified store.
        /// </summary>
        public ProxyLogProvider(ProxyLogStore store)
        {
            _store = store;
        }

        /// <summary>
        /// Creates a logger for the given category. Returns no-op logger for non-YARP categories.
        /// </summary>
        public ILogger CreateLogger(string categoryName)
        {
            if (!categoryName.StartsWith(CategoryPrefix, StringComparison.Ordinal))
                return NullLogger.Instance;

            return new ProxyLogger(_store, categoryName);
        }

        /// <summary>
        /// Releases resources.
        /// </summary>
        public void Dispose() { }

        /// <summary>
        /// Internal logger that writes to the shared ring buffer.
        /// </summary>
        private sealed class ProxyLogger : ILogger
        {
            private readonly ProxyLogStore _store;
            private readonly string _category;

            public ProxyLogger(ProxyLogStore store, string category)
            {
                _store = store;
                _category = category;
            }

            #pragma warning disable CS8633, CS8766
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            #pragma warning restore CS8633, CS8766

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel < LogLevel.Information) return;

                var message = formatter(state, exception);
                if (string.IsNullOrEmpty(message)) return;

                _store.Add(new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Level = GetLevelString(logLevel),
                    Category = _category,
                    Message = message,
                    Exception = exception?.ToString()
                });
            }
        }

        /// <summary>
        /// Fast level-to-string lookup using LogLevel enum integer value as index.
        /// </summary>
        private static string GetLevelString(LogLevel level)
        {
            int idx = (int)level;
            return (uint)idx < (uint)LevelCache.Length ? LevelCache[idx] : level.ToString();
        }

        /// <summary>
        /// No-op logger for categories we do not care about.
        /// </summary>
        private sealed class NullLogger : ILogger
        {
            public static readonly NullLogger Instance = new();

            #pragma warning disable CS8633, CS8766
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            #pragma warning restore CS8633, CS8766
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }
}
