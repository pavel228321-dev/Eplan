using System;
using System.Collections.Generic;
using System.Linq;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.Core
{
    /// <summary>
    /// Группирует плоский список DeviceSignal в модули ET200SP по шкафам,
    /// с резервом 20% (округление вверх) — по той же логике, что уже посчитана
    /// вручную в листе "2. Сигналы по шкафам" файла 1364-Сводная_спецификация.
    ///
    /// Проверено на реальных цифрах из файла:
    ///   ШРП-1: DI 72->87->6 модулей, DO 54->65->5, AI 18->22->3, AO 3->4->1  (совпадает)
    ///   ШРП-2: DI 48->58->4, DO 36->44->3, AI 12->15->2, AO 2->3->1          (совпадает)
    ///   ШУ:    DI 55->66->5, DO 49->59->4, AI 13->16->2, AO 5->6->2          (совпадает)
    ///
    /// Обозначение модуля ("-{станция}A{цифра}.{№}") — по реальной схеме проекта
    /// 1260 (тот же интегратор Aprotec Engineering, см. ModuleGrouperOptions.TypeDigits).
    /// </summary>
    public static class ModuleGrouper
    {
        private static readonly Dictionary<string, (int capacity, string article)> ModuleSpecs =
            new Dictionary<string, (int, string)>
            {
                { "DI", (16, "6ES7131-6BH01-0BA0") },
                { "DO", (16, "6ES7132-6BH01-0BA0") },
                { "AI", (8,  "6ES7134-6GF00-0AA1") },
                { "AO", (4,  "6ES7135-6HD00-0BA1") },
            };

        public static List<PlcModule> GroupIntoModules(List<DeviceSignal> signals, ModuleGrouperOptions options = null, PageStructureSettings pageStructure = null)
        {
            options = options ?? ModuleGrouperOptions.Default();
            pageStructure = pageStructure ?? PageStructureSettings.Default();
            double reserveFactor = options.ReserveFactor;
            string[] typeOrder = options.TypeOrder;

            var result = new List<PlcModule>();

            // Группируем и сразу закрепляем детерминированный порядок каналов —
            // естественная сортировка тега (L1V2 раньше L1V10), не полагаемся
            // на порядок строк исходного xlsx.
            var groupsByKey = signals
                .GroupBy(s => (s.Cabinet, s.ChannelType))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(s => s.Tag, NaturalStringComparer.Instance).ToList());

            var allCabinets = groupsByKey.Keys.Select(k => k.Cabinet).Distinct().ToList();

            // Номер станции: шкаф с ЦП -> "1", остальные -> 2, 3... по естественной
            // сортировке имени шкафа. Не порядок появления в спецификации.
            var otherCabinets = allCabinets
                .Where(c => c != options.CpuCabinet)
                .OrderBy(c => c, NaturalStringComparer.Instance)
                .ToList();

            var stationNumberByCabinet = new Dictionary<string, int>();
            int nextStation = 1;
            if (options.CpuCabinet != null && allCabinets.Contains(options.CpuCabinet))
                stationNumberByCabinet[options.CpuCabinet] = nextStation++;
            foreach (var cabinet in otherCabinets)
                stationNumberByCabinet[cabinet] = nextStation++;

            var cabinetOrder = allCabinets.OrderBy(c => stationNumberByCabinet[c]).ToList();

            foreach (var cabinet in cabinetOrder)
            {
                int station = stationNumberByCabinet[cabinet];

                foreach (var type in typeOrder)
                {
                    if (!groupsByKey.TryGetValue((cabinet, type), out var channelsList) || channelsList.Count == 0)
                        continue;

                    int typeDigit = options.TypeDigits[type];
                    var spec = ModuleSpecs[type];
                    int actualCount = channelsList.Count;
                    int reservedCount = (int)Math.Ceiling(actualCount * reserveFactor);
                    int moduleCount = (int)Math.Ceiling(reservedCount / (double)spec.capacity);

                    int index = 0;
                    for (int m = 0; m < moduleCount; m++)
                    {
                        int moduleNumber = m + 1;

                        var module = new PlcModule
                        {
                            Cabinet = cabinet,
                            Type = (ChannelType)Enum.Parse(typeof(ChannelType), type),
                            ModuleIndex = moduleNumber,
                            Designation = $"-{station}A{typeDigit}.{moduleNumber}",
                            PageStructureId = pageStructure.BuildPageStructureId(cabinet),
                            ArticleNumber = spec.article,
                            ChannelCapacity = spec.capacity
                        };

                        int take = Math.Min(spec.capacity, channelsList.Count - index);
                        if (take > 0)
                        {
                            module.Channels.AddRange(channelsList.GetRange(index, take));
                            index += take;
                        }
                        // остаток каналов модуля (до ChannelCapacity) остаётся пустым — это и есть резерв

                        result.Add(module);
                    }
                }
            }

            return result;
        }
    }
}
