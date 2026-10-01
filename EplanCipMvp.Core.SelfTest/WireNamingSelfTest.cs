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
            TestNets();
            TestNetGraph();
            TestCores();
            TestApplyPlanner();
            TestEngineStatuses();
            TestPotentialSourceRule();
            TestPotentialSourceRuleSharedName();
            Test1260Page190();
            Test1364MainCabinetCores();
            Test1364ExchangeSheet();
            Test1364PotentialsUntouched();
            Test1364ControlGuards();
            TestRestoreFromBackup();
            Test1364AfterRestore();
            TestWireExport();
            TestRuleStore();
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

            Check(WireRuleTemplate.OwnNumber("2QF11") == "11", "OwnNumber 2QF11 -> 11");
            Check(WireRuleTemplate.OwnNumber("2QF30") == "30", "OwnNumber 2QF30 -> 30");
            Check(WireRuleTemplate.OwnNumber("2G1") == "1", "OwnNumber 2G1 -> 1");
            Check(WireRuleTemplate.OwnNumber("BV21+") == "21", "OwnNumber BV21+ -> 21 (хвост '+' не входит)");
            Check(WireRuleTemplate.OwnNumber("XPE") == "", "OwnNumber без цифр в хвосте -> пусто");

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
        }

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

            // Случай 6 (код-ревью): регистр Location/Device не должен ломать поиск по ключу.
            // Критично — разный регистр именно на ДВУХ концах ОДНОГО устройства (2QF11: вход у w1,
            // выход у w2), потому что ключ _byDevice строится по входному концу, а ищется по выходному.
            var caseMix = new List<WireInfo>
            {
                W("w1", 2, 100, 400, E(ET101, "2QF1", "2"), E("+cp.et101", "2qf11", "1"), potential: "1L1"),
                W("w2", 2, 100, 300, E("+CP.ET101", "2QF11", "2"), E(ET101, "2G1", "L1"), potential: "L"),
            };
            var caseMixNets = WireNets.Build(caseMix);
            var graphCaseMix = new WireNetGraph(caseMixNets);
            var netCaseMix = caseMixNets.Single(n => n.Wires.Any(w => w.Id == "w2"));
            Check(graphCaseMix.ResolveSourceNumber(netCaseMix, "??QF*;?QF*;??U*;?U*;??G*;?G*", "L") == "1L11",
                  "регистр Location/Device не ломает наследование -> 1L11");
        }

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
            Check(R("m1").ProposedName == "1M" && R("m1").Status == WireNamingStatus.New && !R("m1").Apply && R("m1").SharedName,
                  "1M — предложен, но провод потенциала сами не отмечаем");
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

        /// <summary>01.10.2026, баг-фикс: два разных узла (две ветки одного автомата 1QF1, общий
        /// минус) получают ОДИН И ТОТ ЖЕ номер "1M" через правило «Потенциал (из источника)» — это
        /// законный повтор (design doc: «общий минус на несколько веток — один и тот же номер, не
        /// дубль»), не дубль. row.SharedName должен быть true у обоих, иначе GUI/Web (у которых нет
        /// фолбэка на LooksLikePotential, в отличие от MarkDuplicates/WireApplyPlanner.Plan) ложно
        /// подсвечивают/блокируют их как дубль.</summary>
        private static void TestPotentialSourceRuleSharedName()
        {
            const string ET101 = "+CP.ET101";
            var wires = new List<WireInfo>
            {
                W("s1", 3, 100, 400, E(ET101, "1QF1", "2"), E(ET101, "DeviceA", "1"), potential: "M"),
                W("s2", 3, 100, 300, E(ET101, "1QF1", "4"), E(ET101, "DeviceB", "1"), potential: "M"),
            };
            var rows = WireNamingEngine.Preview(wires, WireRulePresets.Like1260(), new List<CableCores>());
            var row1 = rows.Single(r => r.WireIds.Contains("s1"));
            var row2 = rows.Single(r => r.WireIds.Contains("s2"));

            Check(row1.RuleName == "Потенциал (из источника)" && row2.RuleName == "Потенциал (из источника)",
                  "обе ветки — через правило генерации потенциала из источника");
            Check(row1.ProposedName == "1M" && row2.ProposedName == "1M",
                  $"обе ветки получают один и тот же номер 1M (получено {row1.ProposedName}/{row2.ProposedName})");
            Check(row1.SharedName && row2.SharedName,
                  "общий минус 1M на двух ветках — SharedName должен быть true (иначе GUI/Web ложно подсвечивают как дубль)");

            var plan = WireApplyPlanner.Plan(rows, new Dictionary<string, string> { [row1.NetId] = "1M", [row2.NetId] = "1M" });
            Check(!plan.Rejected.ContainsKey(row1.NetId) && !plan.Rejected.ContainsKey(row2.NetId),
                  "WireApplyPlanner уже и так не блокирует запись общего 1M (фолбэк на LooksLikePotential)");
        }

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
                ["qf1"] = "32L41", ["qf2"] = "32L42", ["qf3"] = "32L43",
                ["km1"] = "32L44", ["km2"] = "32L45", ["km3"] = "32L46", ["u1"] = "32L47", ["u2"] = "32L48", ["u3"] = "32L49",
                ["core1"] = "32M04-U", ["core2"] = "32M04-V", ["core3"] = "32M04-W", ["corePE"] = "PE",
                ["do"] = "12101", ["di"] = "11101",
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
            foreach (var id in new[] { "pot1L1", "pot31" })
            {
                var row = rows.Single(r => r.WireIds.Contains(id));
                if (row.Status != WireNamingStatus.NoRule) problems.Add($"{id}: провод потенциала не трогаем, получено '{row.ProposedName}' ({row.RuleName})");
            }
            Console.WriteLine($"Лист 190 (1260): проводов {expected.Count}, расхождений {problems.Count}.");
            Check(problems.Count == 0, "расхождения с 1260:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>1364 (PDF 24.09): жилы к датчикам E01 — устройство-клемма, как LS4-1/LS4-3/LS4-2 в самом 1364;
        /// «+» в конце DT не входит (BV21+ -> BV21-1); периферия — как в 1260 (GS101-3).</summary>
        private static void Test1364MainCabinetCores()
        {
            var rows = WireNamingEngine.Preview(new List<WireInfo>
            {
                W("bv", 1, 100, 100, E(E01, "1X24", "31L+", "2"), E(Field, "BV21+", "1"), potential: "31L+", cable: "WV21", name: "B21-1"),
                W("ls", 1, 200, 100, E(E01, "1A1.3", "1"), E(Field, "LS4", "2"), cable: "WLS4", name: "LS4-2"),
                W("gs", 2, 100, 100, E("+CP.ET102", "2X31", "5", "2"), E(Field, "GS101", "3"), cable: "WGS101"),
            }, WireRulePresets.Like1260(), new List<CableCores>());
            string N(string id) => rows.Single(r => r.WireIds.Contains(id)).ProposedName;
            Check(N("bv") == "BV21-1", $"BV21+:1 -> BV21-1 (получено '{N("bv")}')");
            Check(N("ls") == "LS4-2", $"LS4:2 (к модулю ПЛК) -> LS4-2, а не номер ПЛК (получено '{N("ls")}')");
            Check(N("gs") == "GS101-3", "периферия: GS101-3");

            // 1364, лист SN (LT/PT): клеммы датчика «+»/«-» — в номере провода жила кабеля (T6LT1-1, T6LT1-2), как на схеме.
            var lt = WireNamingEngine.Preview(new List<WireInfo>
            {
                W("p", 7, 213, 300, E(E01, "1X24", "33L+", "2"), E(Field, "T6LT1", "+"), potential: "33L+", cable: "WT6LT1"),
                W("m", 7, 264, 300, E(E01, "1A3.1", "3"), E(Field, "T6LT1", "-"), cable: "WT6LT1"),
                W("q", 8, 213, 300, E(E01, "1X24", "33L+", "3"), E(Field, "T7LT2", "+"), cable: "WT7LT2", core: "1"),
            }, WireRulePresets.Like1260(), new List<CableCores> { new CableCores { Cable = "WT6LT1", FreeCores = { "1", "2", "SH" } } });
            string L(string id) => lt.Single(r => r.WireIds.Contains(id)).ProposedName;
            Check(L("p") == "T6LT1-1" && L("m") == "T6LT1-2" && L("q") == "T7LT2-1", $"датчик +/-: {L("p")}, {L("m")}, {L("q")}");

            // Жилы кабеля пневмоострова: в 1260 3VP1-1, в 1364 UP1-1 / UP1-14 (а не номер канала ПЛК).
            var island = WireNamingEngine.Preview(new List<WireInfo>
            {
                W("up1", 3, 100, 100, E(E01, "UP1", "1"), E(E01, "1A2.1", "6"), cable: "WUP1", name: "UP1-1"),
                W("up14", 3, 200, 100, E(E01, "UP1", "14"), E(E01, "1A2.1", "7"), cable: "WUP1", name: "UP1-14"),
                W("vp", 4, 100, 100, E("+CP.ET102", "3VP1", "1"), E("+CP.ET102", "2A2.1", "1"), cable: "W3VP1"),
            }, WireRulePresets.Like1260(), new List<CableCores>());
            string I(string id) => island.Single(r => r.WireIds.Contains(id)).ProposedName;
            Check(I("up1") == "UP1-1" && I("up14") == "UP1-14" && I("vp") == "3VP1-1", $"пневмоостров: {I("up1")}, {I("up14")}, {I("vp")}");
        }

        /// <summary>Выгрузка «Считать провода»: одна строка на провод + строки CORES со свободными жилами.</summary>
        private static void TestWireExport()
        {
            var wire = W("w1", 3, 10.5, 20, E(E01, "1X31", "1", "2"), E(Field, "T32M04", "U1"), potential: "", cable: "W32M04", name: "32M04-U", core: "1");
            string tsv = WireExport.ToTsv(new List<WireInfo> { wire },
                                          new List<CableCores> { new CableCores { Cable = "W32M04", FreeCores = { "2", "GNYE" } } });
            var lines = tsv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Check(lines[0].StartsWith("Id\tPage\tX\tY\tName\tCore\tPotential\tCable\tStart\tEnd\tPoint"), "заголовок выгрузки проводов: " + lines[0]);
            Check(lines[1] == "w1\t4\t10.5\t20\t32M04-U\t1\t\tW32M04\t+CP.E01|1X31|1|+CP.E01-1X31:1#2\t+FIELD|T32M04|U1|+FIELD-T32M04:U1#\t1",
                  "строка провода: " + lines[1]);
            Check(lines.Any(l => l == "CORES\tW32M04\t2;GNYE"), "строка свободных жил");
        }

        /// <summary>25.09.2026, живой прогон 1364, лист 269 «Обмен сигналами»: 156 конфликтов и 220 «дублей».</summary>
        private static void Test1364ExchangeSheet()
        {
            var rows = WireNamingEngine.Preview(new List<WireInfo>
            {
                // Провода внутри поля (между двумя устройствами в +FIELD) — не наши, номер не нужен (было K101-2 / WO16-2 + конфликт).
                W("in1", 269, 100, 100, E(Field, "WO16", "2"), E(Field, "K101", "2")),
                W("in2", 269, 200, 100, E(Field, "WO15", "1"), E(Field, "K101", "2", "b")),
                // Общий потенциал в 30 узлах — это не дубль: не отмечать (было «Дубль», галочка, 41L+ -> L+).
                W("p1", 269, 300, 100, E(E01, "1X24", "31L+", "1"), E("", "", "11"), potential: "L+", name: "41L+"),
                W("p2", 270, 300, 100, E(E01, "1X24", "31L+", "2"), E("", "", "11", "b"), potential: "L+", name: "41L+"),
                // Скопированный номер канала ПЛК в 2 узлах — настоящий дубль: отмечается.
                W("d1", 269, 400, 100, E(E01, "K2415", "A1"), E(E01, "1A2.4", "16"), name: "12304"),
                W("d2", 269, 500, 100, E(E01, "K230", "A1"), E(E01, "1A2.3", "1"), name: "12304"),
                // Конец без DT (контакт в поле не привязан к устройству) — пояснение в строке.
                W("nodt", 269, 600, 100, E("", "", "22"), E(E01, "1A1.5", "16"), potential: "L+"),
            }, WireRulePresets.Like1260(), new List<CableCores>());
            WireNamingRow R(string id) => rows.Single(r => r.WireIds.Contains(id));

            Check(R("in1").Status == WireNamingStatus.NoRule && R("in2").Status == WireNamingStatus.NoRule
                  && rows.All(r => r.Status != WireNamingStatus.Conflict),
                  $"провода внутри поля — без правила и без конфликтов ({R("in1").Status}/{R("in1").ProposedName}, {R("in2").Status})");
            Check(!R("p1").Apply && !R("p2").Apply && R("p1").Status != WireNamingStatus.Duplicate,
                  $"общий потенциал — не дубль, без галочки ({R("p1").Status}, {R("p1").Apply})");
            Check(R("d2").Status == WireNamingStatus.Duplicate && R("d2").Apply && R("d2").ProposedName == "12301", $"скопированный 12304 — дубль ({R("d2").Status}, {R("d2").ProposedName})");
            Check(R("nodt").Note.Contains("DT"), $"конец без DT — пояснение ('{R("nodt").Note}')");
        }

        /// <summary>25.09.2026, запись на 1364: провода потенциалов получили «M», «L+», «L» и счётчики вместо 1M/30L+/1L1.
        /// EPLAN отдаёт у них общее имя потенциала (M, L+) и пустой номер, хотя на схеме виден 1M.</summary>
        private static void Test1364PotentialsUntouched()
        {
            var rows = WireNamingEngine.Preview(new List<WireInfo>
            {
                W("m", 260, 100, 100, E(E01, "1X24", "1M", "2"), E(E01, "1A3.1", "12"), potential: "M"),
                W("lp", 260, 200, 100, E(E01, "1X24", "30L+", "2"), E(E01, "1Q1", "13"), potential: "L+"),
                W("l", 17, 300, 100, E(E01, "1UB1", "1L1"), E(E01, "1QF21", "1"), potential: "L"),
                W("named", 260, 400, 100, E(E01, "1X24", "1M", "3"), E(E01, "1A3.1", "15"), name: "1M"),
                W("named2", 261, 400, 100, E(E01, "1X24", "1M", "4"), E(E01, "1A3.1", "16"), name: "1M"),
                W("sig", 260, 500, 100, E(E01, "K1", "13"), E(E01, "1A3.1", "3")),
            }, WireRulePresets.Like1260(), new List<CableCores>());
            foreach (var id in new[] { "m", "lp", "l", "named", "named2" })
            {
                var r = rows.Single(x => x.WireIds.Contains(id));
                Check(!r.Apply && r.Status != WireNamingStatus.Duplicate && r.Status != WireNamingStatus.New,
                      $"провод потенциала {id} не трогаем (получено '{r.ProposedName}', {r.Status}, {r.RuleName})");
            }
            Check(rows.Single(x => x.WireIds.Contains("sig")).ProposedName == "13103", "обычный сигнал ПЛК нумеруется: 13103");
            Check(WireRuleTemplate.Render("{Потенциал}", new WireRuleContext { Anchor = E(E01, "1X24", "1"), Potential = "M" }) == null,
                  "общее имя потенциала (M) — не номер провода");
        }

        /// <summary>25.09.2026: «Цепь управления» отмечала сама 319 проводов на 1364, в т.ч. перемычки 1X24:3—1X24:1M,
        /// разводку внутри пневмоострова (UP1:15 — UP1-15:+) и провода к контактам без DT.</summary>
        private static void Test1364ControlGuards()
        {
            var rows = WireNamingEngine.Preview(new List<WireInfo>
            {
                W("jump", 1, 10, 10, E(E01, "1X24", "3", "a"), E(E01, "1X24", "1M", "b")),
                W("island", 250, 100, 100, E(E01, "UP1", "15"), E(E01, "UP1-15", "+")),
                W("nodt", 90, 100, 100, E("+", "", "1"), E("+CP.ET101", "2K201", "14")),
                W("relay", 262, 100, 100, E(E01, "K230", "14"), E(E01, "K201", "2")),
            }, WireRulePresets.Like1260(), new List<CableCores>());
            WireNamingRow R(string id) => rows.Single(r => r.WireIds.Contains(id));
            Check(R("jump").Status == WireNamingStatus.NoRule && R("island").Status == WireNamingStatus.NoRule,
                  $"перемычка/внутренняя разводка — без правила ({R("jump").Status} {R("jump").ProposedName}, {R("island").Status} {R("island").ProposedName})");
            Check(!R("nodt").Apply, "конец без DT — сами не отмечаем");
            Check(R("relay").ProposedName.Length > 0 && R("relay").Apply, $"цепь управления реле-реле — отмечена ({R("relay").ProposedName}, {R("relay").Apply})");
        }

        /// <summary>Откат: номера из выгрузки (резервной копии) сопоставляются с текущими проводами по клеммам на концах.</summary>
        private static void TestRestoreFromBackup()
        {
            var before = new List<WireInfo>
            {
                W("w0", 1, 1, 1, E(E01, "1X24", "1M", "2"), E(E01, "1A3.1", "12")),           // было пусто (EPLAN показывал 1M)
                W("w1", 1, 2, 1, E(E01, "K1", "13"), E(E01, "1A1.1", "2"), name: "11101"),
                W("w2", 1, 3, 1, E(E01, "K2", "13"), E(E01, "K3", "A1"), name: "30L+"),
            };
            string tsv = WireExport.ToTsv(before, new List<CableCores>());
            var backup = WireExport.ParseTsv(tsv);
            Check(backup.Count == 3 && backup[1].CurrentName == "11101" && backup[1].End.PinKey == before[1].End.PinKey, "выгрузка читается обратно");

            var now = new List<WireInfo>
            {   // другие id и обратная ориентация провода — как после записи в EPLAN
                W("x9", 1, 2, 1, E(E01, "1A1.1", "2"), E(E01, "K1", "13"), name: "11102"),
                W("x8", 1, 1, 1, E(E01, "1X24", "1M", "2"), E(E01, "1A3.1", "12"), name: "M"),
                W("x7", 1, 3, 1, E(E01, "K2", "13"), E(E01, "K3", "A1"), name: "30L+"),
                W("x6", 1, 4, 1, E(E01, "K4", "1"), E(E01, "K5", "2"), name: "999"),        // нет в копии — не трогаем
            };
            var plan = WireExport.RestorePlan(now, backup);
            Check(plan.Count == 2 && plan["x9"] == "11101" && plan["x8"] == "", "откат: x9 -> 11101, x8 -> пусто; остальные не трогаем: "
                  + string.Join(", ", plan.Select(kv => kv.Key + "=" + kv.Value)));
        }

        /// <summary>25.09.2026: «чтобы галочки стояли сами, где надо» — по правилам 1260, на данных 1364 после отката.</summary>
        private static void Test1364AfterRestore()
        {
            var rows = WireNamingEngine.Preview(new List<WireInfo>
            {
                // ПЧ3: вход автомата от шины — потенциал шины (в 1260 1L1…), не трогаем; выходы 2/4/6 — 03L11…03L13 (1260 л.208: 01L11…).
                W("in2", 194, 100, 500, E(E01, "1UB1-1L2", "1"), E(E01, "03QF01", "3")),
                W("out1", 194, 100, 300, E(E01, "03QF01", "2"), E(E01, "03U01", "R/L1"), name: "03L11"),
                W("out2", 194, 130, 300, E(E01, "03QF01", "4"), E(E01, "03U01", "S/L2")),
                W("out3", 194, 160, 300, E(E01, "03QF01", "6"), E(E01, "03U01", "T/L3")),
                W("in1q", 192, 100, 500, E(E01, "1UB1-1L2", "1", "b"), E(E01, "1QF01", "3")),
                W("out1q", 192, 130, 300, E(E01, "1QF01", "4"), E(E01, "01U01", "S/L2")),
                // Пневмоостров E01: номер = устройство-клемма (UP1-2), как 3VP1-1 в 1260.
                W("up", 250, 100, 100, E(E01, "UP1", "2"), E(E01, "1A2.1", "8"), cable: "WUP1"),
                // Цепь управления: реле-реле — счётчик 1260; питание модуля ПЛК (1A = M), Profinet, шина — не трогаем.
                W("rel", 262, 100, 100, E(E01, "K230", "14"), E(E01, "K201", "2")),
                W("aux", 262, 200, 100, E(E01, "1A2.3", "1A"), E(E01, "K233", "A2")),
                W("pn", 177, 100, 100, E(E01, "1UE3", "P2"), E(E01, "1UE1", "P1")),
                // Жила к полю, где сейчас номер-потенциал 41L+ — по 1260 устройство-клемма.
                W("fld", 269, 100, 100, E(E01, "1X24", "31L+", "2"), E(Field, "WO16", "11"), potential: "L+", cable: "WWO16", name: "41L+"),
                // Существующий осмысленный номер другого вида — не перебиваем сами.
                W("bv", 26, 100, 100, E(E01, "1X24", "31L+", "3"), E(Field, "BV21+", "1"), potential: "L+", cable: "WV21", name: "B21-1"),
                // Скопированный 14101 на DI двух объектов (EPLAN числит провод на L+) — дубль, а не общий потенциал.
                W("c14a", 262, 100, 100, E(Field, "WO1", "12"), E(E01, "1A1.4", "1"), potential: "L+", name: "14101"),
                W("c14b", 263, 100, 100, E(Field, "WO3", "12"), E(E01, "1A1.4", "5"), potential: "L+", name: "14101"),
                W("pe1", 16, 100, 100, E("+CP.ET101", "2X1", "PE", "a"), E("+CP.ET101", "XPE", "1")),
                W("pe2", 17, 100, 100, E("+CP.ET101", "2G1", "PE"), E("+CP.ET101", "XPE", "1", "b"), name: "PE"),
                W("ao", 195, 100, 100, E(E01, "1A3.2", "4"), E(E01, "04U01", "AO1"), name: "13203"),
                W("ao3", 194, 100, 100, E(E01, "1A3.2", "3"), E(E01, "03U01", "AO1"), name: "13203"),
            }, WireRulePresets.Like1260(), new List<CableCores>());
            WireNamingRow R(string id) => rows.Single(r => r.WireIds.Contains(id));
            string S(string id) => $"{id}: '{R(id).ProposedName}' {R(id).Status} {(R(id).Apply ? "✓" : "—")} {R(id).Note}";

            Check(R("in2").Status == WireNamingStatus.NoRule && R("in1q").Status == WireNamingStatus.NoRule, "вход автомата от шины не трогаем: " + S("in2"));
            Check(R("out1").Status == WireNamingStatus.Unchanged && R("out2").ProposedName == "03L12" && R("out2").Apply
                  && R("out3").ProposedName == "03L13" && R("out3").Apply, "выходы ПЧ3: " + S("out1") + "; " + S("out2") + "; " + S("out3"));
            Check(R("out1q").ProposedName == "01L11" && R("out1q").Apply, "1QF01 -> группа 01: " + S("out1q"));
            Check(R("up").ProposedName == "UP1-2" && R("up").Apply, "пневмоостров: " + S("up"));
            Check(R("rel").ProposedName.Length > 0 && R("rel").Apply, "реле-реле — счётчик, отмечен: " + S("rel"));
            Check(R("aux").Status == WireNamingStatus.NoRule && R("pn").Status == WireNamingStatus.NoRule, "питание ПЛК/Profinet не трогаем: " + S("aux") + "; " + S("pn"));
            Check(R("fld").ProposedName == "WO16-11" && R("fld").Apply, "жила к полю вместо 41L+: " + S("fld"));
            Check(R("bv").ProposedName == "BV21-1" && !R("bv").Apply, "осмысленный номер другого вида — без галочки: " + S("bv"));
            Check(R("pe1").Status != WireNamingStatus.Conflict, "PE — не конфликт: " + S("pe1"));
            // Провод без точки номера на схеме (реле-команда ↔ клемма COM/NO) — номер запишется только с «Ставить новые точки».
            var np = WireNamingEngine.Preview(new List<WireInfo>
            {
                new WireInfo { Id = "np", Page = 262, X = 1, Y = 1, Start = E(E01, "K230", "11"), End = E(E01, "K201", "2"), HasDefinitionPoint = false },
            }, WireRulePresets.Like1260(), new List<CableCores>())[0];
            Check(np.ProposedName.Length > 0 && !np.Apply && np.AutoWithPoints && np.Note.Contains("точки"),
                  $"без точки номера — не отмечен, отметится с «Ставить новые точки» ({np.ProposedName}, {np.Apply}, {np.AutoWithPoints}, {np.Note})");
            string tsv = WireExport.ToTsv(new List<WireInfo> { new WireInfo { Id = "x", Start = E(E01, "K1", "1"), End = E(E01, "K2", "2"), HasDefinitionPoint = false } },
                                          new List<CableCores>());
            Check(!WireExport.ParseTsv(tsv)[0].HasDefinitionPoint, "признак точки номера — в выгрузке");

            Check(R("c14a").ProposedName == "WO1-12" && R("c14a").Apply && R("c14b").ProposedName == "WO3-12" && R("c14b").Apply,
                  "скопированный 14101 -> WO1-12 / WO3-12: " + S("c14a") + "; " + S("c14b"));
            Check(R("ao").Apply && R("ao").ProposedName == "13204", "скопированный 13203 -> 13204: " + S("ao"));
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
                rules.Add(new WireRule { Name = "Моё", Device = "K*", CounterStart = 7, Template = "K{Счётчик}", SourceDevice = "??QF*" });
                store.Save(rules);
                var loaded = store.Load();
                Check(loaded.Count == rules.Count && !loaded[0].Enabled && loaded.Last().CounterStart == 7 && loaded.Last().Template == "K{Счётчик}"
                      && loaded.Last().SourceDevice == "??QF*",
                      "сохранение/загрузка правил, включая SourceDevice");
                Check(store.ResetToPreset().Count == WireRulePresets.Like1260().Count, "сброс к пресету");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
