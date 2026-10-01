using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Gui
{
    /// <summary>
    /// 24.09.2026: окно нумерации кабелей (правила + таблица кабелей) — одно на две программы:
    /// вкладка в EplanCipMvp.Gui и диалог надстройки EplanCipMvp.AddIn (файл подключён туда ссылкой).
    /// Сам с EPLAN не работает: чтение/пересчёт/запись приходят делегатами от хозяина, в сигнатурах
    /// только типы Core — поэтому панель можно создать до загрузки сборок EPLAN.
    /// </summary>
    public class CableNumberingPanel : UserControl
    {
        public delegate List<CableNamingRow> ReadCables(IList<CableRule> rules, Action<string> log);
        public delegate List<CableNamingRow> PreviewCables(IList<CableRule> rules);
        public delegate void ApplyNames(IDictionary<string, string> newNameById, Action<string> log);

        private readonly ReadCables _read;
        private readonly PreviewCables _preview;
        private readonly ApplyNames _apply;
        private readonly Action<string> _log;
        private readonly CableRuleStore _ruleStore;

        private DataGridView _gridRules;
        private DataGridView _gridCables;
        private Button _btnRead;
        private Button _btnPreview;
        private Button _btnApply;
        private Label _lblSummary;
        private List<CableRule> _rules = new List<CableRule>();
        private List<CableNamingRow> _rows = new List<CableNamingRow>();
        private bool _filling;

        private readonly Func<string> _export;
        private Button _btnExport;

        public CableNumberingPanel(ReadCables read, PreviewCables preview, ApplyNames apply,
                                   Action<string> log, string rulesFilePath, Func<string> export = null)
        {
            _export = export;
            _read = read;
            _preview = preview;
            _apply = apply;
            _log = log;
            _ruleStore = new CableRuleStore(rulesFilePath);
            Dock = DockStyle.Fill;
            BuildUi();
            LoadRules();
        }

        /// <summary>Кнопка «Считать кабели» доступна, когда хозяин подключён к проекту.</summary>
        public bool CanRead
        {
            get => _btnRead.Enabled;
            set => _btnRead.Enabled = value;
        }

        private void BuildUi()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));

            layout.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(1120, 0),
                ForeColor = Color.DimGray,
                Text = "Правила проверяются сверху вниз, срабатывает первое подходящее. Пустое поле условия — «любое»; в масках * — любые символы, ? — один, # — цифра. " +
                       "Подстановки: {Устройство}, {Устройство:от_цифры}, {МестоИсточника}, {МестоЦели}, {ШкафИсточника}, " +
                       "{ШкафИсточника:2}, {.N} (суффикс .1/.2, только если кабелей к устройству больше одного). " +
                       "Первый прогон — на копии проекта.",
            }, 0, 0);

            _gridRules = NewGrid();
            AddColumn(_gridRules, new DataGridViewCheckBoxColumn(), "Enabled", "Вкл", 5, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Name", "Название", 18, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "SourceLocation", "Место источника", 12, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "TargetLocation", "Место цели", 12, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "TypeContains", "Тип содержит", 12, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "CoresTotal", "Жил", 6, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "TargetDevice", "Устройство цели", 12, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Template", "Шаблон", 23, false);
            layout.Controls.Add(_gridRules, 0, 1);

            var rulesButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            rulesButtons.Controls.Add(NewButton("Добавить правило", (s, e) => _gridRules.Rows.Add(true, "Новое правило", "", "", "", "", "", "")));
            rulesButtons.Controls.Add(NewButton("Удалить", (s, e) => DeleteSelectedRule()));
            rulesButtons.Controls.Add(NewButton("↑", (s, e) => MoveSelectedRule(-1)));
            rulesButtons.Controls.Add(NewButton("↓", (s, e) => MoveSelectedRule(1)));
            rulesButtons.Controls.Add(NewButton("Сохранить правила", (s, e) => SaveRules()));
            rulesButtons.Controls.Add(NewButton("Сбросить к пресету 1260", (s, e) => ResetRules()));
            layout.Controls.Add(rulesButtons, 0, 2);

            var cableButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            _btnRead = NewButton("Считать кабели", (s, e) => ReadClick());
            _btnPreview = NewButton("Пересчитать по правилам", (s, e) => PreviewClick());
            _btnPreview.Enabled = false;
            _btnApply = NewButton("Записать отмеченные", (s, e) => ApplyClick());
            _btnApply.Enabled = false;
            _btnApply.ForeColor = Color.DarkRed;
            _lblSummary = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
            cableButtons.Controls.Add(_btnRead);
            cableButtons.Controls.Add(_btnPreview);
            cableButtons.Controls.Add(_btnApply);
            // 24.09.2026: выгрузка прочитанного (для сверки правил по реальному проекту).
            _btnExport = NewButton("Сохранить выгрузку…", (s, e) => ExportDialog.Save(this, "export-cables.tsv", _export, _log));
            _btnExport.Enabled = false;
            _btnExport.Visible = _export != null;
            cableButtons.Controls.Add(_btnExport);
            cableButtons.Controls.Add(_lblSummary);
            layout.Controls.Add(cableButtons, 0, 3);

            _gridCables = NewGrid();
            AddColumn(_gridCables, new DataGridViewCheckBoxColumn(), "Apply", "✓", 4, false);
            AddColumn(_gridCables, new DataGridViewTextBoxColumn(), "Current", "Сейчас", 11, true);
            AddColumn(_gridCables, new DataGridViewTextBoxColumn(), "Sources", "Источник", 18, true);
            AddColumn(_gridCables, new DataGridViewTextBoxColumn(), "Targets", "Цель", 18, true);
            AddColumn(_gridCables, new DataGridViewTextBoxColumn(), "Type", "Тип / жил", 15, true);
            AddColumn(_gridCables, new DataGridViewTextBoxColumn(), "Rule", "Правило", 12, true);
            AddColumn(_gridCables, new DataGridViewTextBoxColumn(), "NewName", "Новое имя", 11, false);
            AddColumn(_gridCables, new DataGridViewTextBoxColumn(), "Status", "Статус", 20, true);
            _gridCables.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_gridCables.IsCurrentCellDirty) _gridCables.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _gridCables.CellValueChanged += GridCables_CellValueChanged;
            layout.Controls.Add(_gridCables, 0, 4);

            Controls.Add(layout);
        }

        private static DataGridView NewGrid() => new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            BackgroundColor = SystemColors.Window,
        };

        private static void AddColumn(DataGridView grid, DataGridViewColumn column, string name, string header, float weight, bool readOnly)
        {
            column.Name = name;
            column.HeaderText = header;
            column.FillWeight = weight;
            column.ReadOnly = readOnly;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(column);
        }

        private static Button NewButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += onClick;
            return button;
        }

        private static string CellText(DataGridViewRow row, string column) => (row.Cells[column].Value?.ToString() ?? "").Trim();

        private void LoadRules()
        {
            try { _rules = _ruleStore.Load(); }
            catch (Exception ex)
            {
                _log("ОШИБКА чтения cable-numbering-rules.json (взят пресет 1260): " + ex.Message);
                _rules = CableRulePresets.Like1260();
            }
            FillRulesGrid();
        }

        private void FillRulesGrid()
        {
            _gridRules.Rows.Clear();
            foreach (var r in _rules)
                _gridRules.Rows.Add(r.Enabled, r.Name, r.SourceLocation, r.TargetLocation, r.TypeContains,
                                    r.CoresTotal?.ToString() ?? "", r.TargetDevice, r.Template);
        }

        private bool TryReadRulesFromGrid(out List<CableRule> rules)
        {
            _gridRules.EndEdit();
            rules = new List<CableRule>();
            foreach (DataGridViewRow row in _gridRules.Rows)
            {
                string cores = CellText(row, "CoresTotal");
                int? coresTotal = null;
                if (cores.Length > 0)
                {
                    if (!int.TryParse(cores, out int n))
                    {
                        _log($"Правило «{CellText(row, "Name")}»: в «Жил» должно быть число или пусто.");
                        return false;
                    }
                    coresTotal = n;
                }
                rules.Add(new CableRule
                {
                    Enabled = row.Cells["Enabled"].Value is bool enabled && enabled,
                    Name = CellText(row, "Name"),
                    SourceLocation = CellText(row, "SourceLocation"),
                    TargetLocation = CellText(row, "TargetLocation"),
                    TypeContains = CellText(row, "TypeContains"),
                    CoresTotal = coresTotal,
                    TargetDevice = CellText(row, "TargetDevice"),
                    Template = CellText(row, "Template"),
                });
            }
            return true;
        }

        private void DeleteSelectedRule()
        {
            var cell = _gridRules.CurrentCell;
            if (cell != null) _gridRules.Rows.RemoveAt(cell.RowIndex);
        }

        private void MoveSelectedRule(int delta)
        {
            var cell = _gridRules.CurrentCell;
            if (cell == null || !TryReadRulesFromGrid(out var rules)) return;
            int i = cell.RowIndex, j = i + delta;
            if (j < 0 || j >= rules.Count) return;
            var tmp = rules[i]; rules[i] = rules[j]; rules[j] = tmp;
            _rules = rules;
            FillRulesGrid();
            _gridRules.CurrentCell = _gridRules.Rows[j].Cells[cell.ColumnIndex];
        }

        private void SaveRules()
        {
            if (!TryReadRulesFromGrid(out var rules)) return;
            try
            {
                _rules = _ruleStore.Save(rules);
                _log("Правила сохранены.");
            }
            catch (Exception ex)
            {
                _log("ОШИБКА сохранения правил: " + ex.Message);
            }
        }

        private void ResetRules()
        {
            if (MessageBox.Show(this, "Заменить текущие правила пресетом «Как 1260»?", "Правила",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                _rules = _ruleStore.ResetToPreset();
                FillRulesGrid();
                _log("Правила сброшены к пресету «Как 1260».");
            }
            catch (Exception ex)
            {
                // Файл не записался — пресет всё равно применяем в окне (иначе кнопка «ничего не делала»).
                _rules = CableRulePresets.Like1260();
                FillRulesGrid();
                _log("Пресет «Как 1260» применён, но не сохранён в файл: " + ex.Message);
            }
        }

        private void ReadClick()
        {
            if (!TryReadRulesFromGrid(out var rules)) return;
            _rules = rules;
            RunWithWaitCursor("чтения кабелей", () =>
            {
                _rows = _read(_rules, _log);
                FillCablesGrid();
                _btnPreview.Enabled = true;
                _btnApply.Enabled = _rows.Count > 0;
                _btnExport.Enabled = _rows.Count > 0;
            });
        }

        private void PreviewClick()
        {
            if (!TryReadRulesFromGrid(out var rules)) return;
            _rules = rules;
            RunWithWaitCursor("пересчёта", () =>
            {
                _rows = _preview(_rules);
                FillCablesGrid();
            });
        }

        private void ApplyClick()
        {
            _gridCables.EndEdit();
            var items = _rows.Where(r => r.Apply).ToDictionary(r => r.Cable.Id, r => (r.ProposedName ?? "").Trim());
            if (items.Count == 0)
            {
                _log("Нет отмеченных кабелей.");
                return;
            }
            if (HighlightDuplicates())
            {
                _log("Есть отмеченные строки с одинаковыми именами (подсвечены красным) — исправьте перед записью.");
                return;
            }
            if (MessageBox.Show(this, $"Переименовать {items.Count} кабел(ей) в проекте EPLAN? Рекомендуется делать на копии проекта.",
                                "Запись имён кабелей", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RunWithWaitCursor("записи", () =>
            {
                _apply(items, _log);
                _rows = _preview(_rules);
                FillCablesGrid();
            });
        }

        private void RunWithWaitCursor(string what, Action action)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                action();
            }
            catch (Exception ex)
            {
                _log($"ОШИБКА {what}: {ex}");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private static string DescribeEnds(List<CableEnd> ends) =>
            string.Join("; ", (ends ?? new List<CableEnd>()).Select(e => $"{e.Location}-{e.Device}"));

        private void FillCablesGrid()
        {
            _filling = true;
            _gridCables.Rows.Clear();
            foreach (var row in _rows)
            {
                int index = _gridCables.Rows.Add(
                    row.Apply,
                    string.IsNullOrEmpty(row.Cable.CurrentName) ? "(пусто)" : row.Cable.CurrentName,
                    DescribeEnds(row.Cable.Sources),
                    DescribeEnds(row.Cable.Targets),
                    $"{row.Cable.Type} / {row.Cable.CoresTotal}",
                    string.IsNullOrEmpty(row.RuleName) ? "—" : row.RuleName,
                    row.ProposedName,
                    row.Status + (string.IsNullOrEmpty(row.Note) ? "" : " — " + row.Note));
                var gridRow = _gridCables.Rows[index];
                gridRow.Tag = row;
                if (row.Status == CableNamingStatus.Conflict) gridRow.Cells["Apply"].ReadOnly = true;
                if (row.Status == CableNamingStatus.NoRule) gridRow.DefaultCellStyle.ForeColor = Color.Gray;
            }
            _filling = false;

            _lblSummary.Text = "Итого: " + string.Join(", ", _rows.GroupBy(r => r.Status).Select(g => $"{g.Key}: {g.Count()}"));
            HighlightDuplicates();
        }

        private void GridCables_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_filling || e.RowIndex < 0) return;
            var gridRow = _gridCables.Rows[e.RowIndex];
            if (!(gridRow.Tag is CableNamingRow row)) return;

            string column = _gridCables.Columns[e.ColumnIndex].Name;
            if (column == "Apply")
            {
                row.Apply = gridRow.Cells["Apply"].Value is bool apply && apply;
            }
            else if (column == "NewName")
            {
                row.ProposedName = gridRow.Cells["NewName"].Value?.ToString() ?? "";
                if (row.Status == CableNamingStatus.NoRule || row.Status == CableNamingStatus.Unchanged)
                {
                    string value = row.ProposedName.Trim();
                    row.Apply = value.Length > 0 && value != (row.Cable.CurrentName ?? "");
                    _filling = true;
                    gridRow.Cells["Apply"].Value = row.Apply;
                    _filling = false;
                }
            }
            HighlightDuplicates();
        }

        /// <summary>Подсвечивает отмеченные строки с одинаковыми итоговыми именами и конфликты.
        /// true — есть отмеченные дубли (запись нельзя).</summary>
        private bool HighlightDuplicates()
        {
            string Effective(CableNamingRow r) => r.Apply ? (r.ProposedName ?? "").Trim() : (r.Cable.CurrentName ?? "");
            var counts = _rows.Select(Effective).Where(n => n.Length > 0)
                              .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            bool anyAppliedDuplicate = false;
            foreach (DataGridViewRow gridRow in _gridCables.Rows)
            {
                if (!(gridRow.Tag is CableNamingRow row)) continue;
                string name = Effective(row);
                bool duplicate = row.Apply && name.Length > 0 && counts[name] > 1;
                anyAppliedDuplicate |= duplicate;
                gridRow.DefaultCellStyle.BackColor = duplicate || row.Status == CableNamingStatus.Conflict
                    ? Color.MistyRose
                    : Color.Empty;
            }
            return anyAppliedDuplicate;
        }
    }
}
