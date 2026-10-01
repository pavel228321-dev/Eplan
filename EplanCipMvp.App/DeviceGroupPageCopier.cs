using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanCipMvp.Core;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.App
{
    /// <summary>
    /// Отчёт по одной попытке копирования страницы-донора под ОДНУ ПАЧКУ целевых
    /// тегов (пачка = DeviceGroupConfig.DevicesPerDonorPage тегов, например 2 клапана
    /// на одну страницу) — см. класс DeviceGroupPageCopier.
    /// </summary>
    public class DeviceGroupPageCopyResult
    {
        public List<string> BatchTags { get; set; } = new List<string>();
        public bool PageCopied { get; set; }
        public string NewPageName { get; set; }
        public int PlacementsTotal { get; set; }
        public List<string> FoundDeviceTags { get; set; } = new List<string>();

        /// <summary>true, если число разобранных "слотов" на донорской странице
        /// (группировка Function по последней цифре MAINNAME) совпало с размером
        /// пачки — только тогда выполнялось переименование. Если false — донорские
        /// теги оставлены КАК ЕСТЬ на скопированной странице, требуется ручная
        /// правка (см. класс).</summary>
        public bool SlotsMatched { get; set; }

        public List<string> Retagged { get; set; } = new List<string>();
        public string Error { get; set; }
    }

    /// <summary>
    /// 11.09.2026: копирует страницы-доноры "пачками" под типы устройств с
    /// плотностью БОЛЬШЕ 1 на странице (клапаны — 2/лист, датчики люков — 4/лист и
    /// т.д. — см. DeviceGroupConfig/DeviceGroupCatalog, построено на находках
    /// методички 1364-CIP-metodichka.docx, §11, по разбору двух донорских проектов
    /// 1260 и 1166_Kaliningrad_z.pdf).
    ///
    /// НЕ заменяет и не трогает VfdPageCopier.cs — тот остаётся отдельным, уже
    /// частично проверенным живым тестом потоком для насосов с ПЧ (1 устройство =
    /// 1 страница донора, там COUNTER донора никогда не менялся, коллизий не было).
    /// Здесь на одной странице несколько устройств — при копировании под НОВУЮ пару/
    /// четвёрку целевых тегов недостаточно поменять только линию (FUNC_PREFIX), нужно
    /// ещё разрешить, какой донорский объект — какое целевое устройство, и присвоить
    /// каждому свой FUNC_COUNTER (донорский "V1"/"V2" почти наверняка не совпадёт с
    /// целевым "V12"/"V13" из спецификации 1364).
    ///
    /// Эвристика "слотов": все Function на скопированной странице группируются по
    /// ПОСЛЕДНЕЙ цифре их MAINNAME донора (например "V1","Y1" -> слот 1; "V2","Y2" ->
    /// слот 2 — два клапана, у каждого свой позиционер+катушка, но общий числовой
    /// суффикс). Слоты по возрастанию сопоставляются целевым тегам пачки по порядку.
    /// НЕ ПРОВЕРЕНО ЖИВЫМ ТЕСТОМ — как и было с NameService/DeviceService для
    /// насосов, ожидаем 1-2 прогона у пользователя с правками. Если число слотов не
    /// совпало с размером пачки — переименование пропускается целиком для этой
    /// страницы, ошибка явно попадает в лог (см. DeviceGroupPageCopyResult.SlotsMatched),
    /// вместо того чтобы угадывать и получить тихо неверный результат.
    ///
    /// Артикул донора НЕ подменяется ни для одной из новых групп — в отличие от
    /// VfdPageCopier (там ПЧ/автомат меняются на актуальные из спецификации 1364),
    /// у клапанов/датчиков в спецификации 1364 артикул пока TBD (см. методичку) —
    /// менять не на что, донорский образец переносится как есть.
    /// </summary>
    public class DeviceGroupPageCopier
    {
        private static readonly Regex LeadingLetters = new Regex(@"^[A-Za-z]+");
        private static readonly Regex TrailingDigits = new Regex(@"\d+$");

        /// <summary>Как VfdPageCopier.FindCandidateDonorPages, но с настраиваемым
        /// фильтром по имени страницы (у насосов всегда ".M", здесь — ".VL"/".SN" и
        /// т.д. по DeviceGroupConfig.DonorPageFilter) — тот же принцип: НЕ автовыбор,
        /// пользователь видит реальный список в 1260/1166 и выбирает сам.</summary>
        public static List<Page> FindCandidateDonorPages(Project sourceProject, string nameFilter)
        {
            return sourceProject.Pages
                .Where(p => p.Name != null && p.Name.Contains(nameFilter))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Разбивает плоский список целевых тегов на пачки по размеру
        /// DevicesPerDonorPage — последняя пачка может быть неполной (например 5
        /// клапанов при плотности 2 -> пачки [2,2,1]), это нормально, страница донора
        /// просто не заполняется целиком, лишние слоты остаются донорскими (не
        /// критично — они не попадают в отчёт устройств проекта, раз мы не будем на
        /// них ничего переименовывать без соответствующего целевого тега).</summary>
        public static List<List<string>> BuildBatches(IEnumerable<string> targetTags, int devicesPerDonorPage)
        {
            var batches = new List<List<string>>();
            var current = new List<string>();
            foreach (var tag in targetTags)
            {
                current.Add(tag);
                if (current.Count == devicesPerDonorPage)
                {
                    batches.Add(current);
                    current = new List<string>();
                }
            }
            if (current.Count > 0)
                batches.Add(current);
            return batches;
        }

        public List<DeviceGroupPageCopyResult> CopyGroup(Page donorPage, Project targetProject,
            DeviceGroupConfig config, IEnumerable<string> targetTags)
        {
            var results = new List<DeviceGroupPageCopyResult>();
            var batches = BuildBatches(targetTags, config.DevicesPerDonorPage);
            foreach (var batch in batches)
                results.Add(CopyBatch(donorPage, targetProject, config, batch));
            return results;
        }

        private DeviceGroupPageCopyResult CopyBatch(Page donorPage, Project targetProject,
            DeviceGroupConfig config, List<string> batchTags)
        {
            var result = new DeviceGroupPageCopyResult { BatchTags = batchTags };

            try
            {
                var pageDescription = new MultiLangString();
                pageDescription.SetAsString($"{config.DisplayName}: {string.Join(", ", batchTags)}");

                // Тот же паттерн, что VfdPageCopier.CopyForPump — перебираем
                // PAGE_COUNTER, пока CopyTo не вернёт не-null (имя страницы уже
                // занято в целевом проекте другой копией).
                Page copied = null;
                const int maxAttempts = 500;
                for (int counter = 1; counter <= maxAttempts && copied == null; counter++)
                {
                    var pageName = new PagePropertyList { PAGE_COUNTER = counter };
                    copied = donorPage.CopyTo(
                        targetProject,
                        pageName,
                        PageMacro.Enums.NumerationMode.Number,
                        false);
                }

                if (copied == null)
                {
                    result.Error = $"CopyTo вернул null для всех номеров страниц от 1 до {maxAttempts} — все заняты в целевом проекте (overwrite=false).";
                    return result;
                }

                result.PageCopied = true;
                result.NewPageName = copied.Name;

                try
                {
                    copied.Properties.PAGE_NOMINATIOMN = pageDescription;
                }
                catch (Exception ex)
                {
                    result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                    $"set page description failed: {ex.Message}";
                }

                RetagDeviceBatch(copied, batchTags, result);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>12.09.2026: перетэговка/сопоставление слотов на УЖЕ существующей
        /// (`copied`) странице — дословный перенос содержимого CopyBatch ПОСЛЕ
        /// CopyTo. Нужно для реконсиляции после полного копирования проекта (см.
        /// PageReconciler/RetagExistingBatch ниже) — поведение CopyBatch не
        /// меняется, это чистое извлечение.</summary>
        private void RetagDeviceBatch(Page copied, List<string> batchTags, DeviceGroupPageCopyResult result)
        {
            try
            {
                var nameService = new NameService(copied);
                var placements = copied.AllPlacements ?? new StorableObject[0];
                result.PlacementsTotal = placements.Length;

                // Шаг 1: собрать MAINNAME всех Function на копии.
                var mainNames = new Dictionary<Function, string>();
                foreach (var placement in placements)
                {
                    var function = placement as Function;
                    if (function == null)
                        continue;

                    try
                    {
                        string name = function.Properties[Properties.Function.FUNC_DEVICETAG_MAINNAME];
                        if (!string.IsNullOrEmpty(name))
                        {
                            mainNames[function] = name;
                            result.FoundDeviceTags.Add(name);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                        $"read MAINNAME failed: {ex.Message}";
                    }
                }

                // Шаг 2: разбор "слотов" — группировка по последней цифре MAINNAME
                // донора (см. класс). "?Провод1" и подобные (без цифры в хвосте, или
                // кириллица) в слоты не попадают — они не самостоятельные устройства.
                var slots = mainNames
                    .Where(kvp => TrailingDigits.IsMatch(kvp.Value))
                    .GroupBy(kvp => int.Parse(TrailingDigits.Match(kvp.Value).Value))
                    .OrderBy(g => g.Key)
                    .Select(g => g.ToList())
                    .ToList();

                if (slots.Count != batchTags.Count)
                {
                    result.SlotsMatched = false;
                    result.Error = (result.Error == null ? "" : result.Error + " | ") +
                        $"разобрано слотов на донорской странице: {slots.Count}, ожидалось (размер пачки): " +
                        $"{batchTags.Count} — переименование ПРОПУЩЕНО, донорские теги остались как есть, " +
                        "поправьте вручную в EPLAN.";
                    return;
                }

                result.SlotsMatched = true;

                for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    string targetTag = batchTags[slotIndex];
                    string targetLine = TagParser.LinePrefix(targetTag) ?? "";
                    string targetCounter = TagParser.CounterDigits(targetTag);

                    foreach (var kvp in slots[slotIndex])
                    {
                        var function = kvp.Key;
                        var oldName = kvp.Value;

                        // Тот же "?" / DT-adoption случай, что в VfdPageCopier — функция
                        // без самостоятельного тега (наследует от рамки/бокса), для нашего
                        // переименования её нужно сначала сделать главной.
                        var strippedForType = oldName.TrimStart('?');
                        bool looksLikeRealDeviceCode = LeadingLetters.IsMatch(strippedForType);
                        bool wasUnresolved = oldName.StartsWith("?") && looksLikeRealDeviceCode;
                        bool isMain = function.IsMainFunction;

                        if (wasUnresolved && !isMain)
                        {
                            try
                            {
                                isMain = new DeviceService().AssignMainFunction(function, false, true);
                            }
                            catch (Exception ex)
                            {
                                result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                                $"AssignMainFunction for '{oldName}' failed: {ex.Message}";
                            }
                        }

                        if (!isMain || string.IsNullOrEmpty(targetCounter))
                            continue;

                        try
                        {
                            var nameParts = function.NameParts;
                            nameParts.FUNC_PREFIX = targetLine;
                            nameParts.FUNC_COUNTER = targetCounter;
                            nameService.RenameDevice(function, nameParts, false, true);
                            result.Retagged.Add($"{oldName} -> {targetTag} (слот {slotIndex + 1}) через NameService.RenameDevice");
                        }
                        catch (Exception ex)
                        {
                            result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                            $"retag '{oldName}' -> '{targetTag}' failed: {ex.Message}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = (result.Error == null ? "" : result.Error + " | ") + ex.Message;
            }
        }

        /// <summary>12.09.2026: перетэговка/сопоставление слотов на УЖЕ существующей
        /// странице (например, после полного копирования проекта) — страницу НЕ
        /// создаёт, только применяет ту же логику, что CopyBatch после CopyTo.
        /// Используется PageReconciler.</summary>
        public DeviceGroupPageCopyResult RetagExistingBatch(Page existingPage, List<string> batchTags)
        {
            var result = new DeviceGroupPageCopyResult
            {
                BatchTags = batchTags,
                PageCopied = true,
                NewPageName = existingPage.Name,
            };
            RetagDeviceBatch(existingPage, batchTags, result);
            return result;
        }
    }
}
