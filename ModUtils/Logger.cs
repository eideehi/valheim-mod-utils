using System;
using System.Diagnostics.CodeAnalysis;
using BepInEx.Logging;

namespace ModUtils
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    public class Logger
    {
        private readonly Func<LogLevel, bool> _isEnabled;
        private readonly ManualLogSource _logger;

        public Logger(ManualLogSource logger, Func<LogLevel, bool> isEnabled)
        {
            _logger = logger;
            _isEnabled = isEnabled;
        }

        private void Log(LogLevel level, string message)
        {
            if (_isEnabled(level))
                _logger.Log(level, message);
        }

        private void Log(LogLevel level, Func<string> message)
        {
            if (_isEnabled(level))
                _logger.Log(level, message.Invoke());
        }

        public void Fatal(Func<string> message)
        {
            Log(LogLevel.Fatal, message);
        }

        public void Fatal(string message)
        {
            Log(LogLevel.Fatal, message);
        }

        public void Error(Func<string> message)
        {
            Log(LogLevel.Error, message);
        }

        public void Error(string message)
        {
            Log(LogLevel.Error, message);
        }

        public void Warning(Func<string> message)
        {
            Log(LogLevel.Warning, message);
        }

        public void Warning(string message)
        {
            Log(LogLevel.Warning, message);
        }

        public void Info(Func<string> message)
        {
            Log(LogLevel.Info, message);
        }

        public void Info(string message)
        {
            Log(LogLevel.Info, message);
        }

        public void Message(Func<string> message)
        {
            Log(LogLevel.Message, message);
        }

        public void Message(string message)
        {
            Log(LogLevel.Message, message);
        }

        public void Debug(Func<string> message)
        {
            Log(LogLevel.Debug, message);
        }

        public void Debug(string message)
        {
            Log(LogLevel.Debug, message);
        }
    }
}