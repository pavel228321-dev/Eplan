using System.Collections.Generic;

namespace EplanCipMvp.Core
{
    /// <summary>
    /// Настраиваемые параметры группировки — вынесены из жёстко зашитых констант,
    /// чтобы EplanCipMvp.App мог подставлять их из appsettings.json, не трогая Core.
    /// Default() сохраняет прежнее поведение (для Core.SelfTest, где конфиг не нужен).
    /// </summary>
    public class ModuleGrouperOptions
    {
        public double ReserveFactor { get; set; } = 1.2;

        /// <summary>Порядок типов модулей внутри станции — влияет только на порядок
        /// в списках/UI, НЕ на цифру в обозначении (та фиксирована в TypeDigits).</summary>
        public string[] TypeOrder { get; set; } = { "DI", "DO", "AI", "AO" };

        /// <summary>
        /// Цифра типа в обозначении "-{станция}A{цифра}.{№}" — ПОДТВЕРЖДЕНО на реальном
        /// проекте 1260 (Aprotec Engineering, тот же интегратор, что и 1364): станция 2A1
        /// содержит DI-модули 2A1.1..2A1.11, DO — 2A2.1..2A2.9, AI — 2A3.1..2A3.2;
        /// станция 1A1 содержит AO-модули 1A4.1..1A4.2 (цифра 4, даже когда AI отсутствует).
        /// См. README.
        /// </summary>
        public Dictionary<string, int> TypeDigits { get; set; } = new Dictionary<string, int>
        {
            { "DI", 1 },
            { "DO", 2 },
            { "AI", 3 },
            { "AO", 4 },
        };

        /// <summary>
        /// Имя шкафа, где стоит ЦП — получает номер станции "1". Остальные шкафы
        /// нумеруются 2, 3... по естественной сортировке имени. Подтверждено на
        /// 1260: станция с CPU (=1.E01.PLC, шкаф E01) — "1", удалённые станции
        /// ET101/ET102 — "2"/"3" (хотя в дереве страниц E01 идёт третьим по счёту —
        /// номер станции определяется не порядком в проекте, а наличием ЦП).
        /// </summary>
        public string CpuCabinet { get; set; }

        public static ModuleGrouperOptions Default() => new ModuleGrouperOptions();
    }
}
