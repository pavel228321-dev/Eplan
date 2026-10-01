using System;
using System.Collections.Generic;
using System.Linq;

namespace EplanCipMvp.Core.CableNaming
{
    public static class CableNamingEngine
    {
        public static List<CableNamingRow> Preview(IList<CableInfo> cables, IList<CableRule> rules)
        {
            var activeRules = (rules ?? new List<CableRule>()).Where(r => r != null && r.Enabled).ToList();
            var rows = new List<CableNamingRow>();
            foreach (var cable in cables ?? new List<CableInfo>())
            {
                var row = new CableNamingRow { Cable = cable, ProposedName = cable.CurrentName ?? "" };
                foreach (var rule in activeRules)
                {
                    string name = TryRule(rule, cable, cable.Sources, cable.Targets)
                                  ?? TryRule(rule, cable, cable.Targets, cable.Sources);
                    if (name == null) continue;
                    row.RuleName = rule.Name ?? "";
                    row.ProposedName = name;
                    break;
                }
                rows.Add(row);
            }

            ResolveSuffixes(rows);
            foreach (var row in rows) SetDefaultStatus(row);
            MarkConflicts(rows);
            return rows;
        }

        private static string TryRule(CableRule rule, CableInfo cable, List<CableEnd> sources, List<CableEnd> targets)
        {
            sources = sources ?? new List<CableEnd>();
            targets = targets ?? new List<CableEnd>();

            if (!string.IsNullOrWhiteSpace(rule.TypeContains)
                && (cable.Type ?? "").IndexOf(rule.TypeContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                return null;
            if (rule.CoresTotal.HasValue && rule.CoresTotal.Value != cable.CoresTotal) return null;

            var source = sources.FirstOrDefault(e => WildcardMask.MatchesAny(rule.SourceLocation, e.Location));
            var target = targets.FirstOrDefault(e => WildcardMask.MatchesAny(rule.TargetLocation, e.Location));
            if (!string.IsNullOrWhiteSpace(rule.SourceLocation) && source == null) return null;
            if (!string.IsNullOrWhiteSpace(rule.TargetLocation) && target == null) return null;
            if (!string.IsNullOrWhiteSpace(rule.TargetDevice)
                && !targets.Any(t => WildcardMask.MatchesAny(rule.TargetDevice, t.Device)))
                return null;

            return CableRuleTemplate.Render(rule.Template, new RuleContext
            {
                Device = target?.Device ?? "",
                SourceLocation = source?.Location ?? "",
                TargetLocation = target?.Location ?? "",
            });
        }

        private static void ResolveSuffixes(List<CableNamingRow> rows)
        {
            var withToken = rows.Where(r => r.ProposedName.Contains(CableRuleTemplate.SuffixToken)).ToList();
            var counts = withToken.GroupBy(BaseName, StringComparer.OrdinalIgnoreCase)
                                  .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in withToken)
            {
                string baseName = BaseName(row);
                if (counts[baseName] > 1)
                {
                    seen.TryGetValue(baseName, out int n);
                    seen[baseName] = ++n;
                    row.ProposedName = row.ProposedName.Replace(CableRuleTemplate.SuffixToken, "." + n);
                }
                else
                {
                    row.ProposedName = baseName;
                }
            }
        }

        private static string BaseName(CableNamingRow row) => row.ProposedName.Replace(CableRuleTemplate.SuffixToken, "");

        private static void SetDefaultStatus(CableNamingRow row)
        {
            string current = row.Cable.CurrentName ?? "";
            if (string.IsNullOrEmpty(row.RuleName)) { row.Status = CableNamingStatus.NoRule; row.Apply = false; }
            else if (string.Equals(row.ProposedName, current, StringComparison.Ordinal)) { row.Status = CableNamingStatus.Unchanged; row.Apply = false; }
            else if (current.Length == 0 || current.Contains("?")) { row.Status = CableNamingStatus.New; row.Apply = true; }
            else { row.Status = CableNamingStatus.Rename; row.Apply = false; }
        }

        /// <summary>Конфликты считаются так, будто применены ВСЕ предложения (New+Rename) —
        /// тот же CableApplyPlanner, что проверяет реальную запись.</summary>
        private static void MarkConflicts(List<CableNamingRow> rows)
        {
            var proposals = rows.Where(r => r.Status == CableNamingStatus.New || r.Status == CableNamingStatus.Rename)
                                .ToDictionary(r => r.Cable.Id, r => r.ProposedName);
            var plan = CableApplyPlanner.Plan(rows.Select(r => r.Cable).ToList(), proposals);
            foreach (var row in rows)
            {
                if (!plan.Rejected.TryGetValue(row.Cable.Id, out string reason)) continue;
                row.Status = CableNamingStatus.Conflict;
                row.Apply = false;
                row.Note = reason;
            }
        }
    }
}
