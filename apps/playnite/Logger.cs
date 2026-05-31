using System;
using System.IO;

namespace Playdex
{
    public class Logger
    {
        private readonly string _filePath;

        public Logger(string filePath)
        {
            _filePath = filePath;
        }

        public void Log(string message)
        {
            var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} DEBUG: {message}";
            File.AppendAllText(_filePath, text + Environment.NewLine);
        }
    }
}