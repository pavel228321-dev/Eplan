using System.Collections.Generic;

namespace EplanCipMvp.Core.ProjectExport
{
    /// <summary>
    /// 02.10.2026: модели полной вычитки проекта (команда "export-all") — не привязаны
    /// к Eplan.EplApi, чтобы спокойно сериализоваться в JSON без зависимости от сборок EPLAN.
    /// Цель набора — не просто отчёт, а заготовка для сборки нового похожего проекта.
    /// </summary>
    public class ProjectExportManifest
    {
        public string ProjectName { get; set; } = "";
        public string ProjectPath { get; set; } = "";
        public string ExportedAtUtc { get; set; } = "";
        public int DeviceCount { get; set; }
        public int PlcAddressCount { get; set; }
        public int WireCount { get; set; }
        public int CableCount { get; set; }
        public int PageCount { get; set; }
    }

    public class DeviceExport
    {
        /// <summary>Полное обозначение устройства (тег), напр. "=LINE1+ШУ-K1".</summary>
        public string Designation { get; set; } = "";
        public string Article { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string OrderNumber { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string Page { get; set; } = "";
    }

    /// <summary>Та же форма строки, что и привычный Лист1 из PLC-навигатора EPLAN —
    /// для совместимости с уже отработанным процессом (Ctrl+A/Ctrl+C обратно в проект).</summary>
    public class PlcAddressExport
    {
        public string ProjectName { get; set; } = "";
        public string FunctionText { get; set; } = "";
        public string PlcAddress { get; set; } = "";
        public string SymbolicAddress { get; set; } = "";
    }

    public class WireConnectionExport
    {
        public string Name { get; set; } = "";
        public string Potential { get; set; } = "";
        public string Cable { get; set; } = "";
        public string Page { get; set; } = "";
        public string StartDevice { get; set; } = "";
        public string StartTerminal { get; set; } = "";
        public string EndDevice { get; set; } = "";
        public string EndTerminal { get; set; } = "";
    }

    public class CableExport
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public List<string> Cores { get; set; } = new List<string>();
    }

    public class PageExport
    {
        public string Identifier { get; set; } = "";
        public string Name { get; set; } = "";
        public string StructureIdentifier { get; set; } = "";
        public string PageType { get; set; } = "";
    }

    public class ProjectExportBundle
    {
        public ProjectExportManifest Manifest { get; set; } = new ProjectExportManifest();
        public List<DeviceExport> Devices { get; set; } = new List<DeviceExport>();
        public List<PlcAddressExport> PlcAddresses { get; set; } = new List<PlcAddressExport>();
        public List<WireConnectionExport> Wires { get; set; } = new List<WireConnectionExport>();
        public List<CableExport> Cables { get; set; } = new List<CableExport>();
        public List<PageExport> Pages { get; set; } = new List<PageExport>();
    }
}
