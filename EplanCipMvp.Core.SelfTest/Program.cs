using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using EplanCipMvp.Core;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.Core.SelfTest
{
    /// <summary>
    /// Проверка чтения спецификации, группировки по модулям и построения подписей
    /// каналов — БЕЗ EPLAN. Запускается на любой машине с .NET.
    ///
    /// Использование:
    ///   EplanCipMvp.Core.SelfTest.exe "C:\путь\1364-Сводная_специфкация_изделий.xlsx"
    ///
    /// Ожидаемый результат (актуальный, по листу "Перечень устройств" — см. README
    /// про расхождение со старым листом "2. Сигналы по шкафам"):
    ///   ШРП-1: DI 6 модулей, DO 4, AI 3, AO 1
    ///   ШРП-2: DI 4, DO 3, AI 2, AO 1
    ///   ШУ:    DI 2, DO 1, AI 2, AO 0
    ///
    /// ШУ должен получить станцию "1" (там ЦП), ШРП-1/ШРП-2 — "2"/"3" по алфавиту —
    /// см. обозначения вида "-1A1.1" у модулей ШУ. Обвязка ПЧ должна найти 5 насосов
    /// (L1M1…L5M1) — см. раздел в конце вывода.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 23.09.2026: нумерация кабелей — чистая логика, без аргументов и EPLAN.
            CableNamingSelfTest.Run();

            // 24.09.2026: нумерация жил и проводов — чистая логика, без аргументов и EPLAN.
            WireNamingSelfTest.Run();

            // 12.09.2026: не требует аргумента/EPLAN — прогоняется всегда, первым.
            RunPumpParameterReaderSelfTest();

            if (args.Length < 1)
            {
                Console.WriteLine("Использование: EplanCipMvp.Core.SelfTest.exe <путь_к_1364-Сводная_специфкация_изделий.xlsx>");
                return;
            }

            string xlsxPath = args[0];

            Console.WriteLine("Читаю лист \"Перечень устройств\"...");
            var signals = SignalListReader.ReadDeviceList(xlsxPath);
            Console.WriteLine($"Прочитано каналов (сигналов): {signals.Count}\n");

            Console.WriteLine("Группирую по модулям ET200SP (с резервом 20%)...\n");
            // ШУ — шкаф с ЦП (см. PlcHardwareSettings.CpuCabinet в EplanCipMvp.App) —
            // задаём здесь тем же значением, иначе номер станции не совпадёт с App.
            var groupingOptions = new ModuleGrouperOptions { CpuCabinet = "ШУ" };
            var pageStructure = PageStructureSettings.Default();
            var modules = ModuleGrouper.GroupIntoModules(signals, groupingOptions, pageStructure);

            foreach (var cabinetGroup in modules.GroupBy(m => m.Cabinet))
            {
                Console.WriteLine($"=== {cabinetGroup.Key} (страница {pageStructure.BuildPageStructureId(cabinetGroup.Key)}) ===");
                foreach (var typeGroup in cabinetGroup.GroupBy(m => m.Type))
                {
                    int used = typeGroup.Sum(m => m.Channels.Count);
                    int capacity = typeGroup.Sum(m => m.ChannelCapacity);
                    Console.WriteLine($"  {typeGroup.Key}: {typeGroup.Count()} модулей " +
                                       $"(занято {used} из {capacity} каналов) — {typeGroup.First().ArticleNumber}");
                }
                Console.WriteLine();
            }

            Console.WriteLine("Сверьте числа модулей выше со значениями из README (НЕ с листом \"2\" — устарел).");
            Console.WriteLine("Если совпадает — группировка работает верно.\n");

            Console.WriteLine("=== Пример подписей каналов (ChannelLabelBuilder) ===");
            Console.WriteLine("Формат подписи — по образцу реальных страниц МСА (\"+LINE3-V0 Вода в бачок\").");
            var naming = NamingSettings.Default();
            var sampleModules = modules.Where(m => m.Channels.Count > 0).Take(3);
            foreach (var module in sampleModules)
            {
                Console.WriteLine($"  {module.DesignationHint}:");
                foreach (var ch in module.Channels.Take(3))
                {
                    Console.WriteLine($"    {ch.Tag,-8} -> {ChannelLabelBuilder.BuildLabel(ch, naming)}");
                }
            }

            Console.WriteLine("\n=== TBD-позиции (производитель/артикул не указаны) ===");
            var devices = SignalListReader.ReadDevices(xlsxPath);
            var tbdGroups = devices
                .Where(d => d.IsManufacturerTbd || d.IsOrderNumberTbd)
                .GroupBy(d => d.DeviceType);
            foreach (var g in tbdGroups)
                Console.WriteLine($"  {g.Key}: {g.Count()} шт.");
            Console.WriteLine("(Тут без подстановки дефолтов — та часть в EplanCipMvp.App, appsettings.json -> DeviceDefaults)");

            Console.WriteLine("\n=== Обвязка ПЧ (реально заказанный состав, не гипотеза) ===");
            var vfdCircuits = VfdCircuitBuilder.BuildAll(devices);
            if (vfdCircuits.Count == 0)
            {
                Console.WriteLine("Насосов с ПЧ не найдено.");
            }
            else
            {
                foreach (var c in vfdCircuits)
                    Console.WriteLine($"  {c.PumpTag} ({c.Cabinet}): {c.VfdVendor} {c.VfdArticle}" +
                                       (c.VfdIsDiscontinued ? " [СНЯТ С ПРОИЗВОДСТВА]" : "") +
                                       $", панель {c.ControlPanelArticle}, автомат {c.BreakerArticle}, доп. контакты {c.AuxContactArticle}");
            }

            Console.WriteLine("\nМожно переходить к EplanCipMvp.App (требует EPLAN).");
        }

        /// <summary>
        /// 12.09.2026: генерирует во временный xlsx лист с РЕАЛЬНЫМ набором
        /// заголовков из таблицы пользователя "Таблица Для Автоматизма Eplan"
        /// (Google Drive, на момент проверки — пустая, только заголовки) и
        /// проверяет, что PumpParameterReader читает его как ожидается. Задел
        /// под будущий автоподбор донор-страницы — см. план
        /// /home/node/.claude/plans/eventual-humming-charm.md. Никакого внешнего
        /// файла/EPLAN не требует — можно гонять на любой машине.
        /// </summary>
        private static void RunPumpParameterReaderSelfTest()
        {
            Console.WriteLine("=== Самопроверка PumpParameterReader (таблица параметров ПЧ) ===");

            string tempPath = Path.Combine(Path.GetTempPath(), "pump-params-selftest-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Лист1");
                    string[] headers =
                    {
                        "Модель ПЧ", "Характеристики технические", "Автоматический выключатель ОС",
                        "Магнитный пускатель", "Пуск DI", "Обратная связь DO", "Задание частоты AI",
                        "Обратная связь AO", "Программируемый вход DI", "Profibus", "Фото ПЧ",
                        "Модель двигателя", "Характеристики двигателя", "Температурная обратная связь",
                        "Количество Фаз",
                    };
                    for (int i = 0; i < headers.Length; i++)
                        ws.Cell(1, i + 1).Value = headers[i];

                    // Строка 2 — стиль заполнения "+/-", строка 3 — "да"/пусто, разные
                    // насосы: сознательно РАЗНЫЙ стиль по колонкам, т.к. реальный стиль
                    // заполнения пока не известен (таблица пользователя пуста).
                    string[] row1 = { "FC-51", "0.75 кВт, 3ф", "GV3P07", "+", "18", "-", "53", "-", "29", "-", "", "KLX-35-1", "0.75 кВт", "термоконтакт", "3" };
                    string[] row2 = { "ATV320", "1.5 кВт, 3ф", "GV3P16", "-", "18", "29", "53", "-", "", "да", "", "KLX-35-2", "1.5 кВт", "PTC", "3" };
                    for (int i = 0; i < row1.Length; i++) ws.Cell(2, i + 1).Value = row1[i];
                    for (int i = 0; i < row2.Length; i++) ws.Cell(3, i + 1).Value = row2[i];

                    wb.SaveAs(tempPath);
                }

                var rows = PumpParameterReader.ReadRows(tempPath);

                void Check(bool ok, string what)
                {
                    if (!ok) throw new InvalidOperationException("PumpParameterReader self-test FAILED: " + what);
                }

                Check(rows.Count == 2, $"ожидалось 2 строки, получено {rows.Count}");
                Check(rows[0].RowNumber == 2, "RowNumber первой строки должен быть 2");
                Check(rows[0].Tag == null, "Tag должен быть null — колонки-идентификатора в таблице пока нет");
                Check(rows[0].VfdModel == "FC-51", "VfdModel первой строки не распознан");
                Check(rows[0].Breaker == "GV3P07", "Breaker первой строки не распознан");
                Check(rows[0].MotorModel == "KLX-35-1", "MotorModel первой строки не распознан");
                Check(rows[0].ThermalFeedback == "термоконтакт", "ThermalFeedback первой строки не распознан");
                Check(rows[1].VfdModel == "ATV320", "VfdModel второй строки не распознан");
                Check(rows[1].Profibus == "да", "Profibus второй строки не распознан");
                Check(rows[0].RawColumns != null && rows[0].RawColumns["Модель ПЧ"] == "FC-51",
                    "RawColumns должен содержать те же данные, что и именованные свойства");

                Console.WriteLine("PumpParameterReader: OK (2 строки, известные и RawColumns-колонки распознаны верно).\n");
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* не критично */ }
            }
        }
    }
}
