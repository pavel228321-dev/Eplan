using System.Text.RegularExpressions;

namespace EplanCipMvp.Core
{
    /// <summary>
    /// Разбор тега устройства ("L1V12", "GS3", "L2QT1") на составные части — по
    /// схеме P&ID CIP1_1_.pdf ("L{линия}{Тип}{номер}") и по образцу тегов
    /// донора-1166 ("GS1".."GS4" — без номера линии, общие для станции).
    ///
    /// 11.09.2026: вынесено отдельно от VfdPageCopier.LeadingLetters/AnyDigits —
    /// тем НЕ заменяет их (VfdPageCopier.cs сознательно не трогаем, см. README/план
    /// про расширение на клапаны/датчики) — нужен для НОВОГО кода
    /// (DeviceGroupPageCopier), который работает с тегами из спецификации 1364, а
    /// не с MAINNAME донора (та же задача, но другой источник строки).
    /// </summary>
    public static class TagParser
    {
        private static readonly Regex Pattern = new Regex(@"^(L\d+)?([A-Za-z]+)(\d+)$");

        /// <summary>"L1V12" -> "V", "GS3" -> "GS". Буквенный код типа устройства —
        /// тот же принцип, что LeadingLetters в VfdPageCopier (весь буквенный
        /// пробег целиком, не одна буква — иначе "VC" и "V" не различить).</summary>
        public static string TypeCode(string tag)
        {
            var match = Pattern.Match(tag ?? "");
            return match.Success ? match.Groups[2].Value : null;
        }

        /// <summary>"L1V12" -> "L1", "GS3" -> null (без номера линии — общий для станции,
        /// как GS1..GS4 у донора 1166).</summary>
        public static string LinePrefix(string tag)
        {
            var match = Pattern.Match(tag ?? "");
            if (!match.Success) return null;
            var value = match.Groups[1].Value;
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>"L1V12" -> "12", "GS3" -> "3". Номер устройства в теге — то, что
        /// становится FUNC_COUNTER на копии страницы (в отличие от VFD, где COUNTER
        /// донора не трогался — см. DeviceGroupPageCopier про плотность >1/лист).</summary>
        public static string CounterDigits(string tag)
        {
            var match = Pattern.Match(tag ?? "");
            return match.Success ? match.Groups[3].Value : null;
        }
    }
}
