using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using EplanCipMvp.Gui;

namespace EplanCipMvp.AddIn
{
    /// <summary>Окно надстройки: та же панель, что во вкладке Gui, плюс лог внизу.</summary>
    internal class CableNumberingDialog : Form
    {
        private readonly TextBox _log;

        public CableNumberingDialog(string projectName,
                                    CableNumberingPanel.ReadCables read,
                                    CableNumberingPanel.PreviewCables preview,
                                    CableNumberingPanel.ApplyNames apply)
        {
            Text = "Нумерация кабелей — " + projectName;
            Width = 1180;
            Height = 820;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Bottom,
                Height = 160,
                Font = new Font("Consolas", 9f),
            };

            var panel = new CableNumberingPanel(read, preview, apply, Log, AddInLog.RulesFilePath);
            Controls.Add(panel);
            Controls.Add(_log);

            Log($"Проект: {projectName}. Правила: {AddInLog.RulesFilePath}. Журнал: {AddInLog.FilePath}");
        }

        private void Log(string message)
        {
            _log.AppendText(message + Environment.NewLine);
            AddInLog.Write(message);
        }
    }

    /// <summary>Журнал и правила — в %APPDATA%\EplanCipMvp (в папку надстройки может не быть прав на запись).</summary>
    internal static class AddInLog
    {
        private static readonly object Lock = new object();
        public static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EplanCipMvp");
        public static readonly string FilePath = Path.Combine(Folder, "EplanCipMvp.AddIn.log");
        public static readonly string RulesFilePath = Path.Combine(Folder, "cable-numbering-rules.json");

        static AddInLog()
        {
            try { Directory.CreateDirectory(Folder); } catch { /* журнал вспомогательный */ }
        }

        public static void Write(string message)
        {
            lock (Lock)
            {
                try { File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"); }
                catch { /* не роняем EPLAN из-за журнала */ }
            }
        }
    }
}
