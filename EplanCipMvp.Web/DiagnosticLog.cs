using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EplanCipMvp.Web
{
    /// <summary>
    /// 23.09.2026: журнал EplanCipMvp.Web.log рядом с exe — каждое действие, строки
    /// лога операций и ПОЛНЫЕ ошибки (тип, stack trace, внутренние исключения), чтобы
    /// при сбое хватало одного файла для разбора. Запись в журнал никогда не роняет
    /// программу (нет прав на папку — просто молча не пишем).
    /// </summary>
    public static class DiagnosticLog
    {
        private const long MaxBytes = 5 * 1024 * 1024;
        private static readonly object Lock = new object();

        // 25.09.2026: в %APPDATA%\EplanCipMvp (рядом с exe в Program Files писать нельзя — журнал молча пропадал).
        public static readonly string FilePath = EplanCipMvp.App.AppPaths.UserFile("EplanCipMvp.Web.log");

        public static void Start()
        {
            lock (Lock)
            {
                try
                {
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    {
                        File.Copy(FilePath, FilePath + ".old", true);
                        File.Delete(FilePath);
                    }
                }
                catch { /* журнал — вспомогательный, не мешаем запуску */ }
            }
            Info($"===== Запуск EplanCipMvp.Web: {Environment.MachineName}, {Environment.OSVersion}, .NET {Environment.Version} =====");
        }

        public static void Info(string message) => Write(message);

        public static void Lines(IEnumerable<string> lines)
        {
            if (lines == null) return;
            foreach (var line in lines) Write("    " + line);
        }

        public static void Error(string context, Exception ex)
        {
            string text = $"ОШИБКА [{context}]: {ex}";
            Write(text);
            try { Console.WriteLine(text); } catch { /* консоли может не быть */ }
        }

        private static void Write(string message)
        {
            lock (Lock)
            {
                try
                {
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}", Encoding.UTF8);
                }
                catch { /* нет прав на папку и т.п. — не роняем программу из-за журнала */ }
            }
        }
    }
}
