namespace EplanCipMvp.Core.Models
{
    /// <summary>
    /// Один физический прибор из "Перечня устройств" — на уровне устройства
    /// (не канала). В отличие от DeviceSignal (который распадается на каналы),
    /// это нужно для сверки артикулов/производителей и подбора символа-макроса.
    /// </summary>
    public class DeviceInfo
    {
        public string Tag { get; set; }
        public string DeviceType { get; set; }       // "Тип" как есть в xlsx, включая уточнения в скобках
        public string Manufacturer { get; set; }      // может быть "TBD" или "TBD (Alfa Laval / SPX / GEA)"
        public string OrderNumber { get; set; }        // может быть "TBD"
        public string Cabinet { get; set; }

        /// <summary>10.09.2026: столбец "Power" листа "Перечень устройств", как есть в xlsx
        /// (например "11 кВт"). Нужен, чтобы подпись мощности на чертеже брать из
        /// спецификации по каждому насосу отдельно, а не хардкодить одно значение на все —
        /// см. VfdCircuitBuilder/VfdPageCopier.</summary>
        public string Power { get; set; }

        /// <summary>Столбец "Voltage" — не используется пока нигде в генерации чертежа,
        /// но читаем заодно с Power, т.к. это соседний столбец той же таблицы.</summary>
        public string Voltage { get; set; }

        /// <summary>DeviceType без уточнения в скобках — "Клапан пневматический (моющая головка)"
        /// -> "Клапан пневматический". Используется для сопоставления с настройками по умолчанию,
        /// т.к. в реальном файле один и тот же тип устройства встречается с разными уточнениями.</summary>
        public string BaseDeviceType
        {
            get
            {
                int idx = DeviceType?.IndexOf('(') ?? -1;
                return idx > 0 ? DeviceType.Substring(0, idx).Trim() : DeviceType?.Trim();
            }
        }

        public bool IsManufacturerTbd => IsTbd(Manufacturer);
        public bool IsOrderNumberTbd => IsTbd(OrderNumber);

        private static bool IsTbd(string value) =>
            string.IsNullOrWhiteSpace(value) || value.Trim().StartsWith("TBD", System.StringComparison.OrdinalIgnoreCase);
    }
}
