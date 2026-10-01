using System;
using System.IO;
using System.Text;

namespace EplanCipMvp.App
{
    /// <summary>
    /// 24.09.2026: запись выгрузки «Считать…» рядом с программой (нет прав на запись — во временную папку).
    /// Файл перезаписывается при каждом чтении; путь пишется в лог — его и присылают для сверки правил.
    /// </summary>
    public static class ReadExportFile
    {
        public static void Write(string fileName, string content, Action<string> log)
        {
            foreach (string dir in new[] { AppPaths.DataDir, AppDomain.CurrentDomain.BaseDirectory, Path.GetTempPath() })
            {
                if (dir == null) continue;
                try
                {
                    string path = Path.Combine(dir, fileName);
                    File.WriteAllText(path, content, new UTF8Encoding(true));
                    log($"Выгрузка прочитанного сохранена: {path}");
                    return;
                }
                catch (Exception)
                {
                    // нет прав на папку программы (Program Files без администратора) — пробуем временную
                }
            }
            log($"Не удалось сохранить выгрузку {fileName}.");
        }
    }
}
