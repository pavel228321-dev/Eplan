using System.Collections.Generic;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>Конец провода: место установки (+CP.E01), DT устройства без "-" (1A1.1) и клемма (1, U1, PE).</summary>
    public class WireEnd
    {
        public string Location { get; set; } = "";
        public string Device { get; set; } = "";
        public string Terminal { get; set; } = "";
        /// <summary>Ключ контакта (функция EPLAN + вывод). Провода с общим контактом — один узел.
        /// У клеммы две стороны — это два разных ключа, поэтому 32L47 и 32M04-U на -1X31:1 — разные узлы.</summary>
        public string PinKey { get; set; } = "";
    }

    public class WireInfo
    {
        public string Id { get; set; } = "";
        /// <summary>Порядковый номер листа и середина провода на листе (Y растёт вверх, как в EPLAN).</summary>
        public int Page { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>Номер провода (свойство 31011); пусто — не задан.</summary>
        public string CurrentName { get; set; } = "";
        /// <summary>Обозначение жилы (31004); пусто — не назначена или провод не в кабеле.</summary>
        public string CurrentCore { get; set; } = "";
        public string Potential { get; set; } = "";
        /// <summary>Имя кабеля, если провод — его жила; иначе пусто.</summary>
        public string Cable { get; set; } = "";
        /// <summary>Есть ли у провода точка определения соединения (куда пишется номер). Нет данных — считаем, что есть.</summary>
        public bool HasDefinitionPoint { get; set; } = true;
        public WireEnd Start { get; set; } = new WireEnd();
        public WireEnd End { get; set; } = new WireEnd();
    }

    /// <summary>Свободные (не назначенные) жилы кабеля из артикула, в порядке артикула.</summary>
    public class CableCores
    {
        public string Cable { get; set; } = "";
        public List<string> FreeCores { get; set; } = new List<string>();
    }

    public class WireRule
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        /// <summary>Маски «опорного» конца (место, устройство, клемма); несколько масок — через ";".</summary>
        public string Location { get; set; } = "";
        public string Device { get; set; } = "";
        public string Terminal { get; set; } = "";
        /// <summary>25.09.2026: маски устройств, при которых правило НЕ срабатывает (любой конец узла), через ";".</summary>
        public string ExcludeDevice { get; set; } = "";
        /// <summary>Маска места установки любого ДРУГОГО конца узла.</summary>
        public string OtherLocation { get; set; } = "";
        /// <summary>"" — любой; "да" — у узла есть имя потенциала; "нет" — нет.</summary>
        public string Potential { get; set; } = "";
        /// <summary>"" — любой; "да" — в узле есть жила кабеля; "нет" — нет.</summary>
        public string InCable { get; set; } = "";
        /// <summary>01.10.2026: маски устройств-"источников" (автомат/БП и т.п.), на которых обход вверх
        /// для {Потенциал:источник} останавливается. Пусто — ничего не подходит, токен не сработает.</summary>
        public string SourceDevice { get; set; } = "";
        /// <summary>Порядок счётчика: "X" — по столбцам (слева направо, сверху вниз), "Y" — по строкам.</summary>
        public string Order { get; set; } = "X";
        public int? CounterStart { get; set; }
        /// <summary>25.09.2026: "" или "да" — строки по этому правилу отмечаются сами; "нет" — только предлагаются.</summary>
        public string AutoCheck { get; set; } = "";
        public string Template { get; set; } = "";
    }

    public static class WireNamingStatus
    {
        public const string NoRule = "Нет правила";
        public const string Unchanged = "Без изменений";
        public const string New = "Новый номер";
        public const string Duplicate = "Дубль в проекте";
        public const string Rename = "Перенумерация";
        public const string Conflict = "Конфликт";
    }

    public class CoreChange
    {
        public string WireId { get; set; } = "";
        public string Cable { get; set; } = "";
        public string Core { get; set; } = "";
    }

    /// <summary>Строка предпросмотра = узел: все его провода получают один номер.</summary>
    public class WireNamingRow
    {
        public string NetId { get; set; } = "";
        public int Page { get; set; }
        public List<string> WireIds { get; set; } = new List<string>();
        public List<WireEnd> Ends { get; set; } = new List<WireEnd>();
        public string Potential { get; set; } = "";
        public string Cable { get; set; } = "";
        /// <summary>Текущие номера проводов узла через " / " (обычно один).</summary>
        public string CurrentName { get; set; } = "";
        public string RuleName { get; set; } = "";
        public string ProposedName { get; set; } = "";
        /// <summary>Номер может законно повторяться в других узлах (имя потенциала, постоянный шаблон вроде PE).</summary>
        public bool SharedName { get; set; }
        /// <summary>25.09.2026: провод потенциала (есть имя потенциала или номер вида 1M/31L+/1L1) — галочку сами не ставим.</summary>
        public bool OnPotential { get; set; }
        /// <summary>25.09.2026: на схеме нет точки номера — строка отмечается сама только при «Ставить новые точки».</summary>
        public bool AutoWithPoints { get; set; }
        public string Status { get; set; } = "";
        public string Note { get; set; } = "";
        public bool Apply { get; set; }
        public List<CoreChange> Cores { get; set; } = new List<CoreChange>();
    }
}
