using System;

namespace K2SmartObjectGenerator
{
    /// <summary>
    /// Log levels for K2 generation
    /// </summary>
    public enum K2LogLevel
    {
        /// <summary>
        /// Only log errors
        /// </summary>
        Error = 0,

        /// <summary>
        /// Log errors and warnings
        /// </summary>
        Warning = 1,

        /// <summary>
        /// Log errors, warnings, and major steps (default)
        /// </summary>
        Info = 2,

        /// <summary>
        /// Log detailed information about each operation
        /// </summary>
        Verbose = 3,

        /// <summary>
        /// Log everything including internal state and diagnostic info
        /// </summary>
        Debug = 4
    }

    /// <summary>
    /// Static configuration for K2 generation logging
    /// </summary>
    public static class K2LoggingConfiguration
    {
        private static K2LogLevel _currentLogLevel = K2LogLevel.Info;

        /// <summary>
        /// Current log level for K2 generation
        /// </summary>
        public static K2LogLevel CurrentLogLevel
        {
            get => _currentLogLevel;
            set => _currentLogLevel = value;
        }

        /// <summary>
        /// Check if a message at the given level should be logged
        /// </summary>
        public static bool ShouldLog(K2LogLevel level)
        {
            return level <= _currentLogLevel;
        }

        /// <summary>
        /// Enable verbose logging (detailed operation info)
        /// </summary>
        public static void EnableVerbose()
        {
            _currentLogLevel = K2LogLevel.Verbose;
        }

        /// <summary>
        /// Enable debug logging (everything including diagnostics)
        /// </summary>
        public static void EnableDebug()
        {
            _currentLogLevel = K2LogLevel.Debug;
        }

        /// <summary>
        /// Set to normal logging (major steps only)
        /// </summary>
        public static void SetNormal()
        {
            _currentLogLevel = K2LogLevel.Info;
        }

        /// <summary>
        /// Set to minimal logging (errors and warnings only)
        /// </summary>
        public static void SetMinimal()
        {
            _currentLogLevel = K2LogLevel.Warning;
        }
    }

    /// <summary>
    /// Logger helper for K2 generation
    /// </summary>
    public class K2Logger
    {
        private readonly Action<string> _logAction;
        private readonly string _prefix;

        public K2Logger(Action<string> logAction, string prefix = "")
        {
            _logAction = logAction ?? Console.WriteLine;
            _prefix = string.IsNullOrEmpty(prefix) ? "" : $"[{prefix}] ";
        }

        public void Log(K2LogLevel level, string message)
        {
            if (K2LoggingConfiguration.ShouldLog(level))
            {
                var levelPrefix = GetLevelPrefix(level);
                _logAction($"{levelPrefix}{_prefix}{message}");
            }
        }

        public void Error(string message) => Log(K2LogLevel.Error, message);
        public void Warning(string message) => Log(K2LogLevel.Warning, message);
        public void Info(string message) => Log(K2LogLevel.Info, message);
        public void Verbose(string message) => Log(K2LogLevel.Verbose, message);
        public void Debug(string message) => Log(K2LogLevel.Debug, message);

        public void LogSection(string title)
        {
            if (K2LoggingConfiguration.ShouldLog(K2LogLevel.Info))
            {
                var separator = new string('=', 60);
                _logAction($"\n{separator}");
                _logAction($"=== {title}");
                _logAction($"{separator}");
            }
        }

        public void LogSubSection(string title)
        {
            if (K2LoggingConfiguration.ShouldLog(K2LogLevel.Verbose))
            {
                var separator = new string('-', 50);
                _logAction($"\n{separator}");
                _logAction($"--- {title}");
                _logAction($"{separator}");
            }
        }

        private string GetLevelPrefix(K2LogLevel level)
        {
            return level switch
            {
                K2LogLevel.Error => "[ERROR] ",
                K2LogLevel.Warning => "[WARN]  ",
                K2LogLevel.Info => "[INFO]  ",
                K2LogLevel.Verbose => "[VERB]  ",
                K2LogLevel.Debug => "[DEBUG] ",
                _ => ""
            };
        }
    }
}
