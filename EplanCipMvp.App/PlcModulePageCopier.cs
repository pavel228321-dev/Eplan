using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.App
{
    public class PlcTitlePageResult
    {
        public string Cabinet { get; set; }
        public bool PageCopied { get; set; }
        public string NewPageName { get; set; }
        public string Error { get; set; }
    }

    public class PlcModulePageCopyResult
    {
        public List<string> BatchDesignations { get; set; } = new List<string>();
        public bool PageCopied { get; set; }
        public string NewPageName { get; set; }
        public int PlacementsTotal { get; set; }
        public List<string> FoundDeviceTags { get; set; } = new List<string>();
        public bool SlotsMatched { get; set; }
        public List<string> Retagged { get; set; } = new List<string>();
        public List<string> ArticlesSwapped { get; set; } = new List<string>();
        public string Error { get; set; }
    }

    /// <summary>
    /// 11.09.2026: копирует страницы раздела DP (обзор модулей ввода-вывода) —
    /// §11.3 методички 1364-CIP-metodichka.docx: 1 титульный лист "Состав станции"
    /// на шкаф + детализация по 2 модуля/лист на каждый тип канала (DI/DO/AI/AO).
    /// Использует уже готовый расчёт модулей из ModuleGrouper (Core) — та часть
    /// была готова и проверена ранее (числа сверены с реальным листом
    /// "2. Сигналы по шкафам" 1364-Сводная_спецификация), но страниц в EPLAN эта
    /// логика раньше не создавала вообще — только считала.
    ///
    /// РИСК ВЫШЕ, чем у DeviceGroupPageCopier: тег устройства ("L1V12") подтверждён
    /// независимым источником — P&ID CIP1_1_.pdf. Обозначение модуля ("-2A1.5")
    /// подтверждено только на ДОНОРСКОМ проекте 1260 (тот же интегратор), для 1364
    /// это перенос по аналогии (см. ModuleGrouperOptions.TypeDigits/CpuCabinet) —
    /// станция/тип-цифра для 1364 не проверены напрямую. Разбор Designation ("-2A1.5"
    /// -> станция "2" в FUNC_PREFIX, номер модуля "5" в FUNC_COUNTER, "A1" — код,
    /// не трогаем, предполагаем, что у ПРАВИЛЬНО подобранной донорской страницы того
    /// же типа канала он и так совпадёт) — по той же схеме, что уже подтверждена
    /// живым тестом для устройств (NameService.RenameDevice, только FUNC_PREFIX/
    /// FUNC_COUNTER), но САМА схема разбора модульного обозначения — НЕ проверена
    /// живым тестом. Первый прогон стоит сделать именно на DP, до остальных.
    ///
    /// Артикул — высокая уверенность (PlcModule.ArticleNumber, точные каталожные
    /// номера Siemens из закупки 1364_01) — подменяется так же, как ПЧ/автомат
    /// в VfdPageCopier.
    ///
    /// Титульный лист ("Состав станции") — копируется, описание страницы
    /// проставляется, но СОДЕРЖИМОЕ (обзорная картинка/таблица модулей) НЕ
    /// редактируется — донор показывает свой собственный набор модулей, поправить
    /// его под 1364 нужно вручную в EPLAN. Это сознательное ограничение: у нас нет
    /// подтверждённого способа редактировать содержимое обзорной графики так же
    /// надёжно, как тег/артикул одного объекта.
    /// </summary>
    public class PlcModulePageCopier
    {
        private static readonly Regex LeadingLetters = new Regex(@"^[A-Za-z]+");
        private static readonly Regex TrailingDigits = new Regex(@"\d+$");
        private static readonly Regex DesignationPattern = new Regex(@"^-(\d+)A\d+\.(\d+)$");

        public static List<Page> FindCandidateDonorPages(Project sourceProject, string nameFilter) =>
            DeviceGroupPageCopier.FindCandidateDonorPages(sourceProject, nameFilter);

        private static List<List<PlcModule>> BuildBatches(IEnumerable<PlcModule> modules, int size)
        {
            var batches = new List<List<PlcModule>>();
            var current = new List<PlcModule>();
            foreach (var m in modules)
            {
                current.Add(m);
                if (current.Count == size)
                {
                    batches.Add(current);
                    current = new List<PlcModule>();
                }
            }
            if (current.Count > 0)
                batches.Add(current);
            return batches;
        }

        public List<PlcTitlePageResult> CopyTitlePages(Page donorTitlePage, Project targetProject, IEnumerable<string> cabinets)
        {
            var results = new List<PlcTitlePageResult>();
            foreach (var cabinet in cabinets)
            {
                var result = new PlcTitlePageResult { Cabinet = cabinet };
                try
                {
                    var pageDescription = new MultiLangString();
                    pageDescription.SetAsString($"Состав станции {cabinet}");

                    Page copied = null;
                    const int maxAttempts = 500;
                    for (int counter = 1; counter <= maxAttempts && copied == null; counter++)
                    {
                        var pageName = new PagePropertyList { PAGE_COUNTER = counter };
                        copied = donorTitlePage.CopyTo(targetProject, pageName, PageMacro.Enums.NumerationMode.Number, false);
                    }

                    if (copied == null)
                    {
                        result.Error = $"CopyTo вернул null для всех номеров страниц от 1 до {maxAttempts}.";
                        results.Add(result);
                        continue;
                    }

                    result.PageCopied = true;
                    result.NewPageName = copied.Name;

                    try { copied.Properties.PAGE_NOMINATIOMN = pageDescription; }
                    catch (Exception ex) { result.Error = $"set page description failed: {ex.Message}"; }
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
                results.Add(result);
            }
            return results;
        }

        public List<PlcModulePageCopyResult> CopyDetailPages(Page donorDetailPage, Project targetProject, List<PlcModule> modules)
        {
            var results = new List<PlcModulePageCopyResult>();
            foreach (var batch in BuildBatches(modules, 2))
                results.Add(CopyBatch(donorDetailPage, targetProject, batch));
            return results;
        }

        private PlcModulePageCopyResult CopyBatch(Page donorPage, Project targetProject, List<PlcModule> batch)
        {
            var result = new PlcModulePageCopyResult { BatchDesignations = batch.Select(m => m.Designation).ToList() };

            try
            {
                var pageDescription = new MultiLangString();
                pageDescription.SetAsString($"Модули {batch[0].Type}: {string.Join(", ", result.BatchDesignations)}");

                Page copied = null;
                const int maxAttempts = 500;
                for (int counter = 1; counter <= maxAttempts && copied == null; counter++)
                {
                    var pageName = new PagePropertyList { PAGE_COUNTER = counter };
                    copied = donorPage.CopyTo(targetProject, pageName, PageMacro.Enums.NumerationMode.Number, false);
                }

                if (copied == null)
                {
                    result.Error = $"CopyTo вернул null для всех номеров страниц от 1 до {maxAttempts}.";
                    return result;
                }

                result.PageCopied = true;
                result.NewPageName = copied.Name;

                try { copied.Properties.PAGE_NOMINATIOMN = pageDescription; }
                catch (Exception ex) { result.Error = (result.Error == null ? "" : result.Error + " | ") + $"set page description failed: {ex.Message}"; }

                RetagPlcBatch(copied, batch, result);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>12.09.2026: перетэговка/замена артикулов на УЖЕ существующей
        /// (`copied`) странице — дословный перенос содержимого CopyBatch ПОСЛЕ
        /// CopyTo. Нужно для реконсиляции после полного копирования проекта (см.
        /// PageReconciler/RetagExistingDetailBatch ниже) — поведение CopyBatch не
        /// меняется, это чистое извлечение.</summary>
        private void RetagPlcBatch(Page copied, List<PlcModule> batch, PlcModulePageCopyResult result)
        {
            try
            {
                var nameService = new NameService(copied);
                var placements = copied.AllPlacements ?? new StorableObject[0];
                result.PlacementsTotal = placements.Length;

                var mainNames = new Dictionary<Function, string>();
                foreach (var placement in placements)
                {
                    var function = placement as Function;
                    if (function == null) continue;
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
                        result.Error = (result.Error == null ? "" : result.Error + " | ") + $"read MAINNAME failed: {ex.Message}";
                    }
                }

                var slots = mainNames
                    .Where(kvp => TrailingDigits.IsMatch(kvp.Value))
                    .GroupBy(kvp => int.Parse(TrailingDigits.Match(kvp.Value).Value))
                    .OrderBy(g => g.Key)
                    .Select(g => g.ToList())
                    .ToList();

                if (slots.Count != batch.Count)
                {
                    result.SlotsMatched = false;
                    result.Error = (result.Error == null ? "" : result.Error + " | ") +
                        $"разобрано слотов: {slots.Count}, ожидалось {batch.Count} — переименование/артикул ПРОПУЩЕНЫ, поправьте вручную.";
                    return;
                }

                result.SlotsMatched = true;

                for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    var module = batch[slotIndex];
                    var designationMatch = DesignationPattern.Match(module.Designation ?? "");
                    string targetStation = designationMatch.Success ? designationMatch.Groups[1].Value : null;
                    string targetModuleNumber = designationMatch.Success ? designationMatch.Groups[2].Value : null;

                    if (!designationMatch.Success)
                    {
                        result.Error = (result.Error == null ? "" : result.Error + " | ") +
                            $"не удалось разобрать Designation '{module.Designation}' — переименование для этого модуля пропущено.";
                    }

                    foreach (var kvp in slots[slotIndex])
                    {
                        var function = kvp.Key;
                        var oldName = kvp.Value;

                        var strippedForType = oldName.TrimStart('?');
                        bool looksLikeRealDeviceCode = LeadingLetters.IsMatch(strippedForType);
                        bool wasUnresolved = oldName.StartsWith("?") && looksLikeRealDeviceCode;
                        bool isMain = function.IsMainFunction;

                        if (wasUnresolved && !isMain)
                        {
                            try { isMain = new DeviceService().AssignMainFunction(function, false, true); }
                            catch (Exception ex)
                            {
                                result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                                $"AssignMainFunction for '{oldName}' failed: {ex.Message}";
                            }
                        }

                        if (isMain && designationMatch.Success)
                        {
                            try
                            {
                                var nameParts = function.NameParts;
                                nameParts.FUNC_PREFIX = targetStation;
                                nameParts.FUNC_COUNTER = targetModuleNumber;
                                nameService.RenameDevice(function, nameParts, false, true);
                                result.Retagged.Add($"{oldName} -> {module.Designation} (слот {slotIndex + 1}) через NameService.RenameDevice");
                            }
                            catch (Exception ex)
                            {
                                result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                                $"retag '{oldName}' -> '{module.Designation}' failed: {ex.Message}";
                            }
                        }

                        // Артикул — высокая уверенность (точный каталожный номер Siemens из
                        // ModuleGrouper/1364_01), в отличие от переименования выше. Только
                        // главная функция — "Only main functions ... can have article information"
                        // (см. VfdPageCopier).
                        if (isMain && !string.IsNullOrEmpty(module.ArticleNumber))
                        {
                            try
                            {
                                foreach (var existing in function.ArticleReferences ?? new ArticleReference[0])
                                    function.RemoveArticleReference(existing);
                                function.AddArticleReference(module.ArticleNumber);
                                result.ArticlesSwapped.Add($"{oldName} -> {module.ArticleNumber}");
                            }
                            catch (Exception ex)
                            {
                                result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                                $"article swap for '{oldName}' failed: {ex.Message}";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = (result.Error == null ? "" : result.Error + " | ") + ex.Message;
            }
        }

        /// <summary>12.09.2026: перетэговка/замена артикулов на УЖЕ существующей
        /// странице (например, после полного копирования проекта) — страницу НЕ
        /// создаёт, только применяет ту же логику, что CopyBatch после CopyTo.
        /// Используется PageReconciler.</summary>
        public PlcModulePageCopyResult RetagExistingDetailBatch(Page existingPage, List<PlcModule> batch)
        {
            var result = new PlcModulePageCopyResult
            {
                BatchDesignations = batch.Select(m => m.Designation).ToList(),
                PageCopied = true,
                NewPageName = existingPage.Name,
            };
            RetagPlcBatch(existingPage, batch, result);
            return result;
        }
    }
}
