using System;
using System.Collections.Generic;
using System.Linq;

namespace EplanCipMvp.Core.CableNaming
{
    public class CableRenameStep
    {
        public string Id { get; set; }
        public string NewName { get; set; }
    }

    public class CableApplyPlan
    {
        /// <summary>id -> причина отказа (эти кабели не пишутся).</summary>
        public Dictionary<string, string> Rejected { get; } = new Dictionary<string, string>();
        /// <summary>Сначала: кабели, чьё текущее имя нужно другому кабелю, уходят во временное имя.</summary>
        public List<CableRenameStep> TempRenames { get; } = new List<CableRenameStep>();
        /// <summary>Затем: финальные имена.</summary>
        public List<CableRenameStep> FinalRenames { get; } = new List<CableRenameStep>();
    }

    /// <summary>
    /// Решает, что и в каком порядке писать. RenameDevice на уже существующий DT может
    /// слить два устройства в одно, поэтому: (1) дубли и занятые имена — отказ;
    /// (2) если имя освобождает другой переименовываемый кабель — сначала временное имя.
    /// </summary>
    public static class CableApplyPlanner
    {
        public const string TempPrefix = "TMPREN";
        private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        public static CableApplyPlan Plan(IList<CableInfo> cables, IDictionary<string, string> newNameById)
        {
            var plan = new CableApplyPlan();
            cables = cables ?? new List<CableInfo>();
            var byId = cables.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            var requested = new Dictionary<string, string>();

            foreach (var kv in newNameById ?? new Dictionary<string, string>())
            {
                if (!byId.TryGetValue(kv.Key, out var cable))
                {
                    plan.Rejected[kv.Key] = $"Кабель {kv.Key} не найден в последнем чтении — нажмите «Считать» ещё раз.";
                    continue;
                }
                string newName = (kv.Value ?? "").Trim();
                if (newName.Length == 0)
                {
                    plan.Rejected[kv.Key] = $"{Describe(cable)}: пустое новое имя.";
                    continue;
                }
                if (string.Equals(newName, cable.CurrentName ?? "", StringComparison.Ordinal)) continue;
                requested[kv.Key] = newName;
            }

            foreach (var group in requested.GroupBy(kv => kv.Value, Cmp).Where(g => g.Count() > 1))
                foreach (var kv in group)
                    plan.Rejected[kv.Key] = $"{Describe(byId[kv.Key])}: имя «{kv.Value}» получают сразу несколько кабелей.";

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var kv in requested)
                {
                    if (plan.Rejected.ContainsKey(kv.Key)) continue;
                    bool occupied = cables.Any(c => c.Id != kv.Key
                        && Cmp.Equals(c.CurrentName ?? "", kv.Value)
                        && (!requested.ContainsKey(c.Id) || plan.Rejected.ContainsKey(c.Id)));
                    if (occupied)
                    {
                        plan.Rejected[kv.Key] = $"{Describe(byId[kv.Key])}: имя «{kv.Value}» уже занято кабелем, который не переименовывается.";
                        changed = true;
                    }
                }
            }

            var accepted = requested.Where(kv => !plan.Rejected.ContainsKey(kv.Key)).ToList();
            var targetNames = new HashSet<string>(accepted.Select(kv => kv.Value), Cmp);
            var allNames = new HashSet<string>(cables.Select(c => c.CurrentName ?? ""), Cmp);
            int tempCounter = 0;
            foreach (var kv in accepted)
            {
                if (targetNames.Contains(byId[kv.Key].CurrentName ?? ""))
                {
                    string temp;
                    do { temp = TempPrefix + (++tempCounter); } while (allNames.Contains(temp) || targetNames.Contains(temp));
                    plan.TempRenames.Add(new CableRenameStep { Id = kv.Key, NewName = temp });
                }
                plan.FinalRenames.Add(new CableRenameStep { Id = kv.Key, NewName = kv.Value });
            }
            return plan;
        }

        public static string Describe(CableInfo cable)
        {
            string name = string.IsNullOrEmpty(cable.CurrentName) ? "(без имени)" : cable.CurrentName;
            return string.IsNullOrEmpty(cable.CableLocation) ? name : $"{cable.CableLocation}-{name}";
        }
    }
}
