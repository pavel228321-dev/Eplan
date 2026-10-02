using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.Starter;
using Eplan.EplApi.System;
using EplanCipMvp.Core;
using Microsoft.Extensions.Configuration;

namespace EplanCipMvp.App
{
    class Program
    {
        // 09.09.2026: подтверждено по реальным XML-докам EPLAN 2.9 (не по общей
        // документации eplan.help, как раньше) — офлайн-приложения ОБЯЗАНЫ быть
        // однопоточными (COM) и помечены [STAThread], иначе Init может падать
        // непредсказуемо. Раньше этого атрибута не было вообще.
        [STAThread]
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var settings = LoadSettings();

            // 02.10.2026: "export-all" — отдельная команда (полная вычитка проекта +
            // заливка на Google Drive), не трогает основной пайплайн нумерации модулей
            // ниже. Вызов: EplanCipMvp.exe export-all [путь_к_проекту].
            if (args.Length > 0 && args[0] == "export-all")
            {
                string exportProjectPath = args.Length > 1 ? args[1] : settings.Eplan.ProjectPath;
                RunExportAll(settings, exportProjectPath);
                return;
            }

            // Единый источник истины для "какой шкаф с ЦП" — PlcHardware.CpuCabinet;
            // ModuleGrouper нужен тот же факт, чтобы дать этой станции номер "1".
            settings.Grouping.CpuCabinet = settings.PlcHardware.CpuCabinet;

            // args остаются как быстрый оверрайд без правки json — удобно для разовых прогонов
            string xlsxPath = args.Length > 0 ? args[0] : settings.Spec.XlsxPath;
            string projectPath = args.Length > 1 ? args[1] : settings.Eplan.ProjectPath;

            Console.WriteLine("=== Настройки ===");
            Console.WriteLine($"  Спецификация:     {xlsxPath}");
            Console.WriteLine($"  Проект EPLAN:     {projectPath}");
            Console.WriteLine($"  Макросы:          {settings.Eplan.MacroBasePath}");
            Console.WriteLine($"  ЦП:               {settings.PlcHardware.CpuDescription} " +
                               $"({settings.PlcHardware.CpuArticle}) — предположительно в шкафу {settings.PlcHardware.CpuCabinet}");
            Console.WriteLine();

            Console.WriteLine("=== Шаг 1: чтение спецификации ===");
            var signals = SignalListReader.ReadDeviceList(xlsxPath, settings.Spec.SheetName);
            Console.WriteLine($"Прочитано каналов: {signals.Count}");

            Console.WriteLine("\n=== Шаг 2: группировка по модулям ET200SP ===");
            var modules = ModuleGrouper.GroupIntoModules(signals, settings.Grouping, settings.PageStructure);
            foreach (var m in modules)
                Console.WriteLine($"  {m.DesignationHint} — занято {m.Channels.Count}/{m.ChannelCapacity}, страница {m.PageStructureId}");

            PrintOrderReconciliation(modules, settings.PlcHardware);
            PrintLabelSamples(modules, settings.Naming);

            Console.WriteLine("\n=== Шаг 2.5: устройства технологической части (задел на Этап 3) ===");
            var devices = SignalListReader.ReadDevices(xlsxPath, settings.Spec.SheetName);
            PrintArticleResolution(devices, settings.DeviceDefaults);
            PrintSymbolMacroChecklist(devices, settings.DeviceDefaults);

            Console.WriteLine("\n=== Шаг 2.6: обвязка ПЧ (начали с этого узла) ===");
            var vfdCircuits = VfdCircuitBuilder.BuildAll(devices, settings.VfdDefaults);
            PrintVfdChecklist(vfdCircuits);

            Console.WriteLine("\n=== Шаг 3: подключение к EPLAN ===");
            // Реальная (не угаданная) последовательность запуска офлайн-приложения,
            // по XML-докам сборок Starteru/Systemu из вашей EPLAN 2.9:
            //   1. AssemblyResolver — чтобы рантайм вообще нашёл Eplan.EplApi.*.dll
            //      (без этого дальнейшие вызовы просто не загрузятся).
            //   2. EplApplication.EplanBinFolder — путь к w3u.exe вашего варианта
            //      (ОБЯЗАТЕЛЬНО до Init, иначе тоже падает).
            //   3. Init(strApplicationModifier, allowLicenseDialog, allowLoginDialog).
            // strApplicationModifier — ТОЧНОЕ значение не подтверждено (в докладах
            // нет примера конкретной строки), начинаем с "" (стандартная конфигурация).
            // Если Init бросит исключение — пришлите текст, поправим значение.
            var resolver = new AssemblyResolver();
            resolver.SetEplanBinPath(settings.Eplan.BinPath);

            var app = new EplApplication();
            app.EplanBinFolder = settings.Eplan.BinPath;
            app.Init("", true, false);

            Console.WriteLine("Открываю проект...");
            // new Project(path) — НЕПРАВИЛЬНО: конструктор Project всегда бросает
            // NotImplementedException (см. его собственный XML-документ — "Should
            // never be used"). Открывать проект нужно только через ProjectManager.
            var projectManager = new ProjectManager();
            var project = projectManager.OpenProject(projectPath);

            Console.WriteLine("\n=== Шаг 4: расстановка модулей на страницах ПЛК ===");
            var builder = new PlcPageBuilder(settings.Eplan.MacroBasePath, settings.Naming);
            builder.BuildModulePages(project, modules);

            // У Project нет метода Save() — модель EPLAN транзакционная, изменения
            // применяются по ходу дела. Закрыть проект (снять блокировку) — Close().
            Console.WriteLine("\nЗакрываю проект...");
            project.Close();

            Console.WriteLine("Готово.");
        }

        /// <summary>Полная вычитка проекта (устройства/артикулы, PLC-адреса, провода,
        /// кабели, страницы) -> локальная папка с JSON-файлами -> заливка на Google Drive
        /// (отдельный сервисный аккаунт, не личный OAuth пользователя).</summary>
        private static void RunExportAll(AppSettings settings, string projectPath)
        {
            Console.WriteLine("=== export-all: подключение к EPLAN ===");
            Console.WriteLine($"  Проект: {projectPath}");

            // 02.10.2026: раньше здесь была "сырая" инициализация (как в начале Main) —
            // она ловит "The Variant ... is not valid", если BinPath указывает на
            // Platform\<версия>\Bin вместо папки варианта (Electric P8\<версия>\Bin,
            // где лежит W3u.exe). EplanBootstrap.PinOnce — уже проверенный живым
            // запуском способ (тот же, что использует EplanCipMvp.Gui), переиспользуем его.
            string binPathError = EplanBootstrap.CheckBinPath(settings.Eplan.BinPath);
            if (binPathError != null)
            {
                Console.WriteLine("ОШИБКА: " + binPathError);
                return;
            }
            EplanBootstrap.PinOnce(settings.Eplan.BinPath);

            // 02.10.2026: ловушка №3 из EplanBootstrap.cs — PinOnce должен отработать ДО
            // JIT-компиляции любого метода, упоминающего типы EPLAN. Если весь код ниже
            // (new EplApplication, Init, ProjectManager...) лежит в ЭТОМ ЖЕ методе, JIT
            // компилирует его целиком ДО выполнения строки PinOnce выше — отсюда падение
            // "$(CFG_VARIANT)\install.xml doesn't exist!" (как и в EplanCipMvp.Gui,
            // MainForm.cs:417-418, вызов вынесен в отдельный NoInlining-метод).
            RunExportAllConnected(settings, projectPath);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunExportAllConnected(AppSettings settings, string projectPath)
        {
            var app = new EplApplication();
            app.EplanBinFolder = settings.Eplan.BinPath;
            // true (не false) для bAllowCallingLoginDialog — как в EplanCipMvp.Gui (MainForm.cs:432).
            app.Init("", true, true);

            var projectManager = new ProjectManager();
            var project = projectManager.OpenProject(projectPath);

            Core.ProjectExport.ProjectExportBundle bundle;
            try
            {
                bundle = ProjectExporter.ExportAll(project, msg => Console.WriteLine("  " + msg));
            }
            finally
            {
                project.Close();
            }

            Console.WriteLine($"\n=== Готово: {bundle.Manifest.DeviceCount} устройств, " +
                               $"{bundle.Manifest.PlcAddressCount} PLC-адресов, {bundle.Manifest.WireCount} проводов, " +
                               $"{bundle.Manifest.CableCount} кабелей, {bundle.Manifest.PageCount} страниц ===");

            string outDir = Path.Combine(Path.GetTempPath(), "EplanCipMvp-export-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            WriteJson(Path.Combine(outDir, "manifest.json"), bundle.Manifest);
            WriteJson(Path.Combine(outDir, "devices.json"), bundle.Devices);
            WriteJson(Path.Combine(outDir, "plc-addresses.json"), bundle.PlcAddresses);
            WriteJson(Path.Combine(outDir, "wires.json"), bundle.Wires);
            WriteJson(Path.Combine(outDir, "cables.json"), bundle.Cables);
            WriteJson(Path.Combine(outDir, "pages.json"), bundle.Pages);
            Console.WriteLine($"Файлы сохранены локально: {outDir}");

            Console.WriteLine("\n=== Заливка на Google Drive ===");
            var uploader = new GoogleDriveUploader(settings.GoogleDrive.ServiceAccountKeyPath, settings.GoogleDrive.FolderId);
            string link = uploader.UploadFolder(outDir, msg => Console.WriteLine("  " + msg));
            Console.WriteLine($"Готово: {link}");
        }

        private static void WriteJson<T>(string path, T data)
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(data, options), System.Text.Encoding.UTF8);
        }

        private static AppSettings LoadSettings()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

            var config = builder.Build();
            var settings = new AppSettings();
            config.Bind(settings);
            return settings;
        }

        /// <summary>
        /// Сверяет то, что реально насчитал ModuleGrouper по актуальной спецификации,
        /// с тем, что уже физически заказано у Siemens (settings.PlcHardware.OrderedModuleCounts).
        /// См. README — известное расхождение из-за насосов M1 (Profinet vs дискретные).
        /// </summary>
        private static void PrintOrderReconciliation(System.Collections.Generic.List<Core.Models.PlcModule> modules, PlcHardwareSettings hw)
        {
            Console.WriteLine("\n=== Сверка с заказанным оборудованием (Siemens) ===");
            var byType = modules.GroupBy(m => m.Type.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var type in new[] { "DI", "DO", "AI", "AO" })
            {
                int computed = byType.TryGetValue(type, out var c) ? c : 0;
                int ordered = hw.OrderedModuleCounts.TryGetValue(type, out var o) ? o : 0;
                string flag = computed == ordered ? "OK" : (computed < ordered ? "ЗАКАЗАНО БОЛЬШЕ" : "НЕ ХВАТАЕТ ЗАКАЗАННОГО");
                Console.WriteLine($"  {type}: посчитано {computed} модулей, заказано {ordered}  [{flag}]");
            }

            if (byType.Sum(kv => kv.Value) != hw.OrderedModuleCounts.Values.Sum())
            {
                Console.WriteLine("  ВНИМАНИЕ: расхождение — см. README про насосы M1 (Profinet vs дискретные каналы).");
            }
        }

        /// <summary>
        /// Печатает примеры подписей каналов по формату Naming — это именно та строка,
        /// которую в будущем должен будет писать PlcPageBuilder.FillChannelTexts.
        /// Сейчас FillChannelTexts всё ещё заглушка (ждёт Этапа 0), но ЧТО писать —
        /// уже решено и проверяемо здесь, независимо от EPLAN.
        /// </summary>
        private static void PrintLabelSamples(System.Collections.Generic.List<Core.Models.PlcModule> modules, NamingSettings naming)
        {
            Console.WriteLine("\n=== Примеры подписей каналов (формат Naming) ===");
            foreach (var module in modules.FindAll(m => m.Channels.Count > 0).GetRange(0, System.Math.Min(2, modules.Count)))
            {
                Console.WriteLine($"  {module.DesignationHint}:");
                foreach (var ch in module.Channels.GetRange(0, System.Math.Min(2, module.Channels.Count)))
                {
                    Console.WriteLine($"    {ch.Tag,-8} -> {ChannelLabelBuilder.BuildLabel(ch, naming)}");
                }
            }
        }

        /// <summary>
        /// Сверяет TBD-позиции (производитель/артикул) в спецификации с настроенными
        /// дефолтами (settings.DeviceDefaults.ArticleDefaults). Не подставляет ничего
        /// в саму EPLAN — только отчёт, что нужно согласовать.
        /// </summary>
        private static void PrintArticleResolution(System.Collections.Generic.List<Core.Models.DeviceInfo> devices, DeviceDefaultsSettings dd)
        {
            var resolutions = ArticleResolver.ResolveAll(devices, dd.ArticleDefaults)
                .Where(r => r.WasTbd)
                .GroupBy(r => r.DeviceType)
                .ToList();

            if (resolutions.Count == 0)
            {
                Console.WriteLine("TBD-позиций по производителю/артикулу не найдено.");
                return;
            }

            Console.WriteLine($"Найдено {resolutions.Sum(g => g.Count())} устройств с TBD по производителю/артикулу:");
            foreach (var group in resolutions)
            {
                var first = group.First();
                Console.WriteLine($"  {group.Key} ({group.Count()} шт.): {first.Source}");
                if (first.ResolvedVendor != null || first.ResolvedArticle != null)
                    Console.WriteLine($"    -> {first.ResolvedVendor ?? "?"} / {first.ResolvedArticle ?? "?"}");
                if (!string.IsNullOrWhiteSpace(first.Note))
                    Console.WriteLine($"    Примечание: {first.Note}");
            }
        }

        /// <summary>
        /// Чек-лист: для каких типов устройств ещё не готов символ-макрос для
        /// технологической схемы (Этап 3). Сейчас все — TBD, это ожидаемо.
        /// </summary>
        private static void PrintSymbolMacroChecklist(System.Collections.Generic.List<Core.Models.DeviceInfo> devices, DeviceDefaultsSettings dd)
        {
            Console.WriteLine("\n=== Чек-лист символов-макросов для P&ID (Этап 3, задел на будущее) ===");
            var types = devices.Select(d => d.BaseDeviceType).Distinct().OrderBy(t => t);
            foreach (var type in types)
            {
                bool has = dd.SymbolMacros.TryGetValue(type, out var macro) && macro != "TBD.ems";
                string status = has ? $"OK -> {macro}" : "TBD — макрос ещё не извлечён (Этап 0)";
                Console.WriteLine($"  {type,-45} {status}");
            }
        }

        /// <summary>
        /// Печатает состав силового контура на каждый насос с ПЧ — реально
        /// заказанный набор (не TBD-гипотеза), см. VfdComponentDefaults.
        /// Это данные для отрисовки листа "обвязка ПЧ" в EPLAN по образцу 1260 —
        /// сам лист программа не рисует, только готовит состав.
        /// </summary>
        private static void PrintVfdChecklist(System.Collections.Generic.List<Core.Models.VfdCircuit> circuits)
        {
            if (circuits.Count == 0)
            {
                Console.WriteLine("Насосов с ПЧ в спецификации не найдено.");
                return;
            }

            Console.WriteLine($"Найдено {circuits.Count} насос(ов) с ПЧ:");
            foreach (var c in circuits)
            {
                Console.WriteLine($"  {c.PumpTag} (шкаф {c.Cabinet}):");
                Console.WriteLine($"    ПЧ:           {c.VfdVendor} {c.VfdArticle}" + (c.VfdIsDiscontinued ? "  [СНЯТ С ПРОИЗВОДСТВА]" : ""));
                Console.WriteLine($"    Панель:       {c.ControlPanelArticle}");
                Console.WriteLine($"    Автомат:      {c.BreakerVendor} {c.BreakerArticle}");
                Console.WriteLine($"    Доп. контакты: {c.AuxContactArticle} ({c.AuxContactPurpose})");
            }
            if (circuits.Exists(c => c.VfdIsDiscontinued))
                Console.WriteLine("  ВНИМАНИЕ: модель ПЧ снята с производства — уточнить у заказчика перед заказом.");
        }
    }
}
