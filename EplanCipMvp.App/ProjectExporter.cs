using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eplan.EplApi.DataModel;
using EplanCipMvp.Core.ProjectExport;
using EplanCipMvp.Core.WireNaming;

namespace EplanCipMvp.App
{
    /// <summary>
    /// 02.10.2026: команда "export-all" — полная вычитка проекта (устройства/артикулы,
    /// PLC-адреса, провода, кабели, страницы) в структурированный дамп для последующей
    /// выгрузки на Google Drive (см. GoogleDriveUploader). Свойства EPLAN API ниже
    /// подтверждены рефлексией реальной сборки Eplan.EplApi.DataModelu.dll (не угаданы),
    /// КРОМЕ отмеченных "ТРЕБУЕТ ПРОВЕРКИ НА ЖИВОМ ПРОЕКТЕ" — в контейнере нет живой
    /// EPLAN-сессии, чтобы подтвердить их на реальных данных.
    /// </summary>
    public static class ProjectExporter
    {
        /// <summary>02.10.2026: общая точка входа для консоли (Program.RunExportAllConnected)
        /// и GUI (MainForm, кнопка "Экспортировать на Google Drive") — один и тот же код,
        /// не дублируем. Project уже должен быть открыт вызывающей стороной (так у GUI уже
        /// есть готовое, проверенное подключение через _eplan.TargetProject — используем его
        /// вместо повторной офлайн-инициализации EPLAN, которая в консоли падает с
        /// "$(CFG_VARIANT)\install.xml doesn't exist!").</summary>
        public static string ExportAllAndUpload(Project project, GoogleDriveSettings driveSettings, Action<string> log)
        {
            var bundle = ExportAll(project, log);
            log($"Готово: {bundle.Manifest.DeviceCount} устройств, {bundle.Manifest.PlcAddressCount} PLC-адресов, " +
                $"{bundle.Manifest.WireCount} проводов, {bundle.Manifest.CableCount} кабелей, {bundle.Manifest.PageCount} страниц.");

            string outDir = Path.Combine(Path.GetTempPath(), "EplanCipMvp-export-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            WriteJson(Path.Combine(outDir, "manifest.json"), bundle.Manifest);
            WriteJson(Path.Combine(outDir, "devices.json"), bundle.Devices);
            WriteJson(Path.Combine(outDir, "plc-addresses.json"), bundle.PlcAddresses);
            WriteJson(Path.Combine(outDir, "wires.json"), bundle.Wires);
            WriteJson(Path.Combine(outDir, "cables.json"), bundle.Cables);
            WriteJson(Path.Combine(outDir, "pages.json"), bundle.Pages);
            log($"Файлы сохранены локально: {outDir}");

            log("Заливка на Google Drive...");
            // AppPaths.UserFile — для файлов (с переносом "легаси" из папки exe), а тут нужна
            // просто папка под кэш токена; берём тот же %APPDATA%\EplanCipMvp (AppPaths.DataDir).
            string tokenDir = System.IO.Path.Combine(AppPaths.DataDir ?? AppDomain.CurrentDomain.BaseDirectory, "google-drive-token");
            var uploader = new GoogleDriveUploader(driveSettings.CredentialsPath, tokenDir, driveSettings.FolderId);
            return uploader.UploadFolder(outDir, msg => log("  " + msg));
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

        public static ProjectExportBundle ExportAll(Project project, Action<string> log)
        {
            var bundle = new ProjectExportBundle
            {
                Manifest = new ProjectExportManifest
                {
                    ProjectName = ReadString(() => project.ProjectName),
                    ProjectPath = ReadString(() => project.ProjectLinkFilePath),
                    ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                },
            };

            log("Читаю страницы...");
            bundle.Pages = ReadPages(project, log);

            log("Читаю устройства/артикулы...");
            bundle.Devices = ReadDevices(project, log);

            log("Читаю PLC-адреса...");
            bundle.PlcAddresses = ReadPlcAddresses(project, log);

            log("Читаю провода и кабели (переиспользую WireNumbering.ReadAll)...");
            var wireResult = WireNumbering.ReadAll(project, log);
            bundle.Wires = wireResult.Wires.Select(w => new WireConnectionExport
            {
                Name = w.CurrentName,
                Potential = w.Potential,
                Cable = w.Cable,
                Page = w.Page >= 0 && w.Page < bundle.Pages.Count ? bundle.Pages[w.Page].Identifier : "",
                StartDevice = w.Start.Device,
                StartTerminal = w.Start.Terminal,
                EndDevice = w.End.Device,
                EndTerminal = w.End.Terminal,
            }).ToList();
            bundle.Cables = wireResult.Cables.Select(c => new CableExport
            {
                Name = c.Cable,
                Cores = c.FreeCores ?? new List<string>(),
            }).ToList();

            bundle.Manifest.PageCount = bundle.Pages.Count;
            bundle.Manifest.DeviceCount = bundle.Devices.Count;
            bundle.Manifest.PlcAddressCount = bundle.PlcAddresses.Count;
            bundle.Manifest.WireCount = bundle.Wires.Count;
            bundle.Manifest.CableCount = bundle.Cables.Count;

            return bundle;
        }

        private static List<PageExport> ReadPages(Project project, Action<string> log)
        {
            var result = new List<PageExport>();
            foreach (var page in project.Pages ?? new Page[0])
            {
                try
                {
                    result.Add(new PageExport
                    {
                        Identifier = ReadString(() => page.ToStringIdentifier()),
                        Name = ReadString(() => page.Name),
                        // ТРЕБУЕТ ПРОВЕРКИ НА ЖИВОМ ПРОЕКТЕ: структурный идентификатор страницы
                        // (аналог "=LINE1" из PageStructureSettings) — пробуем Properties.Page,
                        // точное имя свойства не подтверждено рефлексией (слишком много кандидатов
                        // в диапазоне Properties/Page без явного "STRUCTURE"/"IDENTIFIER" в имени).
                        StructureIdentifier = "",
                        PageType = ReadString(() => page.PageType.ToString()),
                    });
                }
                catch (Exception ex)
                {
                    log($"ОШИБКА чтения страницы: {WireNumbering.Describe(ex)}");
                }
            }
            return result;
        }

        /// <summary>Устройства группируем по FUNC_DEVICETAG_MAINNAME (та же точка входа,
        /// что уже используют DeviceGroupPageCopier.cs/PlcModulePageCopier.cs/VfdPageCopier.cs) —
        /// одно физическое устройство обычно состоит из нескольких Function (катушка+контакты),
        /// артикул берём с первой функции, где он заполнен.</summary>
        private static List<DeviceExport> ReadDevices(Project project, Action<string> log)
        {
            var result = new List<DeviceExport>();
            var functions = new DMObjectsFinder(project).GetFunctions(new FunctionsFilter()) ?? new Function[0];
            var byTag = functions.GroupBy(f => ReadString(() => f.Properties[Properties.Function.FUNC_DEVICETAG_MAINNAME]))
                .Where(g => g.Key.Length > 0);

            foreach (var group in byTag)
            {
                try
                {
                    var withArticle = group.FirstOrDefault(f =>
                        ReadString(() => f.Properties[Properties.Function.FUNC_ARTICLE_PARTNR]).Length > 0)
                        ?? group.First();

                    result.Add(new DeviceExport
                    {
                        Designation = group.Key,
                        Article = ReadString(() => withArticle.Properties[Properties.Function.FUNC_ARTICLE_PARTNR]),
                        Manufacturer = ReadString(() => withArticle.Properties[Properties.Function.FUNC_ARTICLE_MANUFACTURER]),
                        OrderNumber = ReadString(() => withArticle.Properties[Properties.Function.FUNC_ARTICLE_ORDERNR]),
                        Description = ReadString(() => withArticle.Properties[Properties.Function.FUNC_ARTICLE_DESCR1]),
                        Category = ReadString(() => withArticle.Category.ToString()),
                        Page = ReadString(() => withArticle.Page?.Name ?? ""),
                    });
                }
                catch (Exception ex)
                {
                    log($"ОШИБКА чтения устройства '{group.Key}': {WireNumbering.Describe(ex)}");
                }
            }
            return result;
        }

        /// <summary>PLC-адреса — та же форма строки, что Лист1 из PLC-навигатора EPLAN.
        /// FUNC_PLCADDRESS подтверждён рефлексией. Symbolic address берём по приоритету
        /// Automatic -> Calculated -> Manual (так ведёт себя сам навигатор: "(automatic)"
        /// в заголовке колонки Лист1). FUNC_ARTICLEPLACEMENT_DEVICE_FUNCTIONTEXT как источник
        /// "Function text" — ТРЕБУЕТ ПРОВЕРКИ НА ЖИВОМ ПРОЕКТЕ (имя похоже по смыслу, но
        /// рефлексией подтверждено только существование поля, не его реальное содержимое).</summary>
        private static List<PlcAddressExport> ReadPlcAddresses(Project project, Action<string> log)
        {
            var result = new List<PlcAddressExport>();
            var functions = new DMObjectsFinder(project).GetFunctions(new FunctionsFilter()) ?? new Function[0];

            foreach (var function in functions)
            {
                try
                {
                    string address = ReadString(() => function.Properties[Properties.Function.FUNC_PLCADDRESS]);
                    if (address.Length == 0) continue;

                    string symbolic = ReadString(() => function.Properties[Properties.Function.FUNC_PLCSYMBOLICADDRESS_AUTOMATIC]);
                    if (symbolic.Length == 0) symbolic = ReadString(() => function.Properties[Properties.Function.FUNC_PLCSYMBOLICADDRESS_CALCULATED]);
                    if (symbolic.Length == 0) symbolic = ReadString(() => function.Properties[Properties.Function.FUNC_PLCSYMBOLICADDRESS_MANUAL]);

                    result.Add(new PlcAddressExport
                    {
                        ProjectName = ReadString(() => project.ProjectName),
                        FunctionText = ReadString(() => function.Properties[Properties.Function.FUNC_ARTICLEPLACEMENT_DEVICE_FUNCTIONTEXT]),
                        PlcAddress = address,
                        SymbolicAddress = symbolic,
                    });
                }
                catch (Exception ex)
                {
                    log($"ОШИБКА чтения PLC-адреса: {WireNumbering.Describe(ex)}");
                }
            }
            return result;
        }

        private static string ReadString(Func<object> read)
        {
            try { return (read()?.ToString() ?? "").Trim(); }
            catch { return ""; }
        }
    }
}
