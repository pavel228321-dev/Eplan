namespace EplanCipMvp.Core.Models
{
    /// <summary>
    /// Вендор/артикул "по умолчанию" для типа устройства — подставляется,
    /// когда в спецификации на месте производителя/артикула стоит TBD.
    /// Это ПРЕДЛОЖЕНИЕ для согласования, не готовое решение — Note обычно
    /// хранит источник/оговорку (напр. "нужно уточнить DN и тип клапана").
    /// </summary>
    public class ArticleDefault
    {
        public string Vendor { get; set; }
        public string Article { get; set; }
        public string Note { get; set; }
    }
}
