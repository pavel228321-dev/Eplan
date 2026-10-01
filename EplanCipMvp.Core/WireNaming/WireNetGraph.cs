using System.Collections.Generic;
using System.Linq;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>
    /// Индекс «устройство (Location+Device) -> его клеммы -> узел, подключённый к клемме», построенный
    /// один раз на весь проект. Нужен, чтобы достроить номер потенциала, которому EPLAN дал только букву
    /// (L, N, M, L+) без числа: у опорного узла ищем конец на выходной клемме устройства-источника
    /// (автомат/БП, маска SourceDevice) и смотрим узел на парной входной клемме того же устройства.
    /// Один шаг, без рекурсии — см. docs/superpowers/specs/2026-10-01-potential-number-generation-design.md.
    /// </summary>
    public class WireNetGraph
    {
        /// <summary>Выходные клеммы источника тока (автомат/контактор/ПЧ) -> парная входная клемма —
        /// как в правиле «Силовая цепь группы» (2;4;6 — выход, 1;3;5 — вход).</summary>
        private static readonly Dictionary<string, string> PairedInput = new Dictionary<string, string>
        {
            ["2"] = "1", ["4"] = "3", ["6"] = "5",
        };

        private readonly Dictionary<(string Location, string Device), Dictionary<string, WireNet>> _byDevice =
            new Dictionary<(string, string), Dictionary<string, WireNet>>();

        /// <summary>Остальной код (WireNets.cs — firstByPin/PinKey; WireNamingEngine.cs — Cmp) трактует
        /// Location/Device как case-insensitive — нормализуем регистр перед использованием как ключ,
        /// иначе один и тот же физический конец с разным регистром на двух концах тихо не совпадёт.</summary>
        private static (string, string) Key(string location, string device) =>
            ((location ?? "").Trim().ToUpperInvariant(), (device ?? "").Trim().ToUpperInvariant());

        public WireNetGraph(IList<WireNet> nets)
        {
            foreach (var net in nets ?? new List<WireNet>())
                foreach (var end in WireNets.Ends(net))
                {
                    string device = (end.Device ?? "").Trim();
                    string terminal = (end.Terminal ?? "").Trim();
                    if (device.Length == 0 || terminal.Length == 0) continue;
                    var key = Key(end.Location, device);
                    if (!_byDevice.TryGetValue(key, out var byTerminal))
                        _byDevice[key] = byTerminal = new Dictionary<string, WireNet>(System.StringComparer.OrdinalIgnoreCase);
                    byTerminal[terminal] = net;
                }
        }

        /// <summary>Готовый номер потенциала (буква уже известна и подставляется как есть) для узла net,
        /// или null — среди его концов нет устройства-источника на выходной клемме, или вход этого
        /// устройства ещё сам без специфичного имени (не гадаем).</summary>
        public string ResolveSourceNumber(WireNet net, string sourceDeviceMask, string letter)
        {
            foreach (var end in WireNets.Ends(net))
            {
                string outTerminal = (end.Terminal ?? "").Trim();
                if (!PairedInput.ContainsKey(outTerminal)) continue;
                if (!WildcardMask.MatchesAny(sourceDeviceMask, end.Device ?? "")) continue;

                string own = WireRuleTemplate.OwnNumber(end.Device);
                if (own.Length == 0) continue;

                var key = Key(end.Location, end.Device);
                if (!_byDevice.TryGetValue(key, out var byTerminal)
                    || !byTerminal.TryGetValue(PairedInput[outTerminal], out var upstream))
                    return own + letter; // входной клеммы нет провода — верх цепи, сырой источник

                string specific = FirstSpecificPotential(upstream);
                if (specific.Length == 0) return null; // вход есть, но его потенциал ещё не посчитан — не гадаем
                return LeadingDigits(specific) + letter + own;
            }
            return null;
        }

        private static string FirstSpecificPotential(WireNet net) =>
            net.Wires.Select(w => (w.Potential ?? "").Trim())
               .FirstOrDefault(p => p.Length > 0 && !WireRuleTemplate.IsGenericPotential(p)) ?? "";

        private static string LeadingDigits(string potential) =>
            new string((potential ?? "").TakeWhile(char.IsDigit).ToArray());
    }
}
