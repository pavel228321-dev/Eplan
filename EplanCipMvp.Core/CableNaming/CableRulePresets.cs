using System.Collections.Generic;

namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>
    /// Правила, по которым названы кабели в 1260 (проверено на его перечне кабелей:
    /// 228/228 совпадений, 9 кабелей без источника/цели — WPN-PC1, WEX-* — вручную).
    /// Порядок важен: срабатывает первое подходящее.
    /// </summary>
    public static class CableRulePresets
    {
        public static List<CableRule> Like1260() => new List<CableRule>
        {
            new CableRule { Name = "Profinet", TypeContains = "ETHERLINE PN", Template = "WPN-{МестоИсточника}" },
            new CableRule { Name = "Выравнивание потенциалов", TypeContains = "H07V-K", CoresTotal = 1, Template = "WEQ{ШкафИсточника}" },
            new CableRule { Name = "Питание шкаф→шкаф", TargetLocation = "+CP.E01", TargetDevice = "1X2", Template = "W21{ШкафИсточника:2}" },
            // 24.09.2026: «от первой цифры» — только для устройств «буква участка + 2 цифры» (C01M01, T32M04), как все
            // такие устройства E01 в 1260. Датчики (в 1260 — из ET101/ET102, в 1364 — прямо из E01: BV21+, LS4) —
            // W + полное имя датчика (WB3VP01, WLS4). Первая версия отрезала у датчиков 1364 тип: BV21+ -> W21+;
            // маска «буква + 2 цифры + буква» (@##@*), а не ?##*: иначе B111 (1364) -> W111.
            // Пневмоостров (оба конца в шкафу): 1260 -W3VP1 к -3VP1, 1364 -WUP1 к -UP1.
            new CableRule { Name = "Пневмоостров", TargetDevice = "*VP#;UP#", Template = "W{Устройство}" },
            new CableRule { Name = "Главный шкаф → поле (моторы)", SourceLocation = "+CP.E01", TargetLocation = "+FIELD", TargetDevice = "@##@*", Template = "W{Устройство:от_цифры}{.N}" },
            new CableRule { Name = "Шкаф → поле (датчики)", TargetLocation = "+FIELD", Template = "W{Устройство}{.N}" },
        };
    }
}
