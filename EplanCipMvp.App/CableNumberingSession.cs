using System;
using System.Collections.Generic;
using System.Linq;
using Eplan.EplApi.DataModel;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.App
{
    /// <summary>
    /// 23.09.2026: «Считать → Предпросмотр → Записать» для кабелей — общая логика Gui и Web.
    /// Хранит кабели последнего чтения (id -> Function EPLAN) — создавать ТОЛЬКО после
    /// EplanBootstrap.PinOnce (внутри EPLAN-состояния), см. комментарии к EplanState.
    /// </summary>
    public class CableNumberingSession
    {
        private readonly Dictionary<string, Function> _functionsById = new Dictionary<string, Function>();
        private Dictionary<string, int> _connectionCounts = new Dictionary<string, int>();

        public List<CableInfo> Cables { get; private set; } = new List<CableInfo>();

        public List<CableNamingRow> Read(Project project, IList<CableRule> rules, Action<string> log)
        {
            Remember(CableNumbering.ReadAll(project, log));
            log($"Считано кабелей: {Cables.Count}.");
            ReadExportFile.Write("export-cables.tsv", CableExport.ToTsv(Cables), log);
            var rows = CableNamingEngine.Preview(Cables, rules);
            // 24.09.2026: сводка в лог — чтобы переименования и кабели без правила можно было прислать текстом.
            foreach (var row in rows.Where(r => r.Status == CableNamingStatus.Rename))
                log($"Предложено переименование: {CableApplyPlanner.Describe(row.Cable)} -> {row.ProposedName} ({row.RuleName})");
            var noRule = rows.Where(r => r.Status == CableNamingStatus.NoRule).Select(r => CableApplyPlanner.Describe(r.Cable)).ToList();
            if (noRule.Count > 0) log($"Нет правила ({noRule.Count}): {string.Join(", ", noRule)}");
            return rows;
        }

        public List<CableNamingRow> Preview(IList<CableRule> rules) => CableNamingEngine.Preview(Cables, rules);

        /// <summary>24.09.2026: выгрузка последнего чтения (формат фикстуры 1260-cables.tsv); пусто — ещё не считывали.</summary>
        public string ExportTsv() => Cables.Count == 0 ? "" : CableExport.ToTsv(Cables);

        /// <summary>Пишет новые имена (id -> имя), затем перечитывает и сверяет имя и число жил.</summary>
        public void Apply(Project project, IDictionary<string, string> newNameById, Action<string> log)
        {
            var plan = CableApplyPlanner.Plan(Cables, newNameById);
            foreach (var reason in plan.Rejected.Values) log("ПРОПУЩЕН: " + reason);

            foreach (var step in plan.TempRenames)
                CableNumbering.Rename(_functionsById[step.Id], step.NewName, s => log("(временно) " + s));
            var renamedFrom = new Dictionary<string, string>();
            foreach (var step in plan.FinalRenames)
                if (CableNumbering.Rename(_functionsById[step.Id], step.NewName, log))
                    renamedFrom[step.NewName] = step.Id;

            var reread = CableNumbering.ReadAll(project, s => { });
            var byName = reread.GroupBy(r => r.Info.CurrentName, StringComparer.OrdinalIgnoreCase)
                               .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            int ok = 0;
            foreach (var kv in renamedFrom)
            {
                int before = _connectionCounts.TryGetValue(kv.Value, out int b) ? b : -1;
                if (!byName.TryGetValue(kv.Key, out var found))
                    log($"ПРОВЕРЬТЕ: после записи кабель «{kv.Key}» не найден при перечитывании.");
                else if (found.ConnectionCount != before)
                    log($"ПРОВЕРЬТЕ: у «{kv.Key}» жил под кабелем было {before}, стало {found.ConnectionCount} — жилы могли отвязаться.");
                else ok++;
            }
            log($"Записано и подтверждено перечитыванием: {ok} из {plan.FinalRenames.Count}. Пропущено: {plan.Rejected.Count}.");
            Remember(reread);
        }

        private void Remember(List<ReadCable> read)
        {
            _functionsById.Clear();
            foreach (var r in read) _functionsById[r.Info.Id] = r.Function;
            _connectionCounts = read.ToDictionary(r => r.Info.Id, r => r.ConnectionCount);
            Cables = read.Select(r => r.Info).ToList();
        }
    }
}
