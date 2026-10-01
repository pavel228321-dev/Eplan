using System;
using System.Collections.Generic;
using System.Linq;

namespace EplanCipMvp.Core.WireNaming
{
    public class WireNameStep
    {
        public string NetId { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> WireIds { get; set; } = new List<string>();
    }

    public class WireApplyPlan
    {
        /// <summary>узел -> причина отказа (его номер не пишется; жилы пишутся).</summary>
        public Dictionary<string, string> Rejected { get; } = new Dictionary<string, string>();
        public List<WireNameStep> Names { get; } = new List<WireNameStep>();
        public List<CoreChange> Cores { get; } = new List<CoreChange>();
    }

    /// <summary>
    /// Что писать. Номер узла не пишется, если его же получает другой узел (в запросе или уже в проекте),
    /// кроме «общих» номеров (SharedName: имя потенциала, постоянный шаблон вроде PE). Жилы пишутся всегда.
    /// Порядок записи не важен: номер провода — просто свойство, не DT (в отличие от кабелей, обмен не нужен).
    /// </summary>
    public static class WireApplyPlanner
    {
        private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        public static WireApplyPlan Plan(IList<WireNamingRow> rows, IDictionary<string, string> newNameByNet)
        {
            var plan = new WireApplyPlan();
            rows = rows ?? new List<WireNamingRow>();
            var byNet = rows.GroupBy(r => r.NetId).ToDictionary(g => g.Key, g => g.First());
            var requested = new Dictionary<string, string>();

            foreach (var kv in newNameByNet ?? new Dictionary<string, string>())
            {
                if (!byNet.TryGetValue(kv.Key, out var row))
                {
                    plan.Rejected[kv.Key] = $"Узел {kv.Key} не найден в последнем чтении — нажмите «Считать» ещё раз.";
                    continue;
                }
                plan.Cores.AddRange(row.Cores);
                string name = (kv.Value ?? "").Trim();
                if (name.Length == 0 || string.Equals(name, row.CurrentName, StringComparison.Ordinal)) continue;
                requested[kv.Key] = name;
            }

            string Effective(WireNamingRow r) =>
                requested.TryGetValue(r.NetId, out string n) ? n : (r.CurrentName.Contains(" / ") ? "" : r.CurrentName.Trim());

            foreach (var group in rows.GroupBy(Effective, Cmp).Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                bool shared = group.All(r => r.SharedName && Cmp.Equals(r.ProposedName, group.Key));
                // 25.09.2026: PE, 1M, 31L+ на многих проводах — один потенциал, не дубль (на 1364 было 12 ложных конфликтов PE).
                if (shared || WireRuleTemplate.LooksLikePotential(group.Key)) continue;
                foreach (var r in group.Where(r => requested.ContainsKey(r.NetId)))
                    plan.Rejected[r.NetId] = $"{Describe(r)}: номер «{group.Key}» уже есть или его получает другой узел.";
            }

            foreach (var kv in requested.Where(kv => !plan.Rejected.ContainsKey(kv.Key)))
                plan.Names.Add(new WireNameStep { NetId = kv.Key, Name = kv.Value, WireIds = byNet[kv.Key].WireIds.ToList() });
            return plan;
        }

        public static string Describe(WireNamingRow row)
        {
            var ends = row.Ends.Take(2).Select(e => $"{e.Location}-{e.Device}:{e.Terminal}");
            string where = string.Join(" — ", ends);
            return where.Length > 0 ? $"лист {row.Page + 1}, {where}" : row.NetId;
        }
    }
}
