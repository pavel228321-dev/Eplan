using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EplanCipMvp.Core
{
    /// <summary>
    /// Естественная сортировка строк с числами: "L1V2" раньше "L1V10"
    /// (обычный string-компаратор даёт наоборот, т.к. сравнивает посимвольно).
    /// </summary>
    public class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        private static readonly Regex ChunkPattern = new Regex(@"\d+|\D+", RegexOptions.Compiled);

        public int Compare(string a, string b)
        {
            if (a == null || b == null)
                return string.Compare(a, b, StringComparison.Ordinal);

            var chunksA = ChunkPattern.Matches(a);
            var chunksB = ChunkPattern.Matches(b);
            int len = Math.Min(chunksA.Count, chunksB.Count);

            for (int i = 0; i < len; i++)
            {
                string ca = chunksA[i].Value;
                string cb = chunksB[i].Value;

                bool numA = char.IsDigit(ca[0]);
                bool numB = char.IsDigit(cb[0]);

                int cmp;
                if (numA && numB)
                {
                    // Сравниваем как числа (учитывая возможные ведущие нули и большую длину)
                    cmp = ca.TrimStart('0').Length.CompareTo(cb.TrimStart('0').Length);
                    if (cmp == 0)
                        cmp = string.CompareOrdinal(ca.TrimStart('0'), cb.TrimStart('0'));
                }
                else
                {
                    cmp = string.CompareOrdinal(ca, cb);
                }

                if (cmp != 0)
                    return cmp;
            }

            return chunksA.Count.CompareTo(chunksB.Count);
        }
    }
}
