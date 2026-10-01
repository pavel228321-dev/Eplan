namespace EplanCipMvp.Core
{
    /// <summary>
    /// Реально заказанный состав силового контура на один насос с ПЧ — НЕ гипотеза
    /// (в отличие от ArticleDefault/DeviceDefaultsSettings, которые про TBD-позиции).
    /// Взято из спецификаций заказа 1364_03 (НКУ) и 1364_04 (ПЧ) — см.
    /// docs/1364-order-specs.md. Единый источник истины на все 5 контуров:
    /// количество отличается по объекту (см. VfdCircuitBuilder), а состав — одинаковый.
    /// </summary>
    public class VfdComponentDefaults
    {
        public string VfdDeviceType { get; set; } = "Насос центробежный с ПЧ";

        public string VfdVendor { get; set; } = "Danfoss";
        public string VfdArticle { get; set; } = "FC-051P11KT4E20H3BXCXXXSXXX";

        /// <summary>Явно указано в самой спецификации заказчика: "Официально данная
        /// серия перешла в статус устаревшего оборудования (Limited phase / discontinued)".
        /// Стоит поднять с заказчиком/Aprotec до заказа, не после.</summary>
        public bool VfdIsDiscontinued { get; set; } = true;

        public string VfdNote { get; set; } =
            "VLT Micro Drive FC 51, 11 кВт, 23А, 380В, 3ф. Серия снята с производства " +
            "(Limited phase/discontinued) — уточнить актуальность перед заказом.";

        public string ControlPanelArticle { get; set; } = "LCP12";

        /// <summary>10.09.2026: у донора (1260) мотор на 7,5кВт, у 1364 — 11кВт
        /// (см. "Перечень устройств", столбец Power, L1M1..L5M1). Донорская подпись
        /// "7,5kW" копируется дословно вместе со страницей и не пересчитывается —
        /// ставим правильное значение поверх на моторе и на блоке самого ПЧ. Стиль
        /// ("11kW", без пробела, латиницей) — как у донора ("7,5kW"), не "11 кВт" —
        /// чтобы визуально не выделяться на чертеже.</summary>
        public string MotorPowerLabel { get; set; } = "11kW";

        public string BreakerVendor { get; set; } = "Schneider Electric";
        public string BreakerArticle { get; set; } = "GV3P32";
        public string AuxContactArticle { get; set; } = "GVAN11";
        public string AuxContactPurpose { get; set; } =
            "Диагностика состояния силовой цепи питания ПЧ — требование архитектуры ПО.";

        // ЗАКРЫТО 09.09.2026 (было открытым вопросом до этого): в спецификации 1364
        // у насосов "Тип канала управления" = "Полевой (PN)", DI/DO/AI/AO = 0 —
        // управление по Profinet. Значит ни пускатель (KM), ни какое-либо реле
        // обратной связи (K) — ни внешнее (1260), ни встроенное в сам ПЧ (МСА) —
        // не нужны вообще: вся дискретная секция управления заменяется одним
        // сетевым кабелем. См. VfdPageCopier.NotNeededTypes — такие объекты при
        // копировании удаляются со страницы (RemoveFromPage), артикул не подставляется.

        public static VfdComponentDefaults Default() => new VfdComponentDefaults();
    }
}
