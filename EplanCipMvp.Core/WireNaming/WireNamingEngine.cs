using System;
using System.Collections.Generic;
using System.Linq;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>
    /// Номер узла по правилам (сверху вниз, первое сработавшее; опорный конец — любой конец узла,
    /// подходящий под маски правила) + счётчики + статусы/галочки + жилы кабелей (CoreAssigner).
    /// См. docs/superpowers/specs/2026-09-24-wire-core-numbering-design.md.
    /// </summary>
    public static class WireNamingEngine
    {
        private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        private class Pending
        {
            public WireNamingRow Row;
            public WireRule Rule;
            public string Scope;
            public WireInfo Position;
        }

        public static List<WireNamingRow> Preview(IList<WireInfo> wires, IList<WireRule> rules, IList<CableCores> cables)
        {
            var activeRules = (rules ?? new List<WireRule>()).Where(r => r != null && r.Enabled).ToList();
            var rows = new List<WireNamingRow>();
            var namesByRow = new Dictionary<WireNamingRow, List<string>>();
            var pending = new List<Pending>();
            var guardedRows = new HashSet<WireNamingRow>();
            var fieldRows = new HashSet<WireNamingRow>();

            // Жилы считаются до номеров: номер провода к клемме «+»/«-» берёт жилу (T6LT1-1).
            var coreNotes = new Dictionary<string, string>();
            var coreChanges = CoreAssigner.Assign(wires, cables, coreNotes);
            var coreByWire = (wires ?? new List<WireInfo>()).Where(w => w != null && (w.CurrentCore ?? "").Trim().Length > 0)
                .GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First().CurrentCore.Trim());
            foreach (var c in coreChanges) coreByWire[c.WireId] = c.Core;

            var nets = WireNets.Build(wires);
            var graph = new WireNetGraph(nets);
            foreach (var net in nets)
            {
                var ends = WireNets.Ends(net);
                string potential = net.Wires.Select(w => (w.Potential ?? "").Trim()).FirstOrDefault(p => p.Length > 0) ?? "";
                var names = net.Wires.Select(w => (w.CurrentName ?? "").Trim()).ToList();
                var row = new WireNamingRow
                {
                    NetId = net.Id,
                    Page = net.Wires[0].Page,
                    WireIds = net.Wires.Select(w => w.Id).ToList(),
                    Ends = ends,
                    Potential = potential,
                    Cable = string.Join(", ", net.Wires.Select(w => w.Cable ?? "").Where(c => c.Length > 0).Distinct(Cmp)),
                    CurrentName = string.Join(" / ", names.Where(n => n.Length > 0).Distinct(Cmp)),
                };
                namesByRow[row] = names;
                bool onPotential = potential.Length > 0 || names.Any(WireRuleTemplate.LooksLikePotential)
                                   // клемма клеммника с именем потенциала (1X24:1M, 1X24:31L+); у аппаратов (УПП 1L1/3L2) — не потенциал
                                   || ends.Any(e => System.Text.RegularExpressions.Regex.IsMatch(e.Device ?? "", @"^\d*X\d+$")
                                                    && WireRuleTemplate.LooksLikePotential(e.Terminal));
                row.OnPotential = onPotential;
                bool fieldRule = false, autoCheck = true;
                // Перемычки и разводка внутри одного устройства (1X24:3 — 1X24:1M, UP1:15 — UP1-15:+) не нумеруем.
                bool internalWiring = IsInternalWiring(ends);

                foreach (var rule in internalWiring ? new List<WireRule>() : activeRules)
                {
                    var (name, anchor) = TryRule(rule, net, ends, onPotential, potential, coreByWire, graph);
                    if (name == null) continue;
                    row.RuleName = rule.Name ?? "";
                    fieldRule = WildcardMask.MatchesAny(rule.Location, "+FIELD") && (rule.Location ?? "").Trim().Length > 0;
                    autoCheck = !string.Equals((rule.AutoCheck ?? "").Trim(), "нет", StringComparison.OrdinalIgnoreCase);
                    row.ProposedName = name;
                    // 01.10.2026: {Потенциал:источник} достраивает цифры, которых у «сырого» потенциала
                    // EPLAN нет (M -> 1M) — name никогда не равен potential, хотя повтор (общий минус на
                    // несколько веток) законен. LooksLikePotential — тот же фолбэк, что уже используют
                    // MarkDuplicates и WireApplyPlanner.Plan, так что здесь это не меняет их поведение
                    // (они и так не блокировали такие имена), а только даёт GUI/Web (у них этого фолбэка
                    // нет, они читают только SharedName) верный сигнал.
                    row.SharedName = WireRuleTemplate.IsConstant(rule.Template) || (potential.Length > 0 && Cmp.Equals(name, potential))
                                      || WireRuleTemplate.LooksLikePotential(name);
                    if (name.Contains(WireRuleTemplate.CounterToken))
                        pending.Add(new Pending { Row = row, Rule = rule, Scope = anchor.Location ?? "", Position = net.Wires[0] });
                    break;
                }
                // Провода потенциалов: номер ставит сам EPLAN (1M, 31L+) — предлагаем, но галочку не ставим,
                // кроме жил к полю (как в 1260: GS101-1 на 31L+).
                if (fieldRule) fieldRows.Add(row);
                if ((onPotential && !fieldRule) || !autoCheck || ends.Any(e => (e.Device ?? "").Length == 0)) guardedRows.Add(row);
                rows.Add(row);
            }

            ResolveCounters(rows, pending, activeRules, guardedRows);
            foreach (var row in rows.Where(r => r.Ends.Any(e => (e.Device ?? "").Length == 0)))
                AddNote(row, "у конца нет DT (проверьте устройство в EPLAN)");
            // Жила к полю, где сейчас номер-потенциал (41L+): по 1260 там «устройство-клемма» — считаем номер пустым.
            foreach (var row in rows)
                SetDefaultStatus(row, fieldRows.Contains(row)
                    ? namesByRow[row].Select(n => WireRuleTemplate.LooksLikePotential(n) ? "" : n).ToList()
                    : namesByRow[row]);
            foreach (var row in guardedRows.Where(r => r.RuleName.Length > 0))
            {
                if (row.Status == WireNamingStatus.New && row.OnPotential) AddNote(row, "провод потенциала — проверьте и отметьте вручную");
                row.Apply = false;
            }
            MarkDuplicates(rows, guardedRows);
            MarkConflicts(rows);
            MarkWithoutPoints(rows, wires);
            AttachCores(rows, coreChanges, coreNotes);
            return rows;
        }

        private static (string Name, WireEnd Anchor) TryRule(WireRule rule, WireNet net, List<WireEnd> ends, bool onPotential, string potential,
                                                             Dictionary<string, string> coreByWire, WireNetGraph graph)
        {
            if (!Flag(rule.Potential, onPotential)) return (null, null);
            if (!Flag(rule.InCable, net.Wires.Any(w => (w.Cable ?? "").Length > 0))) return (null, null);
            if (!string.IsNullOrWhiteSpace(rule.ExcludeDevice) && ends.Any(e => WildcardMask.MatchesAny(rule.ExcludeDevice, e.Device ?? "")))
                return (null, null);

            foreach (var anchor in ends)
            {
                if (!WildcardMask.MatchesAny(rule.Location, anchor.Location ?? "")) continue;
                if (!WildcardMask.MatchesAny(rule.Device, anchor.Device ?? "")) continue;
                if (!WildcardMask.MatchesAny(rule.Terminal, anchor.Terminal ?? "")) continue;
                if (!string.IsNullOrWhiteSpace(rule.OtherLocation)
                    && !ends.Any(o => !ReferenceEquals(o, anchor) && WildcardMask.MatchesAny(rule.OtherLocation, o.Location ?? "")))
                    continue;
                var wire = net.Wires.FirstOrDefault(w => ReferenceEquals(w.Start, anchor) || ReferenceEquals(w.End, anchor)
                                                         || (anchor.PinKey ?? "").Length > 0
                                                            && (anchor.PinKey == w.Start?.PinKey || anchor.PinKey == w.End?.PinKey));
                string core = wire != null && coreByWire.TryGetValue(wire.Id, out string c) ? c : "";
                string name = WireRuleTemplate.Render(rule.Template, new WireRuleContext
                {
                    Anchor = anchor, Potential = potential, Core = core,
                    Graph = graph, Net = net, SourceDevice = rule.SourceDevice ?? "",
                });
                if (name != null) return (name, anchor);
            }
            return (null, null);
        }

        /// <summary>"" — любое; "да"/"нет" — требуемое значение.</summary>
        private static bool Flag(string flag, bool value)
        {
            string f = (flag ?? "").Trim().ToLowerInvariant();
            if (f == "да" || f == "yes" || f == "1") return value;
            if (f == "нет" || f == "no" || f == "0") return !value;
            return true;
        }

        /// <summary>
        /// {Счётчик}: отдельная последовательность на (место опорного конца + текст шаблона вокруг счётчика),
        /// начало — CounterStart (по умолчанию 1); номера, уже выданные другими правилами или стоящие
        /// в узлах без правила, пропускаются. Порядок — по правилу (Order: X — столбцы, Y — строки).
        /// </summary>
        private static void ResolveCounters(List<WireNamingRow> rows, List<Pending> pending, List<WireRule> rules,
                                            HashSet<WireNamingRow> guarded)
        {
            var taken = new HashSet<string>(
                rows.Where(r => r.ProposedName.Length > 0 && !r.ProposedName.Contains(WireRuleTemplate.CounterToken)).Select(r => r.ProposedName), Cmp);
            foreach (var r in rows.Where(r => r.RuleName.Length == 0))
                foreach (var n in r.CurrentName.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries)) taken.Add(n);

            var next = new Dictionary<string, int>(Cmp);
            foreach (var group in pending.GroupBy(p => p.Rule).OrderBy(g => rules.IndexOf(g.Key)))
            {
                bool byRows = string.Equals((group.Key.Order ?? "").Trim(), "Y", StringComparison.OrdinalIgnoreCase);
                // 25.09.2026: сначала провода, которые программа отметит сама (номера подряд с начала), потом остальные.
                var ordered = byRows
                    ? group.OrderBy(p => guarded.Contains(p.Row) ? 1 : 0).ThenBy(p => p.Position.Page).ThenByDescending(p => p.Position.Y).ThenBy(p => p.Position.X)
                    : group.OrderBy(p => guarded.Contains(p.Row) ? 1 : 0).ThenBy(p => p.Position.Page).ThenBy(p => p.Position.X).ThenByDescending(p => p.Position.Y);
                foreach (var p in ordered)
                {
                    string template = p.Row.ProposedName;
                    string key = p.Scope + "|" + template;
                    if (!next.TryGetValue(key, out int n)) n = group.Key.CounterStart ?? 1;
                    string name;
                    do { name = template.Replace(WireRuleTemplate.CounterToken, n.ToString()); n++; } while (taken.Contains(name));
                    next[key] = n;
                    taken.Add(name);
                    p.Row.ProposedName = name;
                }
            }
        }

        private static void SetDefaultStatus(WireNamingRow row, List<string> names)
        {
            bool missing = names.Count == 0 || names.Any(n => n.Length == 0 || n.Contains("?"));
            if (row.RuleName.Length == 0) { row.Status = WireNamingStatus.NoRule; row.Apply = false; }
            else if (!missing && Cmp.Equals(row.CurrentName, row.ProposedName) && row.CurrentName == row.ProposedName)
            { row.Status = WireNamingStatus.Unchanged; row.Apply = false; }
            else if (missing) { row.Status = WireNamingStatus.New; row.Apply = true; }
            else { row.Status = WireNamingStatus.Rename; row.Apply = false; }
        }

        /// <summary>Текущий номер повторяется в другом узле (не общий номер) — строки с новым номером отмечаются.</summary>
        private static void MarkDuplicates(List<WireNamingRow> rows, HashSet<WireNamingRow> guarded)
        {
            var groups = rows.Where(r => r.CurrentName.Length > 0 && !r.CurrentName.Contains(" / "))
                             .GroupBy(r => r.CurrentName, Cmp).Where(g => g.Count() > 1);
            foreach (var group in groups)
            {
                if (group.All(r => r.SharedName && Cmp.Equals(r.ProposedName, group.Key))) continue;
                // 25.09.2026: один номер на узлах с потенциалом (41L+ в 30 узлах) — это общий потенциал, а не дубль.
                // …но только если сам номер похож на потенциал: скопированный 14101 на проводах, которые EPLAN числит на L+,
                // — дубль (живой прогон 1364, листы 40–41).
                if (WireRuleTemplate.LooksLikePotential(group.Key)) continue;
                foreach (var row in group.Where(r => r.Status == WireNamingStatus.Rename && !guarded.Contains(r)))
                {
                    row.Status = WireNamingStatus.Duplicate;
                    row.Apply = true;
                    AddNote(row, $"номер «{group.Key}» сейчас стоит в {group.Count()} узлах");
                }
            }
        }

        /// <summary>Конфликты — как будто применены ВСЕ предложения; тот же WireApplyPlanner, что и при записи.</summary>
        private static void MarkConflicts(List<WireNamingRow> rows)
        {
            var proposals = rows.Where(r => r.Status == WireNamingStatus.New || r.Status == WireNamingStatus.Duplicate
                                            || r.Status == WireNamingStatus.Rename)
                                .ToDictionary(r => r.NetId, r => r.ProposedName);
            var plan = WireApplyPlanner.Plan(rows, proposals);
            foreach (var row in rows)
            {
                if (!plan.Rejected.TryGetValue(row.NetId, out string reason)) continue;
                row.Status = WireNamingStatus.Conflict;
                row.Apply = false;
                AddNote(row, reason);
            }
        }

        private static void AttachCores(List<WireNamingRow> rows, List<CoreChange> changes, Dictionary<string, string> notes)
        {
            var rowByWire = new Dictionary<string, WireNamingRow>();
            foreach (var row in rows)
                foreach (var id in row.WireIds) rowByWire[id] = row;

            foreach (var change in changes)
            {
                if (!rowByWire.TryGetValue(change.WireId, out var row)) continue;
                row.Cores.Add(change);
                if (row.Status != WireNamingStatus.Conflict) row.Apply = true;
            }
            foreach (var kv in notes)
                if (rowByWire.TryGetValue(kv.Key, out var row))
                    AddNote(row, kv.Value);
        }

        /// <summary>Все концы — одно устройство или его части: 1X24 и 1X24, UP1 и UP1-15, 3VP1 и 3VP1-4A1.</summary>
        private static bool IsInternalWiring(List<WireEnd> ends)
        {
            if (ends.Count < 2 || ends.Any(e => (e.Device ?? "").Length == 0)) return false;
            string root = ends.Select(e => e.Device).OrderBy(d => d.Length).First();
            return ends.All(e => (e.Location ?? "") == (ends[0].Location ?? "")
                                 && (Cmp.Equals(e.Device, root) || e.Device.StartsWith(root + "-", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Номер некуда записать (у проводов узла нет точки определения соединения) — не отмечаем сами;
        /// UI отметит такие строки при включении «Ставить новые точки» (AutoWithPoints).</summary>
        private static void MarkWithoutPoints(List<WireNamingRow> rows, IList<WireInfo> wires)
        {
            var byId = (wires ?? new List<WireInfo>()).Where(w => w != null).GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var row in rows)
            {
                if (row.ProposedName.Length == 0 || row.WireIds.Any(id => byId.TryGetValue(id, out var w) && w.HasDefinitionPoint)) continue;
                AddNote(row, "на схеме нет точки номера — запишется с галочкой «Ставить новые точки»");
                if (!row.Apply) continue;
                row.Apply = false;
                row.AutoWithPoints = true;
            }
        }

        private static void AddNote(WireNamingRow row, string note) =>
            row.Note = row.Note.Length == 0 ? note : row.Note + "; " + note;
    }
}
