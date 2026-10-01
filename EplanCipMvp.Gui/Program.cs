using System;
using System.Windows.Forms;

namespace EplanCipMvp.Gui
{
    internal static class Program
    {
        // Обязательно — EPLAN API использует COM, офлайн-приложения должны
        // быть однопоточными (то же самое, что и в EplanCipMvp.App.Program).
        [STAThread]
        private static void Main()
        {
            // 09.09.2026: у пользователя окно вообще не появлялось, без единой
            // ошибки — WinForms без консоли может падать молча при старте
            // (например, если что-то не так внутри конструктора MainForm, ещё
            // до показа окна). Оборачиваем всё в try/catch с явным MessageBox,
            // чтобы ЛЮБАЯ ошибка старта была видна, а не тихо проглатывалась.
            Application.ThreadException += (s, e) =>
                ShowFatalError("Необработанное исключение в интерфейсе", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                ShowFatalError("Необработанное исключение", e.ExceptionObject as Exception);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ShowFatalError("Ошибка при запуске", ex);
            }
        }

        private static void ShowFatalError(string title, Exception ex)
        {
            string text = ex != null ? ex.ToString() : "(нет данных об исключении)";
            try
            {
                MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // Если даже MessageBox не может показаться — последний шанс,
                // пишем в файл рядом с exe, чтобы хоть что-то осталось.
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"),
                        title + Environment.NewLine + text);
                }
                catch { /* совсем ничего не можем сделать */ }
            }
        }
    }
}
