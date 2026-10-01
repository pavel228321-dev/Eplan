using System.Linq;
using System.Text.RegularExpressions;

namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>Значения, доступные шаблону: устройство цели и места установки концов.</summary>
    public class RuleContext
    {
        public string Device { get; set; } = "";
        public string SourceLocation { get; set; } = "";
        public string TargetLocation { get; set; } = "";
    }

    /// <summary>
    /// Подстановки: {Устройство}, {Устройство:от_цифры}, {МестоИсточника}, {МестоЦели},
    /// {ШкафИсточника}, {ШкафИсточника:N}, {.N}. Любая пустая/неизвестная подстановка
    /// (кроме {.N}) -> null, т.е. правило не сработало. {.N} остаётся маркером
    /// SuffixToken — его разрешает CableNamingEngine, видя все кабели сразу.
    /// </summary>
    public static class CableRuleTemplate
    {
        public const string SuffixToken = "{.N}";
        private static readonly Regex Placeholder = new Regex(@"\{([^{}]+)\}");

        public static string Render(string template, RuleContext ctx)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;
            ctx = ctx ?? new RuleContext();
            bool failed = false;
            string result = Placeholder.Replace(template.Trim(), m =>
            {
                string token = m.Groups[1].Value.Trim();
                if (token == ".N") return SuffixToken;
                string value = Resolve(token, ctx);
                if (string.IsNullOrEmpty(value)) { failed = true; return ""; }
                return value;
            });
            return failed ? null : result;
        }

        public static string LastSegment(string location)
        {
            if (string.IsNullOrEmpty(location)) return "";
            string trimmed = location.Trim().TrimStart('+', '=');
            int dot = trimmed.LastIndexOf('.');
            return dot >= 0 ? trimmed.Substring(dot + 1) : trimmed;
        }

        private static string Resolve(string token, RuleContext ctx)
        {
            string name = token;
            string arg = null;
            int colon = token.IndexOf(':');
            if (colon >= 0)
            {
                name = token.Substring(0, colon).Trim();
                arg = token.Substring(colon + 1).Trim();
            }

            switch (name)
            {
                case "Устройство":
                    // 24.09.2026: «+» и прочие не буквы/цифры в конце DT в имя не входят (1260: BM02VP05+ -> WBM02VP05).
                    string device = TrimTail(ctx.Device);
                    if (arg == null) return device;
                    return arg == "от_цифры" ? FromFirstDigit(device) : null;
                case "МестоИсточника":
                    return arg == null ? LastSegment(ctx.SourceLocation) : null;
                case "МестоЦели":
                    return arg == null ? LastSegment(ctx.TargetLocation) : null;
                case "ШкафИсточника":
                    string digits = new string(LastSegment(ctx.SourceLocation).Where(char.IsDigit).ToArray());
                    if (arg == null) return digits;
                    if (!int.TryParse(arg, out int n) || n <= 0) return null;
                    return digits.Length <= n ? digits : digits.Substring(digits.Length - n);
                default:
                    return null;
            }
        }

        public static string TrimTail(string s)
        {
            s = s ?? "";
            int end = s.Length;
            while (end > 0 && !char.IsLetterOrDigit(s[end - 1])) end--;
            return s.Substring(0, end);
        }

        private static string FromFirstDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i])) return s.Substring(i);
            return "";
        }
    }
}
