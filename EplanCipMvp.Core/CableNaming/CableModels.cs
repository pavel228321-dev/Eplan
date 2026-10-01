using System.Collections.Generic;

namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>Один конец кабеля: место установки (+CP.ET101) и DT устройства без "-" (2X24).</summary>
    public class CableEnd
    {
        public string Location { get; set; } = "";
        public string Device { get; set; } = "";
    }

    public class CableInfo
    {
        public string Id { get; set; } = "";
        /// <summary>DT кабеля без места установки (WC04VP01), пусто — если не задан.</summary>
        public string CurrentName { get; set; } = "";
        public string CableLocation { get; set; } = "";
        public string Type { get; set; } = "";
        public int CoresTotal { get; set; }
        public List<CableEnd> Sources { get; set; } = new List<CableEnd>();
        public List<CableEnd> Targets { get; set; } = new List<CableEnd>();
    }

    public class CableRule
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string SourceLocation { get; set; } = "";
        public string TargetLocation { get; set; } = "";
        public string TypeContains { get; set; } = "";
        public int? CoresTotal { get; set; }
        public string TargetDevice { get; set; } = "";
        public string Template { get; set; } = "";
    }

    public static class CableNamingStatus
    {
        public const string NoRule = "Нет правила";
        public const string Unchanged = "Без изменений";
        public const string New = "Новое имя";
        public const string Rename = "Переименование";
        public const string Conflict = "Конфликт";
    }

    public class CableNamingRow
    {
        public CableInfo Cable { get; set; }
        public string RuleName { get; set; } = "";
        public string ProposedName { get; set; } = "";
        public string Status { get; set; } = "";
        public string Note { get; set; } = "";
        public bool Apply { get; set; }
    }
}
