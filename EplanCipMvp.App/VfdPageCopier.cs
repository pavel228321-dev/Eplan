using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanCipMvp.Core;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.App
{
    /// <summary>
    /// Отчёт по одной попытке копирования страницы для одного насоса с ПЧ.
    /// </summary>
    public class VfdPageCopyResult
    {
        public string PumpTag { get; set; }
        public bool PageCopied { get; set; }
        public string NewPageName { get; set; }
        public int PlacementsTotal { get; set; }

        /// <summary>Теги устройств, реально найденные на скопированной странице
        /// (FUNC_DEVICETAG_MAINNAME каждого Function), до попытки переименования.</summary>
        public List<string> FoundDeviceTags { get; set; } = new List<string>();

        /// <summary>Устройства, у которых проставлен FUNC_PREFIX (номер линии насоса)
        /// через Function.NameParts — см. класс. Не подтверждено живым тестом.</summary>
        public List<string> Retagged { get; set; } = new List<string>();

        /// <summary>Устройства, у которых артикул заменён на актуальный из
        /// спецификации 1364 (VfdComponentDefaults) — донор даёт только раскладку/
        /// стиль, а не комплектацию. "было(тег) -> новый артикул".</summary>
        public List<string> ArticlesSwapped { get; set; } = new List<string>();

        /// <summary>Устройства, для которых явного аналога в спецификации 1364 нет —
        /// требуют решения человека. Сейчас пусто в норме — было для KM/K, пока не
        /// подтвердили, что вся секция дискретного управления не нужна (см. Removed).</summary>
        public List<string> NoSpecMatch { get; set; } = new List<string>();

        /// <summary>Устройства, УДАЛЁННЫЕ со скопированной страницы (RemoveFromPage) —
        /// вся секция дискретного управления (пускатель, реле): у насосов 1364
        /// "Тип канала управления: Полевой (PN)", DI/DO/AI/AO = 0 (подтверждено по
        /// самой спецификации 1364-Сводная, 09.09.2026) — управление по Profinet,
        /// без единого дискретного сигнала. "тег: причина".</summary>
        public List<string> Removed { get; set; } = new List<string>();

        public string Error { get; set; }
    }

    /// <summary>
    /// Копирует лист "обвязка ПЧ" из проекта-донора (1260) в целевой проект (1364),
    /// по одному разу на каждый насос с ПЧ — вместо вставки макроса (Этап 0 так и не
    /// пройден: нет извлечённого эталонного макроса). Live-копирование через
    /// Page.CopyTo — подтверждённый метод API (см. XML-доки EplanCipMvp.DataModelu).
    ///
    /// 09.09.2026: разобрал реальную страницу-донор из PDF 1260 ("Насос -C01M01
    /// (ПЧ)", .M раздел) через pdftotext и предположил формат тега "-01QF01"
    /// (числовой префикс контура "01" + буквы типа + номер) — ЭТО ОКАЗАЛОСЬ
    /// НЕВЕРНО. Живой прогон на реальной странице показал FUNC_DEVICETAG_MAINNAME
    /// вида "QF1", "X1", "XPE1", "W1" — буквы типа ПЕРВЫЕ, никакого ведущего
    /// 2-значного числового префикса нет вообще (то "01" в PDF было частью имени
    /// страницы/структуры "=1.E01.M+CP.E01", не тега устройства). Старая логика
    /// "самый частый 2-значный токен = префикс контура" поэтому не находила вообще
    /// ничего и переименование всегда проваливалось.
    ///
    /// 09.09.2026, живой тест №2: попытка "косметического" переименования через
    /// `Properties[Properties.Function.FUNC_DEVICETAG_MAINNAME] = ...` провалилась —
    /// на 100% объектов, с ошибкой "S063113 Error setting a new property value".
    /// Причина: это свойство read-only (вычисляемое отображение тега, не хранимое
    /// значение) — писать в него через простой индексатор нельзя вообще.
    ///
    /// 10.09.2026: нашёл реальный обходной путь — тот же паттерн, что и у
    /// `Page`/`PAGE_COUNTER` (тоже read-only через простой индексатор, но
    /// переименование возможно через `Page.NameParts`). Разобрал сборку через
    /// `monodis` (IL), не только доки: `Eplan.EplApi.DataModel.Function` РЕАЛЬНО
    /// наследует `Eplan.EplApi.DataModel.FunctionBase`, у которого подтверждены в IL
    /// (не только в докладах) методы `get_NameParts`/`set_NameParts` (тип
    /// `FunctionBasePropertyList`, публичные, не только через явную реализацию
    /// интерфейса — вызываются прямо как `function.NameParts`). А в самом
    /// `FunctionBasePropertyList` подтверждены (тоже по IL) сеттеры `FUNC_PREFIX`/
    /// `FUNC_CODE`/`FUNC_COUNTER` — ровно то же самое разделение тега на составные
    /// части, что и у Page (PLANT/LOCATION/PAGE_COUNTER). По докам `FUNC_PREFIX`:
    /// "you receive a DT '4K5' with the digit '4' as the prefix" — т.е. видимый тег
    /// = PREFIX + CODE + COUNTER. Значит переименование = задать ТОЛЬКО FUNC_PREFIX
    /// (номер линии насоса), не трогая CODE ("QF"/"U"/"M") и COUNTER (его уже сам
    /// EPLAN раздаёт через NumerationMode.Number при копировании, без коллизий).
    /// Не пройдено живым тестом — компилируется против реальных сборок, но
    /// поведение на реальном проекте не подтверждено, ждём следующего прогона.
    ///
    /// Также нашлась причина ошибок "S063074 Cannot add the part" при замене
    /// артикула: `Function.IsMainFunction` — "Only main functions of a device can
    /// have article information". У одного устройства несколько Function-объектов
    /// (клеммы, доп. контакты — "auxiliary" функции), артикул можно проставить
    /// только на главной. Раньше пытались на каждой — отсюда куча дублей ошибок.
    /// Исправлено: замена артикула теперь только для `IsMainFunction == true`.
    ///
    /// 09.09.2026: пользователь поправил — при копировании из 1260 комплектация
    /// (артикулы) ВСЕГДА берётся из спецификации 1364 (VfdComponentDefaults), а не
    /// с донорской страницы. 1260 даёт только раскладку/стиль. Поэтому для функций
    /// с типом "U" (ПЧ) и "QF" (автомат) артикул на копии автоматически заменяется
    /// на актуальный (Danfoss FC-051.../GV3P32) через RemoveArticleReference +
    /// AddArticleReference — тоже не пройдено живым тестом.
    ///
    /// 09.09.2026, позже в тот же день: РЕШЁН вопрос про пускатель (KM) и реле (K),
    /// который раньше был "требует решения человека". Проверил напрямую строку
    /// L1M1..L5M1 в 1364-Сводная_специфкация_изделий.xlsx: "Тип канала управления":
    /// "Полевой (PN)", DI:0 DO:0 AI:0 AO:0 — управление ПЧ у 1364 идёт ПО PROFINET,
    /// без единого дискретного/аналогового сигнала. Сравнил с МСА (образец
    /// заказчика, тот же Danfoss FC51!) — там тоже дискретная проводка (DIN1 пуск,
    /// AI1 скорость, встроенное реле R01 ПЧ вместо внешнего K), просто без внешнего
    /// пускателя. Значит ни вариант 1260 (внешние KM+K), ни вариант МСА (встроенное
    /// реле ПЧ) не подходят буквально — у 1364 просто НЕТ этой секции вообще, только
    /// силовая часть (автомат+ПЧ+двигатель+кабель) и один сетевой кабель Profinet,
    /// которого ни на одной из донорских страниц нет и который эта программа не
    /// рисует. Поэтому теперь функции с типом "KM"/"K" не пытаемся сопоставить
    /// артикулом — вызываем Function.RemoveFromPage() и убираем их с копии совсем.
    /// </summary>
    public class VfdPageCopier
    {
        private static readonly Regex LeadingLetters = new Regex(@"^[A-Za-z]+");
        private static readonly Regex AnyDigits = new Regex(@"\d+");

        /// <summary>Роли, которых у 1364 просто не существует на этой странице —
        /// подтверждено спецификацией (Полевой (PN), DI/DO/AI/AO=0): пускатель (KM)
        /// и любое релейное звено обратной связи (K), что во внешнем исполнении (1260),
        /// что через встроенное реле ПЧ (МСА) — не важно, весь дискретный слой
        /// заменяется одним кабелем Profinet, которого эта программа не рисует.
        ///
        /// 10.09.2026: пользователь поправил — помимо KM/K, на странице есть ещё
        /// метки/связи модуля ввода-вывода контроллера (тип "A", в живом логе —
        /// "A4:13", "A5:5", "A5:6", "A6:5", "A6:6" и т.п.: старт/стоп, задание
        /// скорости, обратная связь по дискретным/аналоговым каналам). Раз всё это
        /// заменяется Profinet-кабелем — та же логика, что и для KM/K, добавлено сюда.
        /// Провода (тип "W"), идущие к этим меткам, не трогаем — RemoveFromPage
        /// уберёт только конечную точку, провод останется "подвешенным" графически,
        /// это безопаснее слепого удаления и правится вручную в EPLAN при необходимости.</summary>
        private static readonly HashSet<string> NotNeededTypes = new HashSet<string> { "KM", "K", "A" };

        /// <summary>
        /// Тип устройства (буквенная часть тега после числового префикса, IEC-стиль:
        /// U=преобразователь, QF=автомат, M=сам двигатель) -> откуда брать актуальный
        /// артикул в VfdComponentDefaults. 1260 даёт только раскладку — комплектация
        /// всегда из спецификации 1364.
        ///   U  (ПЧ)      -> Danfoss FC-051...    высокая уверенность (прямая замена в спецификации)
        ///   QF (автомат) -> Schneider GV3P32      высокая уверенность (прямо назван "для ПЧ")
        ///   M  (сам насос/двигатель) -> не трогаем: оборудование заказчика, только тег
        ///   KM, K -> см. NotNeededTypes — удаляются со страницы, не переносятся вообще
        /// </summary>
        private static string ResolveTargetArticle(string typeLetters, VfdComponentDefaults defaults)
        {
            // AddArticleReference ищет по номеру артикула в базе изделий EPLAN —
            // передаём только сам номер, не "вендор артикул" одной строкой.
            switch (typeLetters)
            {
                case "U": return defaults.VfdArticle;
                case "QF": return defaults.BreakerArticle;
                default: return null;
            }
        }

        /// <summary>
        /// Ищет в проекте-доноре страницы, похожие на раздел моторов/ПЧ — по имени,
        /// содержащему ".M" (подтверждено структурой проекта 1260, где раздел моторов
        /// имеет идентификатор вида "=1.E01.M"). Список для выбора пользователем в GUI —
        /// НЕ автовыбор, т.к. точное имя раздела в вашем 1260 не подтверждено напрямую.
        /// </summary>
        public static List<Page> FindCandidateDonorPages(Project sourceProject)
        {
            return sourceProject.Pages
                .Where(p => p.Name != null && p.Name.Contains(".M"))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// 10.09.2026: пользователь поправил — мощность на подписи не должна быть
        /// захардкожена (как было с MotorPowerLabel="11kW" на все 5 насосов), а должна
        /// читаться из самой спецификации, по каждому насосу отдельно (столбец "Power"
        /// в "Перечне устройств"). Тот же принцип, что уже применялся к артикулам:
        /// "донор даёт только раскладку/стиль, комплектация и характеристики — из 1364".
        /// Раз в спецификации мощность написана как "11 кВт" (кириллица, с пробелом), а
        /// на самом чертеже донор использует стиль "7,5kW" (латиница, без пробела) —
        /// нормализуем формат, не меняя число.
        /// </summary>
        internal static string NormalizePowerLabel(string rawSpecPower)
        {
            if (string.IsNullOrWhiteSpace(rawSpecPower))
                return null;

            var match = Regex.Match(rawSpecPower, @"\d+([.,]\d+)?");
            return match.Success ? match.Value + "kW" : null;
        }

        public VfdPageCopyResult CopyForPump(Page donorPage, Project targetProject, string pumpTag,
            VfdComponentDefaults defaults = null, string motorPowerLabel = null)
        {
            defaults = defaults ?? VfdComponentDefaults.Default();
            motorPowerLabel = !string.IsNullOrWhiteSpace(motorPowerLabel) ? motorPowerLabel : defaults.MotorPowerLabel;
            var result = new VfdPageCopyResult { PumpTag = pumpTag };

            try
            {
                // 09.09.2026: пустой PagePropertyList() означает "оставить точное имя
                // донора" — при копировании ОДНОЙ и той же донорской страницы для
                // L1M1..L5M1 все 5 попыток претендовали на одно и то же имя, и все,
                // кроме первой, получали null (страница уже существует,
                // overwrite=false). Фикс — задать PAGE_COUNTER (номер страницы,
                // "/19" в "=1.E01.M+CP.E01/19") явно и разным для каждой попытки;
                // PLANT/LOCATION не трогаем — они наследуются от донора (подтверждено
                // документацией: PagePropertyList.PAGE_COUNTER можно проставить
                // прямым присваиванием, см. пример в XML-докax Page.NameParts).
                // Перебираем номера, пока CopyTo не вернёт не-null (на случай, если
                // какой-то номер уже занят в целевом проекте по другой причине).
                //
                // 10.09.2026: пользователь прислал скриншот дерева страниц в 1360_Cheez —
                // ВСЕ скопированные страницы (2..16) показывают одно и то же описание
                // "Motor 12M02" — это буквально описание донорской страницы (свойство
                // "Page description", # 11011), которое CopyTo копирует как есть, не
                // меняя. Нашёл в докладах: `PagePropertyList.PAGE_NOMINATIOMN` — тот же
                // паттерн, что и PAGE_COUNTER (часть PagePropertyList, передаваемого в
                // CopyTo), тип `MultiLangString`, НЕ отмечено read-only, есть метод
                // `SetAsString(string)`. Раньше в этом же 09.09 анализе текста донора
                // (PDF 1260, .M раздел) нашлась формулировка заголовка конкретной
                // страницы-донора: "Насос -C01M01 (ПЧ)" — берём тот же стиль, но со своим
                // тегом насоса вместо донорского "C01M01".
                var pageDescription = new MultiLangString();
                pageDescription.SetAsString($"Насос -{pumpTag} (ПЧ)");

                // 10.09.2026, позже: живой тест — PAGE_COUNTER сработал (уникальные /2../6),
                // а PAGE_NOMINATIOMN — нет, "Motor 12M02" осталось на всех копиях, хотя
                // передавали его в ТОТ ЖЕ PagePropertyList, что и counter. Нашёл причину в
                // докладах Page.CopyTo: параметр называется "pPageName" — "List of property
                // defining NAME of new page". Т.е. этот PagePropertyList используется CopyTo
                // ТОЛЬКО для составления имени страницы (PLANT/LOCATION/COUNTER/SUBCOUNTER),
                // остальные поля (в т.ч. описание) он молча игнорирует, хотя формально они —
                // члены того же класса. Пример из доков Page.Properties показывает прямое
                // присваивание свойств живой странице после её создания
                // ("page.Properties.PAGE_REVISION_APPROVEDBY = ...") — по этому же паттерну
                // ставим описание ПОСЛЕ CopyTo, на уже созданный объект `copied`, а не через
                // параметр копирования.
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

                // 10.09.2026: после многих кругов "лог говорит успех, а тег на странице не
                // меняется" нашёл причину — прямое присваивание `function.NameParts = ...`
                // не то же самое, что переименование тега вручную в GUI. По докладам класса
                // `Eplan.EplApi.HEServices.NameService`: "When using EPLAN interactively, the
                // system keeps track that the structure identifiers of a Function... are
                // adjusted according to the page and according to location boxes or black
                // boxes... In API, the methods of the NameService class help you to do this."
                // То есть простое присваивание свойства НЕ прогоняет эту донастройку
                // (структура/боксы/adoption) — нужен выделенный сервис. Метод
                // `NameService.RenameDevice(Function, FunctionBasePropertyList, bool, bool)` —
                // именно то, чем реально пользуется сама EPLAN при переименовании тега.
                RetagPumpPage(copied, pumpTag, defaults, motorPowerLabel, result);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>12.09.2026: перетэговка/замена артикулов на УЖЕ существующей
        /// (`copied`) странице — дословный перенос содержимого CopyForPump ПОСЛЕ
        /// CopyTo. Отдельный метод нужен для реконсиляции после полного копирования
        /// проекта (см. PageReconciler/RetagExistingPumpPage ниже): NameService и
        /// Function не различают, свежескопирована страница или уже существовала —
        /// значит логику можно переиспользовать без изменений на "не только что
        /// скопированной" странице. Поведение потока CopyForPump не меняется — это
        /// чистое извлечение, без правок самой логики.</summary>
        private void RetagPumpPage(Page copied, string pumpTag, VfdComponentDefaults defaults,
            string motorPowerLabel, VfdPageCopyResult result)
        {
            try
            {
                var nameService = new NameService(copied);

                var placements = copied.AllPlacements ?? new StorableObject[0];
                result.PlacementsTotal = placements.Length;

                // Шаг 1: собрать текущие MAINNAME всех Function на копии.
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

                // Шаг 2: префикс линии целевого насоса, из тега вида "L1M1" -> "L1".
                // 10.09.2026: пользователь прислал эталонный P&ID (CIP1_1_.pdf,
                // СОАО "Беловежские сыры") — на нём ВСЕ приборы линии (не только
                // насос) помечены единой схемой "L{номер линии}{Тип}{номер}":
                // L1M1, L1FQT1, L1ТЕ1, L1FS1, L1QT1, L1LS1..3, L1V1.. — то есть
                // префикс включает букву "L", а не только цифру. Раньше сюда
                // попадала голая цифра ("1") — не совпадало бы с реальным принятым
                // на объекте обозначением линии. FUNC_PREFIX — обычная строка
                // (см. класс), буква в ней ничем не хуже цифры.
                var targetMatch = AnyDigits.Match(pumpTag);
                string targetPrefix = targetMatch.Success ? "L" + targetMatch.Value : null;

                // Шаг 3: тип устройства — буквы в начале MAINNAME донора ("QF1" -> "QF",
                // "XPE1" -> "XPE"). Если роль не нужна у 1364 (KM/K/A — Profinet, см.
                // класс), убираем объект со страницы целиком и не трогаем ни тег, ни
                // артикул — они всё равно исчезают.
                foreach (var kvp in mainNames)
                {
                    var function = kvp.Key;
                    var oldName = kvp.Value;

                    var letterMatch = LeadingLetters.Match(oldName);
                    string typeLetters = letterMatch.Success ? letterMatch.Value : null;

                    if (typeLetters != null && NotNeededTypes.Contains(typeLetters))
                    {
                        try
                        {
                            function.RemoveFromPage();
                            result.Removed.Add($"{oldName} ({typeLetters}): управление по Profinet у 1364 — дискретная секция не нужна");
                        }
                        catch (Exception ex)
                        {
                            result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                            $"remove '{oldName}' failed: {ex.Message}";
                        }
                        continue;
                    }

                    // Переименование — только FUNC_PREFIX (номер линии насоса), не
                    // трогая CODE/COUNTER. Только на главной функции устройства (как
                    // и артикул ниже) — у auxiliary-функций (клемм) своя NameParts
                    // обычно вычисляется от главной, трогать её отдельно не нужно.
                    //
                    // 10.09.2026: пользователь прислал скриншот реальной страницы-донора
                    // из 1260 — там тег вида "-03QF01"/"-03U01"/"-W03M01" (у мотора вообще
                    // "-C03M01"), то есть префикс контура ("03") у донора УЖЕ прописан.
                    // Но FUNC_DEVICETAG_MAINNAME в нашем живом логе показал только "QF1",
                    // "U1" и т.п. БЕЗ этого префикса — то есть MAINNAME похоже вообще НЕ
                    // включает FUNC_PREFIX (это отдельное поле тега, не часть "основного
                    // имени"). Значит: (a) по логам нельзя было судить, пуст ли префикс у
                    // донора на самом деле — наоборот, скорее всего он там был, просто не
                    // виден через MAINNAME; (b) раньше здесь создавался СОВСЕМ НОВЫЙ
                    // FunctionBasePropertyList с одним только FUNC_PREFIX — если сеттер
                    // трактует непроставленные поля как "очистить", это стирало бы CODE/
                    // COUNTER донора вместо того чтобы просто поменять префикс. Теперь
                    // сначала читаем ТЕКУЩИЙ NameParts (значит CODE/COUNTER донора точно
                    // сохранятся) и правим в нём только FUNC_PREFIX.
                    //
                    // 10.09.2026, позже: живой лог показал тег вида "?C8" — попал в
                    // oldName/FoundDeviceTags на первом проходе, до нашего вмешательства.
                    // Первая гипотеза (ниже, теперь отвергнута живым тестом): "?" —
                    // означает непроставленный FUNC_COUNTER, форсировали его сами.
                    // Живой тест это НЕ подтвердил: лог рапортовал успех
                    // ("?C1 -> префикс 'L1' + принудительный counter '1'"), но на самом
                    // чертеже осталось "-?C1" — то есть FUNC_COUNTER-подход тихо "работал"
                    // (не бросал исключение), но реального эффекта не давал.
                    //
                    // 10.09.2026, ещё позже: пользователь прислал скриншот диалога
                    // Properties этого самого объекта прямо из EPLAN — разгадка другая
                    // целиком: чекбокс "Main function" СНЯТ, и есть отдельный блок "DT
                    // adoption: Search direction — According to orientation of plot frame".
                    // По докам (# 12105, # 20035): DT adoption — это механизм, по которому
                    // функция БЕЗ собственного самостоятельного тега "наследует" его от
                    // окружающей рамки/бокса (search по направлению относительно рамки
                    // листа); "?" — это EPLAN сообщает, что для ЭТОГО объекта разрешить DT
                    // adoption не удалось (не нашёл, у кого наследовать). Дело не в
                    // FUNC_COUNTER вообще — FUNC_PREFIX/COUNTER просто не тот API для
                    // объекта, который не является самостоятельной главной функцией.
                    //
                    // Фикс: раз IsMainFunction — settable ("Gets/Sets a flag..." по докам),
                    // для тегов вида "?<латинский код>" сначала пробуем включить
                    // IsMainFunction = true (сделать функцию самостоятельной, а не
                    // наследующей от рамки) — и только ПОСЛЕ этого применяем обычную
                    // FUNC_PREFIX-логику (та же, что уже подтверждена живым тестом для
                    // QF/U). Если EPLAN не разрешит (InvalidOperationException по докам —
                    // "modifying on a non covered function template"), увидим явную ошибку
                    // в логе вместо тихого "успеха" без результата, как было с counter.
                    //
                    // "?Провод1" (обычные безымянные провода, донор их так всегда
                    // показывает, это нормально) по-прежнему не трогаем — после "?" у них
                    // кириллица, не код типа устройства.
                    var strippedForType = oldName.TrimStart('?');
                    bool looksLikeRealDeviceCode = LeadingLetters.IsMatch(strippedForType);
                    bool wasUnresolved = oldName.StartsWith("?") && looksLikeRealDeviceCode;
                    bool isMain = function.IsMainFunction;

                    // 10.09.2026: живой тест подтвердил NameService.RenameDevice для обычных
                    // устройств (QF/U/W — префикс встал правильно), но "?C1" остался "?C1"
                    // (префикс подхватился, а "?" — нет). Причина та же категория бага, что
                    // была у NameParts: прямое присваивание `function.IsMainFunction = true`
                    // не бросает исключение, но реально не проводит объект через нужную
                    // внутреннюю логику. Нашёл отдельный сервисный метод — тот же паттерн,
                    // что и с NameService: `Eplan.EplApi.HEServices.DeviceService.
                    // AssignMainFunction(Function, bool, bool)` — "Converts auxiliary function
                    // into main function... The original main function is then converted to
                    // an auxiliary function." Именно то, что нужно, через выделенный сервис.
                    if (wasUnresolved && !isMain)
                    {
                        try
                        {
                            isMain = new DeviceService().AssignMainFunction(function, false, true);
                        }
                        catch (Exception ex)
                        {
                            result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                            $"AssignMainFunction for '{oldName}' (DT adoption не резолвится) failed: {ex.Message}";
                        }
                    }

                    if (isMain && targetPrefix != null)
                    {
                        try
                        {
                            var nameParts = function.NameParts;
                            nameParts.FUNC_PREFIX = targetPrefix;
                            // 10.09.2026: было `function.NameParts = nameParts;` — компилировалось
                            // и не бросало исключение, но реально тег на странице не менялся (см.
                            // комментарий у NameService выше). Теперь — через выделенный сервис,
                            // который и использует сама EPLAN при переименовании тега.
                            // bRenameCDPsAlso=false — провода отдельно уже переименовываем сами
                            // (или не трогаем "?Провод1"), не нужно задваивать. bKeepDescribingProps
                            // =true — не трогаем описание/функциональный текст устройства.
                            nameService.RenameDevice(function, nameParts, false, true);
                            result.Retagged.Add(wasUnresolved
                                ? $"{oldName} -> IsMainFunction=true + префикс '{targetPrefix}' через NameService.RenameDevice (был '?' — DT adoption не резолвился, см. класс)"
                                : $"{oldName} -> префикс '{targetPrefix}' через NameService.RenameDevice");
                        }
                        catch (Exception ex)
                        {
                            result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                            $"retag '{oldName}' failed: {ex.Message}";
                        }
                    }

                    // 10.09.2026: пользователь сверил результат с реальной спецификацией —
                    // мотор у донора (1260) был на 7,5кВт, у 1364 — 11кВт, а подпись
                    // "7,5kW" на моторе и на блоке самого ПЧ копируется дословно, не
                    // пересчитывается. Нашёл поле `Properties.Function.
                    // FUNC_TECHNICAL_CHARACTERISTIC` (# 20027) — НЕ отмечено read-only в
                    // докладах (в отличие от FUNC_PREFIX и подобных), значит должно
                    // работать через простой индексатор, без NameService. Правим и на
                    // моторе (typeLetters==null, wasUnresolved — это "?C1"), и на самом
                    // ПЧ (typeLetters=="U").
                    if (isMain && (wasUnresolved || typeLetters == "U"))
                    {
                        try
                        {
                            function.Properties[Properties.Function.FUNC_TECHNICAL_CHARACTERISTIC] = motorPowerLabel;
                            result.Retagged.Add($"{oldName}: мощность на подписи -> '{motorPowerLabel}'");
                        }
                        catch (Exception ex)
                        {
                            result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                            $"set power label for '{oldName}' failed: {ex.Message}";
                        }
                    }

                    // Комплектация (артикул) — всегда из спецификации 1364, не с
                    // донорской страницы (1260 даёт только раскладку/стиль). Только
                    // на главной функции устройства — подтверждено (см. класс):
                    // "Only main functions of a device can have article information",
                    // на клеммах/доп.контактах (auxiliary) EPLAN артикул не принимает.
                    if (typeLetters == null || !function.IsMainFunction)
                        continue;

                    string targetArticle = ResolveTargetArticle(typeLetters, defaults);
                    if (targetArticle == null)
                        continue; // "M" (сам насос) — ожидаемо, оборудование заказчика

                    try
                    {
                        foreach (var existing in function.ArticleReferences ?? new ArticleReference[0])
                            function.RemoveArticleReference(existing);
                        function.AddArticleReference(targetArticle);
                        result.ArticlesSwapped.Add($"{oldName} -> {targetArticle}");
                    }
                    catch (Exception ex)
                    {
                        result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                        $"article swap for '{oldName}' failed: {ex.Message}";
                    }
                }

                // Шаг 4: свободные текстовые подписи ("Пуск ПЧ 01U01 (Насос C01M01)",
                // "Задание скорости ПЧ...", "Состояние пускателя...", "Включение
                // пускателя...", "Режим \"Ход\" ПЧ...") — это НЕ Function, а отдельные
                // Text-объекты (Eplan.EplApi.DataModel.Graphics.Text), донорский текст
                // копируется дословно и раньше не трогался вообще (см. класс,
                // 10.09.2026: пользователь прислал PDF-экспорт результата — на всех
                // страницах видна одна и та же донорская подпись "...C01M01)").
                //
                // 10.09.2026, позже: пользователь подтвердил — в 1364 нет магнитного
                // пускателя (KM) вообще (проверено по 1364_04-спец_заказ_ПЧ и
                // 1364_03_спец_заказ_НКУ — там только автомат GV3P32 + доп. контакт
                // GVAN11 на САМ автомат "для диагностики", ни одного пускателя в
                // закупке). Значит вся дискретная секция управления ПЧ (пуск/скорость/
                // обратная связь "Ход") ТОЖЕ убирается — она заменяется Profinet точно
                // так же, как и KM/K. А раз вся эта электрическая часть убирается
                // целиком (Function.RemoveFromPage через NotNeededTypes выше), то и
                // текстовые подписи к ней — переименовывать некого, физически убираем
                // вместе с ней, а не подгоняем под новый насос.
                //
                // Найдено по докладам: все такие подписи содержат "(Насос " — это
                // единственный надёжный маркер, который отличает их от заголовка
                // страницы/штампа (там этой фразы нет). Свойство `.Contents`
                // (MultiLangString) НЕ отмечено read-only, удаление — через общий
                // `Placement.Remove()` (базовый метод, доступный и для Function, и
                // для Text — не только `Function.RemoveFromPage()`).
                foreach (var text in placements.OfType<Text>())
                {
                    string content;
                    try
                    {
                        content = text.Contents?.GetAsString() ?? "";
                    }
                    catch (Exception ex)
                    {
                        result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                        $"read text content failed: {ex.Message}";
                        continue;
                    }

                    if (!content.Contains("(Насос "))
                        continue;

                    try
                    {
                        text.Remove();
                        result.Removed.Add($"текст '{content.Trim()}': относится к убранной дискретной секции (KM/ПЧ по Profinet)");
                    }
                    catch (Exception ex)
                    {
                        result.Error = (result.Error == null ? "" : result.Error + " | ") +
                                        $"remove text '{content.Trim()}' failed: {ex.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = (result.Error == null ? "" : result.Error + " | ") + ex.Message;
            }
        }

        /// <summary>12.09.2026: перетэговка страницы, УЖЕ существующей в целевом
        /// проекте (например, после полного копирования донора через
        /// ProjectManager.CopyProject) — в отличие от CopyForPump, страницу НЕ
        /// создаёт (нет CopyTo), только применяет ту же перетэговку/замену
        /// артикулов к уже данному Page. Используется PageReconciler (см. план
        /// /home/node/.claude/plans/eventual-humming-charm.md).</summary>
        public VfdPageCopyResult RetagExistingPumpPage(Page existingPage, string pumpTag,
            VfdComponentDefaults defaults = null, string motorPowerLabel = null)
        {
            defaults = defaults ?? VfdComponentDefaults.Default();
            motorPowerLabel = !string.IsNullOrWhiteSpace(motorPowerLabel) ? motorPowerLabel : defaults.MotorPowerLabel;
            var result = new VfdPageCopyResult { PumpTag = pumpTag, PageCopied = true, NewPageName = existingPage.Name };
            RetagPumpPage(existingPage, pumpTag, defaults, motorPowerLabel, result);
            return result;
        }

        public List<VfdPageCopyResult> CopyForAllPumps(Page donorPage, Project targetProject, IEnumerable<string> pumpTags, VfdComponentDefaults defaults = null)
        {
            var results = new List<VfdPageCopyResult>();
            foreach (var tag in pumpTags)
                results.Add(CopyForPump(donorPage, targetProject, tag, defaults));
            return results;
        }

        /// <summary>10.09.2026: вариант, который берёт мощность (и в перспективе прочие
        /// характеристики) на каждый насос отдельно из VfdCircuit (т.е. из самой
        /// спецификации), а не одно захардкоженное значение на все контуры сразу.</summary>
        public List<VfdPageCopyResult> CopyForAllPumps(Page donorPage, Project targetProject, IEnumerable<VfdCircuit> circuits, VfdComponentDefaults defaults = null)
        {
            var results = new List<VfdPageCopyResult>();
            foreach (var circuit in circuits)
            {
                string motorPowerLabel = NormalizePowerLabel(circuit.Power);
                results.Add(CopyForPump(donorPage, targetProject, circuit.PumpTag, defaults, motorPowerLabel));
            }
            return results;
        }
    }
}
