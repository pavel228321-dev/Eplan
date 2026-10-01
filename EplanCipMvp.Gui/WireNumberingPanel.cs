using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EplanCipMvp.Core.WireNaming;

namespace EplanCipMvp.Gui
{
    /// <summary>
    /// 24.09.2026: нумерация жил и проводов (правила + таблица узлов). С EPLAN сама не работает:
    /// чтение/пересчёт/запись — делегаты хозяина, в сигнатурах только типы Core.
    /// </summary>
    public class WireNumberingPanel : UserControl
    {
        public delegate List<WireNamingRow> ReadWires(IList<WireRule> rules, Action<string> log);
        public delegate List<WireNamingRow> PreviewWires(IList<WireRule> rules);
        public delegate void ApplyWires(IDictionary<string, string> newNameByNet, bool placeDefinitionPoints, Action<string> log);

        private static readonly string[] RuleColumns =
            { "Name", "Location", "Device", "Terminal", "OtherLocation", "Potential", "InCable", "Order", "CounterStart", "AutoCheck", "Template" };

        private readonly ReadWires _read;
        private readonly PreviewWires _preview;
        private readonly ApplyWires _apply;
        private readonly Action<string> _log;
        private readonly WireRuleStore _ruleStore;
        private readonly string _ruleStorePath;

        private DataGridView _gridRules;
        private DataGridView _gridWires;
        private Button _btnRead;
        private Button _btnPreview;
        private Button _btnApply;
        private CheckBox _chkPlacePoints;
        private Label _lblSummary;
        private List<WireRule> _rules = new List<WireRule>();
        private List<WireNamingRow> _rows = new List<WireNamingRow>();
        private bool _filling;

        private readonly Func<string> _export;
        private readonly Action<string, Action<string>> _restore;
        private Button _btnRestore;
        private Button _btnExport;

        public WireNumberingPanel(ReadWires read, PreviewWires preview, ApplyWires apply, Action<string> log, string rulesFilePath,
                                  Func<string> export = null, Action<string, Action<string>> restore = null)
        {
            _export = export;
            _restore = restore;
            _read = read;
            _preview = preview;
            _apply = apply;
            _log = log;
            _ruleStore = new WireRuleStore(rulesFilePath);
            _ruleStorePath = rulesFilePath;
            Dock = DockStyle.Fill;
            BuildUi();
            LoadRules();
        }

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
                Text = "Строка = узел (провода с общим контактом получают один номер). Правила сверху вниз, первое подходящее; " +
                       "маски опорного конца — место/устройство/клемма, несколько масок через «;» (* — любые символы, ? — один, # — цифра). Подстановки: {Устройство}, " +
                       "{Устройство:от_цифры}, {Клемма}, {Клемма:2}, {Клемма:буквы}, {МодульПЛК:цифры}, {Группа}, {Номер:последняя}, " +
                       "{Потенциал}, {Потенциал:источник} (достраивает номер по маске «Источник», если EPLAN дал только букву), " +
                       "{Счётчик}. Жилы кабелей назначаются из артикула кабеля. Первый прогон — на копии проекта.",
            }, 0, 0);

            _gridRules = NewGrid();
            AddColumn(_gridRules, new DataGridViewCheckBoxColumn(), "Enabled", "Вкл", 4, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Name", "Название", 14, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Location", "Место", 8, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Device", "Устройство", 10, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Terminal", "Клемма", 7, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "OtherLocation", "Место др. конца", 8, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "ExcludeDevice", "Кроме устройств", 8, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Potential", "Потенциал (да/нет)", 7, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "InCable", "Жила (да/нет)", 6, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "SourceDevice", "Источник", 14, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Order", "Порядок X/Y", 5, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "CounterStart", "Счётчик с", 5, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "AutoCheck", "Отмечать сами (да/нет)", 6, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Template", "Шаблон", 20, false);
            layout.Controls.Add(_gridRules, 0, 1);

            var rulesButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            rulesButtons.Controls.Add(NewButton("Добавить правило", (s, e) => _gridRules.Rows.Add(true, "Новое правило", "", "", "", "", "", "", "", "", "X", "", "нет", "")));
            rulesButtons.Controls.Add(NewButton("Удалить", (s, e) => DeleteSelectedRule()));
            rulesButtons.Controls.Add(NewButton("↑", (s, e) => MoveSelectedRule(-1)));
            rulesButtons.Controls.Add(NewButton("↓", (s, e) => MoveSelectedRule(1)));
            rulesButtons.Controls.Add(NewButton("Сохранить правила", (s, e) => SaveRules()));
            rulesButtons.Controls.Add(NewButton("Сбросить к пресету 1260", (s, e) => ResetRules()));
            layout.Controls.Add(rulesButtons, 0, 2);

            var wireButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            _btnRead = NewButton("Считать провода", (s, e) => ReadClick());
            _btnPreview = NewButton("Пересчитать по правилам", (s, e) => PreviewClick());
            _btnPreview.Enabled = false;
            _btnApply = NewButton("Записать отмеченные", (s, e) => ApplyClick());
            _btnApply.Enabled = false;
            _btnApply.ForeColor = Color.DarkRed;
            _chkPlacePoints = new CheckBox { Text = "Ставить новые точки определения (где их нет)", AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
            // 25.09.2026: строки «на схеме нет точки номера» отмечаются сами, когда галочка включена, и снимаются, когда выключена.
            _chkPlacePoints.CheckedChanged += (s, e) =>
            {
                foreach (var r in _rows.Where(r => r.AutoWithPoints)) r.Apply = _chkPlacePoints.Checked;
                FillWiresGrid();
            };
            _lblSummary = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
            wireButtons.Controls.Add(_btnRead);
            wireButtons.Controls.Add(_btnPreview);
            wireButtons.Controls.Add(_btnApply);
            _btnExport = NewButton("Сохранить выгрузку…", (s, e) => ExportDialog.Save(this, "export-wires.tsv", _export, _log));
            _btnExport.Enabled = false;
            _btnExport.Visible = _export != null;
            wireButtons.Controls.Add(_btnExport);
            // 25.09.2026: откат номеров из резервной копии (backup-wires-*.tsv, пишется перед каждой записью) или выгрузки.
            _btnRestore = NewButton("Восстановить номера из файла…", (s, e) => RestoreClick());
            _btnRestore.Visible = _restore != null;
            wireButtons.Controls.Add(_btnRestore);
            wireButtons.Controls.Add(_chkPlacePoints);
            wireButtons.Controls.Add(_lblSummary);
            layout.Controls.Add(wireButtons, 0, 3);

            _gridWires = NewGrid();
            AddColumn(_gridWires, new DataGridViewCheckBoxColumn(), "Apply", "✓", 3, false);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Page", "Лист", 4, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Ends", "Подключён к", 28, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Potential", "Потенциал", 7, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Cable", "Кабель / жила", 11, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Current", "Сейчас", 8, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Rule", "Правило", 11, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "NewName", "Новый номер", 9, false);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Status", "Статус", 19, true);
            _gridWires.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_gridWires.IsCurrentCellDirty) _gridWires.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _gridWires.CellValueChanged += GridWires_CellValueChanged;
            layout.Controls.Add(_gridWires, 0, 4);

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
                _log("ОШИБКА чтения wire-numbering-rules.json (взят пресет 1260): " + ex.Message);
                _rules = WireRulePresets.Like1260();
            }
            FillRulesGrid();
        }

        private void FillRulesGrid()
        {
            _gridRules.Rows.Clear();
            foreach (var r in _rules)
                _gridRules.Rows.Add(r.Enabled, r.Name, r.Location, r.Device, r.Terminal, r.OtherLocation, r.ExcludeDevice, r.Potential, r.InCable,
                                    r.SourceDevice, r.Order, r.CounterStart?.ToString() ?? "", r.AutoCheck, r.Template);
        }

        private bool TryReadRulesFromGrid(out List<WireRule> rules)
        {
            _gridRules.EndEdit();
            rules = new List<WireRule>();
            foreach (DataGridViewRow row in _gridRules.Rows)
            {
                string start = CellText(row, "CounterStart");
                int? counterStart = null;
                if (start.Length > 0)
                {
                    if (!int.TryParse(start, out int n))
                    {
                        _log($"Правило «{CellText(row, "Name")}»: в «Счётчик с» должно быть число или пусто.");
                        return false;
                    }
                    counterStart = n;
                }
                rules.Add(new WireRule
                {
                    Enabled = row.Cells["Enabled"].Value is bool enabled && enabled,
                    Name = CellText(row, "Name"),
                    Location = CellText(row, "Location"),
                    Device = CellText(row, "Device"),
                    Terminal = CellText(row, "Terminal"),
                    OtherLocation = CellText(row, "OtherLocation"),
                    ExcludeDevice = CellText(row, "ExcludeDevice"),
                    Potential = CellText(row, "Potential"),
                    InCable = CellText(row, "InCable"),
                    SourceDevice = CellText(row, "SourceDevice"),
                    Order = CellText(row, "Order").Length == 0 ? "X" : CellText(row, "Order"),
                    CounterStart = counterStart,
                    AutoCheck = CellText(row, "AutoCheck"),
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
                _log("Правила нумерации проводов сохранены.");
            }
            catch (Exception ex)
            {
                _log("ОШИБКА сохранения правил: " + ex.Message);
            }
        }

        private void ResetRules()
        {
            if (MessageBox.Show(this, "Заменить правила нумерации проводов пресетом «Как 1260»?", "Правила",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                _rules = _ruleStore.ResetToPreset();
                FillRulesGrid();
                _log("Правила нумерации проводов сброшены к пресету «Как 1260».");
            }
            catch (Exception ex)
            {
                // Файл не записался — пресет всё равно применяем в окне (иначе кнопка «ничего не делала»).
                _rules = WireRulePresets.Like1260();
                FillRulesGrid();
                _log("Пресет «Как 1260» применён, но не сохранён в файл: " + ex.Message);
            }
        }

        private void ReadClick()
        {
            if (!TryReadRulesFromGrid(out var rules)) return;
            _rules = rules;
            RunWithWaitCursor("чтения проводов", () =>
            {
                _rows = _read(_rules, _log);
                FillWiresGrid();
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
                FillWiresGrid();
            });
        }

        private void ApplyClick()
        {
            _gridWires.EndEdit();
            var items = _rows.Where(r => r.Apply).ToDictionary(r => r.NetId, r => (r.ProposedName ?? "").Trim());
            if (items.Count == 0)
            {
                _log("Нет отмеченных строк.");
                return;
            }
            if (HighlightDuplicates())
            {
                _log("Есть отмеченные строки с одинаковыми номерами (подсвечены красным) — исправьте перед записью.");
                return;
            }
            int cores = _rows.Where(r => r.Apply).Sum(r => r.Cores.Count);
            if (MessageBox.Show(this, $"Записать номера в {items.Count} узл(ах) и назначить {cores} жил(ы) в проекте EPLAN? " +
                                      "Рекомендуется делать на копии проекта.",
                                "Запись номеров проводов", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RunWithWaitCursor("записи", () =>
            {
                _apply(items, _chkPlacePoints.Checked, _log);
                _rows = _preview(_rules);
                FillWiresGrid();
            });
        }

        private void RestoreClick()
        {
            if (!_btnRead.Enabled)
            {
                _log("Сначала подключитесь к проекту.");
                return;
            }
            string text;
            using (var dialog = new OpenFileDialog
            {
                Title = "Резервная копия номеров (backup-wires-*.tsv) или выгрузка export-wires.tsv",
                Filter = "Таблица TSV (*.tsv)|*.tsv|Все файлы (*.*)|*.*",
                InitialDirectory = System.IO.Path.GetDirectoryName(_ruleStorePath),
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { text = System.IO.File.ReadAllText(dialog.FileName, System.Text.Encoding.UTF8); }
                catch (Exception ex)
                {
                    _log("ОШИБКА чтения файла: " + ex.Message);
                    return;
                }
            }
            if (MessageBox.Show(this, "Вернуть проводам номера из выбранного файла? Провода, которых в файле нет, не меняются. " +
                                      "Текущие номера перед этим тоже сохранятся в резервную копию.",
                                "Восстановление номеров", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RunWithWaitCursor("восстановления", () =>
            {
                _restore(text, _log);
                _rows = new List<WireNamingRow>();
                FillWiresGrid();
                _btnApply.Enabled = false;
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

        private static string DescribeEnds(List<WireEnd> ends) =>
            string.Join("  ↔  ", (ends ?? new List<WireEnd>()).Select(e => $"{e.Location}-{e.Device}:{e.Terminal}"));

        private static string DescribeCable(WireNamingRow row)
        {
            string cores = string.Join(", ", row.Cores.Select(c => "→ " + c.Core));
            return row.Cable.Length == 0 ? "" : cores.Length == 0 ? row.Cable : $"{row.Cable} {cores}";
        }

        private void FillWiresGrid()
        {
            if (_chkPlacePoints.Checked)
                foreach (var r in _rows.Where(r => r.AutoWithPoints)) r.Apply = true;
            _filling = true;
            _gridWires.Rows.Clear();
            foreach (var row in _rows)
            {
                int index = _gridWires.Rows.Add(
                    row.Apply,
                    (row.Page + 1).ToString(),
                    DescribeEnds(row.Ends),
                    row.Potential,
                    DescribeCable(row),
                    string.IsNullOrEmpty(row.CurrentName) ? "(пусто)" : row.CurrentName,
                    string.IsNullOrEmpty(row.RuleName) ? "—" : row.RuleName,
                    row.ProposedName,
                    row.Status + (string.IsNullOrEmpty(row.Note) ? "" : " — " + row.Note));
                var gridRow = _gridWires.Rows[index];
                gridRow.Tag = row;
                if (row.Status == WireNamingStatus.Conflict) gridRow.Cells["Apply"].ReadOnly = true;
                if (row.Status == WireNamingStatus.NoRule && row.Cores.Count == 0) gridRow.DefaultCellStyle.ForeColor = Color.Gray;
            }
            _filling = false;

            _lblSummary.Text = "Итого: " + string.Join(", ", _rows.GroupBy(r => r.Status).Select(g => $"{g.Key}: {g.Count()}")) +
                               $"; жил к назначению: {_rows.Sum(r => r.Cores.Count)}";
            HighlightDuplicates();
        }

        private void GridWires_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_filling || e.RowIndex < 0) return;
            var gridRow = _gridWires.Rows[e.RowIndex];
            if (!(gridRow.Tag is WireNamingRow row)) return;

            string column = _gridWires.Columns[e.ColumnIndex].Name;
            if (column == "Apply")
            {
                row.Apply = gridRow.Cells["Apply"].Value is bool apply && apply;
            }
            else if (column == "NewName")
            {
                row.ProposedName = gridRow.Cells["NewName"].Value?.ToString() ?? "";
                if (row.Status == WireNamingStatus.NoRule || row.Status == WireNamingStatus.Unchanged)
                {
                    string value = row.ProposedName.Trim();
                    row.Apply = (value.Length > 0 && value != row.CurrentName) || row.Cores.Count > 0;
                    _filling = true;
                    gridRow.Cells["Apply"].Value = row.Apply;
                    _filling = false;
                }
            }
            HighlightDuplicates();
        }

        /// <summary>Отмеченные строки с одинаковым итоговым номером (кроме общих — потенциал/PE) и конфликты.</summary>
        private bool HighlightDuplicates()
        {
            string Effective(WireNamingRow r) => r.Apply && (r.ProposedName ?? "").Trim().Length > 0
                ? r.ProposedName.Trim()
                : (r.CurrentName.Contains(" / ") ? "" : r.CurrentName);
            var groups = _rows.Where(r => Effective(r).Length > 0)
                              .GroupBy(Effective, StringComparer.OrdinalIgnoreCase)
                              .Where(g => g.Count() > 1 && !g.All(r => r.SharedName))
                              .SelectMany(g => g)
                              .ToList();
            var duplicates = new HashSet<WireNamingRow>(groups);
            bool anyAppliedDuplicate = false;
            foreach (DataGridViewRow gridRow in _gridWires.Rows)
            {
                if (!(gridRow.Tag is WireNamingRow row)) continue;
                bool duplicate = row.Apply && duplicates.Contains(row);
                anyAppliedDuplicate |= duplicate;
                gridRow.DefaultCellStyle.BackColor = duplicate || row.Status == WireNamingStatus.Conflict ? Color.MistyRose : Color.Empty;
            }
            return anyAppliedDuplicate;
        }
    }
}
