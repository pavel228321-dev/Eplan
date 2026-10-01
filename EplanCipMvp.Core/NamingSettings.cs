namespace EplanCipMvp.Core
{
    /// <summary>
    /// Правила построения структурного обозначения канала — по образцу того, что
    /// реально видно в МСА (лист 133/150, "Состав ПЛК. Входы"): подпись канала там
    /// имеет вид "+LINE3-V0 Вода в бачок", а не голый тег "L3V0" как в спецификации.
    ///
    /// ПРЕДПОЛОЖЕНИЕ по ограниченному числу наблюдений — как и Designation в
    /// ModuleGrouperOptions, легко поправить, когда будет больше примеров.
    /// </summary>
    public class NamingSettings
    {
        /// <summary>Шаблон структурного префикса для тегов с номером линии (L1, L2...).
        /// {0} = номер линии. По умолчанию "+LINE{0}" — подтверждено скриншотами МСА.</summary>
        public string LineStructureTemplate { get; set; } = "+LINE{0}";

        /// <summary>Префикс для тегов без номера линии (общие устройства, шкаф ШУ) —
        /// "+MCC1" встречается и в реальном имени проекта 1260 (...+MCC1), и в самой
        /// МСА ("+MCC1-HL1", "+MCC1-HA1" — сигнальные лампы/сирена).</summary>
        public string CommonStructurePrefix { get; set; } = "+MCC1";

        /// <summary>Шаблон итоговой подписи канала. {0}=структурный префикс,
        /// {1}=короткий код устройства (без "L{n}"), {2}=описание из спецификации.</summary>
        public string LabelTemplate { get; set; } = "{0}-{1} {2}";

        public static NamingSettings Default() => new NamingSettings();
    }
}
