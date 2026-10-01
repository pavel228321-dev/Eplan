namespace EplanCipMvp.Core.Models
{
    /// <summary>
    /// Один канал ввода/вывода одного устройства (например, DI "открытие клапана L1V0").
    /// Один физический прибор в спецификации может дать несколько DeviceSignal
    /// (напр. клапан V: 1 DI + 1 DO).
    /// </summary>
    public class DeviceSignal
    {
        public string Tag { get; set; }          // напр. "L1V0" (колонка "Идентификатор")
        public string DeviceType { get; set; }    // напр. "Клапан пневматический" (колонка "Тип")
        public string Cabinet { get; set; }       // "ШРП-1" / "ШРП-2" / "ШУ"
        public string ChannelType { get; set; }   // "DI" / "DO" / "AI" / "AO"

        public override string ToString() => $"{Tag} [{ChannelType}] ({Cabinet})";
    }
}
