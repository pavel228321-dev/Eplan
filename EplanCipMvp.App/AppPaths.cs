using System;
using System.IO;

namespace EplanCipMvp.App
{
    /// <summary>
    /// 25.09.2026: файлы программы (правила, настройки, журнал, выгрузки) — в %APPDATA%\EplanCipMvp.
    /// Программа лежит в Platform\&lt;версия&gt;\Bin (Program Files), а туда без прав администратора писать нельзя
    /// (живой запуск: «Access to the path ...\Bin\cable-numbering-rules.json is denied»). Старый файл рядом с exe,
    /// если он есть, при первом обращении копируется в новую папку.
    /// </summary>
    public static class AppPaths
    {
        public static string DataDir { get; } = ResolveDataDir();

        public static string UserFile(string fileName)
        {
            string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (DataDir == null) return legacy;
            string path = Path.Combine(DataDir, fileName);
            try
            {
                if (!File.Exists(path) && File.Exists(legacy)) File.Copy(legacy, path);
            }
            catch { /* старый файл не скопировался — начнём с пресета/пустых настроек */ }
            return path;
        }

        private static string ResolveDataDir()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EplanCipMvp");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                return null;
            }
        }
    }
}
