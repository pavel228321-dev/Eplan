using System.Collections.Generic;

namespace EplanCipMvp.Core.Models
{
    /// <summary>
    /// Конфиг одного типа устройств для копирования "пачками" со страницы-донора
    /// (клапаны, датчики — в отличие от насосов с ПЧ, где 1 устройство = 1 страница
    /// донора, см. VfdPageCopier). Данные — из методички 1364-CIP-metodichka.docx
    /// (§11), построенной на разборе двух донорских проектов, 1260 и
    /// 1166_Kaliningrad_z.pdf.
    /// </summary>
    public class DeviceGroupConfig
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }

        /// <summary>Буквенный код типа в теге устройства (см. TagParser.TypeCode) —
        /// обычно один код, но допускает несколько (например LT сверяется тем же
        /// артикулом, что и PT, но это разные коды тегов).</summary>
        public string[] TagTypeCodes { get; set; }

        /// <summary>Подстрока в Page.Name для поиска кандидатов в доноре — тот же
        /// принцип, что ".M" у насосов (VfdPageCopier.FindCandidateDonorPages):
        /// НЕ автовыбор, пользователь видит реальный список и выбирает сам.</summary>
        public string DonorPageFilter { get; set; }

        /// <summary>Сколько устройств размещено на одной странице донора — 1, 2 или 4
        /// по докx. При >1 требуется разбор донорских объектов "по слотам" (см.
        /// DeviceGroupPageCopier) — рискованная, не проверенная живым тестом часть.</summary>
        public int DevicesPerDonorPage { get; set; } = 2;

        /// <summary>true для FS/QT — прямого донорского образца НЕТ ни в 1260, ни в
        /// 1166, используется структурный аналог (LS/GS для FS, PT/LT для QT).
        /// GUI/лог должны явно предупреждать о более низкой уверенности.</summary>
        public bool IsAnalogy { get; set; }

        public string AnalogyNote { get; set; }

        /// <summary>11.09.2026: какой из ДВУХ донорских проектов открывать для поиска
        /// страниц этой группы — 1 = 1260 (по умолчанию, там подтверждено большинство
        /// типов), 2 = 1166_Kaliningrad_z.pdf. Пока единственный тип, у которого
        /// подтверждённый донор реально лежит ТОЛЬКО в 1166 — GS (стр. 93, "Датчики
        /// люка"), в 1260 такой страницы не нашли вообще. FS (аналогия с LS/GS)
        /// оставлен на доноре 1 — аналогия строится в первую очередь на LS (тот же
        /// 1×DI, подтверждён именно в 1260), не на GS.</summary>
        public int PreferredDonor { get; set; } = 1;
    }

    public static class DeviceGroupCatalog
    {
        public static List<DeviceGroupConfig> All { get; } = new List<DeviceGroupConfig>
        {
            new DeviceGroupConfig
            {
                Key = "VALVE", DisplayName = "Клапаны (V)",
                TagTypeCodes = new[] { "V" }, DonorPageFilter = "VL", DevicesPerDonorPage = 2,
            },
            new DeviceGroupConfig
            {
                Key = "LS", DisplayName = "Датчики уровня, дискретные (LS)",
                TagTypeCodes = new[] { "LS" }, DonorPageFilter = "SN", DevicesPerDonorPage = 2,
            },
            new DeviceGroupConfig
            {
                Key = "GS", DisplayName = "Датчики люков (GS)",
                // 11.09.2026: ПЕРЕСМОТРЕНО по живому скриншоту пользователя — реальные
                // страницы GS лежат прямо в разделе ".M" ДОНОРА 1 (1260), вперемешку с
                // насосами/мешалками ("Реле безопасности: датчик люка -T32GS01" и т.п.,
                // индексы /2,/4,/6,/8,/10,/12,/14,/16 в =1.E01.M+CP.E01) — а НЕ в 1166,
                // как предполагалось раньше по разбору PDF (там нашли похожую страницу
                // в .SN, но, видимо, это был другой, самостоятельный вариант донора).
                // Плотность тоже пересмотрена: в описании каждой такой страницы виден
                // РОВНО ОДИН тег GS (не 4, как в 1166) — для сравнения, у клапанов
                // описание явно перечисляет ДВА тега ("-B1VP01, -B2VP01"), значит донор
                // действительно пишет все теги страницы в описание, и раз тут только
                // один — плотность 1/лист. Со старым выбором (донор 2, .SN) программа
                // попадала на страницу уровня (Ceraphant PTP33B), а не люка — исправлено.
                TagTypeCodes = new[] { "GS" }, DonorPageFilter = ".M", DevicesPerDonorPage = 1,
                PreferredDonor = 1,
            },
            new DeviceGroupConfig
            {
                Key = "PT", DisplayName = "Датчики давления (PT)",
                TagTypeCodes = new[] { "PT" }, DonorPageFilter = "SN", DevicesPerDonorPage = 2,
            },
            new DeviceGroupConfig
            {
                Key = "LT", DisplayName = "Датчики уровня, аналоговые (LT)",
                TagTypeCodes = new[] { "LT" }, DonorPageFilter = "SN", DevicesPerDonorPage = 2,
            },
            new DeviceGroupConfig
            {
                Key = "TE", DisplayName = "Датчики температуры (TE)",
                TagTypeCodes = new[] { "TE" }, DonorPageFilter = "SN", DevicesPerDonorPage = 2,
            },
            new DeviceGroupConfig
            {
                Key = "FS", DisplayName = "Датчики протока, дискретные (FS) — по аналогии с LS/GS",
                TagTypeCodes = new[] { "FS" }, DonorPageFilter = "SN", DevicesPerDonorPage = 2,
                IsAnalogy = true,
                AnalogyNote = "Прямого донора нет ни в 1260, ни в 1166 — используется тот же 1×DI " +
                               "шаблон, что подтверждён для LS/GS (§11.2/§11.5 методички).",
            },
            new DeviceGroupConfig
            {
                Key = "QT", DisplayName = "Проводимость+температура (QT) — по аналогии с PT/LT",
                TagTypeCodes = new[] { "QT" }, DonorPageFilter = "SN", DevicesPerDonorPage = 2,
                IsAnalogy = true,
                AnalogyNote = "Прямого донора нет ни в 1260, ни в 1166 — используется задвоенный " +
                               "блок PT/LT (§11.2 методички, 1×AI каждый, здесь 2×AI на один прибор).",
            },
        };

        /// <summary>Типы устройств 1364, для которых донора НЕТ ни в одном из двух
        /// разобранных проектов (1260, 1166) — копировать нечего, чертить вручную.
        /// Для информационного блока в GUI, не участвует в копировании.</summary>
        public static readonly string[] NoDonorDeviceTypeNames =
        {
            "VC — клапан регулирующий (DI+AO)",
            "FQT — расходомер (DI+AI, смешанный тип)",
            "Насосы дозирования (M2, мембранные SMC)",
        };
    }
}
