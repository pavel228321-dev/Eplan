# Нумерация жил и проводов (как в 1260) — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Прочитать весь проект EPLAN, автоматически назначить жилам кабелей обозначения (1/2/3/GNYE) и проставить номера проводов/жил по правилам 1260, с предпросмотром и записью только отмеченного.

**Architecture:** Как нумерация кабелей (этап 1): чистая логика в `EplanCipMvp.Core/WireNaming` (узлы, правила, счётчики, жилы, план записи — покрыта самопроверкой без EPLAN), чтение/запись EPLAN в `EplanCipMvp.App/WireNumbering*.cs`, интерфейс — панель `WireNumberingPanel` во вкладке Gui и карточка в Web.

**Tech Stack:** C# (net48, LangVersion latest), EPLAN API 2.9 (`Eplan.EplApi.*u.dll`), WinForms, `HttpListener` + vanilla JS, самопроверка — консольный `EplanCipMvp.Core.SelfTest` под mono.

**Спецификация:** `docs/superpowers/specs/2026-09-24-wire-core-numbering-design.md`.

**Git:** папка `eplan-cip-mvp` не под git (как и весь /workspace) — вместо коммитов в конце задач «контрольная точка»: сборка + самопроверка зелёные.

---

## Файлы

| Файл | Ответственность |
|---|---|
| `tools/container-build.sh` (new) | Сборка в контейнере против DLL EPLAN 2.9 (копия в scratch, замена HintPath), самопроверка под mono |
| `EplanCipMvp.Core/CableNaming/WildcardMask.cs` (mod) | + `MatchesAny` (маски через `;`) |
| `EplanCipMvp.Core/WireNaming/WireModels.cs` | Модели: `WireEnd`, `WireInfo`, `CableCores`, `WireRule`, `WireNamingRow`, `CoreChange`, статусы |
| `EplanCipMvp.Core/WireNaming/WireNets.cs` | Узлы: провода с общим контактом |
| `EplanCipMvp.Core/WireNaming/WireRuleTemplate.cs` | Подстановки шаблона |
| `EplanCipMvp.Core/WireNaming/CoreAssigner.cs` | Выбор жилы кабеля для провода |
| `EplanCipMvp.Core/WireNaming/WireApplyPlanner.cs` | Что писать: дубли/занятые номера — отказ |
| `EplanCipMvp.Core/WireNaming/WireNamingEngine.cs` | Правила → номер узла, счётчики, статусы, жилы |
| `EplanCipMvp.Core/WireNaming/WireRulePresets.cs` | Пресет «Как 1260» |
| `EplanCipMvp.Core/WireNaming/WireRuleStore.cs` | `wire-numbering-rules.json` |
| `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs` (new), `Program.cs` (mod) | Самопроверка (лист 190 проекта 1260 и др.) |
| `EplanCipMvp.App/WireNumbering.cs` | Чтение всех соединений/кабелей EPLAN, запись номера, назначение жилы |
| `EplanCipMvp.App/WireNumberingSession.cs` | Считать → предпросмотр → записать → перечитать |
| `EplanCipMvp.Gui/WireNumberingPanel.cs`, `MainForm.cs` (mod) | Вкладка «Нумерация жил и проводов» |
| `EplanCipMvp.Web/EplanSession.cs`, `ApiServer.cs`, `wwwroot/index.html`, `wwwroot/app.js` (mod) | Карточка и `/api/wires/*` |
| `README.md` (mod) | Раздел «Нумерация жил и проводов» |

Core-проект подключает `*.cs` по маске SDK — новые файлы в `WireNaming/` попадают в сборку сами.

---

### Task 0: Скрипт сборки в контейнере

**Files:** Create: `tools/container-build.sh`

- [ ] **Step 1: Написать скрипт**

```bash
#!/usr/bin/env bash
# Сборка EplanCipMvp в контейнере разработки (Linux + mono) против DLL EPLAN 2.9 из .uploads.
# Исходники не трогаем: копия в scratch, HintPath -> /workspace/.uploads/eplan-2.9-assemblies.
# Использование: tools/container-build.sh [selftest|all]
set -euo pipefail
MODE="${1:-selftest}"
SRC="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${BUILD_DIR:-/tmp/eplan-cip-build}"
rm -rf "$OUT" && mkdir -p "$OUT" && cp -r "$SRC" "$OUT/src"
sed -i 's#C:\\Program Files\\EPLAN\\Platform\\Bin\\#/workspace/.uploads/eplan-2.9-assemblies/#g' "$OUT"/src/*/*.csproj
export DOTNET_ROOT=/workspace/.dotnet PATH=/workspace/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$OUT/src"
if [ "$MODE" = "all" ]; then
  dotnet build EplanCipMvp.sln -c Release -m:1 -nologo -v q
else
  dotnet build EplanCipMvp.Core.SelfTest -c Release -m:1 -nologo -v q
fi
mono "$OUT/src/EplanCipMvp.Core.SelfTest/bin/Release/net48/EplanCipMvp.Core.SelfTest.exe"
```

- [ ] **Step 2: Прогнать (до изменений — должно быть зелёно)**

Run: `chmod +x tools/container-build.sh && BUILD_DIR=$SCRATCH/build tools/container-build.sh selftest`
Expected: `Нумерация кабелей: все проверки пройдены.` и вывод самопроверки таблицы параметров ПЧ, код выхода 0.

---

### Task 1: Модели, `MatchesAny`, шаблон

**Files:**
- Modify: `EplanCipMvp.Core/CableNaming/WildcardMask.cs`
- Create: `EplanCipMvp.Core/WireNaming/WireModels.cs`, `EplanCipMvp.Core/WireNaming/WireRuleTemplate.cs`
- Create: `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs`; Modify: `EplanCipMvp.Core.SelfTest/Program.cs`

- [ ] **Step 1: Самопроверка (падает — типов ещё нет)**

`EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EplanCipMvp.Core.CableNaming;
using EplanCipMvp.Core.WireNaming;

namespace EplanCipMvp.Core.SelfTest
{
    internal static class WireNamingSelfTest
    {
        private const string E01 = "+CP.E01";
        private const string Field = "+FIELD";

        public static void Run()
        {
            Console.WriteLine("=== Самопроверка нумерации жил и проводов ===");
            TestMatchesAny();
            TestTemplate();
            Console.WriteLine("Нумерация жил и проводов: все проверки пройдены.\n");
        }

        private static void Check(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException("WireNaming self-test FAILED: " + what);
        }

        /// <summary>Конец провода; side — сторона клеммы (у клеммы две стороны = два разных контакта).</summary>
        private static WireEnd E(string location, string device, string terminal, string side = "") =>
            new WireEnd { Location = location, Device = device, Terminal = terminal, PinKey = $"{location}-{device}:{terminal}#{side}" };

        private static WireInfo W(string id, int page, double x, double y, WireEnd a, WireEnd b,
                                  string potential = "", string cable = "", string name = "", string core = "") =>
            new WireInfo { Id = id, Page = page, X = x, Y = y, Start = a, End = b, Potential = potential, Cable = cable, CurrentName = name, CurrentCore = core };

        private static void TestMatchesAny()
        {
            Check(WildcardMask.MatchesAny("", "что угодно"), "пустая маска = всё");
            Check(WildcardMask.MatchesAny("??QF*;??KM*", "32KM04"), "32KM04 под ??KM*");
            Check(!WildcardMask.MatchesAny("??QF*;??KM*", "1QF21"), "1QF21 не под ??QF*");
            Check(WildcardMask.MatchesAny("?;?L?;?T?", "5L3") && WildcardMask.MatchesAny("?;?L?;?T?", "1"), "силовые клеммы");
            Check(!WildcardMask.MatchesAny("?;?L?;?T?", "13") && !WildcardMask.MatchesAny("?;?L?;?T?", "A1"), "13/A1 — не силовые");
        }

        private static void TestTemplate()
        {
            string R(string template, WireEnd anchor, string potential = "") =>
                WireRuleTemplate.Render(template, new WireRuleContext { Anchor = anchor, Potential = potential });

            Check(R("{Устройство:от_цифры}-{Клемма:буквы}", E(Field, "T32M04", "U1")) == "32M04-U", "32M04-U");
            Check(R("{Устройство}-{Клемма:буквы}", E(Field, "GS101", "3")) == "GS101-3", "GS101-3");
            Check(R("{МодульПЛК:цифры}{Клемма:2}", E(E01, "1A2.1", "1")) == "12101", "12101");
            Check(R("{МодульПЛК:цифры}{Клемма:2}", E(E01, "1A2.1", "10")) == "12110", "12110");
            Check(R("{МодульПЛК:цифры}{Клемма:2}", E(E01, "1A2.1", "M")) == null, "нечисловая клемма в {Клемма:2} — правило не сработало");
            Check(R("{Потенциал}", E(E01, "1X24", "1"), "31L+") == "31L+", "потенциал");
            Check(R("{Потенциал}", E(E01, "1X24", "1")) == null, "нет потенциала — не сработало");
            Check(R("{Группа}L{Номер:последняя}{Счётчик}", E(E01, "32QF04", "2")) == "32L4" + WireRuleTemplate.CounterToken, "32L4#");
            Check(R("{Группа}L{Номер:последняя}{Счётчик}", E(E01, "1QF21", "2")) == "1L1" + WireRuleTemplate.CounterToken, "1L1#");
            Check(R("{Группа}L{Номер:последняя}", E(E01, "QF4", "2")) == null, "нет группы — не сработало");
            Check(R("PE", E(E01, "1X31", "PE")) == "PE", "постоянный шаблон");
            Check(R("{Неизвестно}", E(E01, "1X31", "1")) == null, "неизвестная подстановка");
            Check(R("{Счётчик}{Счётчик}", E(E01, "1X31", "1")) == null, "два счётчика нельзя");
        }
    }
}
```

`EplanCipMvp.Core.SelfTest/Program.cs` — сразу после `CableNamingSelfTest.Run();`:

```csharp
            // 24.09.2026: нумерация жил и проводов — чистая логика, без аргументов и EPLAN.
            WireNamingSelfTest.Run();
```

- [ ] **Step 2: Убедиться, что не собирается**

Run: `BUILD_DIR=$SCRATCH/build tools/container-build.sh selftest`
Expected: ошибка компиляции `The type or namespace name 'WireNaming' does not exist`.

- [ ] **Step 3: Реализация**

`EplanCipMvp.Core/CableNaming/WildcardMask.cs` — добавить `using System.Linq;` и метод в класс:

```csharp
        /// <summary>Несколько масок через ";" — совпадение с любой; пустая строка совпадает со всем.</summary>
        public static bool MatchesAny(string masks, string value)
        {
            if (string.IsNullOrWhiteSpace(masks)) return true;
            return masks.Split(';').Where(m => m.Trim().Length > 0).Any(m => Matches(m, value));
        }
```

`EplanCipMvp.Core/WireNaming/WireModels.cs`:

```csharp
using System.Collections.Generic;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>Конец провода: место установки (+CP.E01), DT устройства без "-" (1A1.1) и клемма (1, U1, PE).</summary>
    public class WireEnd
    {
        public string Location { get; set; } = "";
        public string Device { get; set; } = "";
        public string Terminal { get; set; } = "";
        /// <summary>Ключ контакта (функция EPLAN + вывод). Провода с общим контактом — один узел.
        /// У клеммы две стороны — это два разных ключа, поэтому 32L47 и 32M04-U на -1X31:1 — разные узлы.</summary>
        public string PinKey { get; set; } = "";
    }

    public class WireInfo
    {
        public string Id { get; set; } = "";
        /// <summary>Порядковый номер листа и середина провода на листе (Y растёт вверх, как в EPLAN).</summary>
        public int Page { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>Номер провода (свойство 31011); пусто — не задан.</summary>
        public string CurrentName { get; set; } = "";
        /// <summary>Обозначение жилы (31004); пусто — не назначена или провод не в кабеле.</summary>
        public string CurrentCore { get; set; } = "";
        public string Potential { get; set; } = "";
        /// <summary>Имя кабеля, если провод — его жила; иначе пусто.</summary>
        public string Cable { get; set; } = "";
        public bool HasDefinitionPoint { get; set; }
        public WireEnd Start { get; set; } = new WireEnd();
        public WireEnd End { get; set; } = new WireEnd();
    }

    /// <summary>Свободные (не назначенные) жилы кабеля из артикула, в порядке артикула.</summary>
    public class CableCores
    {
        public string Cable { get; set; } = "";
        public List<string> FreeCores { get; set; } = new List<string>();
    }

    public class WireRule
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        /// <summary>Маски «опорного» конца (место, устройство, клемма); несколько масок — через ";".</summary>
        public string Location { get; set; } = "";
        public string Device { get; set; } = "";
        public string Terminal { get; set; } = "";
        /// <summary>Маска места установки любого ДРУГОГО конца узла.</summary>
        public string OtherLocation { get; set; } = "";
        /// <summary>"" — любой; "да" — у узла есть имя потенциала; "нет" — нет.</summary>
        public string Potential { get; set; } = "";
        /// <summary>"" — любой; "да" — в узле есть жила кабеля; "нет" — нет.</summary>
        public string InCable { get; set; } = "";
        /// <summary>Порядок счётчика: "X" — по столбцам (слева направо, сверху вниз), "Y" — по строкам.</summary>
        public string Order { get; set; } = "X";
        public int? CounterStart { get; set; }
        public string Template { get; set; } = "";
    }

    public static class WireNamingStatus
    {
        public const string NoRule = "Нет правила";
        public const string Unchanged = "Без изменений";
        public const string New = "Новый номер";
        public const string Duplicate = "Дубль в проекте";
        public const string Rename = "Перенумерация";
        public const string Conflict = "Конфликт";
    }

    public class CoreChange
    {
        public string WireId { get; set; } = "";
        public string Cable { get; set; } = "";
        public string Core { get; set; } = "";
    }

    /// <summary>Строка предпросмотра = узел: все его провода получают один номер.</summary>
    public class WireNamingRow
    {
        public string NetId { get; set; } = "";
        public int Page { get; set; }
        public List<string> WireIds { get; set; } = new List<string>();
        public List<WireEnd> Ends { get; set; } = new List<WireEnd>();
        public string Potential { get; set; } = "";
        public string Cable { get; set; } = "";
        /// <summary>Текущие номера проводов узла через " / " (обычно один).</summary>
        public string CurrentName { get; set; } = "";
        public string RuleName { get; set; } = "";
        public string ProposedName { get; set; } = "";
        /// <summary>Номер может законно повторяться в других узлах (имя потенциала, постоянный шаблон вроде PE).</summary>
        public bool SharedName { get; set; }
        public string Status { get; set; } = "";
        public string Note { get; set; } = "";
        public bool Apply { get; set; }
        public List<CoreChange> Cores { get; set; } = new List<CoreChange>();
    }
}
```

`EplanCipMvp.Core/WireNaming/WireRuleTemplate.cs`:

```csharp
using System.Linq;
using System.Text.RegularExpressions;

namespace EplanCipMvp.Core.WireNaming
{
    public class WireRuleContext
    {
        public WireEnd Anchor { get; set; }
        public string Potential { get; set; } = "";
    }

    /// <summary>
    /// Подстановки по «опорному» концу: {Устройство}, {Устройство:от_цифры}, {Клемма}, {Клемма:2},
    /// {Клемма:буквы}, {МодульПЛК:цифры}, {Группа}, {Номер:последняя}, {Потенциал}, {Счётчик}.
    /// Пустая/неизвестная подстановка -> null (правило не сработало). {Счётчик} (не больше одного)
    /// остаётся маркером CounterToken — его разрешает WireNamingEngine, видя все узлы.
    /// </summary>
    public static class WireRuleTemplate
    {
        public const string CounterToken = "{#}";
        private static readonly Regex Placeholder = new Regex(@"\{([^{}]+)\}");

        public static string Render(string template, WireRuleContext ctx)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;
            var anchor = ctx?.Anchor ?? new WireEnd();
            string potential = ctx?.Potential ?? "";
            bool failed = false;
            int counters = 0;
            string result = Placeholder.Replace(template.Trim(), m =>
            {
                string token = m.Groups[1].Value.Trim();
                if (token == "Счётчик") { counters++; return CounterToken; }
                string value = Resolve(token, anchor, potential);
                if (string.IsNullOrEmpty(value)) { failed = true; return ""; }
                return value;
            });
            return failed || counters > 1 ? null : result;
        }

        /// <summary>true — в шаблоне нет подстановок (номер одинаков для всех узлов, например PE).</summary>
        public static bool IsConstant(string template) => !Placeholder.IsMatch(template ?? "");

        private static string Resolve(string token, WireEnd a, string potential)
        {
            string device = a.Device ?? "";
            string terminal = (a.Terminal ?? "").Trim();
            switch (token)
            {
                case "Устройство": return device;
                case "Устройство:от_цифры": return FromFirstDigit(device);
                case "Клемма": return terminal;
                case "Клемма:2": return terminal.Length > 0 && terminal.All(char.IsDigit) ? terminal.PadLeft(2, '0') : "";
                case "Клемма:буквы":
                    string letters = new string(terminal.TakeWhile(char.IsLetter).ToArray());
                    return letters.Length > 0 ? letters : terminal;
                case "МодульПЛК:цифры": return new string(device.Where(char.IsDigit).ToArray());
                case "Группа": return new string(device.TakeWhile(char.IsDigit).ToArray());
                case "Номер:последняя":
                    string tail = new string(device.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                    return tail.Length > 0 ? tail.Substring(tail.Length - 1) : "";
                case "Потенциал": return potential.Trim();
                default: return null;
            }
        }

        private static string FromFirstDigit(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i])) return s.Substring(i);
            return "";
        }
    }
}
```

- [ ] **Step 4: Прогнать**

Run: `BUILD_DIR=$SCRATCH/build tools/container-build.sh selftest`
Expected: `Нумерация жил и проводов: все проверки пройдены.`

- [ ] **Step 5: Контрольная точка** — сборка и самопроверка зелёные.

---

### Task 2: Узлы (`WireNets`)

**Files:** Create: `EplanCipMvp.Core/WireNaming/WireNets.cs`; Modify: `WireNamingSelfTest.cs`

- [ ] **Step 1: Тест** — добавить в `Run()` вызов `TestNets();` и метод:

```csharp
        private static void TestNets()
        {
            // Т-образный узел: два провода к одному контакту -32SF01:13 — один узел.
            var a = W("a", 1, 1060, 560, E(E01, "32K04", "5"), E(E01, "32SF01", "13"));
            var b = W("b", 1, 900, 400, E(E01, "32U04", "RUN"), E(E01, "32SF01", "13"));
            // Две стороны клеммы -1X31:1 — разные контакты, разные узлы.
            var c = W("c", 1, 395, 400, E(E01, "32U04", "2T1"), E(E01, "1X31", "1", "1"));
            var d = W("d", 1, 395, 150, E(E01, "1X31", "1", "2"), E(Field, "T32M04", "U1"));
            var nets = WireNets.Build(new List<WireInfo> { a, b, c, d });
            Check(nets.Count == 3, $"ожидалось 3 узла, получено {nets.Count}");
            Check(nets[0].Wires.Select(w => w.Id).SequenceEqual(new[] { "c" }), "узлы упорядочены по X: первый — c (X=395, выше d)");
            Check(nets[1].Wires.Select(w => w.Id).SequenceEqual(new[] { "d" }), "второй — d");
            Check(nets[2].Wires.Select(w => w.Id).SequenceEqual(new[] { "b", "a" }), "третий — b+a (Т-узел)");
            Check(WireNets.Ends(nets[2]).Count == 3, "у Т-узла 3 разных конца");
        }
```

- [ ] **Step 2: Прогнать** — Expected: ошибка компиляции `WireNets`.

- [ ] **Step 3: Реализация** `EplanCipMvp.Core/WireNaming/WireNets.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace EplanCipMvp.Core.WireNaming
{
    public class WireNet
    {
        public string Id { get; set; } = "";
        /// <summary>Провода узла по порядку: лист, X, сверху вниз. Wires[0] задаёт позицию узла.</summary>
        public List<WireInfo> Wires { get; set; } = new List<WireInfo>();
    }

    /// <summary>Узел = провода, связанные общими контактами (PinKey). Узлы упорядочены по первому проводу.</summary>
    public static class WireNets
    {
        public static List<WireNet> Build(IList<WireInfo> wires)
        {
            var list = (wires ?? new List<WireInfo>()).Where(w => w != null)
                .OrderBy(w => w.Page).ThenBy(w => w.X).ThenByDescending(w => w.Y).ToList();
            var parent = Enumerable.Range(0, list.Count).ToArray();
            int Find(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }

            var firstByPin = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Count; i++)
                foreach (var key in new[] { list[i].Start?.PinKey, list[i].End?.PinKey })
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    if (firstByPin.TryGetValue(key, out int j)) parent[Find(i)] = Find(j);
                    else firstByPin[key] = i;
                }

            return Enumerable.Range(0, list.Count).GroupBy(Find)
                .OrderBy(g => g.Min())
                .Select((g, n) => new WireNet { Id = "n" + n, Wires = g.OrderBy(i => i).Select(i => list[i]).ToList() })
                .ToList();
        }

        /// <summary>Разные концы узла (по PinKey), в порядке проводов: начало, затем конец.</summary>
        public static List<WireEnd> Ends(WireNet net) =>
            net.Wires.SelectMany(w => new[] { w.Start, w.End })
               .Where(e => e != null && ((e.Device ?? "").Length > 0 || (e.Terminal ?? "").Length > 0))
               .GroupBy(e => (e.PinKey ?? "").Length > 0 ? e.PinKey : $"{e.Location}-{e.Device}:{e.Terminal}",
                        StringComparer.OrdinalIgnoreCase)
               .Select(g => g.First())
               .ToList();
    }
}
```

- [ ] **Step 4: Прогнать** — Expected: все проверки пройдены.
- [ ] **Step 5: Контрольная точка.**

---

### Task 3: Выбор жилы (`CoreAssigner`)

**Files:** Create: `EplanCipMvp.Core/WireNaming/CoreAssigner.cs`; Modify: `WireNamingSelfTest.cs`

- [ ] **Step 1: Тест** — в `Run()` добавить `TestCores();`:

```csharp
        private static void TestCores()
        {
            var wires = new List<WireInfo>
            {
                // -WGS101 5x0,5: полевые клеммы 1/3/4, со стороны шкафа клеммы 5/6/7.
                W("g1", 2, 100, 300, E("+CP.ET102", "2X31", "5", "2"), E(Field, "GS101", "1"), cable: "WGS101"),
                W("g3", 2, 140, 300, E("+CP.ET102", "2X31", "6", "2"), E(Field, "GS101", "3"), cable: "WGS101"),
                W("g4", 2, 180, 300, E("+CP.ET102", "2X31", "7", "2"), E(Field, "GS101", "4"), cable: "WGS101"),
                // -W32M04 4x1,5: мотор U1/V1/W1/PE, со стороны шкафа клеммы 1/2/3/PE.
                W("m1", 1, 325, 150, E(E01, "1X31", "1", "2"), E(Field, "T32M04", "U1"), cable: "W32M04"),
                W("m2", 1, 360, 150, E(E01, "1X31", "2", "2"), E(Field, "T32M04", "V1"), cable: "W32M04"),
                W("m3", 1, 395, 150, E(E01, "1X31", "3", "2"), E(Field, "T32M04", "W1"), cable: "W32M04"),
                W("mpe", 1, 430, 150, E(E01, "1X31", "PE", "2"), E(Field, "T32M04", "PE"), cable: "W32M04"),
                // Уже назначенная жила не трогается; кабель без артикула; не хватает жил.
                W("done", 3, 100, 100, E(E01, "1X1", "1"), E(Field, "B1", "1"), cable: "WB1", core: "1"),
                W("noart", 3, 200, 100, E(E01, "1X2", "1"), E(Field, "B2", "1"), cable: "WB2"),
                W("x1", 3, 300, 100, E(E01, "1X3", "1"), E(Field, "B3", "1"), cable: "WB3"),
                W("x2", 3, 340, 100, E(E01, "1X3", "2"), E(Field, "B3", "2"), cable: "WB3"),
            };
            var cables = new List<CableCores>
            {
                new CableCores { Cable = "WGS101", FreeCores = { "1", "2", "3", "4", "5" } },
                new CableCores { Cable = "W32M04", FreeCores = { "1", "2", "3", "GN/YE" } },
                new CableCores { Cable = "WB1", FreeCores = { "2" } },
                new CableCores { Cable = "WB3", FreeCores = { "BN" } },
            };
            var notes = new Dictionary<string, string>();
            var byWire = CoreAssigner.Assign(wires, cables, notes).ToDictionary(c => c.WireId, c => c.Core);

            Check(byWire["g1"] == "1" && byWire["g3"] == "3" && byWire["g4"] == "4", "WGS101: жилы = клеммам поля 1/3/4");
            Check(byWire["m1"] == "1" && byWire["m2"] == "2" && byWire["m3"] == "3", "W32M04: U/V/W -> 1/2/3 (по клеммам шкафа)");
            Check(byWire["mpe"] == "GN/YE", "W32M04: PE -> GN/YE");
            Check(!byWire.ContainsKey("done"), "назначенная жила не трогается");
            Check(!byWire.ContainsKey("noart") && notes["noart"].Contains("нет жил"), "кабель без артикула — пояснение");
            Check(byWire["x1"] == "BN" && !byWire.ContainsKey("x2") && notes["x2"].Contains("не хватает"), "не хватает жил");
        }
```

- [ ] **Step 2: Прогнать** — Expected: ошибка компиляции `CoreAssigner`.

- [ ] **Step 3: Реализация** `EplanCipMvp.Core/WireNaming/CoreAssigner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>
    /// Назначает свободные жилы кабеля его проводам без жилы (как в 1260), первое сработавшее:
    /// (1) жила с обозначением, равным клемме конца (сначала конец в поле: -WGS101 к клеммам 1/3/4 -> жилы 1/3/4);
    /// (2) клемма/потенциал PE -> жила GNYE (GN/YE); (3) следующая свободная жила, кроме GNYE, по порядку артикула.
    /// Провода кабеля — по порядку: лист, X, сверху вниз.
    /// </summary>
    public static class CoreAssigner
    {
        public const string FieldLocation = "+FIELD";

        public static List<CoreChange> Assign(IList<WireInfo> wires, IList<CableCores> cables, IDictionary<string, string> notes)
        {
            var changes = new List<CoreChange>();
            var free = (cables ?? new List<CableCores>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Cable))
                .GroupBy(c => c.Cable, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key,
                              g => g.First().FreeCores.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList(),
                              StringComparer.OrdinalIgnoreCase);

            var pending = (wires ?? new List<WireInfo>())
                .Where(w => w != null && (w.Cable ?? "").Length > 0 && (w.CurrentCore ?? "").Trim().Length == 0)
                .OrderBy(w => w.Page).ThenBy(w => w.X).ThenByDescending(w => w.Y);

            foreach (var wire in pending)
            {
                if (!free.TryGetValue(wire.Cable, out var cores))
                {
                    notes[wire.Id] = $"у кабеля {wire.Cable} нет жил в артикуле — жилу не назначить";
                    continue;
                }
                string core = Pick(wire, cores);
                if (core == null)
                {
                    notes[wire.Id] = $"у кабеля {wire.Cable} не хватает свободных жил";
                    continue;
                }
                cores.Remove(core);
                changes.Add(new CoreChange { WireId = wire.Id, Cable = wire.Cable, Core = core });
            }
            return changes;
        }

        private static string Pick(WireInfo wire, List<string> cores)
        {
            var terminals = new[] { wire.Start, wire.End }
                .Where(e => e != null && (e.Terminal ?? "").Trim().Length > 0)
                .OrderBy(e => WildcardMask.Matches(FieldLocation, e.Location ?? "") ? 0 : 1)
                .Select(e => e.Terminal.Trim())
                .ToList();

            string byTerminal = terminals
                .Select(t => cores.FirstOrDefault(c => string.Equals(c, t, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(c => c != null);
            if (byTerminal != null) return byTerminal;

            bool protective = terminals.Any(t => string.Equals(t, "PE", StringComparison.OrdinalIgnoreCase))
                              || string.Equals((wire.Potential ?? "").Trim(), "PE", StringComparison.OrdinalIgnoreCase);
            if (protective) return cores.FirstOrDefault(IsProtective);
            return cores.FirstOrDefault(c => !IsProtective(c));
        }

        public static bool IsProtective(string core) =>
            string.Equals((core ?? "").Replace("/", "").Replace("-", "").Trim(), "GNYE", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Прогнать** — Expected: все проверки пройдены.
- [ ] **Step 5: Контрольная точка.**

---

### Task 4: План записи (`WireApplyPlanner`)

**Files:** Create: `EplanCipMvp.Core/WireNaming/WireApplyPlanner.cs`; Modify: `WireNamingSelfTest.cs`

- [ ] **Step 1: Тест** — в `Run()` добавить `TestApplyPlanner();`:

```csharp
        private static WireNamingRow Row(string net, string current, string proposed, string potential = "", bool shared = false,
                                         params string[] wireIds) =>
            new WireNamingRow { NetId = net, CurrentName = current, ProposedName = proposed, Potential = potential, SharedName = shared,
                                WireIds = wireIds.ToList() };

        private static void TestApplyPlanner()
        {
            var rows = new List<WireNamingRow>
            {
                Row("n1", "", "11101", wireIds: "w1"),
                Row("n2", "11101", "11102", wireIds: "w2"),
                Row("n3", "", "121", wireIds: "w3"),
                Row("n4", "121", "", wireIds: "w4"),              // не переименовывается, держит 121
                Row("n5", "", "1M", "1M", true, "w5"),
                Row("n6", "", "1M", "1M", true, "w6"),
                Row("n7", "", "", wireIds: "w7"),
            };
            rows[6].Cores.Add(new CoreChange { WireId = "w7", Cable = "W1", Core = "1" });

            var plan = WireApplyPlanner.Plan(rows, new Dictionary<string, string>
            {
                ["n1"] = "11101", ["n2"] = "11102", ["n3"] = "121", ["n5"] = "1M", ["n6"] = "1M", ["n7"] = "", ["nX"] = "5",
            });

            Check(plan.Names.Any(s => s.NetId == "n1" && s.Name == "11101" && s.WireIds.SequenceEqual(new[] { "w1" })),
                  "n1 -> 11101 (n2 освобождает этот номер)");
            Check(plan.Names.Any(s => s.NetId == "n2" && s.Name == "11102"), "n2 -> 11102");
            Check(plan.Rejected.ContainsKey("n3") && plan.Rejected["n3"].Contains("121"), "121 занят узлом n4 — отказ");
            Check(plan.Names.Count(s => s.Name == "1M") == 2, "1M — имя потенциала, повтор разрешён");
            Check(plan.Cores.Count == 1 && plan.Cores[0].WireId == "w7", "n7: только жила");
            Check(plan.Rejected.ContainsKey("nX"), "неизвестный узел — отказ");

            var dup = WireApplyPlanner.Plan(new List<WireNamingRow> { Row("a", "", "X1", wireIds: "1"), Row("b", "", "X1", wireIds: "2") },
                                            new Dictionary<string, string> { ["a"] = "X1", ["b"] = "X1" });
            Check(dup.Rejected.ContainsKey("a") && dup.Rejected.ContainsKey("b") && dup.Names.Count == 0, "дубль в запросе — отказ обоим");
        }
```

- [ ] **Step 2: Прогнать** — Expected: ошибка компиляции `WireApplyPlanner`.

- [ ] **Step 3: Реализация** `EplanCipMvp.Core/WireNaming/WireApplyPlanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace EplanCipMvp.Core.WireNaming
{
    public class WireNameStep
    {
        public string NetId { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> WireIds { get; set; } = new List<string>();
    }

    public class WireApplyPlan
    {
        /// <summary>узел -> причина отказа (его номер не пишется; жилы пишутся).</summary>
        public Dictionary<string, string> Rejected { get; } = new Dictionary<string, string>();
        public List<WireNameStep> Names { get; } = new List<WireNameStep>();
        public List<CoreChange> Cores { get; } = new List<CoreChange>();
    }

    /// <summary>
    /// Что писать. Номер узла не пишется, если его же получает другой узел (в запросе или уже в проекте),
    /// кроме «общих» номеров (SharedName: имя потенциала, постоянный шаблон вроде PE). Жилы пишутся всегда.
    /// Порядок записи не важен: номер провода — просто свойство, не DT (в отличие от кабелей, обмен не нужен).
    /// </summary>
    public static class WireApplyPlanner
    {
        private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        public static WireApplyPlan Plan(IList<WireNamingRow> rows, IDictionary<string, string> newNameByNet)
        {
            var plan = new WireApplyPlan();
            rows = rows ?? new List<WireNamingRow>();
            var byNet = rows.GroupBy(r => r.NetId).ToDictionary(g => g.Key, g => g.First());
            var requested = new Dictionary<string, string>();

            foreach (var kv in newNameByNet ?? new Dictionary<string, string>())
            {
                if (!byNet.TryGetValue(kv.Key, out var row))
                {
                    plan.Rejected[kv.Key] = $"Узел {kv.Key} не найден в последнем чтении — нажмите «Считать» ещё раз.";
                    continue;
                }
                plan.Cores.AddRange(row.Cores);
                string name = (kv.Value ?? "").Trim();
                if (name.Length == 0 || string.Equals(name, row.CurrentName, StringComparison.Ordinal)) continue;
                requested[kv.Key] = name;
            }

            string Effective(WireNamingRow r) =>
                requested.TryGetValue(r.NetId, out string n) ? n : (r.CurrentName.Contains(" / ") ? "" : r.CurrentName.Trim());

            foreach (var group in rows.GroupBy(Effective, Cmp).Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                bool shared = group.All(r => r.SharedName && Cmp.Equals(r.ProposedName, group.Key));
                if (shared) continue;
                foreach (var r in group.Where(r => requested.ContainsKey(r.NetId)))
                    plan.Rejected[r.NetId] = $"{Describe(r)}: номер «{group.Key}» уже есть или его получает другой узел.";
            }

            foreach (var kv in requested.Where(kv => !plan.Rejected.ContainsKey(kv.Key)))
                plan.Names.Add(new WireNameStep { NetId = kv.Key, Name = kv.Value, WireIds = byNet[kv.Key].WireIds.ToList() });
            return plan;
        }

        public static string Describe(WireNamingRow row)
        {
            var ends = row.Ends.Take(2).Select(e => $"{e.Location}-{e.Device}:{e.Terminal}");
            string where = string.Join(" — ", ends);
            return where.Length > 0 ? $"лист {row.Page + 1}, {where}" : row.NetId;
        }
    }
}
```

- [ ] **Step 4: Прогнать** — Expected: все проверки пройдены.
- [ ] **Step 5: Контрольная точка.**

---

### Task 5: Движок правил (`WireNamingEngine`)

**Files:** Create: `EplanCipMvp.Core/WireNaming/WireNamingEngine.cs`; Modify: `WireNamingSelfTest.cs`

- [ ] **Step 1: Тесты** — в `Run()` добавить `TestEngineStatuses();` (пресет — в Task 6):

```csharp
        private static List<WireRule> SimpleRules() => new List<WireRule>
        {
            new WireRule { Name = "ПЛК", Device = "*A*.*", Template = "{МодульПЛК:цифры}{Клемма:2}" },
            new WireRule { Name = "Потенциал", Potential = "да", Template = "{Потенциал}" },
            new WireRule { Name = "Управление", Location = "+CP.*", CounterStart = 121, Template = "{Счётчик}" },
        };

        private static void TestEngineStatuses()
        {
            var wires = new List<WireInfo>
            {
                // 1364, лист L1M1: DI0 и DI1 оба подписаны 11101 (скопировано с донора).
                W("di0", 5, 1130, 300, E(E01, "01U01", "03"), E(E01, "1A1.1", "1"), name: "11101"),
                W("di1", 5, 1620, 300, E(E01, "01QF01", "14"), E(E01, "1A1.1", "2"), name: "11101"),
                // Потенциал 1M в двух разных узлах — законный повтор.
                W("m1", 5, 1000, 500, E(E01, "1X24", "1M", "1"), E(E01, "01U01", "20"), potential: "1M"),
                W("m2", 6, 1000, 500, E(E01, "1X24", "1M", "2"), E(E01, "1V1", "20"), potential: "1M", name: "1M"),
                // Узел без правила держит номер 121 — счётчик его пропускает.
                W("keep", 7, 100, 100, E("", "", "1"), E("", "", "2"), name: "121"),
                W("c1", 7, 200, 100, E(E01, "1X21", "121"), E(E01, "32K04", "3")),
                W("c2", 7, 300, 100, E(E01, "32K04", "4"), E(E01, "32KM04", "13"), name: "999"),
            };
            var rows = WireNamingEngine.Preview(wires, SimpleRules(), new List<CableCores>());
            WireNamingRow R(string wire) => rows.Single(r => r.WireIds.Contains(wire));

            Check(R("di0").ProposedName == "11101" && R("di0").Status == WireNamingStatus.Unchanged, "DI0 остаётся 11101");
            Check(R("di1").ProposedName == "11102" && R("di1").Status == WireNamingStatus.Duplicate && R("di1").Apply,
                  $"DI1 -> 11102, «дубль в проекте», отмечен (получено {R("di1").ProposedName}/{R("di1").Status})");
            Check(R("m1").ProposedName == "1M" && R("m1").Status == WireNamingStatus.New && R("m1").Apply && R("m1").SharedName, "1M — новый, общий");
            Check(R("m2").Status == WireNamingStatus.Unchanged, "второй 1M — без изменений, не конфликт");
            Check(R("keep").Status == WireNamingStatus.NoRule && !R("keep").Apply, "без правила");
            Check(R("c1").ProposedName == "122", $"счётчик пропустил занятый 121 (получено {R("c1").ProposedName})");
            Check(R("c2").ProposedName == "123" && R("c2").Status == WireNamingStatus.Rename && !R("c2").Apply,
                  "существующий другой номер — перенумерация без галочки");

            // Конфликт: два разных узла получают одинаковый номер по правилу.
            var clash = WireNamingEngine.Preview(new List<WireInfo>
            {
                W("x", 1, 100, 100, E(E01, "1A1.1", "1", "a"), E(E01, "K1", "1")),
                W("y", 1, 200, 100, E(E01, "1A1.1", "1", "b"), E(E01, "K2", "1")),
            }, SimpleRules(), new List<CableCores>());
            Check(clash.All(r => r.Status == WireNamingStatus.Conflict && !r.Apply), "одинаковый номер у двух узлов — конфликт");
        }
```

- [ ] **Step 2: Прогнать** — Expected: ошибка компиляции `WireNamingEngine`.

- [ ] **Step 3: Реализация** `EplanCipMvp.Core/WireNaming/WireNamingEngine.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>
    /// Номер узла по правилам (сверху вниз, первое сработавшее; опорный конец — любой конец узла,
    /// подходящий под маски правила) + счётчики + статусы/галочки + жилы кабелей (CoreAssigner).
    /// См. docs/superpowers/specs/2026-09-24-wire-core-numbering-design.md.
    /// </summary>
    public static class WireNamingEngine
    {
        private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        private class Pending
        {
            public WireNamingRow Row;
            public WireRule Rule;
            public string Scope;
            public WireInfo Position;
        }

        public static List<WireNamingRow> Preview(IList<WireInfo> wires, IList<WireRule> rules, IList<CableCores> cables)
        {
            var activeRules = (rules ?? new List<WireRule>()).Where(r => r != null && r.Enabled).ToList();
            var rows = new List<WireNamingRow>();
            var namesByRow = new Dictionary<WireNamingRow, List<string>>();
            var pending = new List<Pending>();

            foreach (var net in WireNets.Build(wires))
            {
                var ends = WireNets.Ends(net);
                string potential = net.Wires.Select(w => (w.Potential ?? "").Trim()).FirstOrDefault(p => p.Length > 0) ?? "";
                var names = net.Wires.Select(w => (w.CurrentName ?? "").Trim()).ToList();
                var row = new WireNamingRow
                {
                    NetId = net.Id,
                    Page = net.Wires[0].Page,
                    WireIds = net.Wires.Select(w => w.Id).ToList(),
                    Ends = ends,
                    Potential = potential,
                    Cable = string.Join(", ", net.Wires.Select(w => w.Cable ?? "").Where(c => c.Length > 0).Distinct(Cmp)),
                    CurrentName = string.Join(" / ", names.Where(n => n.Length > 0).Distinct(Cmp)),
                };
                namesByRow[row] = names;

                foreach (var rule in activeRules)
                {
                    var (name, anchor) = TryRule(rule, net, ends, potential);
                    if (name == null) continue;
                    row.RuleName = rule.Name ?? "";
                    row.ProposedName = name;
                    row.SharedName = WireRuleTemplate.IsConstant(rule.Template) || (potential.Length > 0 && Cmp.Equals(name, potential));
                    if (name.Contains(WireRuleTemplate.CounterToken))
                        pending.Add(new Pending { Row = row, Rule = rule, Scope = anchor.Location ?? "", Position = net.Wires[0] });
                    break;
                }
                rows.Add(row);
            }

            ResolveCounters(rows, pending, activeRules);
            foreach (var row in rows) SetDefaultStatus(row, namesByRow[row]);
            MarkDuplicates(rows);
            MarkConflicts(rows);
            AttachCores(rows, wires, cables);
            return rows;
        }

        private static (string Name, WireEnd Anchor) TryRule(WireRule rule, WireNet net, List<WireEnd> ends, string potential)
        {
            if (!Flag(rule.Potential, potential.Length > 0)) return (null, null);
            if (!Flag(rule.InCable, net.Wires.Any(w => (w.Cable ?? "").Length > 0))) return (null, null);

            foreach (var anchor in ends)
            {
                if (!WildcardMask.MatchesAny(rule.Location, anchor.Location ?? "")) continue;
                if (!WildcardMask.MatchesAny(rule.Device, anchor.Device ?? "")) continue;
                if (!WildcardMask.MatchesAny(rule.Terminal, anchor.Terminal ?? "")) continue;
                if (!string.IsNullOrWhiteSpace(rule.OtherLocation)
                    && !ends.Any(o => !ReferenceEquals(o, anchor) && WildcardMask.MatchesAny(rule.OtherLocation, o.Location ?? "")))
                    continue;
                string name = WireRuleTemplate.Render(rule.Template, new WireRuleContext { Anchor = anchor, Potential = potential });
                if (name != null) return (name, anchor);
            }
            return (null, null);
        }

        /// <summary>"" — любое; "да"/"нет" — требуемое значение.</summary>
        private static bool Flag(string flag, bool value)
        {
            string f = (flag ?? "").Trim().ToLowerInvariant();
            if (f == "да" || f == "yes" || f == "1") return value;
            if (f == "нет" || f == "no" || f == "0") return !value;
            return true;
        }

        /// <summary>
        /// {Счётчик}: отдельная последовательность на (место опорного конца + текст шаблона вокруг счётчика),
        /// начало — CounterStart (по умолчанию 1); номера, уже выданные другими правилами или стоящие
        /// в узлах без правила, пропускаются. Порядок — по правилу (Order: X — столбцы, Y — строки).
        /// </summary>
        private static void ResolveCounters(List<WireNamingRow> rows, List<Pending> pending, List<WireRule> rules)
        {
            var taken = new HashSet<string>(
                rows.Where(r => r.ProposedName.Length > 0 && !r.ProposedName.Contains(WireRuleTemplate.CounterToken)).Select(r => r.ProposedName), Cmp);
            foreach (var r in rows.Where(r => r.RuleName.Length == 0))
                foreach (var n in r.CurrentName.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries)) taken.Add(n);

            var next = new Dictionary<string, int>(Cmp);
            foreach (var group in pending.GroupBy(p => p.Rule).OrderBy(g => rules.IndexOf(g.Key)))
            {
                bool byRows = string.Equals((group.Key.Order ?? "").Trim(), "Y", StringComparison.OrdinalIgnoreCase);
                var ordered = byRows
                    ? group.OrderBy(p => p.Position.Page).ThenByDescending(p => p.Position.Y).ThenBy(p => p.Position.X)
                    : group.OrderBy(p => p.Position.Page).ThenBy(p => p.Position.X).ThenByDescending(p => p.Position.Y);
                foreach (var p in ordered)
                {
                    string template = p.Row.ProposedName;
                    string key = p.Scope + "|" + template;
                    if (!next.TryGetValue(key, out int n)) n = group.Key.CounterStart ?? 1;
                    string name;
                    do { name = template.Replace(WireRuleTemplate.CounterToken, n.ToString()); n++; } while (taken.Contains(name));
                    next[key] = n;
                    taken.Add(name);
                    p.Row.ProposedName = name;
                }
            }
        }

        private static void SetDefaultStatus(WireNamingRow row, List<string> names)
        {
            bool missing = names.Count == 0 || names.Any(n => n.Length == 0 || n.Contains("?"));
            if (row.RuleName.Length == 0) { row.Status = WireNamingStatus.NoRule; row.Apply = false; }
            else if (!missing && Cmp.Equals(row.CurrentName, row.ProposedName) && row.CurrentName == row.ProposedName)
            { row.Status = WireNamingStatus.Unchanged; row.Apply = false; }
            else if (missing) { row.Status = WireNamingStatus.New; row.Apply = true; }
            else { row.Status = WireNamingStatus.Rename; row.Apply = false; }
        }

        /// <summary>Текущий номер повторяется в другом узле (не общий номер) — строки с новым номером отмечаются.</summary>
        private static void MarkDuplicates(List<WireNamingRow> rows)
        {
            var groups = rows.Where(r => r.CurrentName.Length > 0 && !r.CurrentName.Contains(" / "))
                             .GroupBy(r => r.CurrentName, Cmp).Where(g => g.Count() > 1);
            foreach (var group in groups)
            {
                if (group.All(r => r.SharedName && Cmp.Equals(r.ProposedName, group.Key))) continue;
                foreach (var row in group.Where(r => r.Status == WireNamingStatus.Rename))
                {
                    row.Status = WireNamingStatus.Duplicate;
                    row.Apply = true;
                    row.Note = $"номер «{group.Key}» сейчас стоит в {group.Count()} узлах";
                }
            }
        }

        /// <summary>Конфликты — как будто применены ВСЕ предложения; тот же WireApplyPlanner, что и при записи.</summary>
        private static void MarkConflicts(List<WireNamingRow> rows)
        {
            var proposals = rows.Where(r => r.Status == WireNamingStatus.New || r.Status == WireNamingStatus.Duplicate
                                            || r.Status == WireNamingStatus.Rename)
                                .ToDictionary(r => r.NetId, r => r.ProposedName);
            var plan = WireApplyPlanner.Plan(rows, proposals);
            foreach (var row in rows)
            {
                if (!plan.Rejected.TryGetValue(row.NetId, out string reason)) continue;
                row.Status = WireNamingStatus.Conflict;
                row.Apply = false;
                row.Note = reason;
            }
        }

        private static void AttachCores(List<WireNamingRow> rows, IList<WireInfo> wires, IList<CableCores> cables)
        {
            var notes = new Dictionary<string, string>();
            var changes = CoreAssigner.Assign(wires, cables, notes);
            var rowByWire = new Dictionary<string, WireNamingRow>();
            foreach (var row in rows)
                foreach (var id in row.WireIds) rowByWire[id] = row;

            foreach (var change in changes)
            {
                if (!rowByWire.TryGetValue(change.WireId, out var row)) continue;
                row.Cores.Add(change);
                if (row.Status != WireNamingStatus.Conflict) row.Apply = true;
            }
            foreach (var kv in notes)
                if (rowByWire.TryGetValue(kv.Key, out var row))
                    row.Note = row.Note.Length == 0 ? kv.Value : row.Note + "; " + kv.Value;
        }
    }
}
```

- [ ] **Step 4: Прогнать** — Expected: все проверки пройдены.
- [ ] **Step 5: Контрольная точка.**

---

### Task 6: Пресет «Как 1260», хранилище, лист 190 проекта 1260

**Files:** Create: `EplanCipMvp.Core/WireNaming/WireRulePresets.cs`, `EplanCipMvp.Core/WireNaming/WireRuleStore.cs`; Modify: `WireNamingSelfTest.cs`

- [ ] **Step 1: Тесты** — в `Run()` добавить `Test1260Page190();` и `TestRuleStore();`:

```csharp
        /// <summary>Лист 190 проекта 1260 (=1.E01.M/2, мешалка -T32M04 с УПП): номера, как они стоят в PDF 1260.
        /// Координаты — в системе EPLAN (Y вверх), порядок как на листе.</summary>
        private static void Test1260Page190()
        {
            const int p = 190;
            var wires = new List<WireInfo>
            {
                W("pot1L1", p, 325, 850, E(E01, "1UB1", "1L1"), E(E01, "32QF04", "1"), potential: "1L1"),
                W("qf1", p, 325, 700, E(E01, "32QF04", "2"), E(E01, "32KM04", "1")),
                W("qf2", p, 360, 700, E(E01, "32QF04", "4"), E(E01, "32KM04", "3")),
                W("qf3", p, 395, 700, E(E01, "32QF04", "6"), E(E01, "32KM04", "5")),
                W("km1", p, 325, 550, E(E01, "32KM04", "2"), E(E01, "32U04", "1L1")),
                W("km2", p, 360, 550, E(E01, "32KM04", "4"), E(E01, "32U04", "3L2")),
                W("km3", p, 395, 550, E(E01, "32KM04", "6"), E(E01, "32U04", "5L3")),
                W("u1", p, 325, 400, E(E01, "32U04", "2T1"), E(E01, "1X31", "1", "1")),
                W("u2", p, 360, 400, E(E01, "32U04", "4T2"), E(E01, "1X31", "2", "1")),
                W("u3", p, 395, 400, E(E01, "32U04", "6T3"), E(E01, "1X31", "3", "1")),
                W("core1", p, 330, 150, E(E01, "1X31", "1", "2"), E(Field, "T32M04", "U1"), cable: "W32M04"),
                W("core2", p, 365, 150, E(E01, "1X31", "2", "2"), E(Field, "T32M04", "V1"), cable: "W32M04"),
                W("core3", p, 400, 150, E(E01, "1X31", "3", "2"), E(Field, "T32M04", "W1"), cable: "W32M04"),
                W("corePE", p, 430, 150, E(E01, "1X31", "PE", "2"), E(Field, "T32M04", "PE"), cable: "W32M04"),
                W("do", p, 1365, 500, E(E01, "32K04", "A1"), E(E01, "1A2.1", "1")),
                W("di", p, 1365, 150, E(E01, "32KM04", "22"), E(E01, "1A1.1", "1")),
                W("pot31", p, 1300, 450, E(E01, "1X24", "31L+", "2"), E(E01, "32KM04", "21"), potential: "31L+"),
                W("c121", p, 720, 520, E(E01, "1X21", "121", "2"), E(E01, "32K04", "3")),
                W("c122", p, 830, 480, E(E01, "32K04", "4"), E(E01, "32KM04", "13")),
                W("c123a", p, 1060, 560, E(E01, "32K04", "5"), E(E01, "32SF01", "13")),
                W("c123b", p, 900, 400, E(E01, "32U04", "RUN"), E(E01, "32SF01", "13")),
                W("c124", p, 1060, 300, E(E01, "32SF01", "14"), E(E01, "32QF04", "13")),
                W("c125", p, 1060, 200, E(E01, "32QF04", "14"), E(E01, "32KM04", "A1")),
            };
            var expected = new Dictionary<string, string>
            {
                ["pot1L1"] = "1L1", ["qf1"] = "32L41", ["qf2"] = "32L42", ["qf3"] = "32L43",
                ["km1"] = "32L44", ["km2"] = "32L45", ["km3"] = "32L46", ["u1"] = "32L47", ["u2"] = "32L48", ["u3"] = "32L49",
                ["core1"] = "32M04-U", ["core2"] = "32M04-V", ["core3"] = "32M04-W", ["corePE"] = "PE",
                ["do"] = "12101", ["di"] = "11101", ["pot31"] = "31L+",
                ["c121"] = "121", ["c122"] = "122", ["c123a"] = "123", ["c123b"] = "123", ["c124"] = "124", ["c125"] = "125",
            };
            var cables = new List<CableCores> { new CableCores { Cable = "W32M04", FreeCores = { "1", "2", "3", "GNYE" } } };
            var rows = WireNamingEngine.Preview(wires, WireRulePresets.Like1260(), cables);

            var problems = new List<string>();
            foreach (var kv in expected)
            {
                var row = rows.Single(r => r.WireIds.Contains(kv.Key));
                if (row.ProposedName != kv.Value) problems.Add($"{kv.Key}: ожидалось {kv.Value}, получено '{row.ProposedName}' ({row.RuleName})");
                if (row.Status != WireNamingStatus.New) problems.Add($"{kv.Key}: статус {row.Status} — {row.Note}");
            }
            var cores = rows.SelectMany(r => r.Cores).ToDictionary(c => c.WireId, c => c.Core);
            if (!(cores["core1"] == "1" && cores["core2"] == "2" && cores["core3"] == "3" && cores["corePE"] == "GNYE"))
                problems.Add("жилы W32M04: " + string.Join(", ", cores.Select(kv => kv.Key + "=" + kv.Value)));
            Console.WriteLine($"Лист 190 (1260): проводов {expected.Count}, расхождений {problems.Count}.");
            Check(problems.Count == 0, "расхождения с 1260:\n  " + string.Join("\n  ", problems));
        }

        private static void TestRuleStore()
        {
            string path = Path.Combine(Path.GetTempPath(), "wire-rules-selftest-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new WireRuleStore(path);
                Check(store.Load().Count == WireRulePresets.Like1260().Count, "нет файла — пресет 1260");
                var rules = WireRulePresets.Like1260();
                rules[0].Enabled = false;
                rules.Add(new WireRule { Name = "Моё", Device = "K*", CounterStart = 7, Template = "K{Счётчик}" });
                store.Save(rules);
                var loaded = store.Load();
                Check(loaded.Count == rules.Count && !loaded[0].Enabled && loaded.Last().CounterStart == 7 && loaded.Last().Template == "K{Счётчик}",
                      "сохранение/загрузка правил");
                Check(store.ResetToPreset().Count == WireRulePresets.Like1260().Count, "сброс к пресету");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
```

- [ ] **Step 2: Прогнать** — Expected: ошибка компиляции `WireRulePresets`/`WireRuleStore`.

- [ ] **Step 3: Реализация**

`EplanCipMvp.Core/WireNaming/WireRulePresets.cs`:

```csharp
using System.Collections.Generic;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>
    /// Правила, по которым пронумерованы провода 1260 (выведены из схем PDF 1260, лист 190 и др.;
    /// правила «силовая цепь» и «цепь управления» уточняются по первому живому прогону на копии 1260).
    /// Порядок важен: срабатывает первое подходящее.
    /// </summary>
    public static class WireRulePresets
    {
        public static List<WireRule> Like1260() => new List<WireRule>
        {
            new WireRule { Name = "Защитный провод PE", Terminal = "PE", Template = "PE" },
            new WireRule { Name = "Главный шкаф → поле (жила)", Location = "+FIELD", OtherLocation = "+CP.E01",
                           Template = "{Устройство:от_цифры}-{Клемма:буквы}" },
            new WireRule { Name = "Периферия → поле (жила)", Location = "+FIELD", Template = "{Устройство}-{Клемма:буквы}" },
            new WireRule { Name = "Канал ПЛК", Device = "*A*.*", Template = "{МодульПЛК:цифры}{Клемма:2}" },
            new WireRule { Name = "Потенциал", Potential = "да", Template = "{Потенциал}" },
            new WireRule { Name = "Силовая цепь группы", Device = "??QF*;??KM*;??U*", Terminal = "?;?L?;?T?", Order = "Y",
                           Template = "{Группа}L{Номер:последняя}{Счётчик}" },
            new WireRule { Name = "Цепь управления", Location = "+CP.*", CounterStart = 121, Template = "{Счётчик}" },
        };
    }
}
```

`EplanCipMvp.Core/WireNaming/WireRuleStore.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>Правила нумерации жил и проводов в JSON рядом с exe; нет файла — пресет «Как 1260».</summary>
    public class WireRuleStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly object _lock = new object();

        public WireRuleStore(string path) { _path = path; }

        public List<WireRule> Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return WireRulePresets.Like1260();
                return _json.Deserialize<List<WireRule>>(File.ReadAllText(_path, Encoding.UTF8)) ?? new List<WireRule>();
            }
        }

        public List<WireRule> Save(List<WireRule> rules)
        {
            lock (_lock)
            {
                rules = rules ?? new List<WireRule>();
                File.WriteAllText(_path, _json.Serialize(rules), Encoding.UTF8);
                return rules;
            }
        }

        public List<WireRule> ResetToPreset() => Save(WireRulePresets.Like1260());
    }
}
```

- [ ] **Step 4: Прогнать** — Expected: `Лист 190 (1260): проводов 23, расхождений 0.` и все проверки пройдены. Если расхождения — чинить движок/пресет, не ожидания (ожидания = PDF 1260).
- [ ] **Step 5: Контрольная точка.**

---

### Task 7: Чтение и запись EPLAN (`WireNumbering`, `WireNumberingSession`)

**Files:** Create: `EplanCipMvp.App/WireNumbering.cs`, `EplanCipMvp.App/WireNumberingSession.cs`

Живым тестом не проверяется (нет EPLAN в контейнере) — только сборка против DLL 2.9. Свойства: 31011 `CONNECTION_DESIGNATION` (номер провода), 31004 `CONNECTION_WIRENUMBER` (обозначение жилы), 33000 `POTENTIAL_NAME`.

- [ ] **Step 1: `EplanCipMvp.App/WireNumbering.cs`**

```csharp
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
                    var (start, sx, sy) = ReadEnd(conn.StartPin);
                    var (end, ex, ey) = ReadEnd(conn.EndPin);
                    cableByConnection.TryGetValue(identifier, out string cable);
                    string id = "w" + result.Wires.Count;
                    result.Wires.Add(new WireInfo
                    {
                        Id = id,
                        Page = conn.Page != null && pageIndex.TryGetValue(conn.Page.ToStringIdentifier(), out int p) ? p : -1,
                        X = (sx + ex) / 2,
                        Y = (sy + ey) / 2,
                        CurrentName = ReadString(() => conn.Properties[Properties.Connection.CONNECTION_DESIGNATION]),
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
            if (skipped > 0) log($"Пропущено неразмещённых соединений/шаблонов: {skipped}.");
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
                var at = function?.Location;
                var offset = pin.Location;
                if (at != null) { x = at.X + (offset?.X ?? 0); y = at.Y + (offset?.Y ?? 0); }
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
                    conn.PlaceAsConnectionDefinitionPoint(conn.Page, point);
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

        private static PointD StraightMidPoint(Connection conn)
        {
            var (_, sx, sy) = ReadEnd(conn.StartPin);
            var (_, ex, ey) = ReadEnd(conn.EndPin);
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
```

- [ ] **Step 2: `EplanCipMvp.App/WireNumberingSession.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Eplan.EplApi.DataModel;
using EplanCipMvp.Core.WireNaming;

namespace EplanCipMvp.App
{
    /// <summary>
    /// 24.09.2026: «Считать → Предпросмотр → Записать» для жил и проводов — общая логика Gui и Web.
    /// Создавать ТОЛЬКО после EplanBootstrap.PinOnce (внутри EPLAN-состояния), как CableNumberingSession.
    /// </summary>
    public class WireNumberingSession
    {
        private WireReadResult _last = new WireReadResult();

        public List<WireNamingRow> Rows { get; private set; } = new List<WireNamingRow>();

        public List<WireNamingRow> Read(Project project, IList<WireRule> rules, Action<string> log)
        {
            _last = WireNumbering.ReadAll(project, log);
            log($"Считано проводов: {_last.Wires.Count}, из них жил кабелей: {_last.Wires.Count(w => w.Cable.Length > 0)}; " +
                $"кабелей со свободными жилами в артикуле: {_last.Cables.Count}.");
            return Preview(rules);
        }

        public List<WireNamingRow> Preview(IList<WireRule> rules) =>
            Rows = WireNamingEngine.Preview(_last.Wires, rules, _last.Cables);

        /// <summary>Сначала жилы (Assign), затем номера; потом перечитывание и сверка.</summary>
        public void Apply(Project project, IDictionary<string, string> newNameByNet, bool placeDefinitionPoints, Action<string> log)
        {
            var plan = WireApplyPlanner.Plan(Rows, newNameByNet);
            foreach (var reason in plan.Rejected.Values) log("ПРОПУЩЕН: " + reason);

            int coresOk = 0;
            foreach (var change in plan.Cores)
            {
                if (!_last.ConnectionsById.TryGetValue(change.WireId, out var conn)) continue;
                var template = TakeTemplate(change.Cable, change.Core);
                if (template == null)
                {
                    log($"ПРОПУЩЕНА жила {change.Cable}:{change.Core} — шаблон уже занят, нажмите «Считать».");
                    continue;
                }
                if (WireNumbering.AssignCore(template, conn, $"{change.Cable}:{change.Core}", log)) coresOk++;
            }

            var expected = new Dictionary<string, string>();
            foreach (var step in plan.Names)
                foreach (var wireId in step.WireIds)
                {
                    if (!_last.ConnectionsById.TryGetValue(wireId, out var conn)) continue;
                    if (WireNumbering.WriteName(conn, step.Name, placeDefinitionPoints, $"узел {step.NetId}", log))
                        expected[_last.IdentifierById[wireId]] = step.Name;
                }

            var reread = WireNumbering.ReadAll(project, s => { });
            var wireById = reread.Wires.ToDictionary(w => w.Id);
            var nameByIdentifier = new Dictionary<string, string>();
            foreach (var kv in reread.IdentifierById) nameByIdentifier[kv.Value] = wireById[kv.Key].CurrentName;
            int namesOk = 0;
            foreach (var kv in expected)
            {
                if (nameByIdentifier.TryGetValue(kv.Key, out string actual) && actual == kv.Value) namesOk++;
                else log($"ПРОВЕРЬТЕ: провод должен был получить «{kv.Value}», после перечитывания — «{actual ?? "не найден"}».");
            }
            log($"Жил назначено: {coresOk} из {plan.Cores.Count}. Номеров проводов записано и подтверждено: {namesOk} из {expected.Count}. " +
                $"Пропущено узлов: {plan.Rejected.Count}.");
            _last = reread;
        }

        private Connection TakeTemplate(string cable, string core)
        {
            if (!_last.TemplatesByCable.TryGetValue(cable, out var templates)) return null;
            var template = templates.FirstOrDefault(t =>
                string.Equals((t.Properties[Properties.Connection.CONNECTION_WIRENUMBER]?.ToString() ?? "").Trim(), core, StringComparison.OrdinalIgnoreCase));
            if (template != null) templates.Remove(template);
            return template;
        }
    }
}
```

- [ ] **Step 3: Собрать всё**

Run: `BUILD_DIR=$SCRATCH/build tools/container-build.sh all`
Expected: `0 Error(s)`, затем самопроверка зелёная. Ошибки компиляции против EPLAN API чинить по XML-документации в `/workspace/.uploads/eplan-2.9-assemblies/*.xml` (например, тип `StartPin`, `Location`), не меняя поведение.

- [ ] **Step 4: Контрольная точка.**

---

### Task 8: Gui — вкладка «Нумерация жил и проводов»

**Files:** Create: `EplanCipMvp.Gui/WireNumberingPanel.cs`; Modify: `EplanCipMvp.Gui/MainForm.cs`

- [ ] **Step 1: `EplanCipMvp.Gui/WireNumberingPanel.cs`** (по образцу `CableNumberingPanel`, только типы Core в сигнатурах):

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EplanCipMvp.Core.WireNaming;

namespace EplanCipMvp.Gui
{
    /// <summary>
    /// 24.09.2026: нумерация жил и проводов (правила + таблица узлов). С EPLAN сама не работает:
    /// чтение/пересчёт/запись — делегаты хозяина, в сигнатурах только типы Core.
    /// </summary>
    public class WireNumberingPanel : UserControl
    {
        public delegate List<WireNamingRow> ReadWires(IList<WireRule> rules, Action<string> log);
        public delegate List<WireNamingRow> PreviewWires(IList<WireRule> rules);
        public delegate void ApplyWires(IDictionary<string, string> newNameByNet, bool placeDefinitionPoints, Action<string> log);

        private static readonly string[] RuleColumns =
            { "Name", "Location", "Device", "Terminal", "OtherLocation", "Potential", "InCable", "Order", "CounterStart", "Template" };

        private readonly ReadWires _read;
        private readonly PreviewWires _preview;
        private readonly ApplyWires _apply;
        private readonly Action<string> _log;
        private readonly WireRuleStore _ruleStore;

        private DataGridView _gridRules;
        private DataGridView _gridWires;
        private Button _btnRead;
        private Button _btnPreview;
        private Button _btnApply;
        private CheckBox _chkPlacePoints;
        private Label _lblSummary;
        private List<WireRule> _rules = new List<WireRule>();
        private List<WireNamingRow> _rows = new List<WireNamingRow>();
        private bool _filling;

        public WireNumberingPanel(ReadWires read, PreviewWires preview, ApplyWires apply, Action<string> log, string rulesFilePath)
        {
            _read = read;
            _preview = preview;
            _apply = apply;
            _log = log;
            _ruleStore = new WireRuleStore(rulesFilePath);
            Dock = DockStyle.Fill;
            BuildUi();
            LoadRules();
        }

        public bool CanRead
        {
            get => _btnRead.Enabled;
            set => _btnRead.Enabled = value;
        }

        private void BuildUi()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));

            layout.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(1120, 0),
                ForeColor = Color.DimGray,
                Text = "Строка = узел (провода с общим контактом получают один номер). Правила сверху вниз, первое подходящее; " +
                       "маски опорного конца — место/устройство/клемма, несколько масок через «;». Подстановки: {Устройство}, " +
                       "{Устройство:от_цифры}, {Клемма}, {Клемма:2}, {Клемма:буквы}, {МодульПЛК:цифры}, {Группа}, {Номер:последняя}, " +
                       "{Потенциал}, {Счётчик}. Жилы кабелей назначаются из артикула кабеля. Первый прогон — на копии проекта.",
            }, 0, 0);

            _gridRules = NewGrid();
            AddColumn(_gridRules, new DataGridViewCheckBoxColumn(), "Enabled", "Вкл", 4, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Name", "Название", 14, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Location", "Место", 8, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Device", "Устройство", 10, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Terminal", "Клемма", 7, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "OtherLocation", "Место др. конца", 8, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Potential", "Потенциал (да/нет)", 7, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "InCable", "Жила (да/нет)", 6, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Order", "Порядок X/Y", 5, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "CounterStart", "Счётчик с", 5, false);
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "Template", "Шаблон", 20, false);
            layout.Controls.Add(_gridRules, 0, 1);

            var rulesButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            rulesButtons.Controls.Add(NewButton("Добавить правило", (s, e) => _gridRules.Rows.Add(true, "Новое правило", "", "", "", "", "", "", "X", "", "")));
            rulesButtons.Controls.Add(NewButton("Удалить", (s, e) => DeleteSelectedRule()));
            rulesButtons.Controls.Add(NewButton("↑", (s, e) => MoveSelectedRule(-1)));
            rulesButtons.Controls.Add(NewButton("↓", (s, e) => MoveSelectedRule(1)));
            rulesButtons.Controls.Add(NewButton("Сохранить правила", (s, e) => SaveRules()));
            rulesButtons.Controls.Add(NewButton("Сбросить к пресету 1260", (s, e) => ResetRules()));
            layout.Controls.Add(rulesButtons, 0, 2);

            var wireButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            _btnRead = NewButton("Считать провода", (s, e) => ReadClick());
            _btnPreview = NewButton("Пересчитать по правилам", (s, e) => PreviewClick());
            _btnPreview.Enabled = false;
            _btnApply = NewButton("Записать отмеченные", (s, e) => ApplyClick());
            _btnApply.Enabled = false;
            _btnApply.ForeColor = Color.DarkRed;
            _chkPlacePoints = new CheckBox { Text = "Ставить новые точки определения (где их нет)", AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
            _lblSummary = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
            wireButtons.Controls.Add(_btnRead);
            wireButtons.Controls.Add(_btnPreview);
            wireButtons.Controls.Add(_btnApply);
            wireButtons.Controls.Add(_chkPlacePoints);
            wireButtons.Controls.Add(_lblSummary);
            layout.Controls.Add(wireButtons, 0, 3);

            _gridWires = NewGrid();
            AddColumn(_gridWires, new DataGridViewCheckBoxColumn(), "Apply", "✓", 3, false);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Page", "Лист", 4, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Ends", "Подключён к", 28, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Potential", "Потенциал", 7, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Cable", "Кабель / жила", 11, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Current", "Сейчас", 8, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Rule", "Правило", 11, true);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "NewName", "Новый номер", 9, false);
            AddColumn(_gridWires, new DataGridViewTextBoxColumn(), "Status", "Статус", 19, true);
            _gridWires.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_gridWires.IsCurrentCellDirty) _gridWires.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _gridWires.CellValueChanged += GridWires_CellValueChanged;
            layout.Controls.Add(_gridWires, 0, 4);

            Controls.Add(layout);
        }

        private static DataGridView NewGrid() => new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            BackgroundColor = SystemColors.Window,
        };

        private static void AddColumn(DataGridView grid, DataGridViewColumn column, string name, string header, float weight, bool readOnly)
        {
            column.Name = name;
            column.HeaderText = header;
            column.FillWeight = weight;
            column.ReadOnly = readOnly;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(column);
        }

        private static Button NewButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += onClick;
            return button;
        }

        private static string CellText(DataGridViewRow row, string column) => (row.Cells[column].Value?.ToString() ?? "").Trim();

        private void LoadRules()
        {
            try { _rules = _ruleStore.Load(); }
            catch (Exception ex)
            {
                _log("ОШИБКА чтения wire-numbering-rules.json (взят пресет 1260): " + ex.Message);
                _rules = WireRulePresets.Like1260();
            }
            FillRulesGrid();
        }

        private void FillRulesGrid()
        {
            _gridRules.Rows.Clear();
            foreach (var r in _rules)
                _gridRules.Rows.Add(r.Enabled, r.Name, r.Location, r.Device, r.Terminal, r.OtherLocation, r.Potential, r.InCable,
                                    r.Order, r.CounterStart?.ToString() ?? "", r.Template);
        }

        private bool TryReadRulesFromGrid(out List<WireRule> rules)
        {
            _gridRules.EndEdit();
            rules = new List<WireRule>();
            foreach (DataGridViewRow row in _gridRules.Rows)
            {
                string start = CellText(row, "CounterStart");
                int? counterStart = null;
                if (start.Length > 0)
                {
                    if (!int.TryParse(start, out int n))
                    {
                        _log($"Правило «{CellText(row, "Name")}»: в «Счётчик с» должно быть число или пусто.");
                        return false;
                    }
                    counterStart = n;
                }
                rules.Add(new WireRule
                {
                    Enabled = row.Cells["Enabled"].Value is bool enabled && enabled,
                    Name = CellText(row, "Name"),
                    Location = CellText(row, "Location"),
                    Device = CellText(row, "Device"),
                    Terminal = CellText(row, "Terminal"),
                    OtherLocation = CellText(row, "OtherLocation"),
                    Potential = CellText(row, "Potential"),
                    InCable = CellText(row, "InCable"),
                    Order = CellText(row, "Order").Length == 0 ? "X" : CellText(row, "Order"),
                    CounterStart = counterStart,
                    Template = CellText(row, "Template"),
                });
            }
            return true;
        }

        private void DeleteSelectedRule()
        {
            var cell = _gridRules.CurrentCell;
            if (cell != null) _gridRules.Rows.RemoveAt(cell.RowIndex);
        }

        private void MoveSelectedRule(int delta)
        {
            var cell = _gridRules.CurrentCell;
            if (cell == null || !TryReadRulesFromGrid(out var rules)) return;
            int i = cell.RowIndex, j = i + delta;
            if (j < 0 || j >= rules.Count) return;
            var tmp = rules[i]; rules[i] = rules[j]; rules[j] = tmp;
            _rules = rules;
            FillRulesGrid();
            _gridRules.CurrentCell = _gridRules.Rows[j].Cells[cell.ColumnIndex];
        }

        private void SaveRules()
        {
            if (!TryReadRulesFromGrid(out var rules)) return;
            try
            {
                _rules = _ruleStore.Save(rules);
                _log("Правила нумерации проводов сохранены.");
            }
            catch (Exception ex)
            {
                _log("ОШИБКА сохранения правил: " + ex.Message);
            }
        }

        private void ResetRules()
        {
            if (MessageBox.Show(this, "Заменить правила нумерации проводов пресетом «Как 1260»?", "Правила",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                _rules = _ruleStore.ResetToPreset();
                FillRulesGrid();
                _log("Правила нумерации проводов сброшены к пресету «Как 1260».");
            }
            catch (Exception ex)
            {
                _log("ОШИБКА: " + ex.Message);
            }
        }

        private void ReadClick()
        {
            if (!TryReadRulesFromGrid(out var rules)) return;
            _rules = rules;
            RunWithWaitCursor("чтения проводов", () =>
            {
                _rows = _read(_rules, _log);
                FillWiresGrid();
                _btnPreview.Enabled = true;
                _btnApply.Enabled = _rows.Count > 0;
            });
        }

        private void PreviewClick()
        {
            if (!TryReadRulesFromGrid(out var rules)) return;
            _rules = rules;
            RunWithWaitCursor("пересчёта", () =>
            {
                _rows = _preview(_rules);
                FillWiresGrid();
            });
        }

        private void ApplyClick()
        {
            _gridWires.EndEdit();
            var items = _rows.Where(r => r.Apply).ToDictionary(r => r.NetId, r => (r.ProposedName ?? "").Trim());
            if (items.Count == 0)
            {
                _log("Нет отмеченных строк.");
                return;
            }
            if (HighlightDuplicates())
            {
                _log("Есть отмеченные строки с одинаковыми номерами (подсвечены красным) — исправьте перед записью.");
                return;
            }
            int cores = _rows.Where(r => r.Apply).Sum(r => r.Cores.Count);
            if (MessageBox.Show(this, $"Записать номера в {items.Count} узл(ах) и назначить {cores} жил(ы) в проекте EPLAN? " +
                                      "Рекомендуется делать на копии проекта.",
                                "Запись номеров проводов", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RunWithWaitCursor("записи", () =>
            {
                _apply(items, _chkPlacePoints.Checked, _log);
                _rows = _preview(_rules);
                FillWiresGrid();
            });
        }

        private void RunWithWaitCursor(string what, Action action)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                action();
            }
            catch (Exception ex)
            {
                _log($"ОШИБКА {what}: {ex}");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private static string DescribeEnds(List<WireEnd> ends) =>
            string.Join("  ↔  ", (ends ?? new List<WireEnd>()).Select(e => $"{e.Location}-{e.Device}:{e.Terminal}"));

        private static string DescribeCable(WireNamingRow row)
        {
            string cores = string.Join(", ", row.Cores.Select(c => "→ " + c.Core));
            return row.Cable.Length == 0 ? "" : cores.Length == 0 ? row.Cable : $"{row.Cable} {cores}";
        }

        private void FillWiresGrid()
        {
            _filling = true;
            _gridWires.Rows.Clear();
            foreach (var row in _rows)
            {
                int index = _gridWires.Rows.Add(
                    row.Apply,
                    (row.Page + 1).ToString(),
                    DescribeEnds(row.Ends),
                    row.Potential,
                    DescribeCable(row),
                    string.IsNullOrEmpty(row.CurrentName) ? "(пусто)" : row.CurrentName,
                    string.IsNullOrEmpty(row.RuleName) ? "—" : row.RuleName,
                    row.ProposedName,
                    row.Status + (string.IsNullOrEmpty(row.Note) ? "" : " — " + row.Note));
                var gridRow = _gridWires.Rows[index];
                gridRow.Tag = row;
                if (row.Status == WireNamingStatus.Conflict) gridRow.Cells["Apply"].ReadOnly = true;
                if (row.Status == WireNamingStatus.NoRule && row.Cores.Count == 0) gridRow.DefaultCellStyle.ForeColor = Color.Gray;
            }
            _filling = false;

            _lblSummary.Text = "Итого: " + string.Join(", ", _rows.GroupBy(r => r.Status).Select(g => $"{g.Key}: {g.Count()}")) +
                               $"; жил к назначению: {_rows.Sum(r => r.Cores.Count)}";
            HighlightDuplicates();
        }

        private void GridWires_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_filling || e.RowIndex < 0) return;
            var gridRow = _gridWires.Rows[e.RowIndex];
            if (!(gridRow.Tag is WireNamingRow row)) return;

            string column = _gridWires.Columns[e.ColumnIndex].Name;
            if (column == "Apply")
            {
                row.Apply = gridRow.Cells["Apply"].Value is bool apply && apply;
            }
            else if (column == "NewName")
            {
                row.ProposedName = gridRow.Cells["NewName"].Value?.ToString() ?? "";
                if (row.Status == WireNamingStatus.NoRule || row.Status == WireNamingStatus.Unchanged)
                {
                    string value = row.ProposedName.Trim();
                    row.Apply = (value.Length > 0 && value != row.CurrentName) || row.Cores.Count > 0;
                    _filling = true;
                    gridRow.Cells["Apply"].Value = row.Apply;
                    _filling = false;
                }
            }
            HighlightDuplicates();
        }

        /// <summary>Отмеченные строки с одинаковым итоговым номером (кроме общих — потенциал/PE) и конфликты.</summary>
        private bool HighlightDuplicates()
        {
            string Effective(WireNamingRow r) => r.Apply && (r.ProposedName ?? "").Trim().Length > 0
                ? r.ProposedName.Trim()
                : (r.CurrentName.Contains(" / ") ? "" : r.CurrentName);
            var groups = _rows.Where(r => Effective(r).Length > 0)
                              .GroupBy(Effective, StringComparer.OrdinalIgnoreCase)
                              .Where(g => g.Count() > 1 && !g.All(r => r.SharedName))
                              .SelectMany(g => g)
                              .ToList();
            var duplicates = new HashSet<WireNamingRow>(groups);
            bool anyAppliedDuplicate = false;
            foreach (DataGridViewRow gridRow in _gridWires.Rows)
            {
                if (!(gridRow.Tag is WireNamingRow row)) continue;
                bool duplicate = row.Apply && duplicates.Contains(row);
                anyAppliedDuplicate |= duplicate;
                gridRow.DefaultCellStyle.BackColor = duplicate || row.Status == WireNamingStatus.Conflict ? Color.MistyRose : Color.Empty;
            }
            return anyAppliedDuplicate;
        }
    }
}
```

- [ ] **Step 2: `MainForm.cs`** — поля, вкладка, делегаты, включение после подключения.

Рядом с `private CableNumberingPanel _cablesPanel;`:

```csharp
        // 24.09.2026: вкладка «Нумерация жил и проводов».
        private WireNumberingPanel _wiresPanel;
```

В `EplanState` рядом с `Cables`:

```csharp
            /// <summary>24.09.2026: нумерация жил и проводов.</summary>
            public readonly WireNumberingSession Wires = new WireNumberingSession();
```

В `BuildUi` после создания `_cablesPanel` (и до `tabs.TabPages.Add(tabCables)`):

```csharp
            var tabWires = new TabPage("Нумерация жил и проводов");
            _wiresPanel = new WireNumberingPanel(ReadWires, PreviewWires, ApplyWires, Log,
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wire-numbering-rules.json"));
            _wiresPanel.CanRead = false;
            tabWires.Controls.Add(_wiresPanel);
```

и порядок вкладок:

```csharp
            tabs.TabPages.Add(tabCables);
            tabs.TabPages.Add(tabWires);
            tabs.TabPages.Add(tabLegacy);
```

Там, где `_cablesPanel.CanRead = true;` (после успешного подключения), добавить:

```csharp
            _wiresPanel.CanRead = true;
```

Делегаты — после `ApplyCableNames`:

```csharp
        // ===== 24.09.2026: нумерация жил и проводов — делегаты для WireNumberingPanel =====

        [MethodImpl(MethodImplOptions.NoInlining)]
        private List<WireNamingRow> ReadWires(IList<WireRule> rules, Action<string> log)
        {
            if (_eplan == null || _eplan.TargetProject == null)
            {
                log("Сначала подключитесь к проекту.");
                return new List<WireNamingRow>();
            }
            return _eplan.Wires.Read(_eplan.TargetProject, rules, log);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private List<WireNamingRow> PreviewWires(IList<WireRule> rules) =>
            _eplan == null ? new List<WireNamingRow>() : _eplan.Wires.Preview(rules);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ApplyWires(IDictionary<string, string> newNameByNet, bool placeDefinitionPoints, Action<string> log)
        {
            if (_eplan == null || _eplan.TargetProject == null)
            {
                log("Сначала подключитесь к проекту.");
                return;
            }
            _eplan.Wires.Apply(_eplan.TargetProject, newNameByNet, placeDefinitionPoints, log);
        }
```

В `using` файла добавить `using EplanCipMvp.Core.WireNaming;`.

- [ ] **Step 3: Собрать** — `tools/container-build.sh all`, Expected: `0 Error(s)`.
- [ ] **Step 4: Окно под mono+Xvfb** (как в README для Gui): `xvfb-run -a mono .../EplanCipMvp.Gui.exe &`, снять скриншот `import -window root shot.png`, проверить: три вкладки, во вкладке «Нумерация жил и проводов» 7 правил пресета. Если Gui падает в конструкторе из-за старой `List<GroupRow>` (известно, см. README 23.09) — проверить панель отдельно: `csharp`-скрипт не нужен, достаточно, что вкладка кабелей в том же прогоне тоже не открывается; зафиксировать в отчёте.
- [ ] **Step 5: Контрольная точка.**

---

### Task 9: Web — карточка и `/api/wires/*`

**Files:** Modify: `EplanCipMvp.Web/EplanSession.cs`, `EplanCipMvp.Web/ApiServer.cs`, `EplanCipMvp.Web/wwwroot/index.html`, `EplanCipMvp.Web/wwwroot/app.js`

- [ ] **Step 1: `EplanSession.cs`**

`using EplanCipMvp.Core.WireNaming;`. Рядом с `CableApplyItem`/`CableReadResult`:

```csharp
    public class WireApplyRequest
    {
        public List<WireApplyItem> Items { get; set; } = new List<WireApplyItem>();
        public bool PlaceDefinitionPoints { get; set; }
    }

    public class WireApplyItem
    {
        public string NetId { get; set; }
        public string NewName { get; set; }
    }

    public class WireReadResultDto
    {
        public List<string> Log { get; set; } = new List<string>();
        public List<WireNamingRow> Rows { get; set; } = new List<WireNamingRow>();
    }
```

В `EplanState` рядом с `Cables`: `public readonly WireNumberingSession Wires = new WireNumberingSession();`

Методы после `ApplyCableNamesCore`:

```csharp
        /// <summary>24.09.2026: «Считать провода» — все соединения целевого проекта + предпросмотр.</summary>
        public WireReadResultDto ReadWires(List<WireRule> rules)
        {
            return Invoke(() =>
            {
                if (_eplan == null)
                    return new WireReadResultDto { Log = { "Сначала подключитесь (кнопка «Подключиться»)." } };
                return ReadWiresCore(rules);
            });
        }

        private WireReadResultDto ReadWiresCore(List<WireRule> rules)
        {
            var result = new WireReadResultDto();
            if (_eplan.TargetProject == null)
            {
                result.Log.Add("Целевой проект не открыт — сначала подключитесь.");
                return result;
            }
            result.Rows = _eplan.Wires.Read(_eplan.TargetProject, rules, result.Log.Add);
            return result;
        }

        public List<WireNamingRow> PreviewWires(List<WireRule> rules)
        {
            return Invoke(() =>
            {
                if (_eplan == null) return new List<WireNamingRow>();
                return PreviewWiresCore(rules);
            });
        }

        private List<WireNamingRow> PreviewWiresCore(List<WireRule> rules) => _eplan.Wires.Preview(rules);

        /// <summary>24.09.2026: «Записать отмеченные» — жилы, затем номера; перечитывание и сверка.</summary>
        public List<string> ApplyWires(WireApplyRequest request)
        {
            return Invoke(() =>
            {
                if (_eplan == null) return new List<string> { "Сначала подключитесь (кнопка «Подключиться»)." };
                return ApplyWiresCore(request);
            });
        }

        private List<string> ApplyWiresCore(WireApplyRequest request)
        {
            var log = new List<string>();
            if (_eplan.TargetProject == null)
            {
                log.Add("Целевой проект не открыт — сначала подключитесь.");
                return log;
            }
            var items = (request?.Items ?? new List<WireApplyItem>())
                .Where(i => i != null && i.NetId != null)
                .GroupBy(i => i.NetId)
                .ToDictionary(g => g.Key, g => g.Last().NewName);
            _eplan.Wires.Apply(_eplan.TargetProject, items, request?.PlaceDefinitionPoints ?? false, log.Add);
            return log;
        }
```

- [ ] **Step 2: `ApiServer.cs`**

`using EplanCipMvp.Core.WireNaming;`; поле `private readonly WireRuleStore _wireRules;`, в конструкторе рядом с `_cableRules`:

```csharp
            _wireRules = new WireRuleStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wire-numbering-rules.json"));
```

Эндпоинты — перед `ServeStatic(ctx, path);`:

```csharp
            // 24.09.2026: нумерация жил и проводов (docs/superpowers/specs/2026-09-24-wire-core-numbering-design.md).
            if (req.HttpMethod == "GET" && path == "/api/wires/rules")
            {
                WriteJson(ctx.Response, 200, _wireRules.Load());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/rules")
            {
                var rules = _json.Deserialize<List<WireRule>>(ReadBody(req));
                WriteJson(ctx.Response, 200, _wireRules.Save(rules));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/rules/reset")
            {
                WriteJson(ctx.Response, 200, _wireRules.ResetToPreset());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/read")
            {
                WriteJson(ctx.Response, 200, _session.ReadWires(_wireRules.Load()));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/preview")
            {
                WriteJson(ctx.Response, 200, _session.PreviewWires(_wireRules.Load()));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/apply")
            {
                var body = _json.Deserialize<WireApplyRequest>(ReadBody(req));
                WriteJson(ctx.Response, 200, _session.ApplyWires(body));
                return;
            }
```

В `LogResult` добавить ветку: `case WireReadResultDto wires: DiagnosticLog.Lines(wires.Log); break;`

Проверить лимит `JavaScriptSerializer.MaxJsonLength` в `WriteJson`: при ~2000 узлах ответ > 2 МБ по умолчанию — если в `WriteJson` не выставлен, выставить `_json.MaxJsonLength = int.MaxValue;` в конструкторе.

- [ ] **Step 3: `index.html`** — карточка после `cablesCard` (перед `<details class="legacy hidden" id="legacyBlock">`):

```html
  <section class="card hidden" id="wiresCard">
    <h2><span class="step">3</span> Нумерация жил и проводов</h2>
    <div class="no-donor-note">
      Строка = узел (провода с общим контактом получают один номер). Правила сверху вниз, первое подходящее;
      маски опорного конца — место/устройство/клемма, несколько масок через «;».
      Подстановки: {Устройство}, {Устройство:от_цифры}, {Клемма}, {Клемма:2}, {Клемма:буквы}, {МодульПЛК:цифры}, {Группа}, {Номер:последняя}, {Потенциал}, {Счётчик}.
      Жилы кабелей назначаются из артикула кабеля. <b>Первый прогон делайте на копии проекта.</b>
    </div>
    <div class="table-wrap">
      <table class="pump-params cables" id="wireRulesTable">
        <thead><tr><th>Вкл</th><th>Название</th><th>Место</th><th>Устройство</th><th>Клемма</th><th>Место др. конца</th><th>Потенциал (да/нет)</th><th>Жила (да/нет)</th><th>Порядок X/Y</th><th>Счётчик с</th><th>Шаблон</th><th></th></tr></thead>
        <tbody></tbody>
      </table>
    </div>
    <div class="rules-toolbar">
      <button type="button" id="btnWireRuleAdd">Добавить правило</button>
      <button type="button" id="btnWireRulesSave">Сохранить правила</button>
      <button type="button" id="btnWireRulesReset">Сбросить к пресету 1260</button>
    </div>
    <div class="cables-toolbar">
      <button type="button" id="btnWiresRead">Считать провода</button>
      <button type="button" id="btnWiresPreview" disabled>Пересчитать по правилам</button>
      <button type="button" id="btnWiresApply" class="danger" disabled>Записать отмеченные</button>
      <label><input type="checkbox" id="chkPlacePoints"> Ставить новые точки определения (где их нет)</label>
    </div>
    <div id="wiresSummary"></div>
    <div class="table-wrap">
      <table class="pump-params cables" id="wiresTable">
        <thead><tr><th>✓</th><th>Лист</th><th>Подключён к</th><th>Потенциал</th><th>Кабель / жила</th><th>Сейчас</th><th>Правило</th><th>Новый номер</th><th>Статус</th></tr></thead>
        <tbody></tbody>
      </table>
    </div>
  </section>
```

- [ ] **Step 4: `app.js`** — после подключения (рядом с `$("cablesCard").classList.remove("hidden"); await loadCableRules();`):

```js
    $("wiresCard").classList.remove("hidden");
    await loadWireRules();
```

В конец файла:

```js
// 24.09.2026: нумерация жил и проводов. Данные — только через textContent/value.
let wireRules = [];
let wireRows = [];

const WIRE_RULE_FIELDS = ["Name", "Location", "Device", "Terminal", "OtherLocation", "Potential", "InCable", "Order", "CounterStart", "Template"];

function renderWireRules() {
  const tbody = $("wireRulesTable").querySelector("tbody");
  tbody.innerHTML = "";
  wireRules.forEach((rule, index) => {
    const tr = document.createElement("tr");
    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = rule.Enabled !== false;
    enabled.addEventListener("change", () => { rule.Enabled = enabled.checked; });
    cell(tr, enabled);
    for (const field of WIRE_RULE_FIELDS) {
      cell(tr, textInput(rule[field], (v) => {
        rule[field] = field === "CounterStart" ? (v.trim() === "" ? null : parseInt(v, 10)) : v;
      }));
    }
    const tools = document.createElement("span");
    for (const [label, action] of [["↑", -1], ["↓", 1], ["✕", 0]]) {
      const b = document.createElement("button");
      b.type = "button";
      b.textContent = label;
      b.addEventListener("click", () => {
        if (action === 0) wireRules.splice(index, 1);
        else {
          const j = index + action;
          if (j < 0 || j >= wireRules.length) return;
          [wireRules[index], wireRules[j]] = [wireRules[j], wireRules[index]];
        }
        renderWireRules();
      });
      tools.appendChild(b);
    }
    cell(tr, tools);
    tbody.appendChild(tr);
  });
}

async function loadWireRules() {
  try {
    wireRules = await getJson("/api/wires/rules");
    renderWireRules();
  } catch (err) {
    logLine("ОШИБКА загрузки правил проводов: " + err.message);
  }
}

$("btnWireRuleAdd").addEventListener("click", () => {
  wireRules.push({ Name: "Новое правило", Enabled: true, Location: "", Device: "", Terminal: "", OtherLocation: "",
                   Potential: "", InCable: "", Order: "X", CounterStart: null, Template: "" });
  renderWireRules();
});

$("btnWireRulesSave").addEventListener("click", async () => {
  try {
    wireRules = await postJson("/api/wires/rules", wireRules);
    renderWireRules();
    logLine("Правила нумерации проводов сохранены.");
    if (wireRows.length > 0) await previewWires();
  } catch (err) {
    logLine("ОШИБКА сохранения правил: " + err.message);
  }
});

$("btnWireRulesReset").addEventListener("click", async () => {
  if (!confirm("Заменить правила нумерации проводов пресетом «Как 1260»?")) return;
  try {
    wireRules = await postJson("/api/wires/rules/reset", {});
    renderWireRules();
    logLine("Правила нумерации проводов сброшены к пресету «Как 1260».");
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  }
});

function describeWireEnds(ends) {
  return (ends || []).map((e) => `${e.Location}-${e.Device}:${e.Terminal}`).join("  ↔  ");
}

function describeWireCable(row) {
  const cores = (row.Cores || []).map((c) => "→ " + c.Core).join(", ");
  if (!row.Cable) return "";
  return cores ? `${row.Cable} ${cores}` : row.Cable;
}

function wireEffective(row) {
  const proposed = (row.ProposedName || "").trim();
  if (row.Apply && proposed) return proposed;
  return (row.CurrentName || "").includes(" / ") ? "" : (row.CurrentName || "");
}

function highlightWireDuplicates() {
  const groups = new Map();
  for (const row of wireRows) {
    const name = wireEffective(row).toUpperCase();
    if (!name) continue;
    if (!groups.has(name)) groups.set(name, []);
    groups.get(name).push(row);
  }
  const dup = new Set();
  for (const rows of groups.values()) {
    if (rows.length > 1 && !rows.every((r) => r.SharedName)) rows.forEach((r) => dup.add(r));
  }
  const trs = $("wiresTable").querySelectorAll("tbody tr");
  wireRows.forEach((row, i) => {
    trs[i].classList.toggle("conflict", (row.Apply && dup.has(row)) || row.Status === "Конфликт");
  });
}

function renderWireRows() {
  const tbody = $("wiresTable").querySelector("tbody");
  tbody.innerHTML = "";
  const counts = {};
  let cores = 0;
  for (const row of wireRows) {
    counts[row.Status] = (counts[row.Status] || 0) + 1;
    cores += (row.Cores || []).length;
    const tr = document.createElement("tr");
    if (row.Status === "Нет правила" && !(row.Cores || []).length) tr.classList.add("norule");
    const apply = document.createElement("input");
    apply.type = "checkbox";
    apply.checked = row.Apply;
    apply.disabled = row.Status === "Конфликт";
    apply.addEventListener("change", () => { row.Apply = apply.checked; highlightWireDuplicates(); });
    cell(tr, apply);
    cell(tr, String(row.Page + 1));
    cell(tr, describeWireEnds(row.Ends));
    cell(tr, row.Potential || "");
    cell(tr, describeWireCable(row));
    cell(tr, row.CurrentName || "(пусто)");
    cell(tr, row.RuleName || "—");
    cell(tr, textInput(row.ProposedName, (v) => {
      row.ProposedName = v;
      if (row.Status === "Нет правила" || row.Status === "Без изменений") {
        row.Apply = (v.trim() !== "" && v.trim() !== (row.CurrentName || "")) || (row.Cores || []).length > 0;
        apply.checked = row.Apply;
      }
      highlightWireDuplicates();
    }));
    const status = cell(tr, row.Status + (row.Note ? " — " + row.Note : ""));
    if (row.Note) status.classList.add("note");
    tbody.appendChild(tr);
  }
  $("wiresSummary").textContent = "Итого: " + Object.entries(counts).map(([k, v]) => `${k}: ${v}`).join(", ") + `; жил к назначению: ${cores}`;
  highlightWireDuplicates();
}

async function previewWires() {
  wireRows = await postJson("/api/wires/preview", {});
  renderWireRows();
}

$("btnWiresRead").addEventListener("click", async () => {
  const btn = $("btnWiresRead");
  btn.disabled = true;
  btn.textContent = "Читаю…";
  try {
    const result = await postJson("/api/wires/read", {});
    logLines(result.Log || []);
    wireRows = result.Rows || [];
    renderWireRows();
    $("btnWiresPreview").disabled = false;
    $("btnWiresApply").disabled = wireRows.length === 0;
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Считать провода";
  }
});

$("btnWiresPreview").addEventListener("click", async () => {
  try { await previewWires(); } catch (err) { logLine("ОШИБКА: " + err.message); }
});

$("btnWiresApply").addEventListener("click", async () => {
  const items = wireRows.filter((r) => r.Apply).map((r) => ({ NetId: r.NetId, NewName: (r.ProposedName || "").trim() }));
  if (items.length === 0) { logLine("Нет отмеченных строк."); return; }
  if ($("wiresTable").querySelector("tbody tr.conflict input[type=checkbox]:checked")) {
    logLine("Есть отмеченные строки с одинаковыми номерами (подсвечены красным) — исправьте перед записью.");
    return;
  }
  const cores = wireRows.filter((r) => r.Apply).reduce((n, r) => n + (r.Cores || []).length, 0);
  if (!confirm(`Записать номера в ${items.length} узл(ах) и назначить ${cores} жил(ы) в целевом проекте EPLAN? Рекомендуется делать на копии проекта.`)) return;
  const btn = $("btnWiresApply");
  btn.disabled = true;
  btn.textContent = "Записываю…";
  try {
    logLines(await postJson("/api/wires/apply", { Items: items, PlaceDefinitionPoints: $("chkPlacePoints").checked }));
    await previewWires();
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Записать отмеченные";
  }
});
```

- [ ] **Step 5: Собрать** — `tools/container-build.sh all`, Expected: `0 Error(s)`, самопроверка зелёная.
- [ ] **Step 6: Web под mono без EPLAN**: запустить `mono EplanCipMvp.Web.exe` из папки сборки; `curl -s localhost:5177/api/wires/rules` → JSON из 7 правил; `curl -s -X POST localhost:5177/api/wires/read -d '{}'` → `Log: ["Сначала подключитесь…"]`; `GET /` содержит `wiresCard`. Остановить процесс (`pkill -x mono`).
- [ ] **Step 7: Контрольная точка.**

---

### Task 10: README, архив, выдача

**Files:** Modify: `README.md`

- [ ] **Step 1: README** — перед `### Установка: папка программы в Platform\<версия>\Bin (24.09.2026)` добавить раздел:

```markdown
### Нумерация жил и проводов (24.09.2026)

Спецификация: `docs/superpowers/specs/2026-09-24-wire-core-numbering-design.md`,
план: `docs/superpowers/plans/2026-09-24-wire-core-numbering.md`.

- Gui: вкладка «Нумерация жил и проводов»; Web: карточка 3. «Считать провода» читает
  все соединения проекта; строка = узел (провода с общим контактом — один номер).
- Жилы кабелей: обозначение (1/2/3/GNYE, цвета) назначается из свободных жил артикула
  штатным `Connection.Assign` — жила = клемме поля, PE → GNYE, иначе по порядку артикула.
- Номер провода (31011) по пресету «Как 1260»: PE; жила к полю `{Устройство}-{Клемма}`
  (из E01 — от первой цифры: `32M04-U`); канал ПЛК `11101`; потенциал `31L+`; силовая
  цепь группы `32L41…`; цепь управления — сквозной счётчик с 121. Правила редактируются,
  хранятся в `wire-numbering-rules.json` рядом с exe.
- Запись: жилы, затем номера в точки определения соединения (без точки — только с галочкой
  «Ставить новые точки», прямые провода). После записи — перечитывание и сверка.
- Проверено без EPLAN: самопроверка (лист 190 проекта 1260 — 23 провода, 0 расхождений;
  жилы, дубли, счётчики), сборка против DLL 2.9, Web под mono.
- НЕ ПРОВЕРЕНО ЖИВЫМ ТЕСТОМ: чтение соединений/клемм/потенциалов, `Assign` жил,
  запись номера и постановка точек. Первый прогон — «Считать провода» на копии 1260:
  расхождения с 1260 видны в предпросмотре (особенно силовые цепи и цепи управления).
```

- [ ] **Step 2: Архив** — как 24.09 утром: из `bin/Release/net48` Gui (корень) и Web (`Web/`) собрать `EplanCipMvp.Gui.Ready.zip` (python `zipfile`, без `*.log`, `gui-settings.txt`, `*-rules.json`), положить в `/workspace/dl/` и `/workspace/custom_files/`.
- [ ] **Step 3: Drive** — `gdrive_upload` как `EplanCipMvp.Gui.Ready_24.09.2026_wires.zip`, затем `node /home/node/.config/gdrive-mcp/share_eplan_zip.mjs` с новым FILE_ID (открыть по ссылке), проверить скачивание `curl` + `md5sum`.
- [ ] **Step 4: Память** — `free -m`; убить `mono`/`VBCSCompiler` (через `ps -eo pid,args | grep "[V]BCSCompiler"`), не `pkill -f` с шаблоном из командной строки.
