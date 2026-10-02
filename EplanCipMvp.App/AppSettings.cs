using System;
using System.Collections.Generic;
using EplanCipMvp.Core;
using EplanCipMvp.Core.Models;
using Microsoft.Extensions.Configuration;

namespace EplanCipMvp.App
{
    /// <summary>
    /// Корневая модель настроек — биндится из appsettings.json (+ appsettings.Local.json,
    /// который не попадает в git и хранит пути, специфичные для вашей машины).
    /// </summary>
    public class AppSettings
    {
        /// <summary>02.10.2026: общий загрузчик для Program.cs (консоль) и MainForm.cs (GUI,
        /// вкладка "Экспорт на Google Drive") — один и тот же appsettings.json/.Local.json.</summary>
        public static AppSettings Load()
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

        public EplanSettings Eplan { get; set; } = new EplanSettings();
        public SpecSettings Spec { get; set; } = new SpecSettings();
        public PlcHardwareSettings PlcHardware { get; set; } = new PlcHardwareSettings();
        public ModuleGrouperOptions Grouping { get; set; } = new ModuleGrouperOptions();
        public NamingSettings Naming { get; set; } = new NamingSettings();
        public PageStructureSettings PageStructure { get; set; } = new PageStructureSettings();
        public VfdComponentDefaults VfdDefaults { get; set; } = new VfdComponentDefaults();
        public DeviceDefaultsSettings DeviceDefaults { get; set; } = new DeviceDefaultsSettings();
        public GoogleDriveSettings GoogleDrive { get; set; } = new GoogleDriveSettings();
    }

    /// <summary>
    /// Куда грузить полную вычитку проекта (команда "export-all"). FolderId — не секрет,
    /// можно коммитить. CredentialsPath — файл OAuth client ID (тип "Desktop app" в Google
    /// Cloud Console, НЕ сервисный аккаунт — у сервисных аккаунтов нет квоты на обычном
    /// личном Диске, см. комментарий в GoogleDriveUploader.cs). Сам файл в git НЕ попадает
    /// (см. .gitignore), кладётся рядом с exe вручную. Первый запуск откроет браузер для
    /// входа — дальше токен кэшируется в %APPDATA%\EplanCipMvp (AppPaths.DataDir).
    /// </summary>
    public class GoogleDriveSettings
    {
        public string FolderId { get; set; } = "1BJFWlJhIrVHDucGIZfCg6xAvUhGKaQ7r";
        public string CredentialsPath { get; set; } = "google-oauth-client.json";
    }

    /// <summary>
    /// Данные по типам устройств технологической части — задел на Этап 3 (P&ID),
    /// но сама сверка TBD-артикулов полезна уже сейчас (Этап 1), независимо от
    /// того, когда дойдут руки до размещения символов на схеме.
    /// </summary>
    public class DeviceDefaultsSettings
    {
        /// <summary>
        /// Вендор/артикул "по умолчанию" для типов устройств, где в спецификации
        /// стоит TBD. Ключ — BaseDeviceType (без уточнения в скобках), см. DeviceInfo.
        /// Известные TBD на момент написания (см. лист "Сводная" файла 1364):
        /// "Клапан пневматический" и "Клапан регулирующий".
        /// </summary>
        public Dictionary<string, ArticleDefault> ArticleDefaults { get; set; } = new Dictionary<string, ArticleDefault>
        {
            ["Клапан пневматический"] = new ArticleDefault
            {
                Vendor = "Alfa Laval",
                Article = "TBD",
                Note = "В спецификации указаны кандидаты Alfa Laval / SPX / GEA — Alfa Laval взят как " +
                       "часто встречающийся в CIP-мойках вариант, НЕ окончательное решение. " +
                       "Нужно уточнить тип (запорный/шаровой/дисковый) и DN — см. README проекта 1364.",
            },
            ["Клапан регулирующий"] = new ArticleDefault
            {
                Vendor = null,
                Article = "TBD",
                Note = "Кандидатов в исходных данных нет вообще — этот дефолт пока пустой, " +
                       "чтобы не выдумывать вендора без основания. Заполнить после согласования.",
            },
        };

        /// <summary>
        /// Тип устройства -> файл символа-макроса для технологической схемы (P&ID).
        /// ВСЁ ЕЩЁ TBD — сами макросы не извлечены (Этап 0), это только заготовка
        /// структуры, чтобы не пересобирать конфиг заново, когда дойдёт очередь.
        /// Ключ — BaseDeviceType, как в ArticleDefaults.
        /// </summary>
        public Dictionary<string, string> SymbolMacros { get; set; } = new Dictionary<string, string>
        {
            ["Клапан пневматический"] = "TBD.ems",
            ["Клапан регулирующий"] = "TBD.ems",
            ["Датчик положения клапана"] = "TBD.ems",
            ["Датчик люка"] = "TBD.ems",
            ["Датчик уровня дискретный"] = "TBD.ems",
            ["Датчик уровня аналоговый"] = "TBD.ems",
            ["Датчик температуры"] = "TBD.ems",
            ["Расходомер основной"] = "TBD.ems",
            ["Расходомер дезинфектанта"] = "TBD.ems",
            ["Датчик проводимости и температуры"] = "TBD.ems",
            ["Датчик протока"] = "TBD.ems",
            ["Датчик давления"] = "TBD.ems",
            ["Насос центробежный с ПЧ"] = "TBD.ems",
            ["Насос мембранный пневматический"] = "TBD.ems",
        };
    }

    /// <summary>
    /// Всё, что касается подключения к самой EPLAN и файловой системе проекта.
    /// </summary>
    public class EplanSettings
    {
        /// <summary>Папка Bin установки EPLAN — для справки; сами ссылки на сборки
        /// настраиваются в EplanCipMvp.App.csproj (HintPath), это НЕ runtime-путь.</summary>
        public string BinPath { get; set; } = @"C:\Program Files\EPLAN\Platform\Bin\";

        /// <summary>Путь к файлу проекта (.elk) — тому самому, что открывает сама EPLAN.</summary>
        public string ProjectPath { get; set; } = @"C:\Projects\1364\1364_CIP.elk";

        /// <summary>Куда кладутся извлечённые на Этапе 0 эталонные макросы модулей (.ema).</summary>
        public string MacroBasePath { get; set; } = @"C:\EplanCipMvp\Macros";
    }

    public class SpecSettings
    {
        public string XlsxPath { get; set; } = @"C:\путь\1364-Сводная_специфкация_изделий.xlsx";
        public string SheetName { get; set; } = "Перечень устройств";
    }

    /// <summary>
    /// Факты об оборудовании ПЛК, которые мы узнали из реальных документов проекта —
    /// используется для сверки "посчитано по спецификации" vs "реально заказано у Siemens".
    /// См. 1364_01-спец_заказ_Siem_24-07-2026.xlsx.
    /// </summary>
    public class PlcHardwareSettings
    {
        public string CpuArticle { get; set; } = "6ES7515-2AN03-0AB0"; // SIMATIC S7-1500 CPU 1515-2 PN
        public string CpuDescription { get; set; } = "SIMATIC S7-1500, CPU 1515-2 PN";

        /// <summary>
        /// Шкаф, где физически стоит ЦП — ПРЕДПОЛОЖЕНИЕ (по названию "Шкаф Управления"
        /// и по составу листа "2. Сигналы по шкафам"), не подтверждено документом напрямую.
        /// </summary>
        public string CpuCabinet { get; set; } = "ШУ";

        public string InterfaceModuleArticle { get; set; } = "6ES7155-6AA02-0BN0"; // ET200SP IM 155-6 PN ST
        public int InterfaceModuleCount { get; set; } = 3; // по 1 на шкаф (ШРП-1, ШРП-2, ШУ)

        /// <summary>
        /// Фактически заказанное количество модулей по типам (из спецификации Siemens) —
        /// для сверки с тем, что считает ModuleGrouper по актуальному "Перечню устройств".
        /// Известное расхождение: заказ соответствует СТАРОМУ расчёту (M1 как дискретные),
        /// а не текущему "Перечню устройств" (M1 — чистый Profinet). См. README.
        /// </summary>
        public Dictionary<string, int> OrderedModuleCounts { get; set; } = new Dictionary<string, int>
        {
            { "DI", 15 },
            { "DO", 12 },
            { "AI", 7 },
            { "AO", 4 },
        };
    }
}
