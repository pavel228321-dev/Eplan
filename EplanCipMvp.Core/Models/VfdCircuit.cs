namespace EplanCipMvp.Core.Models
{
    /// <summary>
    /// Один силовой контур "автомат защиты двигателя + ПЧ + панель управления"
    /// на один насос — состав подтверждён спецификациями заказа проекта 1364
    /// (1364_04-спец_заказ_ПЧ, 1364_03_спец_заказ_НКУ): на каждый из 5 насосов
    /// с типом "Насос центробежный с ПЧ" — один Danfoss VLT Micro Drive FC 51
    /// + панель LCP12 + автомат Schneider GV3P32 + доп. контакты GVAN11.
    /// </summary>
    public class VfdCircuit
    {
        public string PumpTag { get; set; }
        public string Cabinet { get; set; }

        /// <summary>10.09.2026: мощность мотора как есть в спецификации ("11 кВт") —
        /// напрямую из строки насоса в "Перечне устройств", а не захардкожена. Используется
        /// для подписи на чертеже (VfdPageCopier) вместо статичного значения по умолчанию.</summary>
        public string Power { get; set; }

        public string VfdVendor { get; set; }
        public string VfdArticle { get; set; }
        public bool VfdIsDiscontinued { get; set; }
        public string VfdNote { get; set; }

        public string ControlPanelArticle { get; set; }

        public string BreakerVendor { get; set; }
        public string BreakerArticle { get; set; }
        public string AuxContactArticle { get; set; }
        public string AuxContactPurpose { get; set; }
    }
}
