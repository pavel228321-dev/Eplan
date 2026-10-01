using System;
using System.Collections.Generic;
using System.Linq;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanCipMvp.Core;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.App
{
    /// <summary>Результат расчёта (без побочных эффектов) для одной группы устройств —
    /// см. PageReconciler. Чистые данные (никаких EPLAN-типов), спокойно уходит через
    /// JSON в веб-интерфейс.</summary>
    public class ReconcileGroupPlan
    {
        public string GroupKey { get; set; }
        public string DisplayName { get; set; }
        public int ExistingCount { get; set; }
        public int NeededCount { get; set; }
        public List<string> ExistingPageNames { get; set; } = new List<string>();

        /// <summary>Страницы, которые будут УДАЛЕНЫ (Page.Remove(), необратимо) —
        /// хвостовые после сортировки, если существующих страниц больше, чем нужно.</summary>
        public List<string> ToDeletePageNames { get; set; } = new List<string>();

        /// <summary>Сколько НОВЫХ страниц будет продублировано (если существующих
        /// меньше, чем нужно) — по образцу последней существующей страницы группы.</summary>
        public int ToDuplicateCount { get; set; }
    }

    /// <summary>
    /// 12.09.2026: после полного копирования донора (ProjectManager.CopyProject,
    /// см. EplanSession.ConnectToEplan) страницы группы устройств уже физически
    /// есть в целевом проекте — под донорскими тегами. Этот класс приводит их
    /// количество в соответствие с реальным списком целевых тегов (дублирует
    /// внутри проекта, если нужно больше; УДАЛЯЕТ лишние — Page.Remove(),
    /// НЕОБРАТИМО — если меньше) и переименовывает оставшиеся/новые через уже
    /// существующие RetagExisting*-методы копирующих классов (VfdPageCopier,
    /// DeviceGroupPageCopier) — их обычный поток донор->страница->Копировать
    /// при этом не меняется, RetagExisting* — чистое переиспользование.
    ///
    /// Plan(...) — только считает, БЕЗ побочных эффектов (для предпросмотра в UI,
    /// прежде чем пользователь подтвердит удаление). Apply(...) — реально
    /// выполняет дублирование/удаление/переименование. См. план
    /// /home/node/.claude/plans/eventual-humming-charm.md.
    ///
    /// Модули ПЛК (группа "DP") в этот проход НЕ входят: в отличие от VFD и
    /// DeviceGroupCatalog (у каждой группы один однородный фильтр страниц),
    /// у DP один и тот же фильтр ".DP" покрывает и титульные листы, и страницы
    /// детализации — программно надёжно отличить их друг от друга внутри
    /// уже существующих страниц целевого проекта сегодня нечем (сегодня выбор
    /// "какая страница — титул, какая — деталь" делает пользователь вручную при
    /// выборе донор-страницы, не по признаку самой страницы). Не гадаем — см. план.
    /// </summary>
    public static class PageReconciler
    {
        // ------------------------------------------------------------------
        // Общая механика (дублирование/удаление) — не зависит от группы.
        // ------------------------------------------------------------------

        private static ReconcileGroupPlan BuildPlan(string key, string displayName, List<Page> existing, int neededCount)
        {
            var plan = new ReconcileGroupPlan
            {
                GroupKey = key,
                DisplayName = displayName,
                ExistingCount = existing.Count,
                NeededCount = neededCount,
                ExistingPageNames = existing.Select(p => p.Name).ToList(),
            };
            if (existing.Count > neededCount)
                plan.ToDeletePageNames = existing.Skip(neededCount).Select(p => p.Name).ToList();
            else if (existing.Count < neededCount)
                plan.ToDuplicateCount = neededCount - existing.Count;
            return plan;
        }

        /// <summary>Удаляет лишние (хвостовые) страницы и/или дублирует последнюю
        /// существующую нужное число раз, возвращает итоговый список страниц длиной
        /// РОВНО neededCount (если получилось) для последующей перетэговки по
        /// позиции. Каждая ошибка (удаление/дублирование одной страницы) логируется
        /// и не прерывает обработку остальных — тот же принцип, что и везде в
        /// проекте (см. Copy*-методы EplanSession).</summary>
        private static List<Page> ReconcilePageCount(Project targetProject, List<Page> existing, int neededCount, Action<string> log)
        {
            var result = new List<Page>(existing);

            if (result.Count > neededCount)
            {
                var toDelete = result.Skip(neededCount).ToList();
                result = result.Take(neededCount).ToList();
                foreach (var page in toDelete)
                {
                    string name = page.Name;
                    try
                    {
                        page.Remove();
                        log($"  Удалена лишняя страница '{name}'.");
                    }
                    catch (Exception ex)
                    {
                        log($"  ОШИБКА удаления страницы '{name}': {ex.Message}");
                    }
                }
            }
            else if (result.Count < neededCount)
            {
                if (result.Count == 0)
                {
                    log("  Нет ни одной существующей страницы этой группы в целевом проекте — " +
                        "дублировать нечего. Скопируйте хотя бы одну страницу-донор вручную (шаг 2), потом повторите реконсиляцию.");
                    return result;
                }

                var template = result[result.Count - 1];
                int toAdd = neededCount - result.Count;
                for (int i = 0; i < toAdd; i++)
                {
                    Page copied = null;
                    const int maxAttempts = 500;
                    for (int counter = 1; counter <= maxAttempts && copied == null; counter++)
                    {
                        var pageName = new PagePropertyList { PAGE_COUNTER = counter };
                        copied = template.CopyTo(targetProject, pageName, PageMacro.Enums.NumerationMode.Number, false);
                    }

                    if (copied == null)
                    {
                        log($"  ОШИБКА: не удалось продублировать страницу '{template.Name}' — все номера страниц заняты в целевом проекте.");
                        continue;
                    }

                    log($"  Продублирована страница '{template.Name}' -> '{copied.Name}'.");
                    result.Add(copied);
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        // VFD (насосы с ПЧ) — 1 страница = 1 насос.
        // ------------------------------------------------------------------

        public static ReconcileGroupPlan PlanVfd(Project targetProject, List<string> pumpTags)
        {
            var existing = VfdPageCopier.FindCandidateDonorPages(targetProject)
                .OrderBy(p => p.Name, NaturalStringComparer.Instance)
                .ToList();
            return BuildPlan("VFD", "Насосы с ПЧ", existing, pumpTags.Count);
        }

        public static List<string> ApplyVfd(Project targetProject, List<string> pumpTags,
            VfdComponentDefaults defaults, Action<string> log)
        {
            var existing = VfdPageCopier.FindCandidateDonorPages(targetProject)
                .OrderBy(p => p.Name, NaturalStringComparer.Instance)
                .ToList();
            var pages = ReconcilePageCount(targetProject, existing, pumpTags.Count, log);

            var copier = new VfdPageCopier();
            var report = new List<string>();
            for (int i = 0; i < Math.Min(pages.Count, pumpTags.Count); i++)
            {
                var result = copier.RetagExistingPumpPage(pages[i], pumpTags[i], defaults);
                report.Add(result.Error == null
                    ? $"  {result.PumpTag}: OK, страница '{result.NewPageName}'."
                    : $"  {result.PumpTag}: страница '{result.NewPageName}', с ошибками — {result.Error}");
            }
            return report;
        }

        // ------------------------------------------------------------------
        // DeviceGroupCatalog (клапаны/датчики и т.п.) — batch по DevicesPerDonorPage.
        // ------------------------------------------------------------------

        public static ReconcileGroupPlan PlanDeviceGroup(Project targetProject, DeviceGroupConfig config, List<string> targetTags)
        {
            var existing = DeviceGroupPageCopier.FindCandidateDonorPages(targetProject, config.DonorPageFilter)
                .OrderBy(p => p.Name, NaturalStringComparer.Instance)
                .ToList();
            var batches = DeviceGroupPageCopier.BuildBatches(targetTags, config.DevicesPerDonorPage);
            return BuildPlan(config.Key, config.DisplayName, existing, batches.Count);
        }

        public static List<string> ApplyDeviceGroup(Project targetProject, DeviceGroupConfig config,
            List<string> targetTags, Action<string> log)
        {
            var existing = DeviceGroupPageCopier.FindCandidateDonorPages(targetProject, config.DonorPageFilter)
                .OrderBy(p => p.Name, NaturalStringComparer.Instance)
                .ToList();
            var batches = DeviceGroupPageCopier.BuildBatches(targetTags, config.DevicesPerDonorPage);
            var pages = ReconcilePageCount(targetProject, existing, batches.Count, log);

            var copier = new DeviceGroupPageCopier();
            var report = new List<string>();
            for (int i = 0; i < Math.Min(pages.Count, batches.Count); i++)
            {
                var result = copier.RetagExistingBatch(pages[i], batches[i]);
                string batchLabel = string.Join(", ", result.BatchTags);
                report.Add(result.SlotsMatched
                    ? $"  {batchLabel}: OK, страница '{result.NewPageName}'." + (result.Error != null ? $" (ошибки: {result.Error})" : "")
                    : $"  {batchLabel}: СЛОТЫ НЕ РАЗОБРАНЫ на странице '{result.NewPageName}' — {result.Error}");
            }
            return report;
        }
    }
}
