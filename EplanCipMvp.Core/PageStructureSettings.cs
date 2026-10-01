using System.Collections.Generic;
using System.Linq;

namespace EplanCipMvp.Core
{
    /// <summary>
    /// Структурный идентификатор СТРАНИЦЫ (не тега устройства!) — по образцу проекта
    /// 1260, где страницы организованы по шкафу и функциональной подгруппе:
    /// "=1.ET101.DP" (состав станции/модули ПЛК), "=1.ET101.VL" (клапаны),
    /// "=1.ET101.LV" (питание), "=1.E01.M" (моторы) и т.д.
    ///
    /// 1260 теперь основной референс (не МСА) — раньше у нас вообще не было
    /// понятия структуры страницы, только структура ТЕГА устройства
    /// (NamingSettings, "+LINE1-V0"). Это разные вещи: тег устройства для 1364,
    /// скорее всего, всё равно останется в стиле МСА/"+LINE" (1364 организован
    /// по линиям мойки, как и МСА, в отличие от 1260, который организован по
    /// бакам) — а вот СТРУКТУРА СТРАНИЦ (как раскладывать листы по шкафам) —
    /// берём по аналогии с 1260, раз это теперь основной образец компании.
    ///
    /// ПРЕДПОЛОЖЕНИЕ по аналогии, не подтверждено собственным файлом 1364 —
    /// тот же уровень достоверности, что у Designation в ModuleGrouperOptions.
    /// </summary>
    public class PageStructureSettings
    {
        /// <summary>Шаблон структурного идентификатора страницы.
        /// {0} = номер производства/раздела (по 1260 всегда "1"), {1} = код шкафа,
        /// {2} = код функциональной подгруппы (DP/VL/VP/SN/SE/LV/M).</summary>
        public string PageStructureTemplate { get; set; } = "={0}.{1}.{2}";

        /// <summary>Код функциональной подгруппы для страниц "Состав ПЛК" (обзор
        /// модулей DI/DO/AI/AO) — в 1260 это ".DP" (Distributed Periphery).</summary>
        public string PlcModulesGroupCode { get; set; } = "DP";

        /// <summary>Явное сопоставление имени шкафа (как в спецификации 1364,
        /// например "ШРП-1") короткому коду без дефисов/пробелов для идентификатора
        /// страницы (например "ШРП1") — EPLAN-идентификаторы дефисы не любят.
        /// Если для шкафа нет явной записи — код строится автоматически
        /// (см. CabinetCode).</summary>
        public Dictionary<string, string> CabinetCodes { get; set; } = new Dictionary<string, string>
        {
            ["ШРП-1"] = "ШРП1",
            ["ШРП-2"] = "ШРП2",
            ["ШУ"] = "ШУ",
        };

        public string CabinetCode(string cabinet)
        {
            if (CabinetCodes != null && CabinetCodes.TryGetValue(cabinet, out var code))
                return code;
            return new string((cabinet ?? "").Where(char.IsLetterOrDigit).ToArray());
        }

        public string BuildPageStructureId(string cabinet, string groupCode = null)
        {
            return string.Format(PageStructureTemplate, "1", CabinetCode(cabinet), groupCode ?? PlcModulesGroupCode);
        }

        public static PageStructureSettings Default() => new PageStructureSettings();
    }
}
