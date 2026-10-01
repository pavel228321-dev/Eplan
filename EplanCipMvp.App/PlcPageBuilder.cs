using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanCipMvp.Core;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.App
{
    /// <summary>
    /// Слой интеграции с EPLAN API. 09.09.2026: сигнатуры Insert.PageMacro,
    /// Project/ProjectManager и т.п. ниже — ПОДТВЕРЖДЕНЫ по реальным сборкам
    /// EPLAN 2.9 (Eplan.EplApi.*u.dll + XML-документация), не по общей
    /// документации eplan.help, как было раньше. Компиляция проверена здесь же
    /// (см. README) против этих сборок. GetOrCreatePlcPage — единственное
    /// место, где "создание новой страницы" всё ещё не реализовано (нашёл, как
    /// найти существующую, но не как создать с нуля — не хватило подтверждённой
    /// информации по Project.CreateLocation/аналогам за одну сессию).
    /// </summary>
    public class PlcPageBuilder
    {
        private readonly string _macroBasePath;
        private readonly NamingSettings _naming;

        public PlcPageBuilder(string macroBasePath, NamingSettings naming = null)
        {
            _macroBasePath = macroBasePath;
            _naming = naming ?? NamingSettings.Default();
        }

        public void BuildModulePages(Project project, IEnumerable<PlcModule> modules)
        {
            var insert = new Insert();

            foreach (var module in modules)
            {
                Console.WriteLine($"Обрабатываю {module.DesignationHint}...");

                string macroFile = GetMacroPathForModule(module.Type);
                if (!File.Exists(macroFile))
                {
                    // Этот блок сработает, пока не выполнен Этап 0 (извлечение эталонных
                    // макросов из проекта 1260 / МСА в самой EPLAN и сохранение их сюда).
                    Console.WriteLine($"  ПРОПУСК: не найден файл макроса {macroFile}");
                    Console.WriteLine("  См. Этап 0 плана — макросы нужно извлечь вручную в EPLAN.");
                    continue;
                }

                Page targetPage = GetOrCreatePlcPage(project, module);

                // Подтверждено дизассемблированием Eplan.EplApi.HEServicesu.dll:
                //   StorableObject[] Insert.PageMacro(string strEMPFileName,
                //     Page oInsertAfterPage, Project oProject, bool overwrite)
                // Возвращает вставленные страницы/функции; элементы с null .Page —
                // то, что не удалось разместить (по документации метода).
                StorableObject[] inserted = insert.PageMacro(macroFile, targetPage, project, true);
                Console.WriteLine($"  Вставлено объектов: {inserted?.Length ?? 0}");

                FillChannelTexts(targetPage, module);
            }
        }

        private void FillChannelTexts(Page page, PlcModule module)
        {
            // ЧТО писать в каждый канал — уже решено и не зависит от EPLAN:
            // ChannelLabelBuilder строит подпись вида "+LINE3-V0 Вода в бачок"
            // по образцу реальных страниц МСА. Проверяется прямо сейчас, без EPLAN,
            // через EplanCipMvp.Core.SelfTest.
            var labels = new List<string>();
            foreach (var channel in module.Channels)
                labels.Add(ChannelLabelBuilder.BuildLabel(channel, _naming));

            // А вот КАК записать эти строки в конкретные текстовые объекты/свойства
            // функций на вставленном макросе — неизвестно, реализуется ПОСЛЕ Этапа 0.
            // Нужно открыть эталонный макрос модуля прямо в EPLAN и посмотреть: это
            // текстовые поля с плейсхолдерами (Function.Properties / TextObject) или
            // отдельные функции-точки, привязанные к тегу устройства.
            throw new NotImplementedException(
                $"Готово {labels.Count} подписей для {module.DesignationHint}, но запись в EPLAN " +
                "ещё не реализована — нужно сначала посмотреть структуру эталонного макроса " +
                "в самой EPLAN (Этап 0), потом вернуться сюда.");
        }

        private string GetMacroPathForModule(ChannelType type)
        {
            string fileName;
            switch (type)
            {
                case ChannelType.DI: fileName = "ET200SP_DI16.ema"; break;
                case ChannelType.DO: fileName = "ET200SP_DO16.ema"; break;
                case ChannelType.AI: fileName = "ET200SP_AI8.ema"; break;
                case ChannelType.AO: fileName = "ET200SP_AO4.ema"; break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
            return Path.Combine(_macroBasePath, fileName);
        }

        /// <summary>
        /// Ищет среди уже существующих страниц проекта последнюю с именем, начинающимся
        /// на PageStructureId модуля (например "=1.ШУ.DP") — макрос модуля вставляется
        /// СРАЗУ ПОСЛЕ неё (см. Insert.PageMacro: "You need to set the page, after which
        /// the new pages are inserted"). Project.Pages.Name подтверждён по XML-докам
        /// как "Equal to PAGE_FULLNAME property" — то есть полное имя с идентификатором
        /// структуры, ровно то, что строит PageStructureSettings.
        ///
        /// ЕСЛИ страниц с такой структурой ещё нет вообще (первый запуск на пустом
        /// разделе) — падаем с понятной ошибкой: создание страницы С НУЛЯ ещё не
        /// реализовано, не хватило подтверждённых сведений про Project.CreateLocation
        /// (или аналог) за одну сессию. На практике для Этапа 1 такое маловероятно —
        /// раздел ПЛК обычно уже существует хотя бы с одной страницей-заготовкой.
        /// </summary>
        private Page GetOrCreatePlcPage(Project project, PlcModule module)
        {
            var candidates = project.Pages
                .Where(p => p.Name != null && p.Name.StartsWith(module.PageStructureId, StringComparison.Ordinal))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            if (candidates.Count > 0)
                return candidates[candidates.Count - 1];

            throw new NotImplementedException(
                $"Не найдено ни одной страницы с идентификатором структуры '{module.PageStructureId}' " +
                $"в проекте — создание страницы с нуля ещё не реализовано (не хватило подтверждённых " +
                $"сведений про создание Location/страницы за одну сессию). Создайте вручную хотя бы одну " +
                $"страницу этого раздела в EPLAN, дальше вставка будет идти после неё автоматически.");
        }
    }
}
