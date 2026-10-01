using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>
    /// 24.09.2026: выгрузка прочитанных кабелей в формате фикстуры Fixtures/1260-cables.tsv
    /// (No, Name, Type, Cores, Sources, Targets; конец — «МЕСТО|УСТРОЙСТВО», несколько — через «;»).
    /// Выгрузка с копии 1260 = готовая фикстура для самопроверки правил на реальных данных.
    /// </summary>
    public static class CableExport
    {
        public static string ToTsv(IEnumerable<CableInfo> cables)
        {
            var sb = new StringBuilder("No\tName\tType\tCores\tSources\tTargets\n");
            int n = 0;
            foreach (var c in cables ?? Enumerable.Empty<CableInfo>())
                sb.Append(++n).Append('\t').Append(Clean(c.CurrentName)).Append('\t').Append(Clean(c.Type)).Append('\t')
                  .Append(c.CoresTotal).Append('\t').Append(Ends(c.Sources)).Append('\t').Append(Ends(c.Targets)).Append('\n');
            return sb.ToString();
        }

        private static string Ends(List<CableEnd> ends) =>
            string.Join(";", (ends ?? new List<CableEnd>()).Select(e => Clean(e.Location) + "|" + Clean(e.Device)));

        public static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
