using System;
using System.Collections.Generic;
using System.Linq;
using Eplan.EplApi.DataModel;
using EplanCipMvp.Core.WireNaming;

namespace EplanCipMvp.App
{
    /// <summary>
    /// 24.09.2026: «Считать → Предпросмотр → Записать» для жил и проводов — общая логика Gui и Web.
    /// Создавать ТОЛЬКО после EplanBootstrap.PinOnce (внутри EPLAN-состояния), как CableNumberingSession.
    /// </summary>
    public class WireNumberingSession
    {
        private WireReadResult _last = new WireReadResult();

        public List<WireNamingRow> Rows { get; private set; } = new List<WireNamingRow>();

        public List<WireNamingRow> Read(Project project, IList<WireRule> rules, Action<string> log)
        {
            _last = WireNumbering.ReadAll(project, log);
            log($"Считано проводов: {_last.Wires.Count}, из них жил кабелей: {_last.Wires.Count(w => w.Cable.Length > 0)}; " +
                $"кабелей со свободными жилами в артикуле: {_last.Cables.Count}.");
            ReadExportFile.Write("export-wires.tsv", WireExport.ToTsv(_last.Wires, _last.Cables), log);
            return Preview(rules);
        }

        public List<WireNamingRow> Preview(IList<WireRule> rules) =>
            Rows = WireNamingEngine.Preview(_last.Wires, rules, _last.Cables);

        /// <summary>24.09.2026: выгрузка последнего чтения; пусто — ещё не считывали.</summary>
        public string ExportTsv() => _last.Wires.Count == 0 ? "" : WireExport.ToTsv(_last.Wires, _last.Cables);

        /// <summary>
        /// Сначала жилы (Assign), затем номера; потом перечитывание и сверка. 25.09.2026: перед записью —
        /// резервная копия номеров (backup-wires-*.tsv в %APPDATA%\EplanCipMvp) для «Восстановить номера из файла…»;
        /// сверка — по контактам концов (после записи EPLAN пересоздаёт соединения, их id меняются).
        /// </summary>
        public void Apply(Project project, IDictionary<string, string> newNameByNet, bool placeDefinitionPoints, Action<string> log)
        {
            var plan = WireApplyPlanner.Plan(Rows, newNameByNet);
            foreach (var reason in plan.Rejected.Values) log("ПРОПУЩЕН: " + reason);
            if (plan.Names.Count == 0 && plan.Cores.Count == 0)
            {
                log("Записывать нечего.");
                return;
            }
            ReadExportFile.Write($"backup-wires-{DateTime.Now:yyyyMMdd-HHmmss}.tsv", WireExport.ToTsv(_last.Wires, _last.Cables),
                                 s => log(s.Replace("Выгрузка прочитанного сохранена", "Резервная копия номеров (для отката)")));

            int coresOk = 0;
            foreach (var change in plan.Cores)
            {
                if (!_last.ConnectionsById.TryGetValue(change.WireId, out var conn)) continue;
                var template = TakeTemplate(change.Cable, change.Core);
                if (template == null)
                {
                    log($"ПРОПУЩЕНА жила {change.Cable}:{change.Core} — шаблон уже занят, нажмите «Считать».");
                    continue;
                }
                if (WireNumbering.AssignCore(template, conn, $"{change.Cable}:{change.Core}", log)) coresOk++;
            }

            var wireById = _last.Wires.ToDictionary(w => w.Id);
            var expected = new Dictionary<string, string>();
            foreach (var step in plan.Names)
                foreach (var wireId in step.WireIds)
                {
                    if (!_last.ConnectionsById.TryGetValue(wireId, out var conn)) continue;
                    if (WireNumbering.WriteName(conn, step.Name, placeDefinitionPoints, $"узел {step.NetId}", log))
                        expected[WireExport.PinPairKey(wireById[wireId]) ?? wireId] = step.Name;
                }

            _last = WireNumbering.ReadAll(project, s => { });
            int namesOk = Verify(expected, log);
            log($"Жил назначено: {coresOk} из {plan.Cores.Count}. Номеров проводов записано и подтверждено: {namesOk} из {expected.Count}. " +
                $"Пропущено узлов: {plan.Rejected.Count}.");
        }

        /// <summary>25.09.2026: откат — номера из выгрузки/резервной копии (TSV) пишутся обратно на те же провода.</summary>
        public void Restore(Project project, string backupTsv, Action<string> log)
        {
            var backup = WireExport.ParseTsv(backupTsv);
            if (backup.Count == 0)
            {
                log("В файле нет проводов — нужна выгрузка «Считать провода» или резервная копия backup-wires-*.tsv.");
                return;
            }
            _last = WireNumbering.ReadAll(project, log);
            var plan = WireExport.RestorePlan(_last.Wires, backup);
            log($"В файле проводов: {backup.Count}; к восстановлению: {plan.Count}.");
            if (plan.Count == 0) return;

            ReadExportFile.Write($"backup-wires-{DateTime.Now:yyyyMMdd-HHmmss}.tsv", WireExport.ToTsv(_last.Wires, _last.Cables),
                                 s => log(s.Replace("Выгрузка прочитанного сохранена", "Резервная копия номеров (до отката)")));
            var wireById = _last.Wires.ToDictionary(w => w.Id);
            var expected = new Dictionary<string, string>();
            foreach (var kv in plan)
            {
                if (!_last.ConnectionsById.TryGetValue(kv.Key, out var conn)) continue;
                var wire = wireById[kv.Key];
                if (WireNumbering.WriteName(conn, kv.Value, false, $"{wire.CurrentName} -> «{kv.Value}»", log))
                    expected[WireExport.PinPairKey(wire)] = kv.Value;
            }
            _last = WireNumbering.ReadAll(project, s => { });
            int ok = Verify(expected, log);
            log($"Восстановлено и подтверждено: {ok} из {plan.Count}. Нажмите «Считать провода», чтобы обновить таблицу.");
        }

        private int Verify(Dictionary<string, string> expected, Action<string> log)
        {
            var actual = new Dictionary<string, string>();
            foreach (var w in _last.Wires)
            {
                string key = WireExport.PinPairKey(w);
                if (key != null && !actual.ContainsKey(key)) actual[key] = w.CurrentName ?? "";
            }
            int ok = 0, reported = 0;
            foreach (var kv in expected)
            {
                if (actual.TryGetValue(kv.Key, out string name) && name == kv.Value) { ok++; continue; }
                if (reported++ < 30)
                    log($"ПРОВЕРЬТЕ: провод {kv.Key.Replace("||", " — ")} должен был получить «{kv.Value}», после перечитывания — «{name ?? "не найден"}».");
            }
            if (reported > 30) log($"… и ещё {reported - 30} расхождений.");
            return ok;
        }

        private Connection TakeTemplate(string cable, string core)
        {
            if (!_last.TemplatesByCable.TryGetValue(cable, out var templates)) return null;
            var template = templates.FirstOrDefault(t => string.Equals(CoreOf(t), core, StringComparison.OrdinalIgnoreCase));
            if (template != null) templates.Remove(template);
            return template;
        }

        /// <summary>Пустое свойство EPLAN при чтении бросает EmptyPropertyException — считаем его пустой строкой.</summary>
        private static string CoreOf(Connection template)
        {
            try { return (template.Properties[Properties.Connection.CONNECTION_WIRENUMBER]?.ToString() ?? "").Trim(); }
            catch { return ""; }
        }
    }
}
