using System;
using System.Diagnostics;
using System.Threading;

namespace EplanCipMvp.Web
{
    /// <summary>
    /// 11.09.2026: браузерная альтернатива EplanCipMvp.Gui (WinForms) — пользователь
    /// явно уточнил (через AskUserQuestion), что открывать браузер будут ТОЛЬКО с
    /// той же машины, где стоит EPLAN, поэтому слушаем строго localhost, без
    /// аутентификации. См. план /home/node/.claude/plans/eventual-humming-charm.md.
    ///
    /// EplanCipMvp.Gui.cs НЕ трогали и не удаляли — WinForms остаётся рабочим
    /// запасным вариантом, если браузерный не заведётся с первого раза на живой
    /// машине (та же осторожность, что и раньше в этом проекте: не ломаем то, что
    /// уже работает, ради нового).
    /// </summary>
    class Program
    {
        private const string Prefix = "http://localhost:5177/";

        [STAThread]
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== EplanCipMvp.Web — сборка 25.09.2026 v8 ===");

            DiagnosticLog.Start();
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                DiagnosticLog.Error("необработанное исключение", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            Console.WriteLine($"Журнал (пришлите его при любой ошибке): {DiagnosticLog.FilePath}");

            var session = new EplanSession();
            var dialogs = new DialogService();
            var server = new ApiServer(session, dialogs, Prefix);

            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("запуск HTTP-сервера", ex);
                Console.WriteLine("ОШИБКА при запуске HTTP-сервера: " + ex.Message);
                Console.WriteLine();
                Console.WriteLine("Если ошибка про отказ в доступе (Access denied) — выполните от имени");
                Console.WriteLine("администратора один раз:");
                Console.WriteLine($"  netsh http add urlacl url={Prefix} user={Environment.UserDomainName}\\{Environment.UserName}");
                Console.WriteLine("Нажмите Enter для выхода.");
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"Сервер запущен: {Prefix}");
            DiagnosticLog.Info($"Сервер запущен: {Prefix}");
            try { Process.Start(Prefix); }
            catch (Exception ex) { Console.WriteLine("Не удалось открыть браузер автоматически: " + ex.Message + $" — откройте {Prefix} вручную."); }

            Console.WriteLine("Нажмите Ctrl+C или закройте окно для выхода.");

            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; exitEvent.Set(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => exitEvent.Set();
            exitEvent.WaitOne();

            server.Stop();
            session.Dispose();
        }
    }
}
