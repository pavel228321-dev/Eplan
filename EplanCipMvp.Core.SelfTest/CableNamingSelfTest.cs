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
            TestTemplate();
            TestApplyPlanner();
            TestEngine();
            Test1260Fixture();
            Test1364MainCabinetSensors();
            TestCableExport();
            TestRuleStore();
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
            Check(WildcardMask.Matches("?##*", "T32M04") && WildcardMask.Matches("?##*", "C01M01"), "# = цифра");
            Check(!WildcardMask.Matches("?##*", "BV21+") && !WildcardMask.Matches("?##*", "LS4"), "# не совпадает с буквой");
            Check(WildcardMask.Matches("@##@*", "T32GS01") && !WildcardMask.Matches("@##@*", "B111"), "@ = буква (B111 — не «буква+2 цифры+буква»)");
        }

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

        /// <summary>24.09.2026, живой прогон 1364: в 1260 кабель к датчику = W + полное имя датчика
        /// (WB3VP01, WBM02VP05 — «+» в конце DT не входит), «от первой цифры» — только для «буква участка + 2 цифры»
        /// (T32M04 -> W32M04). Первая версия отрезала тип у датчиков E01 1364: BV21+ -> W21+, LS4 -> W4.</summary>
        private static void Test1364MainCabinetSensors()
        {
            var cables = new List<CableInfo>
            {
                FieldCable("v21", "WV21", "BV21+", "+CP.E01"),
                FieldCable("ls4", "WLS4", "LS4", "+CP.E01"),
                FieldCable("m", "", "T32M04", "+CP.E01"),
                FieldCable("bm", "", "BM02VP05+", "+CP.ET101"),
                FieldCable("b111", "WB111", "B111", "+CP.E01"),   // выгрузка 1364 от 25.09: было предложено W111
            };
            var rows = CableNamingEngine.Preview(cables, CableRulePresets.Like1260()).ToDictionary(r => r.Cable.Id);
            Check(rows["v21"].ProposedName == "WBV21", $"BV21+ -> WBV21 (получено '{rows["v21"].ProposedName}')");
            Check(rows["ls4"].ProposedName == "WLS4" && rows["ls4"].Status == CableNamingStatus.Unchanged, "LS4 -> WLS4, без изменений");
            Check(rows["m"].ProposedName == "W32M04", "T32M04 из E01 -> W32M04");
            Check(rows["bm"].ProposedName == "WBM02VP05", "BM02VP05+ -> WBM02VP05 (как в 1260)");
            Check(rows["b111"].ProposedName == "WB111" && rows["b111"].Status == CableNamingStatus.Unchanged, $"B111 -> WB111 (получено '{rows["b111"].ProposedName}')");

            // Пневмоостров: в 1260 -W3VP1 к -3VP1, в 1364 -WUP1 к -UP1 (оба конца в шкафу).
            var island = new CableInfo
            {
                Id = "up", CurrentName = "WUP1", Type = "D25-25-3M-A", CoresTotal = 25,
                Sources = new List<CableEnd> { new CableEnd { Location = "+CP.E01", Device = "1A2.1" } },
                Targets = new List<CableEnd> { new CableEnd { Location = "+CP.E01", Device = "UP1" } },
            };
            var islandRow = CableNamingEngine.Preview(new List<CableInfo> { island }, CableRulePresets.Like1260())[0];
            Check(islandRow.ProposedName == "WUP1" && islandRow.Status == CableNamingStatus.Unchanged, $"WUP1 — без изменений (получено '{islandRow.ProposedName}', {islandRow.Status}, {islandRow.Note})");
        }

        /// <summary>Выгрузка «Считать кабели» — в формате фикстуры 1260-cables.tsv (читается тем же ParseEnds).</summary>
        private static void TestCableExport()
        {
            var cable = FieldCable("c7", "WB3VP01", "B3VP01+");
            string tsv = CableExport.ToTsv(new List<CableInfo> { cable });
            var lines = tsv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Check(lines.Length == 2 && lines[0].StartsWith("No\tName\tType\tCores\tSources\tTargets"), "заголовок выгрузки кабелей");
            var f = lines[1].Split('\t');
            Check(f[1] == "WB3VP01" && f[3] == "7" && f[4] == "+CP.ET101|2X24" && f[5] == "+FIELD|B3VP01+", "строка выгрузки кабелей: " + lines[1]);
            Check(ParseEnds(f[5])[0].Device == "B3VP01+", "выгрузка читается ParseEnds");
        }

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

        private static void TestRuleStore()
        {
            string path = Path.Combine(Path.GetTempPath(), "cable-rules-selftest-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new CableRuleStore(path);
                Check(store.Load().Count == CableRulePresets.Like1260().Count, "нет файла -> пресет 1260");
                var saved = store.ResetToPreset();
                var loaded = store.Load();
                Check(loaded.Count == saved.Count, "после сохранения — то же число правил");
                for (int i = 0; i < saved.Count; i++)
                {
                    Check(loaded[i].Name == saved[i].Name && loaded[i].Template == saved[i].Template
                          && loaded[i].CoresTotal == saved[i].CoresTotal && loaded[i].Enabled == saved[i].Enabled,
                          $"правило #{i + 1} не совпало после сохранения/загрузки");
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

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
    }
}
