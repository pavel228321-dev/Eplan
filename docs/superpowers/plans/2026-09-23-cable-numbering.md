# Cable Numbering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить в EplanCipMvp.Web карточку «Нумерация кабелей»: считать все кабели целевого проекта EPLAN, посчитать новые имена по настраиваемому списку правил (пресет «Как 1260»), показать предпросмотр с галочками и записать отмеченное.

**Architecture:** Вся логика правил — чистый C# в `EplanCipMvp.Core/CableNaming` (без EPLAN, тестируется в `EplanCipMvp.Core.SelfTest`, в т.ч. на реальном перечне кабелей 1260). EPLAN-обвязка (чтение кабелей, переименование через `NameService.RenameDevice(..., bRenameCDPsAlso: true, ...)`) — тонкий статический класс в `EplanCipMvp.App`. Web — новые эндпоинты `/api/cables/*` в `ApiServer`, методы в `EplanSession` (STA-очередь), хранение правил в JSON рядом с exe, карточка в `index.html`/`app.js`.

**Tech Stack:** .NET Framework 4.8, C# latest, EPLAN P8 2.9 API (`Eplan.EplApi.DataModelu`, `HEServicesu`), `System.Web.Script.Serialization.JavaScriptSerializer`, vanilla JS. Сборка в контейнере: `dotnet` из `/workspace/.dotnet`, запуск SelfTest под `mono`.

**Спецификация:** `docs/superpowers/specs/2026-09-23-cable-numbering-design.md`

**Коммиты:** НЕ делать — у пользователя правило «коммит только по явной просьбе». Вместо шага «Commit» в каждой задаче — контрольная точка сборки.

---

## Карта файлов

| Файл | Действие | Ответственность |
|---|---|---|
| `EplanCipMvp.Core/CableNaming/CableModels.cs` | создать | DTO: `CableEnd`, `CableInfo`, `CableRule`, `CableNamingRow`, `CableNamingStatus` |
| `EplanCipMvp.Core/CableNaming/DeviceTagParser.cs` | создать | полный DT EPLAN → место установки + DT устройства |
| `EplanCipMvp.Core/CableNaming/DeviceNameSplitter.cs` | создать | имя → `FUNC_CODE` (ведущие буквы) + `FUNC_COUNTER` (остаток) |
| `EplanCipMvp.Core/CableNaming/WildcardMask.cs` | создать | маски `*`/`?` без учёта регистра |
| `EplanCipMvp.Core/CableNaming/CableRuleTemplate.cs` | создать | подстановки шаблона (`{Устройство}`, `{ШкафИсточника:2}`, `{.N}`…) |
| `EplanCipMvp.Core/CableNaming/CableApplyPlanner.cs` | создать | конфликты имён + порядок записи (временные имена для обменов) |
| `EplanCipMvp.Core/CableNaming/CableNamingEngine.cs` | создать | правила сверху вниз, обе ориентации, суффиксы, статусы, галочки |
| `EplanCipMvp.Core/CableNaming/CableRulePresets.cs` | создать | пресет «Как 1260» |
| `EplanCipMvp.Core.SelfTest/CableNamingSelfTest.cs` | создать | все тесты движка + тест на фикстуре 1260 |
| `EplanCipMvp.Core.SelfTest/Fixtures/generate_1260_cables.py` | создать | генератор фикстуры из PDF 1260 |
| `EplanCipMvp.Core.SelfTest/Fixtures/1260-cables.tsv` | сгенерировать | 237 кабелей 1260 (источник, цель, тип, жилы, имя) |
| `EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj` | изменить | копировать `Fixtures\*.tsv` в выходную папку |
| `EplanCipMvp.Core.SelfTest/Program.cs` | изменить | вызвать `CableNamingSelfTest.Run()` первым |
| `EplanCipMvp.App/CableNumbering.cs` | создать | EPLAN: прочитать кабели, переименовать кабель |
| `EplanCipMvp.Web/CableRuleStore.cs` | создать | загрузка/сохранение правил JSON, сброс к пресету |
| `EplanCipMvp.Web/EplanSession.cs` | изменить | `ReadCables`/`PreviewCables`/`ApplyCableNames` |
| `EplanCipMvp.Web/ApiServer.cs` | изменить | маршруты `/api/cables/*` |
| `EplanCipMvp.Web/wwwroot/index.html` | изменить | карточка «4 Нумерация кабелей» + стили |
| `EplanCipMvp.Web/wwwroot/app.js` | изменить | редактор правил, таблица предпросмотра, запись |
| `README.md` | изменить | датированная запись о новой функции |

## Общие команды (используются в задачах)

Сборка и запуск SelfTest (Core не ссылается на EPLAN — подмена HintPath не нужна):

```bash
cd /workspace/eplan-cip-mvp
export PATH=/workspace/.dotnet:$PATH DOTNET_ROOT=/workspace/.dotnet
free -m | head -2
dotnet build EplanCipMvp.Core.SelfTest -c Release 2>&1 | grep -E "error|Warn|Build succeeded|Время|Elapsed" | tail -15
mono EplanCipMvp.Core.SelfTest/bin/Release/net48/EplanCipMvp.Core.SelfTest.exe
```

`SelfTest` без аргументов прогоняет встроенные самопроверки и печатает подсказку про xlsx — это нормально. Провал теста = исключение `InvalidOperationException("... FAILED: ...")` и ненулевой код выхода.

---

### Task 1: Модели + разбор DT + разбиение имени + маски

**Files:**
- Create: `EplanCipMvp.Core/CableNaming/CableModels.cs`
- Create: `EplanCipMvp.Core/CableNaming/DeviceTagParser.cs`
- Create: `EplanCipMvp.Core/CableNaming/DeviceNameSplitter.cs`
- Create: `EplanCipMvp.Core/CableNaming/WildcardMask.cs`
- Create: `EplanCipMvp.Core.SelfTest/CableNamingSelfTest.cs`
- Modify: `EplanCipMvp.Core.SelfTest/Program.cs` (первая строка `Main` после `OutputEncoding`)

- [ ] **Step 1: Написать падающий тест**

Создать `EplanCipMvp.Core.SelfTest/CableNamingSelfTest.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Core.SelfTest
{
    internal static class CableNamingSelfTest
    {
        public static void Run()
        {
            Console.WriteLine("=== Самопроверка нумерации кабелей ===");
            TestDeviceTagParser();
            TestDeviceNameSplitter();
            TestWildcardMask();
            Console.WriteLine("Нумерация кабелей: все проверки пройдены.\n");
        }

        private static void Check(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException("CableNaming self-test FAILED: " + what);
        }

        private static void CheckEnd(CableEnd end, string location, string device, string input)
        {
            Check(end != null, $"Parse('{input}') вернул null");
            Check(end.Location == location, $"Parse('{input}').Location = '{end.Location}', ожидалось '{location}'");
            Check(end.Device == device, $"Parse('{input}').Device = '{end.Device}', ожидалось '{device}'");
        }

        private static void TestDeviceTagParser()
        {
            CheckEnd(DeviceTagParser.Parse("=1.E01.LV+CP.ET101-2X24"), "+CP.ET101", "2X24", "=1.E01.LV+CP.ET101-2X24");
            CheckEnd(DeviceTagParser.Parse("+FIELD-C04VP01"), "+FIELD", "C04VP01", "+FIELD-C04VP01");
            CheckEnd(DeviceTagParser.Parse("+CP.E01-WPN-ET101"), "+CP.E01", "WPN-ET101", "+CP.E01-WPN-ET101");
            CheckEnd(DeviceTagParser.Parse("-WC04VP01"), "", "WC04VP01", "-WC04VP01");
            CheckEnd(DeviceTagParser.Parse("+CP.E01-1X2:5"), "+CP.E01", "1X2", "+CP.E01-1X2:5");
            Check(DeviceTagParser.Parse("  ") == null, "Parse(пусто) должен вернуть null");
        }

        private static void TestDeviceNameSplitter()
        {
            void Expect(string name, string code, string counter)
            {
                var (c, n) = DeviceNameSplitter.Split(name);
                Check(c == code && n == counter, $"Split('{name}') = ('{c}','{n}'), ожидалось ('{code}','{counter}')");
            }
            Expect("WC04VP01", "WC", "04VP01");
            Expect("W01M01", "W", "01M01");
            Expect("WPN-ET101", "WPN", "-ET101");
            Expect("WEQ101", "WEQ", "101");
            Expect("", "", "");
        }

        private static void TestWildcardMask()
        {
            Check(WildcardMask.Matches("", "что угодно"), "пустая маска совпадает со всем");
            Check(WildcardMask.Matches("+FIELD", "+field"), "регистр не важен");
            Check(WildcardMask.Matches("+CP.ET*", "+CP.ET101"), "звёздочка");
            Check(!WildcardMask.Matches("+CP.ET*", "+CP.E01"), "звёздочка не должна совпасть с +CP.E01");
            Check(WildcardMask.Matches("1X?", "1X2"), "вопрос = один символ");
            Check(!WildcardMask.Matches("1X2", "11X2"), "совпадение целиком, не подстрока");
            Check(!WildcardMask.Matches("+FIELD", null), "null не совпадает с непустой маской");
        }
    }
}
```

В `EplanCipMvp.Core.SelfTest/Program.cs` в `Main` сразу после строки `Console.OutputEncoding = System.Text.Encoding.UTF8;` вставить:

```csharp
            // 23.09.2026: нумерация кабелей — чистая логика, без аргументов и EPLAN.
            CableNamingSelfTest.Run();
```

- [ ] **Step 2: Убедиться, что сборка падает**

Run: общие команды (сборка SelfTest).
Expected: ошибки компиляции `The type or namespace name 'CableNaming' does not exist` / `DeviceTagParser does not exist`.

- [ ] **Step 3: Минимальная реализация**

`EplanCipMvp.Core/CableNaming/CableModels.cs`:

```csharp
using System.Collections.Generic;

namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>Один конец кабеля: место установки (+CP.ET101) и DT устройства без "-" (2X24).</summary>
    public class CableEnd
    {
        public string Location { get; set; } = "";
        public string Device { get; set; } = "";
    }

    public class CableInfo
    {
        public string Id { get; set; } = "";
        /// <summary>DT кабеля без места установки (WC04VP01), пусто — если не задан.</summary>
        public string CurrentName { get; set; } = "";
        public string CableLocation { get; set; } = "";
        public string Type { get; set; } = "";
        public int CoresTotal { get; set; }
        public List<CableEnd> Sources { get; set; } = new List<CableEnd>();
        public List<CableEnd> Targets { get; set; } = new List<CableEnd>();
    }

    public class CableRule
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string SourceLocation { get; set; } = "";
        public string TargetLocation { get; set; } = "";
        public string TypeContains { get; set; } = "";
        public int? CoresTotal { get; set; }
        public string TargetDevice { get; set; } = "";
        public string Template { get; set; } = "";
    }

    public static class CableNamingStatus
    {
        public const string NoRule = "Нет правила";
        public const string Unchanged = "Без изменений";
        public const string New = "Новое имя";
        public const string Rename = "Переименование";
        public const string Conflict = "Конфликт";
    }

    public class CableNamingRow
    {
        public CableInfo Cable { get; set; }
        public string RuleName { get; set; } = "";
        public string ProposedName { get; set; } = "";
        public string Status { get; set; } = "";
        public string Note { get; set; } = "";
        public bool Apply { get; set; }
    }
}
```

`EplanCipMvp.Core/CableNaming/DeviceTagParser.cs`:

```csharp
namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>
    /// Полный DT EPLAN вида "=Установка+Место-Устройство[:вывод]" -> место + устройство.
    /// Устройство может само содержать "-" (WPN-ET101), поэтому режем по ПЕРВОМУ "-"
    /// после "+". Предполагается, что в месте установки "-" нет (так во всех проектах
    /// 1260/1364: +CP.E01, +CP.ET101, +FIELD).
    /// </summary>
    public static class DeviceTagParser
    {
        public static CableEnd Parse(string fullDeviceTag)
        {
            if (string.IsNullOrWhiteSpace(fullDeviceTag)) return null;
            string s = fullDeviceTag.Trim();
            int colon = s.IndexOf(':');
            if (colon >= 0) s = s.Substring(0, colon);

            int plus = s.IndexOf('+');
            int dash = s.IndexOf('-', plus >= 0 ? plus : 0);
            string location = plus < 0 ? "" : (dash > plus ? s.Substring(plus, dash - plus) : s.Substring(plus));
            string device = dash >= 0 ? s.Substring(dash + 1) : (plus >= 0 ? "" : s.TrimStart('='));
            return new CableEnd { Location = location, Device = device };
        }
    }
}
```

`EplanCipMvp.Core/CableNaming/DeviceNameSplitter.cs`:

```csharp
namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>
    /// Раскладка имени кабеля на части NameParts EPLAN: FUNC_CODE = ведущие буквы,
    /// FUNC_COUNTER = остаток. Гипотеза (не подтверждена живым тестом) — при чтении
    /// кабелей App логирует реальную раскладку существующих имён для сверки.
    /// </summary>
    public static class DeviceNameSplitter
    {
        public static (string Code, string Counter) Split(string name)
        {
            name = name ?? "";
            int i = 0;
            while (i < name.Length && char.IsLetter(name[i])) i++;
            return (name.Substring(0, i), name.Substring(i));
        }
    }
}
```

`EplanCipMvp.Core/CableNaming/WildcardMask.cs`:

```csharp
using System.Text.RegularExpressions;

namespace EplanCipMvp.Core.CableNaming
{
    public static class WildcardMask
    {
        /// <summary>Пустая маска совпадает со всем; иначе совпадение целиком, * и ?, без учёта регистра.</summary>
        public static bool Matches(string mask, string value)
        {
            if (string.IsNullOrWhiteSpace(mask)) return true;
            if (value == null) return false;
            string pattern = "^" + Regex.Escape(mask.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
```

- [ ] **Step 4: Прогнать тест**

Run: общие команды.
Expected: сборка `0 Error(s)`, в выводе `Нумерация кабелей: все проверки пройдены.`, код выхода 0.

- [ ] **Step 5: Контрольная точка** — `free -m`, коммит не делаем (правило пользователя).

---

### Task 2: Шаблоны правил

**Files:**
- Create: `EplanCipMvp.Core/CableNaming/CableRuleTemplate.cs`
- Modify: `EplanCipMvp.Core.SelfTest/CableNamingSelfTest.cs`

- [ ] **Step 1: Написать падающий тест**

В `CableNamingSelfTest.Run()` после `TestWildcardMask();` добавить `TestTemplate();`, и добавить метод:

```csharp
        private static void TestTemplate()
        {
            var ctx = new RuleContext { Device = "C04VP01", SourceLocation = "+CP.ET101", TargetLocation = "+FIELD" };
            void Expect(string template, RuleContext c, string expected)
            {
                string got = CableRuleTemplate.Render(template, c);
                Check(got == expected, $"Render('{template}') = '{got ?? "null"}', ожидалось '{expected ?? "null"}'");
            }
            Expect("W{Устройство}{.N}", ctx, "WC04VP01" + CableRuleTemplate.SuffixToken);
            Expect("W{Устройство:от_цифры}", new RuleContext { Device = "C01M01" }, "W01M01");
            Expect("W21{ШкафИсточника:2}", ctx, "W2101");
            Expect("WEQ{ШкафИсточника}", new RuleContext { SourceLocation = "+CP.ET102" }, "WEQ102");
            Expect("WPN-{МестоИсточника}", ctx, "WPN-ET101");
            Expect("X-{МестоЦели}", ctx, "X-FIELD");
            Expect("WPN-{МестоИсточника}", new RuleContext(), null);
            Expect("W{Неизвестно}", ctx, null);
            Expect("W{Устройство:от_цифры}", new RuleContext { Device = "ABC" }, null);
            Expect("", ctx, null);
            Check(CableRuleTemplate.LastSegment("+CP.ET101") == "ET101", "LastSegment(+CP.ET101)");
            Check(CableRuleTemplate.LastSegment("+FIELD") == "FIELD", "LastSegment(+FIELD)");
        }
```

- [ ] **Step 2: Убедиться, что сборка падает**

Run: общие команды. Expected: `RuleContext`/`CableRuleTemplate` does not exist.

- [ ] **Step 3: Реализация**

`EplanCipMvp.Core/CableNaming/CableRuleTemplate.cs`:

```csharp
using System.Linq;
using System.Text.RegularExpressions;

namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>Значения, доступные шаблону: устройство цели и места установки концов.</summary>
    public class RuleContext
    {
        public string Device { get; set; } = "";
        public string SourceLocation { get; set; } = "";
        public string TargetLocation { get; set; } = "";
    }

    /// <summary>
    /// Подстановки: {Устройство}, {Устройство:от_цифры}, {МестоИсточника}, {МестоЦели},
    /// {ШкафИсточника}, {ШкафИсточника:N}, {.N}. Любая пустая/неизвестная подстановка
    /// (кроме {.N}) -> null, т.е. правило не сработало. {.N} остаётся маркером
    /// SuffixToken — его разрешает CableNamingEngine, видя все кабели сразу.
    /// </summary>
    public static class CableRuleTemplate
    {
        public const string SuffixToken = "{.N}";
        private static readonly Regex Placeholder = new Regex(@"\{([^{}]+)\}");

        public static string Render(string template, RuleContext ctx)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;
            ctx = ctx ?? new RuleContext();
            bool failed = false;
            string result = Placeholder.Replace(template.Trim(), m =>
            {
                string token = m.Groups[1].Value.Trim();
                if (token == ".N") return SuffixToken;
                string value = Resolve(token, ctx);
                if (string.IsNullOrEmpty(value)) { failed = true; return ""; }
                return value;
            });
            return failed ? null : result;
        }

        public static string LastSegment(string location)
        {
            if (string.IsNullOrEmpty(location)) return "";
            string trimmed = location.Trim().TrimStart('+', '=');
            int dot = trimmed.LastIndexOf('.');
            return dot >= 0 ? trimmed.Substring(dot + 1) : trimmed;
        }

        private static string Resolve(string token, RuleContext ctx)
        {
            string name = token;
            string arg = null;
            int colon = token.IndexOf(':');
            if (colon >= 0)
            {
                name = token.Substring(0, colon).Trim();
                arg = token.Substring(colon + 1).Trim();
            }

            switch (name)
            {
                case "Устройство":
                    if (arg == null) return ctx.Device;
                    return arg == "от_цифры" ? FromFirstDigit(ctx.Device) : null;
                case "МестоИсточника":
                    return arg == null ? LastSegment(ctx.SourceLocation) : null;
                case "МестоЦели":
                    return arg == null ? LastSegment(ctx.TargetLocation) : null;
                case "ШкафИсточника":
                    string digits = new string(LastSegment(ctx.SourceLocation).Where(char.IsDigit).ToArray());
                    if (arg == null) return digits;
                    if (!int.TryParse(arg, out int n) || n <= 0) return null;
                    return digits.Length <= n ? digits : digits.Substring(digits.Length - n);
                default:
                    return null;
            }
        }

        private static string FromFirstDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i])) return s.Substring(i);
            return "";
        }
    }
}
```

- [ ] **Step 4: Прогнать тест** — общие команды. Expected: `все проверки пройдены`.

- [ ] **Step 5: Контрольная точка** — `free -m`.

---

### Task 3: Планировщик записи (конфликты, обмены именами)

**Files:**
- Create: `EplanCipMvp.Core/CableNaming/CableApplyPlanner.cs`
- Modify: `EplanCipMvp.Core.SelfTest/CableNamingSelfTest.cs`

- [ ] **Step 1: Написать падающий тест**

В `Run()` после `TestTemplate();` добавить `TestApplyPlanner();` и метод:

```csharp
        private static CableInfo Cab(string id, string current) => new CableInfo { Id = id, CurrentName = current };

        private static void TestApplyPlanner()
        {
            // Обмен именами A<->B: без отказов, оба сначала уходят во временные имена.
            var swap = CableApplyPlanner.Plan(
                new List<CableInfo> { Cab("a", "X"), Cab("b", "Y") },
                new Dictionary<string, string> { { "a", "Y" }, { "b", "X" } });
            Check(swap.Rejected.Count == 0, "обмен: отказов быть не должно");
            Check(swap.TempRenames.Count == 2, $"обмен: ожидалось 2 временных, получено {swap.TempRenames.Count}");
            Check(swap.FinalRenames.Count == 2, "обмен: ожидалось 2 финальных переименования");
            Check(swap.TempRenames.All(t => t.NewName.StartsWith(CableApplyPlanner.TempPrefix)), "временные имена с префиксом");

            // Имя занято кабелем, который не переименовывается.
            var occupied = CableApplyPlanner.Plan(
                new List<CableInfo> { Cab("a", "OLD"), Cab("b", "Y") },
                new Dictionary<string, string> { { "a", "Y" } });
            Check(occupied.Rejected.ContainsKey("a"), "занятое имя должно дать отказ");
            Check(occupied.FinalRenames.Count == 0, "занятое имя: записей быть не должно");

            // Дубли среди новых имён (регистр не важен) + каскад: c хотел имя b, но b отклонён и остаётся "Y".
            var dup = CableApplyPlanner.Plan(
                new List<CableInfo> { Cab("a", ""), Cab("b", "Y"), Cab("c", "Q"), Cab("d", "") },
                new Dictionary<string, string> { { "a", "Z" }, { "b", "z" }, { "c", "Y" } });
            Check(dup.Rejected.ContainsKey("a") && dup.Rejected.ContainsKey("b"), "дубли должны отклоняться оба");
            Check(dup.Rejected.ContainsKey("c"), "каскад: имя b не освободилось, c должен быть отклонён");

            // Неизвестный id, пустое имя, имя без изменений.
            var misc = CableApplyPlanner.Plan(
                new List<CableInfo> { Cab("a", "SAME"), Cab("b", "") },
                new Dictionary<string, string> { { "a", "SAME" }, { "b", "  " }, { "zzz", "W1" } });
            Check(misc.Rejected.ContainsKey("b"), "пустое имя — отказ");
            Check(misc.Rejected.ContainsKey("zzz"), "неизвестный id — отказ");
            Check(!misc.Rejected.ContainsKey("a") && misc.FinalRenames.Count == 0, "имя без изменений — просто пропуск");
        }
```

- [ ] **Step 2: Убедиться, что сборка падает** — `CableApplyPlanner does not exist`.

- [ ] **Step 3: Реализация**

`EplanCipMvp.Core/CableNaming/CableApplyPlanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace EplanCipMvp.Core.CableNaming
{
    public class CableRenameStep
    {
        public string Id { get; set; }
        public string NewName { get; set; }
    }

    public class CableApplyPlan
    {
        /// <summary>id -> причина отказа (эти кабели не пишутся).</summary>
        public Dictionary<string, string> Rejected { get; } = new Dictionary<string, string>();
        /// <summary>Сначала: кабели, чьё текущее имя нужно другому кабелю, уходят во временное имя.</summary>
        public List<CableRenameStep> TempRenames { get; } = new List<CableRenameStep>();
        /// <summary>Затем: финальные имена.</summary>
        public List<CableRenameStep> FinalRenames { get; } = new List<CableRenameStep>();
    }

    /// <summary>
    /// Решает, что и в каком порядке писать. RenameDevice на уже существующий DT может
    /// слить два устройства в одно, поэтому: (1) дубли и занятые имена — отказ;
    /// (2) если имя освобождает другой переименовываемый кабель — сначала временное имя.
    /// </summary>
    public static class CableApplyPlanner
    {
        public const string TempPrefix = "TMPREN";
        private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        public static CableApplyPlan Plan(IList<CableInfo> cables, IDictionary<string, string> newNameById)
        {
            var plan = new CableApplyPlan();
            cables = cables ?? new List<CableInfo>();
            var byId = cables.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            var requested = new Dictionary<string, string>();

            foreach (var kv in newNameById ?? new Dictionary<string, string>())
            {
                if (!byId.TryGetValue(kv.Key, out var cable))
                {
                    plan.Rejected[kv.Key] = $"Кабель {kv.Key} не найден в последнем чтении — нажмите «Считать» ещё раз.";
                    continue;
                }
                string newName = (kv.Value ?? "").Trim();
                if (newName.Length == 0)
                {
                    plan.Rejected[kv.Key] = $"{Describe(cable)}: пустое новое имя.";
                    continue;
                }
                if (string.Equals(newName, cable.CurrentName ?? "", StringComparison.Ordinal)) continue;
                requested[kv.Key] = newName;
            }

            foreach (var group in requested.GroupBy(kv => kv.Value, Cmp).Where(g => g.Count() > 1))
                foreach (var kv in group)
                    plan.Rejected[kv.Key] = $"{Describe(byId[kv.Key])}: имя «{kv.Value}» получают сразу несколько кабелей.";

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var kv in requested)
                {
                    if (plan.Rejected.ContainsKey(kv.Key)) continue;
                    bool occupied = cables.Any(c => c.Id != kv.Key
                        && Cmp.Equals(c.CurrentName ?? "", kv.Value)
                        && (!requested.ContainsKey(c.Id) || plan.Rejected.ContainsKey(c.Id)));
                    if (occupied)
                    {
                        plan.Rejected[kv.Key] = $"{Describe(byId[kv.Key])}: имя «{kv.Value}» уже занято кабелем, который не переименовывается.";
                        changed = true;
                    }
                }
            }

            var accepted = requested.Where(kv => !plan.Rejected.ContainsKey(kv.Key)).ToList();
            var targetNames = new HashSet<string>(accepted.Select(kv => kv.Value), Cmp);
            var allNames = new HashSet<string>(cables.Select(c => c.CurrentName ?? ""), Cmp);
            int tempCounter = 0;
            foreach (var kv in accepted)
            {
                if (targetNames.Contains(byId[kv.Key].CurrentName ?? ""))
                {
                    string temp;
                    do { temp = TempPrefix + (++tempCounter); } while (allNames.Contains(temp) || targetNames.Contains(temp));
                    plan.TempRenames.Add(new CableRenameStep { Id = kv.Key, NewName = temp });
                }
                plan.FinalRenames.Add(new CableRenameStep { Id = kv.Key, NewName = kv.Value });
            }
            return plan;
        }

        public static string Describe(CableInfo cable)
        {
            string name = string.IsNullOrEmpty(cable.CurrentName) ? "(без имени)" : cable.CurrentName;
            return string.IsNullOrEmpty(cable.CableLocation) ? name : $"{cable.CableLocation}-{name}";
        }
    }
}
```

- [ ] **Step 4: Прогнать тест** — общие команды. Expected: `все проверки пройдены`.

- [ ] **Step 5: Контрольная точка** — `free -m`.

---

### Task 4: Движок правил (предпросмотр)

**Files:**
- Create: `EplanCipMvp.Core/CableNaming/CableNamingEngine.cs`
- Modify: `EplanCipMvp.Core.SelfTest/CableNamingSelfTest.cs`

- [ ] **Step 1: Написать падающий тест**

В `Run()` после `TestApplyPlanner();` добавить `TestEngine();` и методы:

```csharp
        private static CableInfo FieldCable(string id, string current, string device, string sourceLocation = "+CP.ET101")
            => new CableInfo
            {
                Id = id, CurrentName = current, Type = "ÖLFLEX CLASSIC 110 7x0,5 мм²", CoresTotal = 7,
                Sources = new List<CableEnd> { new CableEnd { Location = sourceLocation, Device = "2X24" } },
                Targets = new List<CableEnd> { new CableEnd { Location = "+FIELD", Device = device } },
            };

        private static void TestEngine()
        {
            var rules = new List<CableRule>
            {
                new CableRule { Name = "Поле", TargetLocation = "+FIELD", Template = "W{Устройство}{.N}" },
            };

            var rows = CableNamingEngine.Preview(new List<CableInfo>
            {
                FieldCable("1", "", "C04VP01"),
                FieldCable("2", "?W1", "C05VP01"),
                FieldCable("3", "WC06VP01", "C06VP01"),
                FieldCable("4", "WOLD", "C07VP01"),
                new CableInfo { Id = "5", CurrentName = "WEX-1" },
                FieldCable("6", "", "C01FT01"),
                FieldCable("7", "", "C01FT01"),
            }, rules);

            CableNamingRow R(string id) => rows.First(r => r.Cable.Id == id);
            Check(R("1").Status == CableNamingStatus.New && R("1").Apply && R("1").ProposedName == "WC04VP01", "пустое имя -> New, отмечено");
            Check(R("2").Status == CableNamingStatus.New && R("2").Apply, "имя с '?' -> New, отмечено");
            Check(R("3").Status == CableNamingStatus.Unchanged && !R("3").Apply, "совпадает -> Без изменений");
            Check(R("4").Status == CableNamingStatus.Rename && !R("4").Apply && R("4").ProposedName == "WC07VP01", "другое имя -> Переименование, не отмечено");
            Check(R("5").Status == CableNamingStatus.NoRule && R("5").ProposedName == "WEX-1" && R("5").RuleName == "", "нет правила -> имя остаётся");
            Check(R("6").ProposedName == "WC01FT01.1" && R("7").ProposedName == "WC01FT01.2", "суффиксы .1/.2 по порядку");

            // Обратная ориентация: полевое устройство записано в Sources.
            var swapped = new CableInfo
            {
                Id = "s", CurrentName = "",
                Sources = new List<CableEnd> { new CableEnd { Location = "+FIELD", Device = "C09VP01" } },
                Targets = new List<CableEnd> { new CableEnd { Location = "+CP.ET101", Device = "2X24" } },
            };
            var sw = CableNamingEngine.Preview(new List<CableInfo> { swapped }, rules).Single();
            Check(sw.ProposedName == "WC09VP01", $"обратная ориентация: получено '{sw.ProposedName}'");

            // Выключенное правило не работает.
            var off = new List<CableRule> { new CableRule { Name = "x", Enabled = false, TargetLocation = "+FIELD", Template = "W{Устройство}" } };
            Check(CableNamingEngine.Preview(new List<CableInfo> { FieldCable("1", "", "C04VP01") }, off).Single().Status == CableNamingStatus.NoRule, "выключенное правило");

            // Конфликт: два кабеля -> одно имя без {.N}; и предложение = текущее имя кабеля без правила.
            var clash = new List<CableRule> { new CableRule { Name = "Все в X", TargetLocation = "+FIELD", Template = "WX" } };
            var cr = CableNamingEngine.Preview(new List<CableInfo> { FieldCable("1", "", "A1"), FieldCable("2", "", "A2") }, clash);
            Check(cr.All(r => r.Status == CableNamingStatus.Conflict && !r.Apply && r.Note.Length > 0), "два кабеля -> одно имя: оба Конфликт");
            var taken = CableNamingEngine.Preview(new List<CableInfo> { FieldCable("1", "", "C04VP01"), new CableInfo { Id = "2", CurrentName = "WC04VP01" } }, rules);
            Check(taken.First(r => r.Cable.Id == "1").Status == CableNamingStatus.Conflict, "имя занято кабелем без правила -> Конфликт");
        }
```

- [ ] **Step 2: Убедиться, что сборка падает** — `CableNamingEngine does not exist`.

- [ ] **Step 3: Реализация**

`EplanCipMvp.Core/CableNaming/CableNamingEngine.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace EplanCipMvp.Core.CableNaming
{
    public static class CableNamingEngine
    {
        public static List<CableNamingRow> Preview(IList<CableInfo> cables, IList<CableRule> rules)
        {
            var activeRules = (rules ?? new List<CableRule>()).Where(r => r != null && r.Enabled).ToList();
            var rows = new List<CableNamingRow>();
            foreach (var cable in cables ?? new List<CableInfo>())
            {
                var row = new CableNamingRow { Cable = cable, ProposedName = cable.CurrentName ?? "" };
                foreach (var rule in activeRules)
                {
                    string name = TryRule(rule, cable, cable.Sources, cable.Targets)
                                  ?? TryRule(rule, cable, cable.Targets, cable.Sources);
                    if (name == null) continue;
                    row.RuleName = rule.Name ?? "";
                    row.ProposedName = name;
                    break;
                }
                rows.Add(row);
            }

            ResolveSuffixes(rows);
            foreach (var row in rows) SetDefaultStatus(row);
            MarkConflicts(rows);
            return rows;
        }

        private static string TryRule(CableRule rule, CableInfo cable, List<CableEnd> sources, List<CableEnd> targets)
        {
            sources = sources ?? new List<CableEnd>();
            targets = targets ?? new List<CableEnd>();

            if (!string.IsNullOrWhiteSpace(rule.TypeContains)
                && (cable.Type ?? "").IndexOf(rule.TypeContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                return null;
            if (rule.CoresTotal.HasValue && rule.CoresTotal.Value != cable.CoresTotal) return null;

            var source = sources.FirstOrDefault(e => WildcardMask.Matches(rule.SourceLocation, e.Location));
            var target = targets.FirstOrDefault(e => WildcardMask.Matches(rule.TargetLocation, e.Location));
            if (!string.IsNullOrWhiteSpace(rule.SourceLocation) && source == null) return null;
            if (!string.IsNullOrWhiteSpace(rule.TargetLocation) && target == null) return null;
            if (!string.IsNullOrWhiteSpace(rule.TargetDevice)
                && !targets.Any(t => WildcardMask.Matches(rule.TargetDevice, t.Device)))
                return null;

            return CableRuleTemplate.Render(rule.Template, new RuleContext
            {
                Device = target?.Device ?? "",
                SourceLocation = source?.Location ?? "",
                TargetLocation = target?.Location ?? "",
            });
        }

        private static void ResolveSuffixes(List<CableNamingRow> rows)
        {
            var withToken = rows.Where(r => r.ProposedName.Contains(CableRuleTemplate.SuffixToken)).ToList();
            var counts = withToken.GroupBy(BaseName, StringComparer.OrdinalIgnoreCase)
                                  .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in withToken)
            {
                string baseName = BaseName(row);
                if (counts[baseName] > 1)
                {
                    seen.TryGetValue(baseName, out int n);
                    seen[baseName] = ++n;
                    row.ProposedName = row.ProposedName.Replace(CableRuleTemplate.SuffixToken, "." + n);
                }
                else
                {
                    row.ProposedName = baseName;
                }
            }
        }

        private static string BaseName(CableNamingRow row) => row.ProposedName.Replace(CableRuleTemplate.SuffixToken, "");

        private static void SetDefaultStatus(CableNamingRow row)
        {
            string current = row.Cable.CurrentName ?? "";
            if (string.IsNullOrEmpty(row.RuleName)) { row.Status = CableNamingStatus.NoRule; row.Apply = false; }
            else if (string.Equals(row.ProposedName, current, StringComparison.Ordinal)) { row.Status = CableNamingStatus.Unchanged; row.Apply = false; }
            else if (current.Length == 0 || current.Contains("?")) { row.Status = CableNamingStatus.New; row.Apply = true; }
            else { row.Status = CableNamingStatus.Rename; row.Apply = false; }
        }

        /// <summary>Конфликты считаются так, будто применены ВСЕ предложения (New+Rename) —
        /// тот же CableApplyPlanner, что проверяет реальную запись.</summary>
        private static void MarkConflicts(List<CableNamingRow> rows)
        {
            var proposals = rows.Where(r => r.Status == CableNamingStatus.New || r.Status == CableNamingStatus.Rename)
                                .ToDictionary(r => r.Cable.Id, r => r.ProposedName);
            var plan = CableApplyPlanner.Plan(rows.Select(r => r.Cable).ToList(), proposals);
            foreach (var row in rows)
            {
                if (!plan.Rejected.TryGetValue(row.Cable.Id, out string reason)) continue;
                row.Status = CableNamingStatus.Conflict;
                row.Apply = false;
                row.Note = reason;
            }
        }
    }
}
```

- [ ] **Step 4: Прогнать тест** — общие команды. Expected: `все проверки пройдены`.

- [ ] **Step 5: Контрольная точка** — `free -m`.

---

### Task 5: Пресет «Как 1260» + фикстура из реального перечня 1260

**Files:**
- Create: `EplanCipMvp.Core/CableNaming/CableRulePresets.cs`
- Create: `EplanCipMvp.Core.SelfTest/Fixtures/generate_1260_cables.py`
- Create (сгенерировать): `EplanCipMvp.Core.SelfTest/Fixtures/1260-cables.tsv`
- Modify: `EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj`
- Modify: `EplanCipMvp.Core.SelfTest/CableNamingSelfTest.cs`

- [ ] **Step 1: Генератор фикстуры**

`EplanCipMvp.Core.SelfTest/Fixtures/generate_1260_cables.py`:

```python
#!/usr/bin/env python3
"""Build 1260-cables.tsv from the 1260 project PDF (report "Перечень кабелей").

Usage: generate_1260_cables.py <1260_Milk_storage_site.pdf> <out.tsv>
Columns: No, Name, Type, Cores, Sources, Targets; an end list is "LOC|DEV;LOC|DEV".
The cable type is printed on the line right above each row in the report.
"""
import re
import subprocess
import sys

ROW = re.compile(r'^(\d+)  (\+\S+)  (W\S*)  (\S+)  (\d+)  (\d+)(.*)$')


def ends(text):
    out = []
    for part in text.split(';'):
        m = re.match(r'^(\+\S+)?\s*\[(.*)\]$', part.strip())
        if m and m.group(2):
            device = re.sub(r'[^A-Za-z0-9]+$', '', m.group(2))
            out.append((m.group(1) or '') + '|' + device)
    return ';'.join(out)


def main(pdf, out_path):
    text = subprocess.run(['pdftotext', '-layout', pdf, '-'], capture_output=True, text=True, check=True).stdout
    rows = {}
    prev = ''
    for raw in text.split('\n'):
        line = re.sub(r'  +', '  ', raw).strip()
        m = ROW.match(line)
        if m:
            rest = [x for x in m.group(7).split('  ') if x.strip()]
            if len(rest) >= 2:
                src, tgt = ends(rest[0]), ends(rest[1])
            elif len(rest) == 1:
                one = ends(rest[0])
                src, tgt = ('', one) if one.startswith('+FIELD|') else (one, '')
            else:
                src, tgt = '', ''
            rows.setdefault(int(m.group(1)), (m.group(3), prev, m.group(5), src, tgt))
        if line:
            prev = line
    with open(out_path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('No\tName\tType\tCores\tSources\tTargets\n')
        for no in sorted(rows):
            name, ctype, cores, src, tgt = rows[no]
            f.write(f'{no}\t{name}\t{ctype}\t{cores}\t{src}\t{tgt}\n')
    print(f'{len(rows)} cables -> {out_path}')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
```

Run:
```bash
cd /workspace/eplan-cip-mvp/EplanCipMvp.Core.SelfTest/Fixtures
python3 generate_1260_cables.py /workspace/custom_files/1260_Milk_storage_site.pdf 1260-cables.tsv
grep -c . 1260-cables.tsv
grep -E "WC01FT01|WEQ101|W2101|WPN-ET101|W01M01|WT40PT01" 1260-cables.tsv
```
Expected: `237 cables -> 1260-cables.tsv`, 238 строк (с заголовком). Строки-образцы, например:
`8	WC01FT01.1	ÖLFLEX CLASSIC 110 5x0,75 мм²	5	+CP.ET101|2X24;+CP.ET101|2A1.9	+FIELD|C01FT01`
и `74	WT40PT01	ÖLFLEX CLASSIC 110 4x0,5 мм²…	4		+FIELD|T40PT01` (источник пуст — так в отчёте).

В `EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj` перед `</Project>` добавить:

```xml
  <ItemGroup>
    <None Include="Fixtures\*.tsv">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 2: Написать падающий тест**

В `Run()` после `TestEngine();` добавить `Test1260Fixture();` и метод:

```csharp
        private static readonly HashSet<string> ManualIn1260 = new HashSet<string>
        {
            "WPN-PC1", "WEX-POU1", "WEX-POU2", "WEX-POU3", "WEX-POU4", "WEX-FA1", "WEX-FA2", "WEX-FA3", "WEX-FA4",
        };

        private static List<CableEnd> ParseEnds(string text) =>
            (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('|'))
                .Select(p => new CableEnd { Location = p[0], Device = p.Length > 1 ? p[1] : "" })
                .ToList();

        private static void Test1260Fixture()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "1260-cables.tsv");
            Check(File.Exists(path), "нет фикстуры " + path);

            var expected = new Dictionary<string, string>();
            var cables = new List<CableInfo>();
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8).Skip(1).Where(l => l.Trim().Length > 0))
            {
                var f = line.Split('\t');
                cables.Add(new CableInfo
                {
                    Id = f[0], CurrentName = "", Type = f[2], CoresTotal = int.Parse(f[3]),
                    Sources = ParseEnds(f[4]), Targets = ParseEnds(f[5]),
                });
                expected[f[0]] = f[1];
            }
            Check(cables.Count == 237, $"в фикстуре ожидалось 237 кабелей, найдено {cables.Count}");

            var rows = CableNamingEngine.Preview(cables, CableRulePresets.Like1260());
            int matched = 0;
            var problems = new List<string>();
            foreach (var row in rows)
            {
                string name1260 = expected[row.Cable.Id];
                if (ManualIn1260.Contains(name1260))
                {
                    if (row.Status != CableNamingStatus.NoRule) problems.Add($"{name1260}: ожидалось «нет правила», получено '{row.ProposedName}' ({row.RuleName})");
                }
                else if (row.ProposedName == name1260) matched++;
                else problems.Add($"{name1260}: получено '{row.ProposedName}' ({row.RuleName}, {row.Status})");
            }
            Console.WriteLine($"Фикстура 1260: совпало {matched} из {cables.Count - ManualIn1260.Count}, вручную {ManualIn1260.Count}.");
            Check(problems.Count == 0, "расхождения с 1260:\n  " + string.Join("\n  ", problems.Take(20)));
            Check(matched == 228, $"ожидалось 228 совпадений, получено {matched}");
        }
```

- [ ] **Step 3: Убедиться, что сборка падает** — `CableRulePresets does not exist`.

- [ ] **Step 4: Реализация пресета**

`EplanCipMvp.Core/CableNaming/CableRulePresets.cs`:

```csharp
using System.Collections.Generic;

namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>
    /// Правила, по которым названы кабели в 1260 (проверено на его перечне кабелей:
    /// 228/228 совпадений, 9 кабелей без источника/цели — WPN-PC1, WEX-* — вручную).
    /// Порядок важен: срабатывает первое подходящее.
    /// </summary>
    public static class CableRulePresets
    {
        public static List<CableRule> Like1260() => new List<CableRule>
        {
            new CableRule { Name = "Profinet", TypeContains = "ETHERLINE PN", Template = "WPN-{МестоИсточника}" },
            new CableRule { Name = "Выравнивание потенциалов", TypeContains = "H07V-K", CoresTotal = 1, Template = "WEQ{ШкафИсточника}" },
            new CableRule { Name = "Питание шкаф→шкаф", TargetLocation = "+CP.E01", TargetDevice = "1X2", Template = "W21{ШкафИсточника:2}" },
            new CableRule { Name = "Главный шкаф → поле", SourceLocation = "+CP.E01", TargetLocation = "+FIELD", Template = "W{Устройство:от_цифры}{.N}" },
            new CableRule { Name = "Периферия → поле", TargetLocation = "+FIELD", Template = "W{Устройство}{.N}" },
        };
    }
}
```

- [ ] **Step 5: Прогнать тест**

Run: общие команды.
Expected: строка `Фикстура 1260: совпало 228 из 228, вручную 9.` и `все проверки пройдены`.

- [ ] **Step 6: Контрольная точка** — `free -m`.

---

### Task 6: EPLAN — чтение и переименование кабелей

**Files:**
- Create: `EplanCipMvp.App/CableNumbering.cs`

Проверить без EPLAN нельзя — только компиляция против реальных DLL (Task 9) и живой прогон у пользователя.

- [ ] **Step 1: Реализация**

`EplanCipMvp.App/CableNumbering.cs`:

```csharp
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
                        var parts = function.NameParts;
                        log($"Разбор имени (для сверки): {info.CurrentName} = PREFIX '{parts.FUNC_PREFIX}' CODE '{parts.FUNC_CODE}' COUNTER '{parts.FUNC_COUNTER}'");
                        examplesLogged++;
                    }
                }
                catch (Exception ex)
                {
                    log($"ОШИБКА чтения кабеля '{function.Name}': {ex.Message}");
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
                parts.FUNC_PREFIX = "";
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
                log($"ОШИБКА {oldName} -> {newName}: {ex.Message}");
                return false;
            }
        }

        private static List<CableEnd> ToEnds(IEnumerable<StorableObject> objects) =>
            objects.OfType<FunctionBase>()
                   .Select(f => DeviceTagParser.Parse(f.Name))
                   .Where(e => e != null && e.Device.Length > 0)
                   .ToList();

        private static string ReadString(Func<object> read)
        {
            try { return read()?.ToString() ?? ""; }
            catch { return ""; }
        }

        private static int ReadInt(Func<object> read) => int.TryParse(ReadString(read), out int n) ? n : 0;
    }
}
```

- [ ] **Step 2: Контрольная точка** — компиляция проверяется в Task 9 (нужны EPLAN DLL). Если `Properties.Function.FUNC_CABLETYPE` или `CABLE_COUNTOFALLWIRES` не существуют в таком виде — компилятор покажет; сверить имена по `/workspace/.uploads/eplan-2.9-assemblies/Eplan.EplApi.DataModelu.xml` (`grep FUNC_CABLETYPE`, `grep CABLE_COUNTOFALLWIRES`).

---

### Task 7: Web — хранилище правил, сессия, API

**Files:**
- Create: `EplanCipMvp.Web/CableRuleStore.cs`
- Modify: `EplanCipMvp.Web/EplanSession.cs`
- Modify: `EplanCipMvp.Web/ApiServer.cs`

- [ ] **Step 1: Хранилище правил**

`EplanCipMvp.Web/CableRuleStore.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Web
{
    /// <summary>Правила нумерации кабелей в JSON рядом с exe; нет файла — пресет «Как 1260».</summary>
    public class CableRuleStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly object _lock = new object();

        public CableRuleStore(string path) { _path = path; }

        public List<CableRule> Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return CableRulePresets.Like1260();
                return _json.Deserialize<List<CableRule>>(File.ReadAllText(_path, Encoding.UTF8)) ?? new List<CableRule>();
            }
        }

        public List<CableRule> Save(List<CableRule> rules)
        {
            lock (_lock)
            {
                rules = rules ?? new List<CableRule>();
                File.WriteAllText(_path, _json.Serialize(rules), Encoding.UTF8);
                return rules;
            }
        }

        public List<CableRule> ResetToPreset() => Save(CableRulePresets.Like1260());
    }
}
```

- [ ] **Step 2: Методы в EplanSession**

В `EplanCipMvp.Web/EplanSession.cs`:

1) В `using` добавить `using EplanCipMvp.Core.CableNaming;`.

2) После класса `CopyItem` (перед комментарием `/// <summary>` класса `EplanSession`) добавить DTO:

```csharp
    public class CableApplyItem
    {
        public string Id { get; set; }
        public string NewName { get; set; }
    }

    public class CableReadResult
    {
        public List<string> Log { get; set; } = new List<string>();
        public List<CableNamingRow> Rows { get; set; } = new List<CableNamingRow>();
    }
```

3) В класс `EplanState` после `CandidatesByGroup` добавить:

```csharp
            /// <summary>23.09.2026: кабели последнего «Считать» по id из CableInfo.Id.</summary>
            public readonly Dictionary<string, Function> CablesById = new Dictionary<string, Function>();
```

4) В `EplanSession` после поля `_pumpParameterRows` добавить (не EPLAN-типы — безопасно, см. комментарий у EplanState):

```csharp
        private List<CableInfo> _lastCables = new List<CableInfo>();
        private Dictionary<string, int> _lastConnectionCounts = new Dictionary<string, int>();
```

5) После метода `ReconcileApplyCore` добавить:

```csharp
        /// <summary>23.09.2026: «Считать» — все кабели целевого проекта + предпросмотр по правилам.</summary>
        public CableReadResult ReadCables(List<CableRule> rules)
        {
            return Invoke(() =>
            {
                // Только "_eplan == null" здесь — см. комментарий в ReconcilePreview.
                if (_eplan == null)
                    return new CableReadResult { Log = { "Сначала подключитесь (кнопка «Подключиться»)." } };
                return ReadCablesCore(rules);
            });
        }

        private CableReadResult ReadCablesCore(List<CableRule> rules)
        {
            var result = new CableReadResult();
            if (_eplan.TargetProject == null)
            {
                result.Log.Add("Целевой проект не открыт — сначала подключитесь.");
                return result;
            }
            var read = CableNumbering.ReadAll(_eplan.TargetProject, result.Log.Add);
            _eplan.CablesById.Clear();
            foreach (var r in read) _eplan.CablesById[r.Info.Id] = r.Function;
            _lastCables = read.Select(r => r.Info).ToList();
            _lastConnectionCounts = read.ToDictionary(r => r.Info.Id, r => r.ConnectionCount);
            result.Rows = CableNamingEngine.Preview(_lastCables, rules);
            result.Log.Add($"Считано кабелей: {_lastCables.Count}.");
            return result;
        }

        /// <summary>Пересчёт предпросмотра по уже считанным кабелям (после правки правил), без EPLAN.</summary>
        public List<CableNamingRow> PreviewCables(List<CableRule> rules) =>
            Invoke(() => CableNamingEngine.Preview(_lastCables, rules));

        /// <summary>23.09.2026: «Записать» — переименовывает отмеченные кабели, затем перечитывает и сверяет.</summary>
        public List<string> ApplyCableNames(List<CableApplyItem> items)
        {
            return Invoke(() =>
            {
                if (_eplan == null) return new List<string> { "Сначала подключитесь (кнопка «Подключиться»)." };
                return ApplyCableNamesCore(items);
            });
        }

        private List<string> ApplyCableNamesCore(List<CableApplyItem> items)
        {
            var log = new List<string>();
            if (_eplan.TargetProject == null)
            {
                log.Add("Целевой проект не открыт — сначала подключитесь.");
                return log;
            }

            var request = (items ?? new List<CableApplyItem>())
                .Where(i => i != null && i.Id != null)
                .GroupBy(i => i.Id)
                .ToDictionary(g => g.Key, g => g.Last().NewName);
            var plan = CableApplyPlanner.Plan(_lastCables, request);
            foreach (var reason in plan.Rejected.Values) log.Add("ПРОПУЩЕН: " + reason);

            foreach (var step in plan.TempRenames)
                CableNumbering.Rename(_eplan.CablesById[step.Id], step.NewName, s => log.Add("(временно) " + s));
            var renamedFrom = new Dictionary<string, string>();
            foreach (var step in plan.FinalRenames)
                if (CableNumbering.Rename(_eplan.CablesById[step.Id], step.NewName, log.Add))
                    renamedFrom[step.NewName] = step.Id;

            // Проверка: перечитать и сверить имя + число жил под кабелем.
            var reread = CableNumbering.ReadAll(_eplan.TargetProject, s => { });
            var byName = reread.GroupBy(r => r.Info.CurrentName, StringComparer.OrdinalIgnoreCase)
                               .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            int ok = 0;
            foreach (var kv in renamedFrom)
            {
                int before = _lastConnectionCounts.TryGetValue(kv.Value, out int b) ? b : -1;
                if (!byName.TryGetValue(kv.Key, out var found))
                    log.Add($"ПРОВЕРЬТЕ: после записи кабель «{kv.Key}» не найден при перечитывании.");
                else if (found.ConnectionCount != before)
                    log.Add($"ПРОВЕРЬТЕ: у «{kv.Key}» жил под кабелем было {before}, стало {found.ConnectionCount} — жилы могли отвязаться.");
                else ok++;
            }
            log.Add($"Записано и подтверждено перечитыванием: {ok} из {plan.FinalRenames.Count}. Пропущено: {plan.Rejected.Count}.");

            _eplan.CablesById.Clear();
            foreach (var r in reread) _eplan.CablesById[r.Info.Id] = r.Function;
            _lastCables = reread.Select(r => r.Info).ToList();
            _lastConnectionCounts = reread.ToDictionary(r => r.Info.Id, r => r.ConnectionCount);
            return log;
        }
```

- [ ] **Step 3: Маршруты в ApiServer**

В `EplanCipMvp.Web/ApiServer.cs`:

1) В `using` добавить `using EplanCipMvp.Core.CableNaming;`.

2) Поле после `_json`:

```csharp
        private readonly CableRuleStore _cableRules;
```

3) В конструкторе после строки `_wwwroot = ...`:

```csharp
            _cableRules = new CableRuleStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cable-numbering-rules.json"));
```

4) В `Handle` перед строкой `ServeStatic(ctx, path);`:

```csharp
            // 23.09.2026: нумерация кабелей по правилам (см. docs/superpowers/specs/2026-09-23-cable-numbering-design.md).
            if (req.HttpMethod == "GET" && path == "/api/cables/rules")
            {
                WriteJson(ctx.Response, 200, _cableRules.Load());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/rules")
            {
                var rules = _json.Deserialize<List<CableRule>>(ReadBody(req));
                WriteJson(ctx.Response, 200, _cableRules.Save(rules));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/rules/reset")
            {
                WriteJson(ctx.Response, 200, _cableRules.ResetToPreset());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/read")
            {
                WriteJson(ctx.Response, 200, _session.ReadCables(_cableRules.Load()));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/preview")
            {
                WriteJson(ctx.Response, 200, _session.PreviewCables(_cableRules.Load()));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/apply")
            {
                var items = _json.Deserialize<List<CableApplyItem>>(ReadBody(req));
                WriteJson(ctx.Response, 200, _session.ApplyCableNames(items));
                return;
            }
```

- [ ] **Step 4: Контрольная точка** — компиляция в Task 9.

---

### Task 8: Web UI — карточка «Нумерация кабелей»

**Files:**
- Modify: `EplanCipMvp.Web/wwwroot/index.html`
- Modify: `EplanCipMvp.Web/wwwroot/app.js`

- [ ] **Step 1: Разметка и стили**

В `index.html` в `<style>` перед строкой `.hidden { display: none; }` добавить:

```css
  table.cables td input[type="text"] { width: 100%; min-width: 110px; padding: 3px 6px; border: 1px solid var(--border); border-radius: 4px; font-size: 12px; }
  table.cables tr.conflict td { background: #fdecea; }
  table.cables tr.norule td { color: var(--muted); }
  table.cables td.note { white-space: normal; color: var(--warn); max-width: 320px; }
  .rules-toolbar, .cables-toolbar { display: flex; flex-wrap: wrap; gap: 8px; margin: 8px 0; }
```

Перед `<section class="card">` с заголовком `Лог` вставить:

```html
  <section class="card hidden" id="cablesCard">
    <h2><span class="step">4</span> Нумерация кабелей</h2>
    <div class="no-donor-note">
      Правила проверяются сверху вниз, срабатывает первое подходящее. Пустое поле условия — «любое».
      Подстановки: {Устройство}, {Устройство:от_цифры}, {МестоИсточника}, {МестоЦели}, {ШкафИсточника}, {ШкафИсточника:2}, {.N} (суффикс .1/.2, только если кабелей к устройству больше одного).
      <b>Первый прогон делайте на копии проекта.</b>
    </div>
    <div class="table-wrap">
      <table class="pump-params cables" id="rulesTable">
        <thead><tr><th>Вкл</th><th>Название</th><th>Место источника</th><th>Место цели</th><th>Тип содержит</th><th>Жил</th><th>Устройство цели</th><th>Шаблон</th><th></th></tr></thead>
        <tbody></tbody>
      </table>
    </div>
    <div class="rules-toolbar">
      <button type="button" id="btnRuleAdd">Добавить правило</button>
      <button type="button" id="btnRulesSave">Сохранить правила</button>
      <button type="button" id="btnRulesReset">Сбросить к пресету 1260</button>
    </div>
    <div class="cables-toolbar">
      <button type="button" id="btnCablesRead">Считать кабели</button>
      <button type="button" id="btnCablesPreview" disabled>Пересчитать по правилам</button>
      <button type="button" id="btnCablesApply" class="danger" disabled>Записать отмеченные</button>
    </div>
    <div id="cablesSummary"></div>
    <div class="table-wrap">
      <table class="pump-params cables" id="cablesTable">
        <thead><tr><th>✓</th><th>Сейчас</th><th>Источник</th><th>Цель</th><th>Тип / жил</th><th>Правило</th><th>Новое имя</th><th>Статус</th></tr></thead>
        <tbody></tbody>
      </table>
    </div>
  </section>
```

- [ ] **Step 2: Логика**

В `app.js` в обработчике `btnConnect` сразу после строки `$("reconcileCard").classList.remove("hidden");` добавить:

```js
    $("cablesCard").classList.remove("hidden");
    await loadCableRules();
```

В конец `app.js` добавить:

```js
// 23.09.2026: нумерация кабелей по правилам. Все данные выводятся через
// textContent/value (не innerHTML) — имена и типы приходят из проекта EPLAN.
let cableRules = [];
let cableRows = [];

const RULE_FIELDS = ["Name", "SourceLocation", "TargetLocation", "TypeContains", "CoresTotal", "TargetDevice", "Template"];

function textInput(value, onChange) {
  const input = document.createElement("input");
  input.type = "text";
  input.value = value ?? "";
  input.addEventListener("input", () => onChange(input.value));
  return input;
}

function cell(tr, content) {
  const td = document.createElement("td");
  if (content instanceof Node) td.appendChild(content);
  else td.textContent = content ?? "";
  tr.appendChild(td);
  return td;
}

function renderCableRules() {
  const tbody = $("rulesTable").querySelector("tbody");
  tbody.innerHTML = "";
  cableRules.forEach((rule, index) => {
    const tr = document.createElement("tr");
    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = rule.Enabled !== false;
    enabled.addEventListener("change", () => { rule.Enabled = enabled.checked; });
    cell(tr, enabled);
    for (const field of RULE_FIELDS) {
      cell(tr, textInput(rule[field], (v) => {
        rule[field] = field === "CoresTotal" ? (v.trim() === "" ? null : parseInt(v, 10)) : v;
      }));
    }
    const tools = document.createElement("span");
    for (const [label, action] of [["↑", -1], ["↓", 1], ["✕", 0]]) {
      const b = document.createElement("button");
      b.type = "button";
      b.textContent = label;
      b.addEventListener("click", () => {
        if (action === 0) cableRules.splice(index, 1);
        else {
          const j = index + action;
          if (j < 0 || j >= cableRules.length) return;
          [cableRules[index], cableRules[j]] = [cableRules[j], cableRules[index]];
        }
        renderCableRules();
      });
      tools.appendChild(b);
    }
    cell(tr, tools);
    tbody.appendChild(tr);
  });
}

async function loadCableRules() {
  try {
    cableRules = await getJson("/api/cables/rules");
    renderCableRules();
  } catch (err) {
    logLine("ОШИБКА загрузки правил: " + err.message);
  }
}

$("btnRuleAdd").addEventListener("click", () => {
  cableRules.push({ Name: "Новое правило", Enabled: true, SourceLocation: "", TargetLocation: "", TypeContains: "", CoresTotal: null, TargetDevice: "", Template: "" });
  renderCableRules();
});

$("btnRulesSave").addEventListener("click", async () => {
  try {
    cableRules = await postJson("/api/cables/rules", cableRules);
    renderCableRules();
    logLine("Правила сохранены.");
    if (cableRows.length > 0) await previewCables();
  } catch (err) {
    logLine("ОШИБКА сохранения правил: " + err.message);
  }
});

$("btnRulesReset").addEventListener("click", async () => {
  if (!confirm("Заменить текущие правила пресетом «Как 1260»?")) return;
  try {
    cableRules = await postJson("/api/cables/rules/reset", {});
    renderCableRules();
    logLine("Правила сброшены к пресету «Как 1260».");
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  }
});

function describeEnds(ends) {
  return (ends || []).map((e) => `${e.Location}-${e.Device}`).join("; ");
}

function effectiveName(row) {
  return row.Apply ? (row.ProposedName || "").trim() : (row.Cable.CurrentName || "");
}

function highlightDuplicates() {
  const counts = new Map();
  for (const row of cableRows) {
    const name = effectiveName(row).toUpperCase();
    if (name) counts.set(name, (counts.get(name) || 0) + 1);
  }
  const trs = $("cablesTable").querySelectorAll("tbody tr");
  cableRows.forEach((row, i) => {
    const dup = row.Apply && (counts.get(effectiveName(row).toUpperCase()) || 0) > 1;
    trs[i].classList.toggle("conflict", dup || row.Status === "Конфликт");
  });
}

function renderCableRows() {
  const tbody = $("cablesTable").querySelector("tbody");
  tbody.innerHTML = "";
  const counts = {};
  for (const row of cableRows) {
    counts[row.Status] = (counts[row.Status] || 0) + 1;
    const tr = document.createElement("tr");
    if (row.Status === "Нет правила") tr.classList.add("norule");
    const apply = document.createElement("input");
    apply.type = "checkbox";
    apply.checked = row.Apply;
    apply.disabled = row.Status === "Конфликт";
    apply.addEventListener("change", () => { row.Apply = apply.checked; highlightDuplicates(); });
    cell(tr, apply);
    cell(tr, row.Cable.CurrentName || "(пусто)");
    cell(tr, describeEnds(row.Cable.Sources));
    cell(tr, describeEnds(row.Cable.Targets));
    cell(tr, `${row.Cable.Type || ""} / ${row.Cable.CoresTotal}`);
    cell(tr, row.RuleName || "—");
    cell(tr, textInput(row.ProposedName, (v) => {
      row.ProposedName = v;
      if (row.Status === "Нет правила" || row.Status === "Без изменений") {
        row.Apply = v.trim() !== "" && v.trim() !== (row.Cable.CurrentName || "");
        apply.checked = row.Apply;
      }
      highlightDuplicates();
    }));
    const status = cell(tr, row.Status + (row.Note ? " — " + row.Note : ""));
    if (row.Note) status.classList.add("note");
    tbody.appendChild(tr);
  }
  $("cablesSummary").textContent = "Итого: " + Object.entries(counts).map(([k, v]) => `${k}: ${v}`).join(", ");
  highlightDuplicates();
}

async function previewCables() {
  cableRows = await postJson("/api/cables/preview", {});
  renderCableRows();
}

$("btnCablesRead").addEventListener("click", async () => {
  const btn = $("btnCablesRead");
  btn.disabled = true;
  btn.textContent = "Читаю…";
  try {
    const result = await postJson("/api/cables/read", {});
    logLines(result.Log || []);
    cableRows = result.Rows || [];
    renderCableRows();
    $("btnCablesPreview").disabled = false;
    $("btnCablesApply").disabled = cableRows.length === 0;
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Считать кабели";
  }
});

$("btnCablesPreview").addEventListener("click", async () => {
  try { await previewCables(); } catch (err) { logLine("ОШИБКА: " + err.message); }
});

$("btnCablesApply").addEventListener("click", async () => {
  const items = cableRows.filter((r) => r.Apply).map((r) => ({ Id: r.Cable.Id, NewName: (r.ProposedName || "").trim() }));
  if (items.length === 0) { logLine("Нет отмеченных кабелей."); return; }
  if ($("cablesTable").querySelector("tbody tr.conflict input[type=checkbox]:checked")) {
    logLine("Есть отмеченные строки с одинаковыми именами (подсвечены красным) — исправьте перед записью.");
    return;
  }
  if (!confirm(`Переименовать ${items.length} кабел(ей) в целевом проекте EPLAN? Рекомендуется делать на копии проекта.`)) return;
  const btn = $("btnCablesApply");
  btn.disabled = true;
  btn.textContent = "Записываю…";
  try {
    logLines(await postJson("/api/cables/apply", items));
    await previewCables();
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Записать отмеченные";
  }
});
```

- [ ] **Step 3: Проверка синтаксиса JS**

Run: `node --check /workspace/eplan-cip-mvp/EplanCipMvp.Web/wwwroot/app.js && echo OK`
Expected: `OK`.

---

### Task 9: Полная сборка против реальных EPLAN DLL, смоук под mono, упаковка

**Files:** временно `EplanCipMvp.App/EplanCipMvp.App.csproj`, `EplanCipMvp.Gui/EplanCipMvp.Gui.csproj`, `EplanCipMvp.Web/EplanCipMvp.Web.csproj` (HintPath), затем восстановить.

- [ ] **Step 1: Сборка с подменой HintPath**

```bash
cd /workspace/eplan-cip-mvp
export PATH=/workspace/.dotnet:$PATH DOTNET_ROOT=/workspace/.dotnet
free -m | head -2
BK=/tmp/claude-1000/-workspace/92f7865c-3d0a-4d80-b5d7-6342eb31bc27/scratchpad/csproj-backup; mkdir -p $BK
for p in App Gui Web; do cp EplanCipMvp.$p/EplanCipMvp.$p.csproj $BK/; done
mkdir -p EplanCipMvp.App/.eplan-refs-local
cp /workspace/.uploads/eplan-2.9-assemblies/*.dll EplanCipMvp.App/.eplan-refs-local/
REF=$(pwd)/EplanCipMvp.App/.eplan-refs-local
for p in App Gui Web; do sed -i "s#C:\\\\Program Files\\\\EPLAN\\\\Platform\\\\Bin#$REF#g" EplanCipMvp.$p/EplanCipMvp.$p.csproj; done
grep -h HintPath EplanCipMvp.Web/EplanCipMvp.Web.csproj | head -2
dotnet build EplanCipMvp.Gui -c Release 2>&1 | grep -E " error |Build succeeded" | head -20
dotnet build EplanCipMvp.Web -c Release 2>&1 | grep -E " error |Build succeeded" | head -20
```
Expected: `HintPath` указывает на `.eplan-refs-local`; оба `Build succeeded`, 0 ошибок. Если ошибка по `FUNC_CABLETYPE`/`CABLE_COUNTOFALLWIRES`/`GetSourcesAndTargets` — сверить сигнатуры по XML-докам и поправить `CableNumbering.cs`.

- [ ] **Step 2: Смоук HTTP под mono (без EPLAN)**

```bash
cd /workspace/eplan-cip-mvp/EplanCipMvp.Web/bin/Release/net48
(timeout 25 mono EplanCipMvp.Web.exe > /tmp/claude-1000/-workspace/92f7865c-3d0a-4d80-b5d7-6342eb31bc27/scratchpad/web-smoke.log 2>&1 &)
sleep 6
curl -s http://localhost:5177/api/cables/rules | head -c 300; echo
curl -s -X POST http://localhost:5177/api/cables/read -d '{}' ; echo
curl -s -X POST http://localhost:5177/api/cables/preview -d '{}' ; echo
curl -s -X POST http://localhost:5177/api/cables/apply -d '[{"Id":"c0","NewName":"W1"}]' ; echo
ls cable-numbering-rules.json 2>/dev/null || echo "rules file not created yet (ok)"
pkill -f EplanCipMvp.Web.exe; sleep 1; free -m | head -2
```
Expected: `/rules` — JSON с 5 правилами (`"Name":"Profinet"`…); `/read` — `{"Log":["Сначала подключитесь (кнопка «Подключиться»)."],"Rows":[]}`; `/preview` — `[]`; `/apply` — `["Сначала подключитесь ..."]`. Если порт в `Program.cs` не 5177 — взять из вывода `web-smoke.log`.

- [ ] **Step 3: Упаковка и выкладка (тот же состав, что раньше: WinForms в корне, Web в `Web/`)**

```bash
cd /workspace/eplan-cip-mvp
python3 - <<'EOF'
import os, zipfile
root = '/workspace/eplan-cip-mvp'
out = '/workspace/dl/EplanCipMvp.Gui.Ready.zip'
gui = f'{root}/EplanCipMvp.Gui/bin/Release/net48'
web = f'{root}/EplanCipMvp.Web/bin/Release/net48'
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    for base, prefix in ((gui, ''), (web, 'Web/')):
        for d, _, files in os.walk(base):
            for f in files:
                if f.endswith('.pdb') or f.startswith('Eplan.EplApi') or f == 'cable-numbering-rules.json':
                    continue
                full = os.path.join(d, f)
                z.write(full, prefix + os.path.relpath(full, base))
names = zipfile.ZipFile(out).namelist()
bad = [n for n in names if n.endswith('.pdb') or 'Eplan.EplApi' in n]
print(len(names), 'files; leaked:', bad)
assert not bad
assert 'Web/EplanCipMvp.Web.exe' in names and 'Web/wwwroot/app.js' in names
EOF
cp /workspace/dl/EplanCipMvp.Gui.Ready.zip /workspace/custom_files/EplanCipMvp.Gui.Ready.zip
curl -s -o /dev/null -w "%{http_code}\n" https://claude-p-karpuk.dt.conveyor.echelon.business/EplanCipMvp.Gui.Ready.zip
```
Expected: `leaked: []`, HTTP `200`.

- [ ] **Step 4: Восстановить csproj и убрать мусор**

```bash
cd /workspace/eplan-cip-mvp
BK=/tmp/claude-1000/-workspace/92f7865c-3d0a-4d80-b5d7-6342eb31bc27/scratchpad/csproj-backup
for p in App Gui Web; do cp $BK/EplanCipMvp.$p.csproj EplanCipMvp.$p/; done
grep -c "Program Files" EplanCipMvp.Web/EplanCipMvp.Web.csproj
rm -rf EplanCipMvp.App/.eplan-refs-local */bin */obj
free -m | head -2
```
Expected: счётчик `Program Files` > 0 (HintPath вернулся к Windows-пути), папок `bin`/`obj` нет.

---

### Task 10: README

**Files:**
- Modify: `README.md` (перед разделом `## Известные ограничения этой версии`)

- [ ] **Step 1: Дописать раздел**

```markdown
## 23.09.2026: нумерация кабелей по правилам (браузерная версия, карточка 4)

Спецификация: `docs/superpowers/specs/2026-09-23-cable-numbering-design.md`,
план: `docs/superpowers/plans/2026-09-23-cable-numbering.md`.

- «Считать кабели» → таблица всех кабелей целевого проекта с предложенным
  именем по правилам; «Записать отмеченные» переименовывает только отмеченные.
- Правила — таблица «условие → шаблон», сверху вниз, первое подходящее; хранятся
  в `cable-numbering-rules.json` рядом с `EplanCipMvp.Web.exe`. Пресет
  «Как 1260» проверен автотестом на реальном перечне кабелей 1260: 228/228
  совпадений, 9 кабелей без источника/цели (WPN-PC1, WEX-*) — вручную.
- Запись: `NameService.RenameDevice(cable, parts, bRenameCDPsAlso: true, true)` —
  вместе с жилами. Обмен именами идёт через временные имена `TMPREN*`;
  дубли и занятые имена не пишутся. После записи — перечитывание и сверка
  числа жил под кабелем.
- НЕ ПРОВЕРЕНО ЖИВЫМ ТЕСТОМ: раскладка имени на `FUNC_CODE`/`FUNC_COUNTER`
  (при «Считать» в лог пишутся 5 примеров реальной раскладки — сверить) и
  поведение `RenameDevice` для кабелей. Первый прогон — на копии проекта.
- Этап 2 (жилы/провода) — отдельно.
```

- [ ] **Step 2: Контрольная точка** — финальный прогон SelfTest (общие команды), `free -m`.

---

## Самопроверка плана

- Покрытие спецификации: правила/подстановки (Task 2, 4), пустые подстановки и обратная ориентация (Task 2, 4), `{.N}` (Task 4), пресет 1260 + фикстура 228/228 (Task 5), галочки по умолчанию (Task 4), дубли/занятые имена (Task 3, 4, UI в Task 8), запись с жилами + перечитывание (Task 6, 7), редактор правил и JSON (Task 7, 8), напоминание про копию (Task 8), README (Task 10).
- Типы согласованы: `CableInfo/CableEnd/CableRule/CableNamingRow/CableNamingStatus` (Task 1) используются в Task 2–8 с теми же именами; `CableApplyPlanner.Plan` возвращает `CableApplyPlan{Rejected, TempRenames, FinalRenames}` — так же в Task 4 и Task 7; `CableNumbering.ReadAll/Rename`, `ReadCable{Info, Function, ConnectionCount}` — Task 6 и Task 7.
