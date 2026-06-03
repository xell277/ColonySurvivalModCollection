using System;
using System.IO;

namespace BetterWater
{
    public static class BetterWaterLogger
    {
        private static string _path;

        public static void Initialize(string path)
        {
            _path = path;
            Write("BetterWater logger initialized.");
        }

        public static void Debug(string message)
        {
            if (BetterWaterModEntry.Config != null && BetterWaterModEntry.Config.DebugLogging)
            {
                Write(message);
            }
        }

        public static void Write(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(_path))
                {
                    Pipliz.Log.Write("[BetterWater] " + message);
                    return;
                }

                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch
            {
                try
                {
                    Pipliz.Log.Write("[BetterWater] " + message);
                }
                catch
                {
                }
            }
        }
    }
}
