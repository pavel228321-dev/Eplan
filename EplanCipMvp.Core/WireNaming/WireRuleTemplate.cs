using System.Linq;
using System.Text.RegularExpressions;

namespace EplanCipMvp.Core.WireNaming
{
    public class WireRuleContext
    {
        public WireEnd Anchor { get; set; }
        public string Potential { get; set; } = "";
        /// <summary>Жила кабеля на проводе опорного конца (текущая или назначаемая); пусто — не в кабеле.</summary>
        public string Core { get; set; } = "";
        /// <summary>01.10.2026: для {Потенциал:источник} — граф устройств/узлов (строится один раз на
        /// Preview в WireNamingEngine) и текущий узел, в котором рендерится токен.</summary>
        public WireNetGraph Graph { get; set; }
        public WireNet Net { get; set; }
        /// <summary>Маска устройств-источников (WireRule.SourceDevice) для {Потенциал:источник}.</summary>
        public string SourceDevice { get; set; } = "";
    }

    /// <summary>
    /// Подстановки по «опорному» концу: {Устройство}, {Устройство:от_цифры}, {Клемма}, {Клемма:2},
    /// {Клемма:буквы}, {МодульПЛК:цифры}, {Группа}, {Номер:последняя}, {Потенциал}, {Счётчик}.
    /// Пустая/неизвестная подстановка -> null (правило не сработало). {Счётчик} (не больше одного)
    /// остаётся маркером CounterToken — его разрешает WireNamingEngine, видя все узлы.
    /// </summary>
    public static class WireRuleTemplate
    {
        public const string CounterToken = "{#}";
        private static readonly Regex Placeholder = new Regex(@"\{([^{}]+)\}");

        public static string Render(string template, WireRuleContext ctx)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;
            var anchor = ctx?.Anchor ?? new WireEnd();
            string potential = ctx?.Potential ?? "";
            string core = ctx?.Core ?? "";
            bool failed = false;
            int counters = 0;
            string result = Placeholder.Replace(template.Trim(), m =>
            {
                string token = m.Groups[1].Value.Trim();
                if (token == "Счётчик") { counters++; return CounterToken; }
                string value = Resolve(token, anchor, potential, core, ctx);
                if (string.IsNullOrEmpty(value)) { failed = true; return ""; }
                return value;
            });
            return failed || counters > 1 ? null : result;
        }

        /// <summary>true — в шаблоне нет подстановок (номер одинаков для всех узлов, например PE).</summary>
        public static bool IsConstant(string template) => !Placeholder.IsMatch(template ?? "");

        /// <summary>Полный числовой хвост DT устройства после буквенного кода: сначала отбрасывается
        /// нецифро-буквенный хвост (+/-), затем берутся цифры с конца; группа шкафа в номер не входит
        /// (2QF11 -> "11", 2QF30 -> "30", 2G1 -> "1", BV21+ -> "21"). Нет цифр в хвосте -> пусто.</summary>
        public static string OwnNumber(string device)
        {
            string trimmed = CableNaming.CableRuleTemplate.TrimTail(device ?? "");
            return new string(trimmed.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        }

        private static string Resolve(string token, WireEnd a, string potential, string core, WireRuleContext ctx)
        {
            // «+» в конце DT в номер не входит (BV21+ -> BV21-1), как и в имени кабеля.
            string device = CableNaming.CableRuleTemplate.TrimTail(a.Device);
            string terminal = (a.Terminal ?? "").Trim();
            switch (token)
            {
                case "Устройство": return device;
                case "Устройство:от_цифры": return FromFirstDigit(device);
                case "Клемма": return terminal;
                case "Клемма:2": return terminal.Length > 0 && terminal.All(char.IsDigit) ? terminal.PadLeft(2, '0') : "";
                case "Клемма:буквы":
                    // 25.09.2026: клеммы без букв и цифр («+», «-» у датчиков 4…20 мА в 1364) — берём жилу кабеля
                    // (на схеме 1364: T6LT1-1, T6LT1-2); нет жилы — клемма как есть.
                    if (!terminal.Any(char.IsLetterOrDigit)) return core.Trim().Length > 0 ? core.Trim() : terminal;
                    string letters = new string(terminal.TakeWhile(char.IsLetter).ToArray());
                    return letters.Length > 0 ? letters : terminal;
                case "МодульПЛК:цифры": return new string(device.Where(char.IsDigit).ToArray());
                case "Группа": return new string(device.TakeWhile(char.IsDigit).ToArray());
                case "Группа:2":   // 1QF01 -> 01 (в 1364 у ПЧ1 и 01QF01, и 1QF01)
                    string g = new string(device.TakeWhile(char.IsDigit).ToArray());
                    return g.Length == 0 ? "" : g.PadLeft(2, '0');
                case "Номер:последняя":
                    string tail = OwnNumber(a.Device);
                    return tail.Length > 0 ? tail.Substring(tail.Length - 1) : "";
                case "Потенциал": return IsGenericPotential(potential) ? "" : potential.Trim();
                case "Потенциал:источник": return ResolveFromSource(potential, ctx);
                default: return null;
            }
        }

        /// <summary>25.09.2026: EPLAN отдаёт у многих проводов общее имя потенциала (M, L+, L, N, U…), а на схеме
        /// виден 1M/30L+/1L1 — такое имя номером провода не годится (запись на 1364 испортила 1M -> M).</summary>
        public static bool IsGenericPotential(string potential)
        {
            string p = (potential ?? "").Trim().ToUpperInvariant();
            return p.Length == 0 || new[] { "L", "L+", "L-", "M", "N", "PE", "PEN", "SH", "U", "V", "W", "+", "-" }.Contains(p);
        }

        /// <summary>Номер вида 1M, 31L+, 1L1, 1N11, UM, PE — это имя потенциала, такие провода не перенумеровываем.</summary>
        public static bool LooksLikePotential(string name)
        {
            string n = (name ?? "").Trim();
            // Групповой номер силовой цепи 1260 (32L41, 03L11: 2 цифры + L + 2 цифры) — не потенциал.
            if (Regex.IsMatch(n, @"^\d{2}L\d{2}$")) return false;
            return Regex.IsMatch(n, @"^(\d*(L\d*[+-]?|M|N\d*|PEN?|UM)|L\d*[+-]?|U|V|W)$", RegexOptions.IgnoreCase);
        }

        /// <summary>{Потенциал:источник}: достраивает номер, когда буква уже есть у EPLAN, а числа нет —
        /// см. WireNetGraph.ResolveSourceNumber. Специфичный или пустой потенциал — токен не трогает.</summary>
        private static string ResolveFromSource(string potential, WireRuleContext ctx)
        {
            string p = (potential ?? "").Trim();
            if (p.Length == 0 || !IsGenericPotential(p)) return "";
            return ctx?.Graph?.ResolveSourceNumber(ctx.Net, ctx.SourceDevice, p) ?? "";
        }

        private static string FromFirstDigit(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i])) return s.Substring(i);
            return "";
        }
    }
}
