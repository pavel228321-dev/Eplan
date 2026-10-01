using System;
using System.Collections.Generic;
using System.Linq;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.HEServices;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.App
{
    public class ReadCable
    {
        public CableInfo Info;
        public Function Function;
        /// <summary>Число жил под кабелем (CableConnections) — для проверки после переименования.</summary>
        public int ConnectionCount;
    }

    /// <summary>
    /// 23.09.2026: чтение/переименование кабелей для нумерации по правилам
    /// (docs/superpowers/specs/2026-09-23-cable-numbering-design.md).
    /// Кабель = Function категории Cable; у кабеля на нескольких листах несколько
    /// функций с одним DT — берём одну (главную), RenameDevice всё равно
    /// переименовывает все функции устройства. НЕ ПРОВЕРЕНО ЖИВЫМ ТЕСТОМ.
    /// </summary>
    public static class CableNumbering
    {
        public static List<ReadCable> ReadAll(Project project, Action<string> log)
        {
            var functions = new DMObjectsFinder(project)
                .GetFunctions(new FunctionsFilter { Category = Function.Enums.Category.Cable });

            var result = new List<ReadCable>();
            int examplesLogged = 0;
            foreach (var group in functions.Where(f => f is Cable).GroupBy(f => f.Name ?? ""))
            {
                var function = group.FirstOrDefault(f => f.IsMainFunction) ?? group.First();
                var cable = (Cable)function;
                try
                {
                    var own = DeviceTagParser.Parse(function.Name) ?? new CableEnd();
                    var sources = new List<StorableObject>();
                    var targets = new List<StorableObject>();
                    cable.GetSourcesAndTargets(sources, targets);

                    var info = new CableInfo
                    {
                        Id = "c" + result.Count,
                        CurrentName = own.Device,
                        CableLocation = own.Location,
                        Type = ReadString(() => function.Properties[Properties.Function.FUNC_CABLETYPE]),
                        CoresTotal = ReadInt(() => cable.Properties.CABLE_COUNTOFALLWIRES),
                        Sources = ToEnds(sources),
                        Targets = ToEnds(targets),
                    };
                    result.Add(new ReadCable { Info = info, Function = function, ConnectionCount = cable.CableConnections?.Length ?? 0 });

                    if (examplesLogged < 5 && info.CurrentName.Length > 0)
                    {
                        // 24.09.2026: пустая часть имени (обычно PREFIX) при чтении бросает EmptyPropertyException —
                        // читаем безопасно, иначе эта строка сыпала ошибкой на каждый кабель (живой прогон 1364).
                        examplesLogged++;
                        var parts = function.NameParts;
                        log($"Разбор имени (для сверки): {info.CurrentName} = PREFIX '{ReadString(() => parts.FUNC_PREFIX)}' " +
                            $"CODE '{ReadString(() => parts.FUNC_CODE)}' COUNTER '{ReadString(() => parts.FUNC_COUNTER)}'");
                    }
                }
                catch (Exception ex)
                {
                    log($"ОШИБКА чтения кабеля '{function.Name}': {Describe(ex)}");
                }
            }
            return result;
        }

        public static bool Rename(Function cable, string newName, Action<string> log)
        {
            string oldName = cable.Name;
            try
            {
                var parts = cable.NameParts;
                var (code, counter) = DeviceNameSplitter.Split(newName);
                // Пустой PREFIX не трогаем: чтение/запись пустого свойства EPLAN может бросить EmptyPropertyException.
                if (ReadString(() => parts.FUNC_PREFIX).Length > 0) parts.FUNC_PREFIX = "";
                parts.FUNC_CODE = code;
                parts.FUNC_COUNTER = counter;
                // bRenameCDPsAlso=true — переименовать и жилы кабеля (связь кабель<->жилы
                // идёт по имени, см. remarks у Cable.CableConnections); bKeepDescribingProps=true.
                new NameService().RenameDevice(cable, parts, true, true);
                log($"{oldName} -> {newName}");
                return true;
            }
            catch (Exception ex)
            {
                log($"ОШИБКА {oldName} -> {newName}: {Describe(ex)}");
                return false;
            }
        }

        private static List<CableEnd> ToEnds(IEnumerable<StorableObject> objects) =>
            objects.OfType<FunctionBase>()
                   .Select(f => DeviceTagParser.Parse(f.Name))
                   .Where(e => e != null && e.Device.Length > 0)
                   .ToList();

        private static string Describe(Exception ex) =>
            ex.InnerException == null
                ? $"{ex.GetType().Name}: {ex.Message}"
                : $"{ex.GetType().Name}: {ex.Message} <- {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";

        private static string ReadString(Func<object> read)
        {
            try { return read()?.ToString() ?? ""; }
            catch { return ""; }
        }

        private static int ReadInt(Func<object> read) => int.TryParse(ReadString(read), out int n) ? n : 0;
    }
}
