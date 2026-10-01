using System;
using System.Collections.Generic;
using System.Linq;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanCipMvp.Core.CableNaming;
using EplanCipMvp.Core.WireNaming;

namespace EplanCipMvp.App
{
    public class WireReadResult
    {
        public List<WireInfo> Wires { get; } = new List<WireInfo>();
        public List<CableCores> Cables { get; } = new List<CableCores>();
        public Dictionary<string, Connection> ConnectionsById { get; } = new Dictionary<string, Connection>();
        /// <summary>id провода -> ToStringIdentifier соединения (для сверки после перечитывания).</summary>
        public Dictionary<string, string> IdentifierById { get; } = new Dictionary<string, string>();
        /// <summary>кабель -> свободные шаблоны жил (для Assign).</summary>
        public Dictionary<string, List<Connection>> TemplatesByCable { get; } =
            new Dictionary<string, List<Connection>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 24.09.2026: чтение всех соединений проекта для нумерации жил и проводов
    /// (docs/superpowers/specs/2026-09-24-wire-core-numbering-design.md), запись номера в точки
    /// определения соединения и назначение жил штатным Assign. НЕ ПРОВЕРЕНО ЖИВЫМ ТЕСТОМ.
    /// </summary>
    public static class WireNumbering
    {
        public static WireReadResult ReadAll(Project project, Action<string> log)
        {
            var result = new WireReadResult();
            var pageIndex = new Dictionary<string, int>();
            var pages = project.Pages ?? new Page[0];
            for (int i = 0; i < pages.Length; i++) pageIndex[pages[i].ToStringIdentifier()] = i;

            var cableByConnection = ReadCables(project, result, log);

            int skipped = 0;
            foreach (var conn in new DMObjectsFinder(project).GetConnections(null) ?? new Connection[0])
            {
                try
                {
                    if (conn.IsTemplate || !conn.IsPlaced) { skipped++; continue; }
                    string identifier = conn.ToStringIdentifier();
                    var (start, sx, sy) = ReadEndSafe(() => conn.StartPin);
                    var (end, ex, ey) = ReadEndSafe(() => conn.EndPin);
                    // 25.09.2026: соединения без контактов на обоих концах (на 1364 — 10 шт., лист 0) не показываем.
                    if (start.PinKey.Length == 0 && end.PinKey.Length == 0) { skipped++; continue; }
                    cableByConnection.TryGetValue(identifier, out string cable);
                    string id = "w" + result.Wires.Count;
                    result.Wires.Add(new WireInfo
                    {
                        Id = id,
                        Page = conn.Page != null && pageIndex.TryGetValue(conn.Page.ToStringIdentifier(), out int p) ? p : -1,
                        X = (sx + ex) / 2,
                        Y = (sy + ey) / 2,
                        CurrentName = ReadName(conn),
                        CurrentCore = cable == null ? "" : ReadString(() => conn.Properties[Properties.Connection.CONNECTION_WIRENUMBER]),
                        Potential = ReadString(() => conn.Properties[Properties.Connection.POTENTIAL_NAME]),
                        Cable = cable ?? "",
                        HasDefinitionPoint = (conn.ConnectionDefPoints?.Length ?? 0) > 0,
                        Start = start,
                        End = end,
                    });
                    result.ConnectionsById[id] = conn;
                    result.IdentifierById[id] = identifier;
                }
                catch (Exception ex)
                {
                    log($"ОШИБКА чтения соединения: {Describe(ex)}");
                }
            }
            if (skipped > 0) log($"Пропущено неразмещённых соединений, шаблонов и соединений без контактов: {skipped}.");
            return result;
        }

        /// <summary>Кабели: какие соединения — их жилы, и свободные шаблоны жил из артикула.</summary>
        private static Dictionary<string, string> ReadCables(Project project, WireReadResult result, Action<string> log)
        {
            var cableByConnection = new Dictionary<string, string>();
            var functions = new DMObjectsFinder(project)
                .GetFunctions(new FunctionsFilter { Category = Function.Enums.Category.Cable });
            foreach (var group in functions.OfType<Cable>().GroupBy(f => f.Name ?? ""))
            {
                var own = DeviceTagParser.Parse(group.Key) ?? new CableEnd();
                string name = own.Device.Length > 0 ? own.Device : group.Key;
                try
                {
                    var templates = new List<Connection>();
                    foreach (var cable in group)
                    {
                        foreach (var c in cable.CableConnections ?? new Connection[0])
                            cableByConnection[c.ToStringIdentifier()] = name;
                        foreach (var c in cable.Connections ?? new Connection[0])
                            if (c.IsTemplate && !c.IsCoveredTemplate && !templates.Any(t => t.ToStringIdentifier() == c.ToStringIdentifier()))
                                templates.Add(c);
                    }
                    if (templates.Count > 0)
                    {
                        result.TemplatesByCable[name] = templates;
                        result.Cables.Add(new CableCores
                        {
                            Cable = name,
                            FreeCores = templates.Select(t => ReadString(() => t.Properties[Properties.Connection.CONNECTION_WIRENUMBER])).ToList(),
                        });
                    }
                }
                catch (Exception ex)
                {
                    log($"ОШИБКА чтения жил кабеля '{group.Key}': {Describe(ex)}");
                }
            }
            return cableByConnection;
        }

        /// <summary>25.09.2026: на 1364 чтение контакта у 3 соединений бросало WrongCategoryException
        /// (функция категории «кабель» с другим определением) — такой конец считаем пустым, провод не теряем.</summary>
        private static (WireEnd End, double X, double Y) ReadEndSafe(Func<Pin> pin)
        {
            try { return ReadEnd(pin()); }
            catch { return (new WireEnd(), 0, 0); }
        }

        /// <summary>Конец провода и абсолютная точка контакта (вставка функции + смещение вывода).</summary>
        private static (WireEnd End, double X, double Y) ReadEnd(Pin pin)
        {
            if (pin == null) return (new WireEnd(), 0, 0);
            var function = pin.ParentFunction;
            string full = function?.Name ?? "";
            var parsed = DeviceTagParser.Parse(full) ?? new CableEnd();
            string pinName = ReadString(() => pin.Name);
            if (pinName.Length == 0) pinName = ReadString(() => pin.Designation);

            string terminal = pinName;
            bool isTerminal = function != null && function.Category == Function.Enums.Category.Terminal;
            int colon = full.LastIndexOf(':');
            if (isTerminal && colon >= 0 && colon < full.Length - 1) terminal = full.Substring(colon + 1);

            double x = 0, y = 0;
            try
            {
                if (function != null)
                {
                    PointD at = function.Location;
                    PointD offset = pin.Location;
                    x = at.X + offset.X;
                    y = at.Y + offset.Y;
                }
            }
            catch { /* нет координат — узел просто уйдёт в конец порядка */ }

            return (new WireEnd
            {
                Location = parsed.Location,
                Device = parsed.Device,
                Terminal = terminal,
                PinKey = (function?.ToStringIdentifier() ?? full) + "#" + pinName,
            }, x, y);
        }

        /// <summary>
        /// 25.09.2026: номер провода — из точек определения соединения (там его пишем мы и EPLAN), а свойство самого
        /// соединения — только если точек нет. Соединение EPLAN обновляет не сразу: на 1364 после записи оно ещё
        /// показывало старый номер («0 из 328», «0 из 203»), и, вероятно, пустой номер у проводов, где на схеме 1M.
        /// </summary>
        private static string ReadName(Connection conn)
        {
            try
            {
                foreach (var cdp in conn.ConnectionDefPoints ?? new ConnectionDefinitionPoint[0])
                {
                    string name = ReadString(() => cdp.Properties[Properties.ConnectionDefinitionPoint.CONNECTION_DESIGNATION]);
                    if (name.Length > 0) return name;
                }
                if ((conn.ConnectionDefPoints?.Length ?? 0) > 0) return "";
            }
            catch { /* нет доступа к точкам — берём свойство соединения */ }
            return ReadString(() => conn.Properties[Properties.Connection.CONNECTION_DESIGNATION]);
        }

        /// <summary>Номер провода -> во все точки определения соединения; нет точки — ставим (если разрешено).</summary>
        public static bool WriteName(Connection conn, string name, bool placeDefinitionPoint, string label, Action<string> log)
        {
            try
            {
                var cdps = conn.ConnectionDefPoints ?? new ConnectionDefinitionPoint[0];
                if (cdps.Length == 0)
                {
                    if (!placeDefinitionPoint)
                    {
                        log($"ПРОПУЩЕН {label}: нет точки определения соединения (включите «Ставить новые точки»).");
                        return false;
                    }
                    var point = StraightMidPoint(conn);
                    if (point == null)
                    {
                        log($"ПРОПУЩЕН {label}: провод с изломом — поставьте точку определения в EPLAN вручную.");
                        return false;
                    }
                    conn.PlaceAsConnectionDefinitionPoint(conn.Page, point.Value);
                    cdps = conn.ConnectionDefPoints ?? new ConnectionDefinitionPoint[0];
                    if (cdps.Length == 0)
                    {
                        log($"ОШИБКА {label}: точка определения не поставилась.");
                        return false;
                    }
                }
                foreach (var cdp in cdps)
                    cdp.Properties[Properties.ConnectionDefinitionPoint.CONNECTION_DESIGNATION] = name;
                return true;
            }
            catch (Exception ex)
            {
                log($"ОШИБКА {label} -> {name}: {Describe(ex)}");
                return false;
            }
        }

        /// <summary>Жила: шаблон жилы кабеля назначается на провод (как «Назначить» в навигаторе).
        /// Assign копирует свойства шаблона — поэтому жилы пишутся ДО номеров проводов.</summary>
        public static bool AssignCore(Connection template, Connection conn, string label, Action<string> log)
        {
            try
            {
                template.Assign(conn);
                return true;
            }
            catch (Exception ex)
            {
                log($"ОШИБКА жилы {label}: {Describe(ex)}");
                return false;
            }
        }

        private static PointD? StraightMidPoint(Connection conn)
        {
            var (_, sx, sy) = ReadEndSafe(() => conn.StartPin);
            var (_, ex, ey) = ReadEndSafe(() => conn.EndPin);
            bool vertical = Math.Abs(sx - ex) < 0.01, horizontal = Math.Abs(sy - ey) < 0.01;
            if (!vertical && !horizontal) return null;
            return new PointD((sx + ex) / 2, (sy + ey) / 2);
        }

        public static string Describe(Exception ex) =>
            ex.InnerException == null
                ? $"{ex.GetType().Name}: {ex.Message}"
                : $"{ex.GetType().Name}: {ex.Message} <- {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";

        private static string ReadString(Func<object> read)
        {
            try { return (read()?.ToString() ?? "").Trim(); }
            catch { return ""; }
        }
    }
}
