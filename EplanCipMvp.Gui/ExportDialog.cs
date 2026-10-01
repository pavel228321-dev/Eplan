using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace EplanCipMvp.Gui
{
    /// <summary>24.09.2026: «Сохранить выгрузку…» — выбор файла, запись TSV (UTF-8 с BOM, открывается в Excel), показ в проводнике.</summary>
    public static class ExportDialog
    {
        public static void Save(IWin32Window owner, string defaultFileName, Func<string> content, Action<string> log)
        {
            string text;
            try { text = content?.Invoke(); }
            catch (Exception ex)
            {
                log("ОШИБКА выгрузки: " + ex.Message);
                return;
            }
            if (string.IsNullOrEmpty(text))
            {
                log("Нечего выгружать — сначала нажмите «Считать».");
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                FileName = defaultFileName,
                Filter = "Таблица TSV (*.tsv)|*.tsv|Все файлы (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, text, new UTF8Encoding(true));
                    log($"Выгрузка сохранена: {dialog.FileName}");
                    try { Process.Start("explorer.exe", $"/select,\"{dialog.FileName}\""); }
                    catch { /* проводник не открылся — путь уже в логе */ }
                }
                catch (Exception ex)
                {
                    log("ОШИБКА сохранения выгрузки: " + ex.Message);
                }
            }
        }
    }
}
