using System;
using System.Collections.Generic;
using System.Linq;

namespace EplanCipMvp.Core.WireNaming
{
    public class WireNet
    {
        public string Id { get; set; } = "";
        /// <summary>Провода узла по порядку: лист, X, сверху вниз. Wires[0] задаёт позицию узла.</summary>
        public List<WireInfo> Wires { get; set; } = new List<WireInfo>();
    }

    /// <summary>Узел = провода, связанные общими контактами (PinKey). Узлы упорядочены по первому проводу.</summary>
    public static class WireNets
    {
        public static List<WireNet> Build(IList<WireInfo> wires)
        {
            var list = (wires ?? new List<WireInfo>()).Where(w => w != null)
                .OrderBy(w => w.Page).ThenBy(w => w.X).ThenByDescending(w => w.Y).ToList();
            var parent = Enumerable.Range(0, list.Count).ToArray();
            int Find(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }

            var firstByPin = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Count; i++)
                foreach (var key in new[] { list[i].Start?.PinKey, list[i].End?.PinKey })
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    if (firstByPin.TryGetValue(key, out int j)) parent[Find(i)] = Find(j);
                    else firstByPin[key] = i;
                }

            return Enumerable.Range(0, list.Count).GroupBy(Find)
                .OrderBy(g => g.Min())
                .Select((g, n) => new WireNet { Id = "n" + n, Wires = g.OrderBy(i => i).Select(i => list[i]).ToList() })
                .ToList();
        }

        /// <summary>Разные концы узла (по PinKey), в порядке проводов: начало, затем конец.</summary>
        public static List<WireEnd> Ends(WireNet net) =>
            net.Wires.SelectMany(w => new[] { w.Start, w.End })
               .Where(e => e != null && ((e.Device ?? "").Length > 0 || (e.Terminal ?? "").Length > 0))
               .GroupBy(e => (e.PinKey ?? "").Length > 0 ? e.PinKey : $"{e.Location}-{e.Device}:{e.Terminal}",
                        StringComparer.OrdinalIgnoreCase)
               .Select(g => g.First())
               .ToList();
    }
}
