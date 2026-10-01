using System.Collections.Generic;

namespace EplanCipMvp.Core.Models
{
    public enum ChannelType { DI, DO, AI, AO }

    /// <summary>
    /// Один физический модуль ET200SP (напр. -A106 DQ 8x24VDC/0.5A) с распределёнными
    /// по его каналам сигналами. Channels.Count может быть меньше ChannelCapacity —
    /// незаполненные каналы = резерв 20%, заложенный в спецификации.
    /// </summary>
    public class PlcModule
    {
        public string Cabinet { get; set; }
        public ChannelType Type { get; set; }
        public int ModuleIndex { get; set; }        // порядковый номер модуля этого типа в шкафу (1, 2, 3...)
        public string ArticleNumber { get; set; }    // напр. "6ES7131-6BH01-0BA0"
        public int ChannelCapacity { get; set; }     // 16 / 16 / 8 / 4
        public List<DeviceSignal> Channels { get; } = new List<DeviceSignal>();

        /// <summary>
        /// Обозначение вида "-2A1.5" (станция 2, тип DI (цифра 1), 5-й модуль).
        /// Схема подтверждена на реальном проекте 1260 того же интегратора
        /// (Aprotec Engineering) — см. ModuleGrouperOptions.TypeDigits/CpuCabinet.
        /// Номер станции для КОНКРЕТНО проекта 1364 всё же не подтверждён напрямую
        /// (нет живого .elk/PDF по 1364) — переносим схему по аналогии.
        /// </summary>
        public string Designation { get; set; }

        /// <summary>
        /// Структурный идентификатор СТРАНИЦЫ, на которой должен оказаться модуль
        /// (например "=1.ШРП1.DP") — по аналогии со страницами "Состав станции"/
        /// "Обзор модулей" в проекте 1260 (там ".DP"). См. PageStructureSettings.
        /// Это НЕ тег устройства (тот остаётся в стиле "+LINE{n}", см. NamingSettings) —
        /// а именно то, куда в дереве страниц EPLAN кладётся сам лист.
        /// </summary>
        public string PageStructureId { get; set; }

        public string DesignationHint => $"{Cabinet} / {Type} модуль №{ModuleIndex} ({Designation}, {ArticleNumber})";
    }
}
