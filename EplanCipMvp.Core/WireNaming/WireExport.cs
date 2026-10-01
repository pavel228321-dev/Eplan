using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>
    /// 24.09.2026: выгрузка прочитанных проводов (всё, что видит движок правил) — чтобы сверить правила
    /// с реальным проектом (копия 1260) без EPLAN. Конец провода — «МЕСТО|УСТРОЙСТВО|КЛЕММА|КЛЮЧ_КОНТАКТА»,
    /// лист — с 1. В конце строки CORES: кабель и его свободные жилы через «;».
    /// </summary>
    public static class WireExport
    {
        public static string ToTsv(IEnumerable<WireInfo> wires, IEnumerable<CableCores> cables)
        {
            var sb = new StringBuilder("Id\tPage\tX\tY\tName\tCore\tPotential\tCable\tStart\tEnd\tPoint\n");
            foreach (var w in wires ?? Enumerable.Empty<WireInfo>())
                sb.Append(string.Join("\t", new[]
                {
                    CableExport.Clean(w.Id), (w.Page + 1).ToString(CultureInfo.InvariantCulture),
                    w.X.ToString(CultureInfo.InvariantCulture), w.Y.ToString(CultureInfo.InvariantCulture),
                    CableExport.Clean(w.CurrentName), CableExport.Clean(w.CurrentCore), CableExport.Clean(w.Potential),
                    CableExport.Clean(w.Cable), End(w.Start), End(w.End), w.HasDefinitionPoint ? "1" : "0",
                })).Append('\n');
            foreach (var c in cables ?? Enumerable.Empty<CableCores>())
                sb.Append("CORES\t").Append(CableExport.Clean(c.Cable)).Append('\t')
                  .Append(string.Join(";", c.FreeCores.Select(CableExport.Clean))).Append('\n');
            return sb.ToString();
        }

        /// <summary>Обратно из выгрузки (строки проводов; CORES пропускаются). Лист в файле — с 1.</summary>
        public static List<WireInfo> ParseTsv(string tsv)
        {
            var wires = new List<WireInfo>();
            foreach (var line in (tsv ?? "").TrimStart('\uFEFF').Split('\n').Skip(1))
            {
                var f = line.TrimEnd('\r').Split('\t');
                if (f.Length < 10 || f[0] == "CORES") continue;
                double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
                double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
                int.TryParse(f[1], out int page);
                wires.Add(new WireInfo
                {
                    Id = f[0], Page = page - 1, X = x, Y = y, CurrentName = f[4], CurrentCore = f[5], Potential = f[6], Cable = f[7],
                    Start = ParseEnd(f[8]), End = ParseEnd(f[9]),
                    HasDefinitionPoint = f.Length < 11 || f[10].Trim() != "0",
                });
            }
            return wires;
        }

        /// <summary>
        /// 25.09.2026: откат номеров. Провод из копии и текущий провод — один и тот же, если совпадают контакты
        /// на обоих концах (в любом порядке): после записи EPLAN пересоздаёт соединения, их id меняются.
        /// Результат: id текущего провода -> номер из копии (в т.ч. пустой); совпадающие и не найденные не входят.
        /// </summary>
        public static Dictionary<string, string> RestorePlan(IList<WireInfo> current, IList<WireInfo> backup)
        {
            var old = new Dictionary<string, string>();
            var ambiguous = new HashSet<string>();
            foreach (var w in backup ?? new List<WireInfo>())
            {
                string key = PinPairKey(w);
                if (key == null) continue;
                if (old.ContainsKey(key)) ambiguous.Add(key); else old[key] = w.CurrentName ?? "";
            }
            var plan = new Dictionary<string, string>();
            foreach (var w in current ?? new List<WireInfo>())
            {
                string key = PinPairKey(w);
                if (key == null || ambiguous.Contains(key) || !old.TryGetValue(key, out string name)) continue;
                if (!string.Equals(name, w.CurrentName ?? "", System.StringComparison.Ordinal)) plan[w.Id] = name;
            }
            return plan;
        }

        /// <summary>Ключ провода по контактам концов (без учёта направления); null — у провода нет контактов.</summary>
        public static string PinPairKey(WireInfo w)
        {
            string a = w?.Start?.PinKey ?? "", b = w?.End?.PinKey ?? "";
            if (a.Length == 0 && b.Length == 0) return null;
            return string.CompareOrdinal(a, b) <= 0 ? a + "||" + b : b + "||" + a;
        }

        private static WireEnd ParseEnd(string s)
        {
            var f = (s ?? "").Split('|');
            return new WireEnd { Location = f[0], Device = f.Length > 1 ? f[1] : "", Terminal = f.Length > 2 ? f[2] : "", PinKey = f.Length > 3 ? f[3] : "" };
        }

        private static string End(WireEnd e)
        {
            e = e ?? new WireEnd();
            return string.Join("|", CableExport.Clean(e.Location), CableExport.Clean(e.Device), CableExport.Clean(e.Terminal), CableExport.Clean(e.PinKey));
        }
    }
}
