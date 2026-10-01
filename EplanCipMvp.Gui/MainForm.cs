using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.Starter;
using Eplan.EplApi.System;
using EplanCipMvp.App;
using EplanCipMvp.Core;
using EplanCipMvp.Core.CableNaming;
using EplanCipMvp.Core.WireNaming;
using EplanCipMvp.Core.Models;
using Microsoft.Extensions.Configuration;

namespace EplanCipMvp.Gui
{
    /// <summary>
    /// GUI: xlsx + пути к проектам -> копирование листов из 1260/1166 в 1364.
    /// (см. /home/node/.claude/plans/eventual-humming-charm.md для контекста плана).
    /// Собственной сборки не проверял (нет графики в контейнере разработки) — но
    /// EPLAN-обвязка (AssemblyResolver/EplApplication/ProjectManager) идентична
    /// уже скомпилированной и логически проверенной в EplanCipMvp.App.Program.
    ///
    /// 11.09.2026: было — только насосы с ПЧ (VfdPageCopier). Добавлено копирование
    /// "пачками" остальных типов устройств из методички 1364-CIP-metodichka.docx §11
    /// (клапаны, датчики) через новый DeviceGroupPageCopier — не трогая существующий
    /// путь насосов вообще (см. комментарии у DeviceGroupPageCopier про риск). Типы
    /// без донора вообще (VC/FQT/насосы дозирования) сюда не включены — см.
    /// DeviceGroupCatalog.NoDonorDeviceTypeNames, информационная строка в GUI.
    ///
    /// 11.09.2026, позже в тот же день: пользователь попросил (1) вести за один раз
    /// НЕСКОЛЬКО типов устройств (не по одному) — "в идеале должен получиться
    /// цельный проект по максимуму" — и (2) второй проект-донор, т.к. донор GS
    /// реально лежит только в 1166, а не в 1260. Чекбоксы конкретно на страницах-
    /// донорах внутри одной группы пользователь явно ОТКЛОНИЛ ("не нужно вовсе — один
    /// шаблон это ок") — там осталась обычная одна страница на группу, просто теперь
    /// групп можно отметить сразу несколько (CheckedListBox), и под каждую отмеченную
    /// группу появляется своя строка с выбором донорской страницы.
    /// </summary>
    public class MainForm : Form
    {
        private TextBox _txtXlsx;
        private TextBox _txtBinPath;
        private TextBox _txtSourceProject;
        private TextBox _txtSourceProject2;
        private TextBox _txtTargetProject;
        private CheckedListBox _clbGroups;
        private Panel _pnlGroupDonors;
        private Button _btnConnect;
        private Button _btnCopy;
        private TextBox _txtLog;

        // 23.09.2026: вкладка «Нумерация кабелей» — общая с надстройкой EPLAN панель (CableNumberingPanel).
        private CableNumberingPanel _cablesPanel;
        // 24.09.2026: вкладка «Нумерация жил и проводов».
        private WireNumberingPanel _wiresPanel;

        /// <summary>Пункты _clbGroups, в том же порядке. Первый элемент — null
        /// (значит "Насосы с ПЧ", старый путь через VfdPageCopier).</summary>
        private readonly List<DeviceGroupConfig> _deviceGroups =
            new List<DeviceGroupConfig> { null }.Concat(DeviceGroupCatalog.All).ToList();

        /// <summary>Одна строка выбора донорской страницы под ОДНУ отмеченную группу —
        /// пересобирается целиком при каждом изменении отметок в _clbGroups.</summary>
        private class GroupRow
        {
            public DeviceGroupConfig Config; // null = насосы с ПЧ (при IsDpModules == false)
            public ComboBox DonorCombo;      // null, если донор недоступен (см. StatusText)
            public List<Page> Candidates = new List<Page>();
            public string StatusText;        // причина, почему DonorCombo == null / пусто

            /// <summary>11.09.2026: раздел DP (модули ПЛК) не укладывается в обычную
            /// DeviceGroupConfig-модель — нужны ДВЕ страницы-донора (титул + деталь),
            /// не одна, и целевые "теги" приходят не из DeviceInfo.Tag, а из
            /// ModuleGrouper.GroupIntoModules. Отдельный набор полей вместо DonorCombo.</summary>
            public bool IsDpModules;
            public ComboBox TitleCombo;
            public List<Page> TitleCandidates = new List<Page>();
        }

        private readonly List<GroupRow> _groupRows = new List<GroupRow>();

        /// <summary>Индекс пункта "Модули ПЛК (DP)" в _clbGroups — последний, после
        /// насосов и всех DeviceGroupCatalog.All (см. BuildUi).</summary>
        private int _dpIndex;

        /// <summary>
        /// 11.09.2026: пойманная (под Mono, но не факт что только там —
        /// перестраховались) TypeLoadException: "Could not load type of field
        /// 'MainForm:_app'... Could not load file or assembly 'Eplan.EplApi.Systemu'".
        /// Поля с EPLAN-типами (EplApplication/LockingStep/ProjectManager/Project),
        /// объявленные НАПРЯМУЮ на MainForm, заставляют рантайм резолвить их сборки
        /// уже в момент создания САМОЙ ФОРМЫ (`new MainForm()` в Program.cs) — то
        /// есть ДО того, как AssemblyResolver.PinToEplan() вообще успевает
        /// зарегистрировать, откуда их брать (тот вызывается только внутри
        /// BtnConnect_Click, по нажатию кнопки, уже после того как форма создана и
        /// показана). Раньше это не всплывало просто потому, что живьём ни разу не
        /// запускали (см. историю сессии) — теперь проверили под Mono и поймали.
        /// Фикс: вынести все EPLAN-типизированные поля в отдельный маленький класс
        /// `EplanState`, который создаётся НЕ в конструкторе формы, а внутри
        /// BtnConnect_Click, СРАЗУ ПОСЛЕ PinToEplan() — тогда резолвинг сборок
        /// откладывается до момента, когда AssemblyResolver уже готов их отдать.
        /// </summary>
        private class EplanState
        {
            public EplApplication App;
            // 09.09.2026: НАШЁЛ причину NoLockingStepException — по докладам
            // Eplan.EplApi.DataModelu.xml (класс LockingStep, раздел про офлайн-
            // приложения): "LockingStep is created and disposed implicitly by
            // built-in API actions, BUT it must be created explicitly in следующих
            // случаях: ... API offline applications." Мы и есть офлайн-приложение —
            // без явного LockingStep автосоздание внутри OpenProject падает. Нужен
            // ОДИН экземпляр на всё время сессии (создаётся сразу после Init,
            // держится живым, Dispose — при закрытии/выходе).
            public LockingStep LockingStep;
            public ProjectManager ProjectManager;

            /// <summary>Донор 1 — по умолчанию 1260, покрывает большинство типов.</summary>
            public Project SourceProject;

            /// <summary>11.09.2026: донор 2 — опционально, 1166_Kaliningrad_z.pdf. Нужен
            /// только для групп с DeviceGroupConfig.PreferredDonor == 2 (сейчас — только
            /// GS, донор которой подтверждён ИСКЛЮЧИТЕЛЬНО в 1166). Если поле пустое —
            /// такие группы просто недоступны для копирования (см. RebuildGroupRows).</summary>
            public Project SourceProject2;

            public Project TargetProject;

            /// <summary>23.09.2026: нумерация кабелей — общая с браузерной версией логика.</summary>
            public readonly CableNumberingSession Cables = new CableNumberingSession();

            /// <summary>24.09.2026: нумерация жил и проводов.</summary>
            public readonly WireNumberingSession Wires = new WireNumberingSession();
        }

        /// <summary>null, пока не нажата "Подключиться" — создаётся ПОСЛЕ PinToEplan(),
        /// см. комментарий у EplanState.</summary>
        private EplanState _eplan;

        // 09.09.2026: пользователь терял введённые пути (особенно "Папка Bin",
        // после того как менял её с дефолтного значения) при каждом перезапуске
        // программы — EPLAN несколько раз показывал модальные диалоги (выбор
        // лицензии), после закрытия/перезапуска все поля обнулялись обратно на
        // дефолт. Сохраняем текстовые поля в файл рядом с exe, подгружаем при
        // следующем запуске.
        // 25.09.2026: не рядом с exe (Program Files — нет прав на запись), а в %APPDATA%\EplanCipMvp.
        private static readonly string SettingsFilePath = AppPaths.UserFile("gui-settings.txt");

        public MainForm()
        {
            BuildUi();
            LoadSettings();
            Log($"Правила, настройки и выгрузки хранятся в: {AppPaths.DataDir ?? AppDomain.CurrentDomain.BaseDirectory}");
            FormClosed += (s, e) => CleanupEplan();
        }

        /// <summary>Порядок из официального примера: Close() проектов (сделано
        /// раньше в коде копирования) -> Dispose() LockingStep -> Exit() приложения.</summary>
        private void CleanupEplan()
        {
            if (_eplan == null) return; // "Подключиться" ни разу не нажималась
            try { _eplan.LockingStep?.Dispose(); } catch { /* закрываем окно в любом случае */ }
            try { _eplan.App?.Exit(); } catch { /* закрываем окно в любом случае */ }
        }

        private void LoadSettings()
        {
            try
            {
                if (!System.IO.File.Exists(SettingsFilePath))
                    return;
                var lines = System.IO.File.ReadAllLines(SettingsFilePath);
                if (lines.Length > 0 && lines[0].Length > 0) _txtXlsx.Text = lines[0];
                if (lines.Length > 1 && lines[1].Length > 0) _txtBinPath.Text = lines[1];
                if (lines.Length > 2 && lines[2].Length > 0) _txtSourceProject.Text = lines[2];
                if (lines.Length > 3 && lines[3].Length > 0) _txtTargetProject.Text = lines[3];
                // 11.09.2026: 5-я строка — донор 2 (1166), добавлена позже, поэтому
                // может отсутствовать в старом gui-settings.txt — проверяем длину.
                if (lines.Length > 4 && lines[4].Length > 0) _txtSourceProject2.Text = lines[4];
            }
            catch
            {
                // Не критично — просто останутся дефолтные значения полей.
            }
        }

        private void SaveSettings()
        {
            try
            {
                System.IO.File.WriteAllLines(SettingsFilePath, new[]
                {
                    _txtXlsx.Text,
                    _txtBinPath.Text,
                    _txtSourceProject.Text,
                    _txtTargetProject.Text,
                    _txtSourceProject2.Text,
                });
            }
            catch
            {
                // Не критично — просто придётся ввести пути заново в следующий раз.
            }
        }

        /// <summary>25.09.2026: версия в заголовке окна — чтобы по скриншоту было видно, какая сборка запущена.</summary>
        private const string BuildLabel = "сборка 25.09.2026 v8";

        private void BuildUi()
        {
            Text = "EplanCipMvp — нумерация кабелей (и генерация листов из доноров) — " + BuildLabel;
            Width = 1180;
            Height = 860;
            StartPosition = FormStartPosition.CenterScreen;

            // 23.09.2026: главное — нумерация. Сверху только подключение (Bin + проект);
            // прежняя генерация листов — на отдельной вкладке, доноры для подключения не нужны.
            var top = NewFieldsLayout(DockStyle.Top);

            _txtBinPath = AddRow(top, "Папка Bin вашей EPLAN:", out var btnBin);
            _txtBinPath.Text = @"C:\Program Files\EPLAN\Electric P8\2.9.4\Bin";
            btnBin.Click += (s, e) => BrowseFolder(_txtBinPath);
            AddNoteLabel(top, @"Папка, где лежит W3u.exe (Electric P8\<версия>\Bin). Саму программу держите в Platform\<версия>\Bin — рядом с Eplan.EplApi.*u.dll.");

            _txtTargetProject = AddRow(top, "Проект EPLAN (.elk):", out var btnTgt);
            btnTgt.Click += (s, e) => BrowseFile(_txtTargetProject, "EPLAN project (*.elk)|*.elk");
            AddNoteLabel(top, "Существующий проект. Первый прогон нумерации — на копии проекта.");

            _btnConnect = new Button { Text = "Подключиться", AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
            _btnConnect.Click += BtnConnect_Click;
            top.Controls.Add(_btnConnect, 1, top.RowCount);
            top.RowCount++;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tabCables = new TabPage("Нумерация кабелей");
            var tabLegacy = new TabPage("Генерация листов из доноров (прежние функции)");
            _cablesPanel = new CableNumberingPanel(ReadCables, PreviewCables, ApplyCableNames, Log,
                AppPaths.UserFile("cable-numbering-rules.json"), ExportCables);
            _cablesPanel.CanRead = false;
            tabCables.Controls.Add(_cablesPanel);
            var tabWires = new TabPage("Нумерация жил и проводов");
            _wiresPanel = new WireNumberingPanel(ReadWires, PreviewWires, ApplyWires, Log,
                AppPaths.UserFile("wire-rules-v7.json"), ExportWires, RestoreWires);
            _wiresPanel.CanRead = false;
            tabWires.Controls.Add(_wiresPanel);
            BuildLegacyTab(tabLegacy);
            tabs.TabPages.Add(tabCables);
            tabs.TabPages.Add(tabWires);
            tabs.TabPages.Add(tabLegacy);

            _txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Bottom,
                Height = 170,
                Font = new System.Drawing.Font("Consolas", 9f),
            };

            // Докинг обрабатывается в обратном порядке добавления: Fill — первым.
            Controls.Add(tabs);
            Controls.Add(_txtLog);
            Controls.Add(top);
        }

        private static TableLayoutPanel NewFieldsLayout(DockStyle dock)
        {
            var layout = new TableLayoutPanel
            {
                Dock = dock,
                ColumnCount = 3,
                AutoSize = true,
                Padding = new Padding(10),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            return layout;
        }

        private void BuildLegacyTab(TabPage page)
        {
            page.AutoScroll = true;
            var layout = NewFieldsLayout(DockStyle.Top);

            AddNoteLabel(layout, "Эти поля нужны только для генерации листов. Для нумерации кабелей их можно не заполнять. " +
                                  "Если донор 1 указан — «Подключиться» откроет доноры (только чтение) вместе с проектом.");

            _txtXlsx = AddRow(layout, "Спецификация (xlsx):", out var btnXlsx);
            btnXlsx.Click += (s, e) => BrowseFile(_txtXlsx, "Excel (*.xlsx)|*.xlsx");

            _txtSourceProject = AddRow(layout, "Проект-донор 1 (1260, .elk):", out var btnSrc);
            btnSrc.Click += (s, e) => BrowseFile(_txtSourceProject, "EPLAN project (*.elk)|*.elk");

            _txtSourceProject2 = AddRow(layout, "Проект-донор 2 (1166, опц., .elk):", out var btnSrc2);
            btnSrc2.Click += (s, e) => BrowseFile(_txtSourceProject2, "EPLAN project (*.elk)|*.elk");
            AddNoteLabel(layout, "Нужен только для групп, у которых донор реально лежит в 1166 (сейчас — " +
                                  "датчики люков GS). Остальные группы используют донор 1. Можно оставить пустым.");

            var lblGroup = new Label { Text = "Типы устройств (можно несколько):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) };
            layout.Controls.Add(lblGroup, 0, layout.RowCount);
            _clbGroups = new CheckedListBox { Dock = DockStyle.Fill, Height = 140, CheckOnClick = true };
            _clbGroups.Items.Add("Насосы с ПЧ (готово, проверено)");
            foreach (var g in DeviceGroupCatalog.All)
                _clbGroups.Items.Add(g.DisplayName + (g.IsAnalogy ? " ⚠ по аналогии" : ""));
            _clbGroups.Items.Add("Модули ПЛК (DP: обзор + детализация) ⚠ риск выше среднего");
            _dpIndex = _clbGroups.Items.Count - 1;
            // ItemCheck срабатывает ДО того, как состояние галочки реально обновится —
            // откладываем пересборку строк на "после" через BeginInvoke.
            // До подключения RebuildGroupRows не зовём: его JIT требует сборок EPLAN (Project),
            // а они подгружаются только после EplanBootstrap.PinOnce.
            _clbGroups.ItemCheck += (s, e) => { if (_eplan != null) BeginInvoke((Action)RebuildGroupRows); };
            layout.Controls.Add(_clbGroups, 1, layout.RowCount);
            layout.RowCount++;
            AddNoteLabel(layout, "Без донора ни в 1260, ни в 1166 (в списке выше их нет, программа их не копирует): " +
                                  string.Join("; ", DeviceGroupCatalog.NoDonorDeviceTypeNames) + " — чертить вручную.");

            var lblDonor = new Label { Text = "Донорская страница на группу:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) };
            layout.Controls.Add(lblDonor, 0, layout.RowCount);
            _pnlGroupDonors = new Panel { Dock = DockStyle.Fill, AutoSize = true, Height = 1 };
            layout.Controls.Add(_pnlGroupDonors, 1, layout.RowCount);
            layout.RowCount++;

            _btnCopy = new Button { Text = "Скопировать для всех отмеченных типов", AutoSize = true, Enabled = false, Margin = new Padding(0, 8, 0, 8) };
            _btnCopy.Click += BtnCopy_Click;
            layout.Controls.Add(_btnCopy, 1, layout.RowCount);
            layout.RowCount++;

            page.Controls.Add(layout);
        }

        private TextBox AddRow(TableLayoutPanel layout, string label, out Button browseButton)
        {
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left };
            layout.Controls.Add(lbl, 0, layout.RowCount);

            var txt = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(txt, 1, layout.RowCount);

            browseButton = new Button { Text = "Обзор...", AutoSize = true };
            layout.Controls.Add(browseButton, 2, layout.RowCount);

            layout.RowCount++;
            return txt;
        }

        private void AddNoteLabel(TableLayoutPanel layout, string text)
        {
            var lbl = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(560, 0),
                ForeColor = System.Drawing.Color.DimGray,
                Font = new System.Drawing.Font(Font.FontFamily, 8f, System.Drawing.FontStyle.Italic),
            };
            layout.Controls.Add(lbl, 1, layout.RowCount);
            layout.RowCount++;
        }

        private void BrowseFile(TextBox target, string filter)
        {
            using (var dlg = new OpenFileDialog { Filter = filter })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    target.Text = dlg.FileName;
            }
        }

        private void BrowseFolder(TextBox target)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    target.Text = dlg.SelectedPath;
            }
        }

        private void Log(string message)
        {
            _txtLog.AppendText(message + Environment.NewLine);
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            SaveSettings(); // сохраняем введённые пути СРАЗУ — даже если дальше будет ошибка

            // 23.09.2026: те же проверки и тот же порядок загрузки EPLAN, что в браузерной
            // версии (см. EplanBootstrap): папка варианта с W3u.exe; PinToEplan —
            // отдельным шагом ДО JIT-компиляции ConnectToEplan.
            string binError = EplanBootstrap.CheckBinPath(_txtBinPath.Text);
            if (binError != null)
            {
                Log(binError);
                return;
            }
            if (string.IsNullOrWhiteSpace(_txtTargetProject.Text))
            {
                Log("Укажите проект EPLAN (.elk).");
                return;
            }

            try
            {
                Log("Подключаюсь к EPLAN...");
                EplanBootstrap.PinOnce(_txtBinPath.Text);
                ConnectToEplan();
            }
            catch (Exception ex)
            {
                Log("ОШИБКА: " + ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ConnectToEplan()
        {
            if (_eplan == null)
            {
                // `_eplan` присваиваем только после ПОЛНОЙ инициализации: иначе при сбое
                // Init он остаётся «полуживым» (ProjectManager == null), а повторное
                // подключение init пропускает и падает с NullReferenceException.
                var state = new EplanState();
                try
                {
                    Log("Запускаю EPLAN API (Init)...");
                    state.App = new EplApplication();
                    state.App.EplanBinFolder = _txtBinPath.Text;
                    // Init("", true, true): true — EPLAN может показать свои окна (вход, лицензия).
                    state.App.Init("", true, true);
                    state.App.ResetQuietMode();
                    // LockingStep — ОДИН на всю сессию, сразу после Init, до ProjectManager
                    // (иначе NoLockingStepException в OpenProject, см. комментарий у EplanState).
                    state.LockingStep = new LockingStep();
                    state.ProjectManager = new ProjectManager();
                }
                catch
                {
                    try { state.LockingStep?.Dispose(); } catch { /* уже сбой — важнее исходное исключение */ }
                    try { state.App?.Exit(); } catch { /* то же */ }
                    throw;
                }
                _eplan = state;
                Log("EPLAN инициализирована.");
            }

            // Доноры — только для генерации листов; открываем ТОЛЬКО НА ЧТЕНИЕ.
            if (!string.IsNullOrWhiteSpace(_txtSourceProject.Text))
            {
                Log($"Открываю проект-донор 1 (только чтение): {_txtSourceProject.Text}");
                _eplan.SourceProject = _eplan.ProjectManager.OpenProject(_txtSourceProject.Text, ProjectManager.OpenMode.ReadOnly);

                if (!string.IsNullOrWhiteSpace(_txtSourceProject2.Text))
                {
                    try
                    {
                        Log($"Открываю проект-донор 2 (только чтение): {_txtSourceProject2.Text}");
                        _eplan.SourceProject2 = _eplan.ProjectManager.OpenProject(_txtSourceProject2.Text, ProjectManager.OpenMode.ReadOnly);
                    }
                    catch (Exception ex)
                    {
                        Log($"Донор 2 не открылся ({ex.Message}) — группы, для которых он нужен (GS), будут недоступны.");
                    }
                }
            }
            else
            {
                Log("Доноры не указаны — открываю только проект (режим нумерации).");
            }

            Log($"Открываю проект: {_txtTargetProject.Text}");
            _eplan.TargetProject = _eplan.ProjectManager.OpenProject(_txtTargetProject.Text);

            Log("Подключено.");
            _cablesPanel.CanRead = true;
            _wiresPanel.CanRead = true;
            RebuildGroupRows();
        }

        /// <summary>Пересобирает строки выбора донорской страницы под ТЕКУЩИЙ набор
        /// отмеченных галочками типов устройств (_clbGroups) — вызывается и сразу
        /// после подключения, и при каждом изменении отметок.</summary>
        private void RebuildGroupRows()
        {
            _pnlGroupDonors.Controls.Clear();
            _groupRows.Clear();

            if (_eplan == null || _eplan.SourceProject == null)
            {
                _btnCopy.Enabled = false;
                return; // ещё не подключились — галочки можно ставить и раньше, просто нечего искать
            }

            int y = 0;
            for (int i = 0; i < _clbGroups.Items.Count; i++)
            {
                if (!_clbGroups.GetItemChecked(i))
                    continue;

                if (i == _dpIndex)
                {
                    y = AddDpRow(y);
                    continue;
                }

                var config = _deviceGroups[i];
                var row = new GroupRow { Config = config };

                Project donorProject = config == null || config.PreferredDonor != 2 ? _eplan.SourceProject : _eplan.SourceProject2;
                string groupLabel = config == null ? "Насосы с ПЧ" : config.DisplayName;

                if (donorProject == null)
                {
                    row.StatusText = $"донор {(config?.PreferredDonor ?? 1)} не подключён — заполните поле выше и переподключитесь";
                }
                else
                {
                    string filter = config?.DonorPageFilter ?? ".M";
                    row.Candidates = config == null
                        ? VfdPageCopier.FindCandidateDonorPages(donorProject)
                        : DeviceGroupPageCopier.FindCandidateDonorPages(donorProject, filter);

                    if (row.Candidates.Count == 0)
                        row.StatusText = $"страниц с \"{filter}\" в имени не найдено в доноре {(config?.PreferredDonor ?? 1)}";
                }

                var lbl = new Label
                {
                    Text = groupLabel + ":",
                    AutoSize = true,
                    Location = new System.Drawing.Point(0, y + 4),
                };
                _pnlGroupDonors.Controls.Add(lbl);

                if (row.StatusText != null)
                {
                    var warn = new Label
                    {
                        Text = "⚠ " + row.StatusText,
                        AutoSize = true,
                        ForeColor = System.Drawing.Color.DarkRed,
                        Location = new System.Drawing.Point(220, y + 4),
                    };
                    _pnlGroupDonors.Controls.Add(warn);
                }
                else
                {
                    var combo = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Location = new System.Drawing.Point(220, y),
                        Width = 560,
                        DropDownWidth = 900, // сам список пошире — описание страницы длиннее, чем поле
                    };
                    foreach (var page in row.Candidates)
                        combo.Items.Add(DescribePage(page));
                    combo.SelectedIndex = 0;
                    row.DonorCombo = combo;
                    _pnlGroupDonors.Controls.Add(combo);
                }

                _groupRows.Add(row);
                y += 28;
            }

            _pnlGroupDonors.Height = Math.Max(1, y);
            _btnCopy.Enabled = _groupRows.Any(r => r.DonorCombo != null);

            if (_groupRows.Count == 0)
                Log("Отметьте хотя бы один тип устройств галочкой.");
        }

        /// <summary>11.09.2026: пользователь прислал скриншот — все поля выбора донора
        /// показывали ОДИН И ТОТ ЖЕ структурный идентификатор (".../1", первый
        /// кандидат по алфавиту) для РАЗНЫХ типов датчиков (LS/GS/PT/LT/TE), т.к.
        /// это просто идентификатор страницы, а не то, что на ней реально нарисовано —
        /// по нему нельзя отличить донора LS от донора PT. Добавляем в комбобокс ещё и
        /// описание страницы (то же свойство PAGE_NOMINATIOMN, которое сама программа
        /// проставляет на копиях, см. VfdPageCopier) — читаем его с ДОНОРСКОЙ страницы,
        /// это единственный текстовый ориентир, который у нас есть, кроме голого имени.
        /// Если описание не задано/не читается — не страшно, просто короче строка.</summary>
        private static string DescribePage(Page page)
        {
            string description = null;
            // 11.09.2026: PAGE_NOMINATIOMN на чтение возвращает PropertyValue, не
            // MultiLangString напрямую (в отличие от записи, см. VfdPageCopier) — та же
            // схема, что уже подтверждена для FUNC_DEVICETAG_MAINNAME: неявное
            // приведение к string через индексатор.
            try { description = page.Properties.PAGE_NOMINATIOMN; }
            catch { /* не критично — часть донорских страниц может не иметь описания */ }

            return string.IsNullOrWhiteSpace(description) ? page.Name : $"{page.Name}  —  {description}";
        }

        /// <summary>11.09.2026: строка для DP (модули ПЛК) — нужны ДВЕ страницы-донора
        /// (титул шкафа + деталь модулей), обе из того же ".DP" списка кандидатов
        /// (донор 1 — там подтверждена хотя бы DI-детализация, 1260 стр. 20-21).
        /// Возвращает новый Y-офсет для следующей строки (эта занимает 2 подстроки).</summary>
        private int AddDpRow(int y)
        {
            var row = new GroupRow { IsDpModules = true };
            const string filter = ".DP";
            var candidates = DeviceGroupPageCopier.FindCandidateDonorPages(_eplan.SourceProject, filter);
            row.Candidates = candidates;
            row.TitleCandidates = candidates;

            var lbl = new Label { Text = "Модули ПЛК (DP):", AutoSize = true, Location = new System.Drawing.Point(0, y + 4) };
            _pnlGroupDonors.Controls.Add(lbl);

            if (candidates.Count == 0)
            {
                var warn = new Label
                {
                    Text = $"⚠ страниц с \"{filter}\" в имени не найдено в доноре 1",
                    AutoSize = true, ForeColor = System.Drawing.Color.DarkRed,
                    Location = new System.Drawing.Point(220, y + 4),
                };
                _pnlGroupDonors.Controls.Add(warn);
                _groupRows.Add(row);
                return y + 28;
            }

            var lblTitle = new Label { Text = "титул:", AutoSize = true, Location = new System.Drawing.Point(220, y + 4) };
            _pnlGroupDonors.Controls.Add(lblTitle);
            var titleCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new System.Drawing.Point(280, y), Width = 500, DropDownWidth = 900 };
            foreach (var page in candidates) titleCombo.Items.Add(DescribePage(page));
            titleCombo.SelectedIndex = 0;
            row.TitleCombo = titleCombo;
            _pnlGroupDonors.Controls.Add(titleCombo);

            var lblDetail = new Label { Text = "деталь:", AutoSize = true, Location = new System.Drawing.Point(220, y + 32) };
            _pnlGroupDonors.Controls.Add(lblDetail);
            var detailCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new System.Drawing.Point(280, y + 28), Width = 500, DropDownWidth = 900 };
            foreach (var page in candidates) detailCombo.Items.Add(DescribePage(page));
            detailCombo.SelectedIndex = candidates.Count > 1 ? 1 : 0; // деталь почти наверняка не та же страница, что титул
            row.DonorCombo = detailCombo;
            _pnlGroupDonors.Controls.Add(detailCombo);

            _groupRows.Add(row);
            return y + 56;
        }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            if (_eplan == null || _eplan.SourceProject == null || _eplan.TargetProject == null)
            {
                Log("Сначала нажмите шаг 1 (Подключиться).");
                return;
            }

            var runnable = _groupRows.Where(r => r.DonorCombo != null).ToList();
            if (runnable.Count == 0)
            {
                Log("Нет ни одной готовой к копированию группы — проверьте предупреждения выше.");
                return;
            }

            Log($"Запускаю копирование для {runnable.Count} тип(ов) устройств...");
            foreach (var row in runnable)
            {
                var donorPage = row.Candidates[row.DonorCombo.SelectedIndex];
                if (row.IsDpModules)
                {
                    var titlePage = row.TitleCandidates[row.TitleCombo.SelectedIndex];
                    CopyPlcModules(titlePage, donorPage);
                }
                else if (row.Config == null)
                {
                    CopyVfdPumps(donorPage);
                }
                else
                {
                    CopyDeviceGroup(row.Config, donorPage);
                }
            }
            Log("Всё готово. Проверьте результат в самой EPLAN и пришлите лог целиком, если что-то не так.");
        }

        /// <summary>Старый путь — насосы с ПЧ, логика не изменилась с прошлой версии,
        /// только страница-донор теперь приходит параметром (раньше — из единственного
        /// глобального комбобокса).</summary>
        private void CopyVfdPumps(Page donorPage)
        {
            try
            {
                Log("Читаю спецификацию, ищу насосы с ПЧ...");
                var devices = SignalListReader.ReadDevices(_txtXlsx.Text);
                var circuits = VfdCircuitBuilder.BuildAll(devices);

                if (circuits.Count == 0)
                {
                    Log("Насосов с ПЧ в спецификации не найдено — нечего копировать.");
                    return;
                }

                Log($"Найдено {circuits.Count} насос(ов) с ПЧ: {string.Join(", ", circuits.Select(c => c.PumpTag))}");
                Log($"Копирую страницу '{donorPage.Name}' для каждого...");

                var copier = new VfdPageCopier();
                // Комплектация (артикулы ПЧ/автомата) — всегда из спецификации 1364
                // (VfdComponentDefaults.Default()), не с донорской страницы 1260.
                // 10.09.2026: передаём сами circuits (не только теги) — мощность на
                // подписи теперь берётся из VfdCircuit.Power (столбец "Power" в самой
                // спецификации), по каждому насосу отдельно, а не хардкодом на все сразу.
                var results = copier.CopyForAllPumps(donorPage, _eplan.TargetProject, circuits, VfdComponentDefaults.Default());

                foreach (var r in results)
                {
                    if (r.PageCopied)
                    {
                        Log($"  {r.PumpTag}: OK, новая страница '{r.NewPageName}', {r.PlacementsTotal} объектов.");
                        Log($"    Найдены теги: {string.Join(", ", r.FoundDeviceTags)}");
                        if (r.Retagged.Count > 0)
                            Log($"    Переименовано: {string.Join(", ", r.Retagged)}");
                        if (r.ArticlesSwapped.Count > 0)
                            Log($"    Артикул заменён на актуальный (1364): {string.Join(", ", r.ArticlesSwapped)}");
                        if (r.Removed.Count > 0)
                            Log($"    Убрано со страницы (Profinet, не нужно): {string.Join(" | ", r.Removed)}");
                        if (r.NoSpecMatch.Count > 0)
                            Log($"    ⚠ ТРЕБУЕТ РЕШЕНИЯ ЧЕЛОВЕКА: {string.Join(" | ", r.NoSpecMatch)}");
                        if (r.Error != null)
                            Log($"    Были ошибки на части объектов: {r.Error}");
                    }
                    else
                    {
                        Log($"  {r.PumpTag}: ОШИБКА — {r.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ОШИБКА (насосы с ПЧ): " + ex);
            }
        }

        /// <summary>11.09.2026: путь для клапанов/датчиков, "пачками" через
        /// DeviceGroupPageCopier (см. класс и план по расширению). Плотность
        /// (устройств/лист) и подсчёт страниц должны совпадать с методичкой
        /// 1364-CIP-metodichka.docx §11 — там те же числа (DeviceGroupConfig
        /// построен на тех же данных, что и таблицы в docx).</summary>
        private void CopyDeviceGroup(DeviceGroupConfig group, Page donorPage)
        {
            try
            {
                if (group.IsAnalogy)
                    Log($"⚠ [{group.DisplayName}] {group.AnalogyNote}");

                Log($"[{group.DisplayName}] Читаю спецификацию, ищу устройства типа {string.Join("/", group.TagTypeCodes)}...");
                var devices = SignalListReader.ReadDevices(_txtXlsx.Text);
                var tags = devices
                    .Select(d => d.Tag)
                    .Where(t => group.TagTypeCodes.Contains(TagParser.TypeCode(t)))
                    .OrderBy(t => t, NaturalStringComparer.Instance)
                    .ToList();

                if (tags.Count == 0)
                {
                    Log($"[{group.DisplayName}] Устройств в спецификации не найдено — нечего копировать.");
                    return;
                }

                var batches = DeviceGroupPageCopier.BuildBatches(tags, group.DevicesPerDonorPage);
                Log($"[{group.DisplayName}] Найдено {tags.Count} устройств(о): {string.Join(", ", tags)}");
                Log($"[{group.DisplayName}] Плотность донора: {group.DevicesPerDonorPage}/лист -> {batches.Count} страниц. " +
                    "Копирую страницу '" + donorPage.Name + "' на каждую пачку...");

                var copier = new DeviceGroupPageCopier();
                var results = copier.CopyGroup(donorPage, _eplan.TargetProject, group, tags);

                foreach (var r in results)
                {
                    string batchLabel = string.Join(", ", r.BatchTags);
                    if (r.PageCopied)
                    {
                        Log($"  {batchLabel}: OK, новая страница '{r.NewPageName}', {r.PlacementsTotal} объектов.");
                        Log($"    Найдены теги донора: {string.Join(", ", r.FoundDeviceTags)}");
                        if (r.SlotsMatched)
                        {
                            if (r.Retagged.Count > 0)
                                Log($"    Переименовано: {string.Join(", ", r.Retagged)}");
                        }
                        else
                        {
                            Log("    ⚠ СЛОТЫ НЕ РАЗОБРАНЫ — донорские теги оставлены как есть, поправьте вручную в EPLAN.");
                        }
                        if (r.Error != null)
                            Log($"    Были ошибки: {r.Error}");
                    }
                    else
                    {
                        Log($"  {batchLabel}: ОШИБКА — {r.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"ОШИБКА ({group.DisplayName}): " + ex);
            }
        }

        /// <summary>11.09.2026: раздел DP — обзор + детализация модулей ввода-вывода
        /// (§11.3 методички). Использует уже готовый ModuleGrouper (Core), который
        /// раньше только считал модули, страниц не создавал. См. PlcModulePageCopier —
        /// самая рискованная из новых частей: разбор обозначения модуля ("-2A1.5")
        /// подтверждён на ДОНОРЕ (1260), но не на самом 1364 напрямую.</summary>
        private void CopyPlcModules(Page titlePage, Page detailPage)
        {
            try
            {
                Log("[DP] Читаю спецификацию, группирую сигналы по модулям ET200SP...");

                // Та же схема конфигурации, что и в EplanCipMvp.App.Program (appsettings.json
                // + appsettings.Local.json, копируются в вывод сборки транзитивно через
                // ProjectReference на EplanCipMvp.App) — не дублируем настройки вручную здесь.
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
                var config = configBuilder.Build();
                var settings = new AppSettings();
                config.Bind(settings);
                settings.Grouping.CpuCabinet = settings.PlcHardware.CpuCabinet;

                var signals = SignalListReader.ReadDeviceList(_txtXlsx.Text, settings.Spec.SheetName);
                var modules = ModuleGrouper.GroupIntoModules(signals, settings.Grouping, settings.PageStructure);

                if (modules.Count == 0)
                {
                    Log("[DP] Модулей не найдено — нечего копировать.");
                    return;
                }

                var cabinets = modules.Select(m => m.Cabinet).Distinct().ToList();
                Log($"[DP] Найдено {modules.Count} модул(ей) в {cabinets.Count} шкаф(ах): {string.Join(", ", cabinets)}");

                var copier = new PlcModulePageCopier();

                Log($"[DP] Копирую титульные листы ('{titlePage.Name}') — по одному на шкаф. " +
                    "Содержимое (обзорная картинка) НЕ редактируется, только описание страницы.");
                var titleResults = copier.CopyTitlePages(titlePage, _eplan.TargetProject, cabinets);
                foreach (var r in titleResults)
                {
                    if (r.PageCopied)
                        Log($"  {r.Cabinet}: OK, новая страница '{r.NewPageName}'." + (r.Error != null ? $" (были ошибки: {r.Error})" : ""));
                    else
                        Log($"  {r.Cabinet}: ОШИБКА — {r.Error}");
                }

                Log($"[DP] Копирую детализацию ('{detailPage.Name}') — по 2 модуля на лист, " +
                    $"{PlcModulePageCopier_BatchCountHint(modules.Count)} страниц ожидается.");
                var detailResults = copier.CopyDetailPages(detailPage, _eplan.TargetProject, modules);
                foreach (var r in detailResults)
                {
                    string batchLabel = string.Join(", ", r.BatchDesignations);
                    if (r.PageCopied)
                    {
                        Log($"  {batchLabel}: OK, новая страница '{r.NewPageName}', {r.PlacementsTotal} объектов.");
                        Log($"    Найдены теги донора: {string.Join(", ", r.FoundDeviceTags)}");
                        if (r.SlotsMatched)
                        {
                            if (r.Retagged.Count > 0)
                                Log($"    Переименовано: {string.Join(", ", r.Retagged)}");
                            if (r.ArticlesSwapped.Count > 0)
                                Log($"    Артикул заменён: {string.Join(", ", r.ArticlesSwapped)}");
                        }
                        else
                        {
                            Log("    ⚠ СЛОТЫ НЕ РАЗОБРАНЫ — донорские теги/артикул оставлены как есть, поправьте вручную в EPLAN.");
                        }
                        if (r.Error != null)
                            Log($"    Были ошибки: {r.Error}");
                    }
                    else
                    {
                        Log($"  {batchLabel}: ОШИБКА — {r.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ОШИБКА (DP): " + ex);
            }
        }

        // ===== 23.09.2026: нумерация кабелей — делегаты для CableNumberingPanel =====
        // Отдельные методы (NoInlining): в их теле — EPLAN-состояние; JIT только после подключения.

        [MethodImpl(MethodImplOptions.NoInlining)]
        private List<CableNamingRow> ReadCables(IList<CableRule> rules, Action<string> log)
        {
            if (_eplan == null || _eplan.TargetProject == null)
            {
                log("Сначала подключитесь к проекту.");
                return new List<CableNamingRow>();
            }
            return _eplan.Cables.Read(_eplan.TargetProject, rules, log);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private List<CableNamingRow> PreviewCables(IList<CableRule> rules) =>
            _eplan == null ? new List<CableNamingRow>() : _eplan.Cables.Preview(rules);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ApplyCableNames(IDictionary<string, string> newNameById, Action<string> log)
        {
            if (_eplan == null || _eplan.TargetProject == null)
            {
                log("Сначала подключитесь к проекту.");
                return;
            }
            _eplan.Cables.Apply(_eplan.TargetProject, newNameById, log);
        }

        // ===== 24.09.2026: нумерация жил и проводов — делегаты для WireNumberingPanel =====

        [MethodImpl(MethodImplOptions.NoInlining)]
        private List<WireNamingRow> ReadWires(IList<WireRule> rules, Action<string> log)
        {
            if (_eplan == null || _eplan.TargetProject == null)
            {
                log("Сначала подключитесь к проекту.");
                return new List<WireNamingRow>();
            }
            return _eplan.Wires.Read(_eplan.TargetProject, rules, log);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private List<WireNamingRow> PreviewWires(IList<WireRule> rules) =>
            _eplan == null ? new List<WireNamingRow>() : _eplan.Wires.Preview(rules);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ApplyWires(IDictionary<string, string> newNameByNet, bool placeDefinitionPoints, Action<string> log)
        {
            if (_eplan == null || _eplan.TargetProject == null)
            {
                log("Сначала подключитесь к проекту.");
                return;
            }
            _eplan.Wires.Apply(_eplan.TargetProject, newNameByNet, placeDefinitionPoints, log);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string ExportCables() => _eplan == null ? "" : _eplan.Cables.ExportTsv();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RestoreWires(string backupTsv, Action<string> log)
        {
            if (_eplan == null || _eplan.TargetProject == null)
            {
                log("Сначала подключитесь к проекту.");
                return;
            }
            _eplan.Wires.Restore(_eplan.TargetProject, backupTsv, log);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string ExportWires() => _eplan == null ? "" : _eplan.Wires.ExportTsv();

        private static int PlcModulePageCopier_BatchCountHint(int moduleCount) => (moduleCount + 1) / 2;
    }
}
