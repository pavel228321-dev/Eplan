# Генерация номеров потенциалов Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Достроить номер потенциала (`30L+`, `1L11`, `1M`...), когда EPLAN даёт только букву без числа — через обход вверх до ближайшего автомата/БП, по правилам, выведенным из проекта 1260.

**Architecture:** Новый класс `WireNetGraph` в `EplanCipMvp.Core/WireNaming/` индексирует устройства по клеммам один раз на весь `Preview`; новый токен шаблона `{Потенциал:источник}` и поле правила `SourceDevice` подключают это к существующему движку правил (`WireRuleTemplate`/`WireNamingEngine`) без изменения его общей структуры. GUI/Web получают одну новую колонку маски, как у остальных правил.

**Tech Stack:** C#, .NET Framework 4.8 (SDK-style csproj), `EplanCipMvp.Core` (без зависимости на EPLAN), `EplanCipMvp.Core.SelfTest` (консольные автотесты).

**Сборка и прогон тестов везде одинаковые — используются в каждом шаге:**

```bash
cd /workspace/eplan-cip-mvp
export PATH="/workspace/.dotnet:$PATH"
export DOTNET_ROOT=/workspace/.dotnet
dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj
mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe
```

Ожидаемый вывод самотеста при успехе заканчивается строкой
`Нумерация жил и проводов: все проверки пройдены.` (после блока «Самопроверка
нумерации жил и проводов»). Самотест бросает `InvalidOperationException` с
текстом конкретной непройденной проверки — это и есть сигнал "FAIL" на
шагах «запустить и убедиться, что падает».

---

### Task 1: Поле `SourceDevice` в `WireRule`

**Files:**
- Modify: `EplanCipMvp.Core/WireNaming/WireModels.cs`
- Modify: `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs` (расширить `TestRuleStore`)

- [ ] **Step 1: Написать падающий тест — новое поле переживает сохранение/загрузку JSON**

В `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs`, метод `TestRuleStore` (строки 477-497), заменить:

```csharp
                rules.Add(new WireRule { Name = "Моё", Device = "K*", CounterStart = 7, Template = "K{Счётчик}" });
                store.Save(rules);
                var loaded = store.Load();
                Check(loaded.Count == rules.Count && !loaded[0].Enabled && loaded.Last().CounterStart == 7 && loaded.Last().Template == "K{Счётчик}",
                      "сохранение/загрузка правил");
```

на:

```csharp
                rules.Add(new WireRule { Name = "Моё", Device = "K*", CounterStart = 7, Template = "K{Счётчик}", SourceDevice = "??QF*" });
                store.Save(rules);
                var loaded = store.Load();
                Check(loaded.Count == rules.Count && !loaded[0].Enabled && loaded.Last().CounterStart == 7 && loaded.Last().Template == "K{Счётчик}"
                      && loaded.Last().SourceDevice == "??QF*",
                      "сохранение/загрузка правил, включая SourceDevice");
```

- [ ] **Step 2: Собрать и убедиться, что падает (ошибка компиляции — поля ещё нет)**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj
```
Expected: FAIL — `CS0117: 'WireRule' does not contain a definition for 'SourceDevice'` (и для `.SourceDevice ==` тоже).

- [ ] **Step 3: Добавить поле**

В `EplanCipMvp.Core/WireNaming/WireModels.cs`, в классе `WireRule`, сразу после:
```csharp
        /// <summary>"" — любой; "да" — в узле есть жила кабеля; "нет" — нет.</summary>
        public string InCable { get; set; } = "";
```
добавить:
```csharp
        /// <summary>01.10.2026: маски устройств-"источников" (автомат/БП и т.п.), на которых обход вверх
        /// для {Потенциал:источник} останавливается. Пусто — ничего не подходит, токен не сработает.</summary>
        public string SourceDevice { get; set; } = "";
```

- [ ] **Step 4: Собрать и прогнать — убедиться, что проходит**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj && mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe
```
Expected: PASS — сборка без ошибок, в выводе `Нумерация жил и проводов: все проверки пройдены.`

- [ ] **Step 5: Commit**

```bash
cd /workspace
git add eplan-cip-mvp/EplanCipMvp.Core/WireNaming/WireModels.cs eplan-cip-mvp/EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs
git commit -m "feat(wire-naming): add SourceDevice mask field to WireRule"
```

---

### Task 2: `WireRuleTemplate.OwnNumber` — полный числовой хвост DT

**Files:**
- Modify: `EplanCipMvp.Core/WireNaming/WireRuleTemplate.cs`
- Modify: `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs` (метод `TestTemplate`)

- [ ] **Step 1: Написать падающий тест**

В `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs`, в конец метода `TestTemplate()` (перед его закрывающей `}`, после существующих проверок `{Группа}L{Номер:последняя}...`) добавить:

```csharp
            Check(WireRuleTemplate.OwnNumber("2QF11") == "11", "OwnNumber 2QF11 -> 11");
            Check(WireRuleTemplate.OwnNumber("2QF30") == "30", "OwnNumber 2QF30 -> 30");
            Check(WireRuleTemplate.OwnNumber("2G1") == "1", "OwnNumber 2G1 -> 1");
            Check(WireRuleTemplate.OwnNumber("BV21+") == "21", "OwnNumber BV21+ -> 21 (хвост '+' не входит)");
            Check(WireRuleTemplate.OwnNumber("X24") == "", "OwnNumber без цифр в хвосте -> пусто");
```

- [ ] **Step 2: Собрать и убедиться, что падает**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj
```
Expected: FAIL — `CS0117: 'WireRuleTemplate' does not contain a definition for 'OwnNumber'`.

- [ ] **Step 3: Добавить метод и переиспользовать его в `{Номер:последняя}`**

В `EplanCipMvp.Core/WireNaming/WireRuleTemplate.cs` добавить публичный метод сразу после `IsConstant`:

```csharp
        /// <summary>Полный числовой хвост DT устройства после буквенного кода, группа шкафа в него
        /// не входит (2QF11 -> "11", 2QF30 -> "30", 2G1 -> "1"). Пусто — хвоста нет.</summary>
        public static string OwnNumber(string device)
        {
            string trimmed = CableNaming.CableRuleTemplate.TrimTail(device ?? "");
            return new string(trimmed.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        }
```

Заменить существующий кейс `"Номер:последняя"` в `Resolve`:
```csharp
                case "Номер:последняя":
                    string tail = new string(device.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                    return tail.Length > 0 ? tail.Substring(tail.Length - 1) : "";
```
на:
```csharp
                case "Номер:последняя":
                    string tail = OwnNumber(a.Device);
                    return tail.Length > 0 ? tail.Substring(tail.Length - 1) : "";
```

- [ ] **Step 4: Собрать и прогнать — убедиться, что проходит**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj && mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /workspace
git add eplan-cip-mvp/EplanCipMvp.Core/WireNaming/WireRuleTemplate.cs eplan-cip-mvp/EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs
git commit -m "refactor(wire-naming): extract WireRuleTemplate.OwnNumber, reuse in {Номер:последняя}"
```

---

### Task 3: `WireNetGraph` — индекс устройств и обход на один шаг

**Files:**
- Create: `EplanCipMvp.Core/WireNaming/WireNetGraph.cs`
- Modify: `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs` (новый метод `TestNetGraph`, регистрация в `Run()`)

- [ ] **Step 1: Написать падающий тест**

В `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs` добавить новый приватный метод (после `TestNets()`, перед `TestCores()` — порядок в файле не важен для компиляции, но так рядом по смыслу):

```csharp
        private static void TestNetGraph()
        {
            const string ET101 = "+CP.ET101";
            const string ET102 = "+CP.ET102";

            // Случай 1: автомат прямо на сыром выходе БП (30L+) — входной клеммы устройства нет вообще.
            var raw = new List<WireInfo> { W("w1", 2, 100, 500, E(ET101, "2QF30", "2"), E(ET101, "2X24", "30"), potential: "L+") };
            var graphRaw = new WireNetGraph(WireNets.Build(raw));
            var netRaw = WireNets.Build(raw)[0];
            Check(graphRaw.ResolveSourceNumber(netRaw, "??QF*;?QF*;??U*;?U*;??G*;?G*", "L+") == "30L+", "сырой источник -> 30L+");

            // Случай 2: автомат запитан от уже именованного рельса (1L11).
            var chain = new List<WireInfo>
            {
                W("w1", 2, 100, 400, E(ET101, "2QF1", "2"), E(ET101, "2QF11", "1"), potential: "1L1"),
                W("w2", 2, 100, 300, E(ET101, "2QF11", "2"), E(ET101, "2G1", "L1"), potential: "L"),
            };
            var chainNets = WireNets.Build(chain);
            var graphChain = new WireNetGraph(chainNets);
            var netToName = chainNets.Single(n => n.Wires.Any(w => w.Id == "w2"));
            Check(graphChain.ResolveSourceNumber(netToName, "??QF*;?QF*;??U*;?U*;??G*;?G*", "L") == "1L11", "унаследованный номер -> 1L11");

            // Случай 3: среди концов узла нет устройства-источника — не срабатывает.
            var noSource = new List<WireInfo> { W("w1", 2, 100, 200, E(ET101, "2X24", "5"), E(ET101, "2A1.1", "3"), potential: "L") };
            var graphNoSource = new WireNetGraph(WireNets.Build(noSource));
            Check(graphNoSource.ResolveSourceNumber(WireNets.Build(noSource)[0], "??QF*;?QF*;??U*;?U*;??G*;?G*", "L") == null,
                  "нет устройства-источника -> null");

            // Случай 4: вход устройства-источника есть, но его потенциал тоже generic — не гадаем.
            var unresolved = new List<WireInfo>
            {
                W("w1", 2, 100, 400, E(ET101, "2QF1", "2"), E(ET101, "2QF11", "1"), potential: "L"),
                W("w2", 2, 100, 300, E(ET101, "2QF11", "2"), E(ET101, "2G1", "L1"), potential: "L"),
            };
            var unresolvedNets = WireNets.Build(unresolved);
            var graphUnresolved = new WireNetGraph(unresolvedNets);
            var netUnresolved = unresolvedNets.Single(n => n.Wires.Any(w => w.Id == "w2"));
            Check(graphUnresolved.ResolveSourceNumber(netUnresolved, "??QF*;?QF*;??U*;?U*;??G*;?G*", "L") == null,
                  "вход тоже generic -> null, не наследуем недосчитанное");

            // Случай 5: два шкафа с одинаковым DT устройства ("2QF30" в обоих) не путаются друг с другом.
            // Буква "N" — не "L+" (own-число после буквы с суффиксом +/- в PDF не встречалось и не
            // проходит под WireRuleTemplate.LooksLikePotential — не плодим неподтверждённую форму).
            var twoCabinets = new List<WireInfo>
            {
                W("w1", 2, 100, 400, E(ET101, "2QF1", "2"), E(ET101, "2QF30", "1"), potential: "1N1"),
                W("w2", 2, 100, 300, E(ET101, "2QF30", "2"), E(ET101, "2X24", "30"), potential: "N"),
                W("w3", 3, 100, 300, E(ET102, "2QF30", "2"), E(ET102, "3X24", "30"), potential: "N"),
            };
            var twoCabinetsNets = WireNets.Build(twoCabinets);
            var graphTwoCabinets = new WireNetGraph(twoCabinetsNets);
            var netEt101 = twoCabinetsNets.Single(n => n.Wires.Any(w => w.Id == "w2"));
            var netEt102 = twoCabinetsNets.Single(n => n.Wires.Any(w => w.Id == "w3"));
            Check(graphTwoCabinets.ResolveSourceNumber(netEt101, "??QF*;?QF*;??U*;?U*;??G*;?G*", "N") == "1N30",
                  "ET101: 2QF30 наследует вход своего шкафа -> 1N30");
            Check(graphTwoCabinets.ResolveSourceNumber(netEt102, "??QF*;?QF*;??U*;?U*;??G*;?G*", "N") == "30N",
                  "ET102: 2QF30 — сырой источник в своём шкафу, НЕ видит вход ET101 с тем же DT -> 30N");
        }
```

Добавить вызов `TestNetGraph();` в `Run()` сразу после `TestNets();`:
```csharp
            TestNets();
            TestNetGraph();
            TestCores();
```

- [ ] **Step 2: Собрать и убедиться, что падает**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj
```
Expected: FAIL — `CS0246: The type or namespace name 'WireNetGraph' could not be found`.

- [ ] **Step 3: Создать `WireNetGraph.cs`**

Создать файл `EplanCipMvp.Core/WireNaming/WireNetGraph.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using EplanCipMvp.Core.CableNaming;

namespace EplanCipMvp.Core.WireNaming
{
    /// <summary>
    /// Индекс «устройство (Location+Device) -> его клеммы -> узел, подключённый к клемме», построенный
    /// один раз на весь проект. Нужен, чтобы достроить номер потенциала, которому EPLAN дал только букву
    /// (L, N, M, L+) без числа: у опорного узла ищем конец на выходной клемме устройства-источника
    /// (автомат/БП, маска SourceDevice) и смотрим узел на парной входной клемме того же устройства.
    /// Один шаг, без рекурсии — см. docs/superpowers/specs/2026-10-01-potential-number-generation-design.md.
    /// </summary>
    public class WireNetGraph
    {
        /// <summary>Выходные клеммы источника тока (автомат/контактор/ПЧ) -> парная входная клемма —
        /// как в правиле «Силовая цепь группы» (2;4;6 — выход, 1;3;5 — вход).</summary>
        private static readonly Dictionary<string, string> PairedInput = new Dictionary<string, string>
        {
            ["2"] = "1", ["4"] = "3", ["6"] = "5",
        };

        private readonly Dictionary<(string Location, string Device), Dictionary<string, WireNet>> _byDevice =
            new Dictionary<(string, string), Dictionary<string, WireNet>>();

        public WireNetGraph(IList<WireNet> nets)
        {
            foreach (var net in nets ?? new List<WireNet>())
                foreach (var end in WireNets.Ends(net))
                {
                    string device = (end.Device ?? "").Trim();
                    string terminal = (end.Terminal ?? "").Trim();
                    if (device.Length == 0 || terminal.Length == 0) continue;
                    var key = ((end.Location ?? "").Trim(), device);
                    if (!_byDevice.TryGetValue(key, out var byTerminal))
                        _byDevice[key] = byTerminal = new Dictionary<string, WireNet>(System.StringComparer.OrdinalIgnoreCase);
                    byTerminal[terminal] = net;
                }
        }

        /// <summary>Готовый номер потенциала (буква уже известна и подставляется как есть) для узла net,
        /// или null — среди его концов нет устройства-источника на выходной клемме, или вход этого
        /// устройства ещё сам без специфичного имени (не гадаем).</summary>
        public string ResolveSourceNumber(WireNet net, string sourceDeviceMask, string letter)
        {
            foreach (var end in WireNets.Ends(net))
            {
                string outTerminal = (end.Terminal ?? "").Trim();
                if (!PairedInput.ContainsKey(outTerminal)) continue;
                if (!WildcardMask.MatchesAny(sourceDeviceMask, end.Device ?? "")) continue;

                string own = WireRuleTemplate.OwnNumber(end.Device);
                if (own.Length == 0) continue;

                var key = ((end.Location ?? "").Trim(), (end.Device ?? "").Trim());
                if (!_byDevice.TryGetValue(key, out var byTerminal)
                    || !byTerminal.TryGetValue(PairedInput[outTerminal], out var upstream))
                    return own + letter; // входной клеммы нет провода — верх цепи, сырой источник

                string specific = FirstSpecificPotential(upstream);
                if (specific.Length == 0) return null; // вход есть, но его потенциал ещё не посчитан — не гадаем
                return LeadingDigits(specific) + letter + own;
            }
            return null;
        }

        private static string FirstSpecificPotential(WireNet net) =>
            net.Wires.Select(w => (w.Potential ?? "").Trim())
               .FirstOrDefault(p => p.Length > 0 && !WireRuleTemplate.IsGenericPotential(p)) ?? "";

        private static string LeadingDigits(string potential) =>
            new string((potential ?? "").TakeWhile(char.IsDigit).ToArray());
    }
}
```

- [ ] **Step 4: Собрать и прогнать — убедиться, что проходит**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj && mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /workspace
git add eplan-cip-mvp/EplanCipMvp.Core/WireNaming/WireNetGraph.cs eplan-cip-mvp/EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs
git commit -m "feat(wire-naming): add WireNetGraph, one-step upstream device walk"
```

---

### Task 4: Токен `{Потенциал:источник}` в `WireRuleTemplate`

**Files:**
- Modify: `EplanCipMvp.Core/WireNaming/WireRuleTemplate.cs`
- Modify: `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs` (метод `TestTemplate`)

- [ ] **Step 1: Написать падающий тест**

В `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs`, в конец метода `TestTemplate()` добавить (после проверок `OwnNumber` из Task 2):

```csharp
            var graphForToken = new WireNetGraph(WireNets.Build(new List<WireInfo>
            {
                W("w1", 2, 100, 500, E(E01, "2QF30", "2"), E(E01, "2X24", "30"), potential: "L+"),
            }));
            var netForToken = WireNets.Build(new List<WireInfo>
            {
                W("w1", 2, 100, 500, E(E01, "2QF30", "2"), E(E01, "2X24", "30"), potential: "L+"),
            })[0];
            string sourceResult = WireRuleTemplate.Render("{Потенциал:источник}", new WireRuleContext
            {
                Anchor = E(E01, "2QF30", "2"), Potential = "L+",
                Graph = graphForToken, Net = netForToken, SourceDevice = "??QF*;?QF*;??U*;?U*;??G*;?G*",
            });
            Check(sourceResult == "30L+", "{Потенциал:источник} через контекст -> 30L+");
            Check(WireRuleTemplate.Render("{Потенциал:источник}", new WireRuleContext { Potential = "31L+" }) == null,
                  "{Потенциал:источник} при уже специфичном потенциале не срабатывает");
```

- [ ] **Step 2: Собрать и убедиться, что падает**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj
```
Expected: FAIL — `CS1061: 'WireRuleContext' does not contain a definition for 'Graph'` (и `'Net'`, `'SourceDevice'`).

- [ ] **Step 3: Расширить `WireRuleContext`, `Render` и `Resolve`**

В `EplanCipMvp.Core/WireNaming/WireRuleTemplate.cs` заменить класс `WireRuleContext`:
```csharp
    public class WireRuleContext
    {
        public WireEnd Anchor { get; set; }
        public string Potential { get; set; } = "";
        /// <summary>Жила кабеля на проводе опорного конца (текущая или назначаемая); пусто — не в кабеле.</summary>
        public string Core { get; set; } = "";
    }
```
на:
```csharp
    public class WireRuleContext
    {
        public WireEnd Anchor { get; set; }
        public string Potential { get; set; } = "";
        /// <summary>Жила кабеля на проводе опорного конца (текущая или назначаемая); пусто — не в кабеле.</summary>
        public string Core { get; set; } = "";
        /// <summary>01.10.2026: для {Потенциал:источник} — граф устройств/узлов (строится один раз на
        /// Preview в WireNamingEngine) и текущий узел, в котором рендерится токен.</summary>
        public WireNetGraph Graph { get; set; }
        public WireNet Net { get; set; }
        /// <summary>Маска устройств-источников (WireRule.SourceDevice) для {Потенциал:источник}.</summary>
        public string SourceDevice { get; set; } = "";
    }
```

Заменить `Render`:
```csharp
        public static string Render(string template, WireRuleContext ctx)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;
            var anchor = ctx?.Anchor ?? new WireEnd();
            string potential = ctx?.Potential ?? "";
            string core = ctx?.Core ?? "";
            bool failed = false;
            int counters = 0;
            string result = Placeholder.Replace(template.Trim(), m =>
            {
                string token = m.Groups[1].Value.Trim();
                if (token == "Счётчик") { counters++; return CounterToken; }
                string value = Resolve(token, anchor, potential, core);
                if (string.IsNullOrEmpty(value)) { failed = true; return ""; }
                return value;
            });
            return failed || counters > 1 ? null : result;
        }
```
на:
```csharp
        public static string Render(string template, WireRuleContext ctx)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;
            var anchor = ctx?.Anchor ?? new WireEnd();
            string potential = ctx?.Potential ?? "";
            string core = ctx?.Core ?? "";
            bool failed = false;
            int counters = 0;
            string result = Placeholder.Replace(template.Trim(), m =>
            {
                string token = m.Groups[1].Value.Trim();
                if (token == "Счётчик") { counters++; return CounterToken; }
                string value = Resolve(token, anchor, potential, core, ctx);
                if (string.IsNullOrEmpty(value)) { failed = true; return ""; }
                return value;
            });
            return failed || counters > 1 ? null : result;
        }
```

Заменить сигнатуру и тело `Resolve` (добавить параметр `ctx` и новый `case`):
```csharp
        private static string Resolve(string token, WireEnd a, string potential, string core)
        {
```
на:
```csharp
        private static string Resolve(string token, WireEnd a, string potential, string core, WireRuleContext ctx)
        {
```
и после кейса `case "Потенциал": return IsGenericPotential(potential) ? "" : potential.Trim();` добавить:
```csharp
                case "Потенциал:источник": return ResolveFromSource(potential, ctx);
```

Добавить приватный метод в конец класса (перед `FromFirstDigit`):
```csharp
        /// <summary>{Потенциал:источник}: достраивает номер, когда буква уже есть у EPLAN, а числа нет —
        /// см. WireNetGraph.ResolveSourceNumber. Специфичный или пустой потенциал — токен не трогает.</summary>
        private static string ResolveFromSource(string potential, WireRuleContext ctx)
        {
            string p = (potential ?? "").Trim();
            if (p.Length == 0 || !IsGenericPotential(p)) return "";
            return ctx?.Graph?.ResolveSourceNumber(ctx.Net, ctx.SourceDevice, p) ?? "";
        }
```

- [ ] **Step 4: Собрать и прогнать — убедиться, что проходит**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj && mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /workspace
git add eplan-cip-mvp/EplanCipMvp.Core/WireNaming/WireRuleTemplate.cs eplan-cip-mvp/EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs
git commit -m "feat(wire-naming): wire {Потенциал:источник} token to WireNetGraph via WireRuleContext"
```

---

### Task 5: Правило в пресете + подключение графа в `WireNamingEngine`

**Files:**
- Modify: `EplanCipMvp.Core/WireNaming/WireRulePresets.cs`
- Modify: `EplanCipMvp.Core/WireNaming/WireNamingEngine.cs`
- Modify: `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs` (новый метод `TestPotentialSourceRule`, регистрация в `Run()`)

- [ ] **Step 1: Написать падающий тест (сквозной, через `WireNamingEngine.Preview`)**

В `EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs` добавить метод (после `TestEngineStatuses()`):

```csharp
        private static void TestPotentialSourceRule()
        {
            const string ET101 = "+CP.ET101";
            var wires = new List<WireInfo>
            {
                W("w1", 2, 100, 400, E(ET101, "2QF1", "2"), E(ET101, "2QF11", "1"), potential: "1L1"),
                W("w2", 2, 100, 300, E(ET101, "2QF11", "2"), E(ET101, "2G1", "L1"), potential: "L"),
                W("w3", 2, 100, 200, E(ET101, "2QF30", "2"), E(ET101, "2X24", "30"), potential: "L+"),
            };
            var rows = WireNamingEngine.Preview(wires, WireRulePresets.Like1260(), new List<CableCores>());

            var row2 = rows.Single(r => r.WireIds.Contains("w2"));
            Check(row2.RuleName == "Потенциал (из источника)", "w2: сработало правило генерации потенциала");
            Check(row2.ProposedName == "1L11", "w2: предложен номер 1L11");
            Check(!row2.Apply, "w2: провод потенциала — галочку сами не ставим");

            var row3 = rows.Single(r => r.WireIds.Contains("w3"));
            Check(row3.ProposedName == "30L+", "w3: предложен номер 30L+ (сырой источник)");
        }
```

Добавить вызов `TestPotentialSourceRule();` в `Run()` сразу после `TestEngineStatuses();`:
```csharp
            TestEngineStatuses();
            TestPotentialSourceRule();
            Test1260Page190();
```

- [ ] **Step 2: Собрать и убедиться, что падает**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj && mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe
```
Expected: FAIL — сборка пройдёт, но самотест бросит `InvalidOperationException` с текстом `w2: сработало правило генерации потенциала` (правила пока нет в пресете, `row2.RuleName` будет пустым).

- [ ] **Step 3: Добавить правило в пресет**

В `EplanCipMvp.Core/WireNaming/WireRulePresets.cs`, в `Like1260()`, между правилом «Канал ПЛК» и правилом «Силовая цепь группы» вставить:
```csharp
            // 01.10.2026: достраивает номер потенциала, когда EPLAN дал только букву (M, L+, L...) без
            // числа — обход вверх до ближайшего автомата/БП (docs/superpowers/specs/2026-10-01-potential-number-generation-design.md).
            new WireRule { Name = "Потенциал (из источника)", Potential = "да",
                           SourceDevice = "??QF*;?QF*;??U*;?U*;??G*;?G*", Template = "{Потенциал:источник}" },
```
(сразу перед строкой `new WireRule { Name = "Силовая цепь группы", ... }`).

- [ ] **Step 4: Построить граф один раз на `Preview` и передать его в рендер**

В `EplanCipMvp.Core/WireNaming/WireNamingEngine.cs`:

Заменить начало `Preview`:
```csharp
            var coreByWire = (wires ?? new List<WireInfo>()).Where(w => w != null && (w.CurrentCore ?? "").Trim().Length > 0)
                .GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First().CurrentCore.Trim());
            foreach (var c in coreChanges) coreByWire[c.WireId] = c.Core;

            foreach (var net in WireNets.Build(wires))
```
на:
```csharp
            var coreByWire = (wires ?? new List<WireInfo>()).Where(w => w != null && (w.CurrentCore ?? "").Trim().Length > 0)
                .GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First().CurrentCore.Trim());
            foreach (var c in coreChanges) coreByWire[c.WireId] = c.Core;

            var nets = WireNets.Build(wires);
            var graph = new WireNetGraph(nets);
            foreach (var net in nets)
```

Заменить вызов `TryRule` внутри цикла:
```csharp
                    var (name, anchor) = TryRule(rule, net, ends, onPotential, potential, coreByWire);
```
на:
```csharp
                    var (name, anchor) = TryRule(rule, net, ends, onPotential, potential, coreByWire, graph);
```

Заменить сигнатуру `TryRule`:
```csharp
        private static (string Name, WireEnd Anchor) TryRule(WireRule rule, WireNet net, List<WireEnd> ends, bool onPotential, string potential,
                                                             Dictionary<string, string> coreByWire)
```
на:
```csharp
        private static (string Name, WireEnd Anchor) TryRule(WireRule rule, WireNet net, List<WireEnd> ends, bool onPotential, string potential,
                                                             Dictionary<string, string> coreByWire, WireNetGraph graph)
```

Заменить конструирование контекста внутри `TryRule`:
```csharp
                string name = WireRuleTemplate.Render(rule.Template, new WireRuleContext { Anchor = anchor, Potential = potential, Core = core });
```
на:
```csharp
                string name = WireRuleTemplate.Render(rule.Template, new WireRuleContext
                {
                    Anchor = anchor, Potential = potential, Core = core,
                    Graph = graph, Net = net, SourceDevice = rule.SourceDevice ?? "",
                });
```

- [ ] **Step 5: Собрать и прогнать — убедиться, что проходит**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj && mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe
```
Expected: PASS — полный вывод самотеста без исключений.

- [ ] **Step 6: Прогнать ПОЛНЫЙ набор автотестов проекта (не только wire-naming) — нет регрессии в кабелях/прочем**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe 2>&1 | grep -c "все проверки пройдены"
```
Expected: `3` (нумерация кабелей, нумерация жил и проводов, PumpParameterReader — три секции самотеста, как было до изменений).

- [ ] **Step 7: Commit**

```bash
cd /workspace
git add eplan-cip-mvp/EplanCipMvp.Core/WireNaming/WireRulePresets.cs eplan-cip-mvp/EplanCipMvp.Core/WireNaming/WireNamingEngine.cs eplan-cip-mvp/EplanCipMvp.Core.SelfTest/WireNamingSelfTest.cs
git commit -m "feat(wire-naming): add preset rule + wire WireNetGraph into WireNamingEngine.Preview"
```

---

### Task 6: GUI — колонка `SourceDevice` в `WireNumberingPanel.cs`

**Files:**
- Modify: `EplanCipMvp.Gui/WireNumberingPanel.cs`

Эта задача не покрывается автотестами (WinForms, живой EPLAN DLL — не собираются и не запускаются в этом окружении). Проверка — компиляция `EplanCipMvp.Core`/остального неизменна (GUI-проект не входит в `EplanCipMvp.Core.SelfTest.csproj`, его отдельно не собрать здесь); правильность — по аналогии 1:1 с уже существующими колонками-масками в этом же файле.

- [ ] **Step 1: Добавить колонку в грид правил**

В `EplanCipMvp.Gui/WireNumberingPanel.cs`, после строки:
```csharp
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "InCable", "Жила (да/нет)", 6, false);
```
добавить:
```csharp
            AddColumn(_gridRules, new DataGridViewTextBoxColumn(), "SourceDevice", "Источник (вход)", 10, false);
```

- [ ] **Step 2: Обновить кнопку «Добавить правило» (на одно значение-плейсхолдер больше)**

Заменить:
```csharp
            rulesButtons.Controls.Add(NewButton("Добавить правило", (s, e) => _gridRules.Rows.Add(true, "Новое правило", "", "", "", "", "", "", "", "X", "", "нет", "")));
```
на:
```csharp
            rulesButtons.Controls.Add(NewButton("Добавить правило", (s, e) => _gridRules.Rows.Add(true, "Новое правило", "", "", "", "", "", "", "", "", "X", "", "нет", "")));
```
(добавлена ровно одна пустая `""` — для новой колонки `SourceDevice`, между `InCable` и `Order`).

- [ ] **Step 3: Обновить `FillRulesGrid` (заполнение грида из модели)**

Заменить:
```csharp
                _gridRules.Rows.Add(r.Enabled, r.Name, r.Location, r.Device, r.Terminal, r.OtherLocation, r.ExcludeDevice, r.Potential, r.InCable,
                                    r.Order, r.CounterStart?.ToString() ?? "", r.AutoCheck, r.Template);
```
на:
```csharp
                _gridRules.Rows.Add(r.Enabled, r.Name, r.Location, r.Device, r.Terminal, r.OtherLocation, r.ExcludeDevice, r.Potential, r.InCable,
                                    r.SourceDevice, r.Order, r.CounterStart?.ToString() ?? "", r.AutoCheck, r.Template);
```

- [ ] **Step 4: Обновить `TryReadRulesFromGrid` (чтение грида в модель)**

Заменить:
```csharp
                    InCable = CellText(row, "InCable"),
                    Order = CellText(row, "Order").Length == 0 ? "X" : CellText(row, "Order"),
```
на:
```csharp
                    InCable = CellText(row, "InCable"),
                    SourceDevice = CellText(row, "SourceDevice"),
                    Order = CellText(row, "Order").Length == 0 ? "X" : CellText(row, "Order"),
```

- [ ] **Step 5: Добавить новый токен в текст подсказки**

Заменить:
```csharp
                       "{Устройство:от_цифры}, {Клемма}, {Клемма:2}, {Клемма:буквы}, {МодульПЛК:цифры}, {Группа}, {Номер:последняя}, " +
                       "{Потенциал}, {Счётчик}. Жилы кабелей назначаются из артикула кабеля. Первый прогон — на копии проекта.",
```
на:
```csharp
                       "{Устройство:от_цифры}, {Клемма}, {Клемма:2}, {Клемма:буквы}, {МодульПЛК:цифры}, {Группа}, {Номер:последняя}, " +
                       "{Потенциал}, {Потенциал:источник} (достраивает номер по маске «Источник», если EPLAN дал только букву), " +
                       "{Счётчик}. Жилы кабелей назначаются из артикула кабеля. Первый прогон — на копии проекта.",
```

- [ ] **Step 6: Проверить компиляцию всего, что собирается в этом окружении, не затронута**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet && dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj && mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe
```
Expected: PASS (этот шаг не трогает `EplanCipMvp.Core`/`SelfTest`, только фиксирует, что ничего не сломалось попутно; сама GUI-сборка требует EPLAN DLL и Windows — проверяется в живом прогоне по чеклисту ниже).

- [ ] **Step 7: Commit**

```bash
cd /workspace
git add eplan-cip-mvp/EplanCipMvp.Gui/WireNumberingPanel.cs
git commit -m "feat(wire-naming-gui): add SourceDevice column to rules grid"
```

---

### Task 7: Web — колонка `SourceDevice` в `index.html`/`app.js`

**Files:**
- Modify: `EplanCipMvp.Web/wwwroot/index.html`
- Modify: `EplanCipMvp.Web/wwwroot/app.js`

`ApiServer.cs` менять не нужно: `/api/wires/rules` сериализует/десериализует `List<WireRule>` целиком через `JavaScriptSerializer` без поимённой карты полей — новое поле пройдёт насквозь автоматически.

- [ ] **Step 1: Добавить поле в список полей таблицы**

В `EplanCipMvp.Web/wwwroot/app.js` заменить:
```javascript
const WIRE_RULE_FIELDS = ["Name", "Location", "Device", "Terminal", "OtherLocation", "ExcludeDevice", "Potential", "InCable", "Order", "CounterStart", "AutoCheck", "Template"];
```
на:
```javascript
const WIRE_RULE_FIELDS = ["Name", "Location", "Device", "Terminal", "OtherLocation", "ExcludeDevice", "Potential", "InCable", "SourceDevice", "Order", "CounterStart", "AutoCheck", "Template"];
```

- [ ] **Step 2: Добавить поле в объект новой строки по умолчанию**

Заменить:
```javascript
  wireRules.push({ Name: "Новое правило", Enabled: true, Location: "", Device: "", Terminal: "", OtherLocation: "", ExcludeDevice: "",
                   Potential: "", InCable: "", Order: "X", CounterStart: null, AutoCheck: "нет", Template: "" });
```
на:
```javascript
  wireRules.push({ Name: "Новое правило", Enabled: true, Location: "", Device: "", Terminal: "", OtherLocation: "", ExcludeDevice: "",
                   Potential: "", InCable: "", SourceDevice: "", Order: "X", CounterStart: null, AutoCheck: "нет", Template: "" });
```

- [ ] **Step 3: Добавить колонку в заголовок таблицы и текст подсказки**

В `EplanCipMvp.Web/wwwroot/index.html` заменить:
```html
        <thead><tr><th>Вкл</th><th>Название</th><th>Место</th><th>Устройство</th><th>Клемма</th><th>Место др. конца</th><th>Кроме устройств</th><th>Потенциал (да/нет)</th><th>Жила (да/нет)</th><th>Порядок X/Y</th><th>Счётчик с</th><th>Отмечать сами (да/нет)</th><th>Шаблон</th><th></th></tr></thead>
```
на:
```html
        <thead><tr><th>Вкл</th><th>Название</th><th>Место</th><th>Устройство</th><th>Клемма</th><th>Место др. конца</th><th>Кроме устройств</th><th>Потенциал (да/нет)</th><th>Жила (да/нет)</th><th>Источник (вход)</th><th>Порядок X/Y</th><th>Счётчик с</th><th>Отмечать сами (да/нет)</th><th>Шаблон</th><th></th></tr></thead>
```

Заменить:
```html
      Подстановки: {Устройство}, {Устройство:от_цифры}, {Клемма}, {Клемма:2}, {Клемма:буквы}, {МодульПЛК:цифры}, {Группа}, {Номер:последняя}, {Потенциал}, {Счётчик}.
```
на:
```html
      Подстановки: {Устройство}, {Устройство:от_цифры}, {Клемма}, {Клемма:2}, {Клемма:буквы}, {МодульПЛК:цифры}, {Группа}, {Номер:последняя}, {Потенциал}, {Потенциал:источник} (достраивает номер по маске «Источник», если EPLAN дал только букву), {Счётчик}.
```

- [ ] **Step 4: Проверить HTML/JS на синтаксические ошибки**

Run:
```bash
cd /workspace/eplan-cip-mvp/EplanCipMvp.Web/wwwroot && node --check app.js
```
Expected: без вывода (exit code 0) — скрипт синтаксически корректен. (Полный визуальный прогон Web UI требует живого `EplanCipMvp.Web` сервера с EPLAN-данными — вне этого окружения, проверяется в живом прогоне по чеклисту ниже.)

- [ ] **Step 5: Commit**

```bash
cd /workspace
git add eplan-cip-mvp/EplanCipMvp.Web/wwwroot/index.html eplan-cip-mvp/EplanCipMvp.Web/wwwroot/app.js
git commit -m "feat(wire-naming-web): add SourceDevice column to rules table"
```

---

### Task 8: Финальная проверка и чеклист живого прогона

**Files:** нет изменений — только верификация.

- [ ] **Step 1: Полный прогон автотестов с нуля (чистая пересборка)**

Run:
```bash
cd /workspace/eplan-cip-mvp && export PATH="/workspace/.dotnet:$PATH" && export DOTNET_ROOT=/workspace/.dotnet
rm -rf EplanCipMvp.Core/bin EplanCipMvp.Core/obj EplanCipMvp.Core.SelfTest/bin EplanCipMvp.Core.SelfTest/obj
dotnet build EplanCipMvp.Core.SelfTest/EplanCipMvp.Core.SelfTest.csproj
mono EplanCipMvp.Core.SelfTest/bin/Debug/net48/EplanCipMvp.Core.SelfTest.exe
```
Expected: сборка без ошибок/предупреждений, в выводе три раза `все проверки пройдены` (кабели, жилы/провода, PumpParameterReader), без исключений.

- [ ] **Step 2: Чеклист живого прогона (выполняет пользователь на копии проекта 1260/1364 в EPLAN — не часть автотестов этой сессии)**

- [ ] В GUI/Web в таблице правил появилась и редактируется колонка «Источник (вход)» у правила «Потенциал (из источника)».
- [ ] «Считать» → «Пересчитать по правилам» на копии проекта с известными generic-потенциалами (как в `Test1364PotentialsUntouched`) — в колонке «Правило» у таких строк теперь «Потенциал (из источника)», а не пусто.
- [ ] Предложенные номера сверяются глазами с PDF/факстическим состоянием шкафа; галочка на строках потенциалов не стоит сама (как и раньше) — пользователь подтверждает вручную.
- [ ] Строки, где устройство-источник не опознано (ещё не в масках `SourceDevice`) — остаются «нет правила» как раньше, ничего не ломается; маску при необходимости правят в GUI/Web без пересборки программы.

- [ ] **Step 3: Финальный commit (если чеклист живого прогона потребовал правок масок/пресета)**

Если по итогам живого прогона потребовалась правка пресета/маски — отдельный коммит по той же схеме, что и остальные задачи этого плана (frequent commits), с кратким описанием, что именно скорректировано и на каком реальном случае.
