using System.Linq;
using System.Text.RegularExpressions;

namespace EplanCipMvp.Core.CableNaming
{
    public static class WildcardMask
    {
        /// <summary>Пустая маска совпадает со всем; иначе совпадение целиком, * и ?, # — одна цифра
        /// (24.09.2026), @ — одна буква (25.09.2026), без учёта регистра.</summary>
        public static bool Matches(string mask, string value)
        {
            if (string.IsNullOrWhiteSpace(mask)) return true;
            if (value == null) return false;
            string pattern = "^" + Regex.Escape(mask.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".").Replace("\\#", "#").Replace("#", "[0-9]").Replace("@", "\\p{L}") + "$";
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Несколько масок через ";" — совпадение с любой; пустая строка совпадает со всем.</summary>
        public static bool MatchesAny(string masks, string value)
        {
            if (string.IsNullOrWhiteSpace(masks)) return true;
            return masks.Split(';').Where(m => m.Trim().Length > 0).Any(m => Matches(m, value));
        }
    }
}
