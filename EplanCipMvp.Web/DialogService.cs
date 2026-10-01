using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace EplanCipMvp.Web
{
    public class BrowseResult
    {
        public string Path { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// 11.09.2026: пользователь попросил кнопки "Обзор..." в браузерной версии —
    /// браузер по соображениям безопасности не может отдать JS реальный
    /// абсолютный путь к файлу на диске. Но раз сервер и браузер, по условию
    /// пользователя (см. план), физически на одной машине — сервер сам может
    /// открыть системный диалог Windows и вернуть выбранный путь как обычный
    /// текст, без всяких файловых загрузок через HTTP.
    ///
    /// Диалоги (OpenFileDialog/FolderBrowserDialog) — тоже WinForms/STA, как и
    /// сама EPLAN, но ОТДЕЛЬНЫЙ выделенный поток, не тот же, что у EplanSession —
    /// смешивать COM-сессию EPLAN с диалогами выбора файла незачем и рискованно.
    ///
    /// 11.09.2026, позже: живой тест — кнопка нажималась, но НИЧЕГО не появлялось
    /// (ни диалога, ни ошибки в консоли браузера). Два вероятных бага сразу:
    /// (1) ShowDialog() без владельца-окна у консольного приложения мог открыть
    /// диалог БЕЗ фокуса — за окном браузера, не видно, а fetch() в JS просто
    /// висел, ожидая закрытия невидимого диалога; (2) Invoke() глотал ЛЮБОЕ
    /// исключение молча (`catch { result = null; }`) — если ShowDialog() по
    /// какой-то причине падал, мы бы никогда об этом не узнали. Оба исправлены:
    /// диалог теперь показывается с временным невидимым TopMost-владельцем
    /// (гарантированно поверх всех окон, с фокусом), а ошибки пробрасываются в
    /// ответ вместо тихого null.
    /// </summary>
    public class DialogService
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();

        public DialogService()
        {
            var thread = new Thread(RunQueue) { IsBackground = true, Name = "EplanCipMvp-Dialogs" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void RunQueue()
        {
            foreach (var action in _queue.GetConsumingEnumerable())
                action();
        }

        private BrowseResult Invoke(Func<string> func)
        {
            var result = new BrowseResult();
            using (var done = new ManualResetEventSlim(false))
            {
                _queue.Add(() =>
                {
                    try { result.Path = func(); }
                    catch (Exception ex) { result.Error = ex.ToString(); }
                    finally { done.Set(); }
                });
                done.Wait();
            }
            return result;
        }

        /// <summary>Невидимый TopMost-владелец — без него ShowDialog(), вызванный
        /// из консольного приложения (нет своего "активного" окна), мог открыть
        /// диалог без фокуса, за окном браузера. Окно ставим далеко за пределами
        /// экрана (не просто Opacity=0 — так оно не мелькает и не мешает).</summary>
        private static Form CreateTopMostOwner()
        {
            // 23.09.2026: раньше подложка стояла в (-32000,-32000) — диалог
            // центрируется относительно владельца и мог открыться ЗА ЭКРАНОМ
            // (кнопка «Обзор…» висела серой в ожидании невидимого окна).
            // Теперь — по центру экрана, но полностью прозрачная.
            var owner = new Form
            {
                TopMost = true,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(1, 1),
                FormBorderStyle = FormBorderStyle.None,
                Opacity = 0,
            };
            owner.Show();
            owner.Activate();
            owner.BringToFront();
            return owner;
        }

        /// <summary>filter — как в WinForms OpenFileDialog.Filter, например
        /// "Excel (*.xlsx)|*.xlsx" или "EPLAN project (*.elk)|*.elk".</summary>
        public BrowseResult BrowseFile(string filter)
        {
            return Invoke(() =>
            {
                using (var owner = CreateTopMostOwner())
                using (var dlg = new OpenFileDialog
                {
                    Filter = string.IsNullOrWhiteSpace(filter) ? "Все файлы (*.*)|*.*" : filter,
                })
                {
                    var dr = dlg.ShowDialog(owner);
                    owner.Close();
                    return dr == DialogResult.OK ? dlg.FileName : null;
                }
            });
        }

        public BrowseResult BrowseFolder()
        {
            return Invoke(() =>
            {
                using (var owner = CreateTopMostOwner())
                using (var dlg = new FolderBrowserDialog())
                {
                    var dr = dlg.ShowDialog(owner);
                    owner.Close();
                    return dr == DialogResult.OK ? dlg.SelectedPath : null;
                }
            });
        }

        /// <summary>12.09.2026: для полей вида "путь для НОВОГО файла" (например,
        /// целевой проект как полная копия донора — см. план
        /// eventual-humming-charm.md) — OpenFileDialog требует существующий файл,
        /// тут нужен SaveFileDialog (позволяет выбрать папку и вписать ещё не
        /// существующее имя).</summary>
        public BrowseResult BrowseSaveFile(string filter)
        {
            return Invoke(() =>
            {
                using (var owner = CreateTopMostOwner())
                using (var dlg = new SaveFileDialog
                {
                    Filter = string.IsNullOrWhiteSpace(filter) ? "Все файлы (*.*)|*.*" : filter,
                    OverwritePrompt = true,
                })
                {
                    var dr = dlg.ShowDialog(owner);
                    owner.Close();
                    return dr == DialogResult.OK ? dlg.FileName : null;
                }
            });
        }
    }
}
