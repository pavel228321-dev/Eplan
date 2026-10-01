using System;
using System.Collections.Generic;
using System.Linq;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>
    /// Назначает свободные жилы кабеля его проводам без жилы (как в 1260), первое сработавшее:
    /// (1) жила с обозначением, равным клемме конца (сначала конец в поле: -WGS101 к клеммам 1/3/4 -> жилы 1/3/4);
    /// (2) клемма/потенциал PE -> жила GNYE (GN/YE); (3) следующая свободная жила, кроме GNYE, по порядку артикула.
    /// Провода кабеля — по порядку: лист, X, сверху вниз.
    /// </summary>
    public static class CoreAssigner
    {
        public const string FieldLocation = "+FIELD";

        public static List<CoreChange> Assign(IList<WireInfo> wires, IList<CableCores> cables, IDictionary<string, string> notes)
        {
            var changes = new List<CoreChange>();
            var free = (cables ?? new List<CableCores>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Cable))
                .GroupBy(c => c.Cable, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key,
                              g => g.First().FreeCores.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList(),
                              StringComparer.OrdinalIgnoreCase);

            var pending = (wires ?? new List<WireInfo>())
                .Where(w => w != null && (w.Cable ?? "").Length > 0 && (w.CurrentCore ?? "").Trim().Length == 0)
                .OrderBy(w => w.Page).ThenBy(w => w.X).ThenByDescending(w => w.Y);

            foreach (var wire in pending)
            {
                if (!free.TryGetValue(wire.Cable, out var cores))
                {
                    notes[wire.Id] = $"у кабеля {wire.Cable} нет жил в артикуле — жилу не назначить";
                    continue;
                }
                string core = Pick(wire, cores);
                if (core == null)
                {
                    notes[wire.Id] = $"у кабеля {wire.Cable} не хватает свободных жил";
                    continue;
                }
                cores.Remove(core);
                changes.Add(new CoreChange { WireId = wire.Id, Cable = wire.Cable, Core = core });
            }
            return changes;
        }

        private static string Pick(WireInfo wire, List<string> cores)
        {
            var terminals = new[] { wire.Start, wire.End }
                .Where(e => e != null && (e.Terminal ?? "").Trim().Length > 0)
                .OrderBy(e => WildcardMask.Matches(FieldLocation, e.Location ?? "") ? 0 : 1)
                .Select(e => e.Terminal.Trim())
                .ToList();

            string byTerminal = terminals
                .Select(t => cores.FirstOrDefault(c => string.Equals(c, t, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(c => c != null);
            if (byTerminal != null) return byTerminal;

            bool protective = terminals.Any(t => string.Equals(t, "PE", StringComparison.OrdinalIgnoreCase))
                              || string.Equals((wire.Potential ?? "").Trim(), "PE", StringComparison.OrdinalIgnoreCase);
            if (protective) return cores.FirstOrDefault(IsProtective);
            return cores.FirstOrDefault(c => !IsProtective(c));
        }

        public static bool IsProtective(string core) =>
            string.Equals((core ?? "").Replace("/", "").Replace("-", "").Trim(), "GNYE", StringComparison.OrdinalIgnoreCase);
    }
}
