using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.Starter;
using Eplan.EplApi.System;
using EplanCipMvp.App;
using EplanCipMvp.Core;
using EplanCipMvp.Core.CableNaming;
using EplanCipMvp.Core.WireNaming;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.Web
{
    public class PageOption
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class GroupSummary
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool IsAnalogy { get; set; }
        public string AnalogyNote { get; set; }
        public bool IsDp { get; set; }
    }

    public class ConnectRequest
    {
        public string BinPath { get; set; }
        public string XlsxPath { get; set; }
        public string SourceProject1 { get; set; }
        public string SourceProject2 { get; set; }
        public string TargetProject { get; set; }

        /// <summary>12.09.2026: необязательный путь к таблице параметров ПЧ —
        /// задел на будущее (см. GetPumpParameters). Пусто/null — как раньше,
        /// ни на что не влияет.</summary>
        public string PumpParamsXlsxPath { get; set; }

        /// <summary>12.09.2026: если true и файла по пути TargetProject ещё нет —
        /// перед открытием TargetProject он создаётся как ПОЛНАЯ копия донора 1
        /// (ProjectManager.CopyProject, CopyMode.Snapshot). См. ConnectToEplan.</summary>
        public bool CreateTargetAsFullCopyOfDonor1 { get; set; }
    }

    public class ReconcileRequest
    {
        public List<string> Groups { get; set; } = new List<string>();
    }

    public class ConnectResult
    {
        public List<string> Log { get; set; } = new List<string>();
        public List<GroupSummary> Groups { get; set; } = new List<GroupSummary>();
        public bool Donor2Connected { get; set; }
        public bool Connected { get; set; }
    }

    public class CopyItem
    {
        public string Group { get; set; }
        public string DonorPage { get; set; }
        public string TitlePage { get; set; }
        public string DetailPage { get; set; }
    }

    public class CableApplyItem
    {
        public string Id { get; set; }
        public string NewName { get; set; }
    }

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

    public class CableReadResult
    {
        public List<string> Log { get; set; } = new List<string>();
        public List<CableNamingRow> Rows { get; set; } = new List<CableNamingRow>();
    }

    /// <summary>
    /// 11.09.2026: то же состояние и та же логика, что раньше жили в полях/методах
    /// EplanCipMvp.Gui.MainForm — перенесено сюда без WinForms, чтобы веб-интерфейс
    /// (EplanCipMvp.Web) мог дёргать ровно те же операции (Connect/GetDonorPages/Copy),
    /// что раньше делали BtnConnect_Click/RebuildGroupRows/BtnCopy_Click. MainForm.cs
    /// не тронут — WinForms остаётся рабочим запасным вариантом (план в
    /// /home/node/.claude/plans/eventual-humming-charm.md).
    ///
    /// EPLAN требует STA и один живой сеанс (EplApplication/LockingStep/ProjectManager) —
    /// HTTP-запросы приходят на пуле потоков, поэтому все вызовы EPLAN API идут через
    /// очередь на ОДИН выделенный STA-поток (тот же приём, что и раньше в этом проекте
    /// для [STAThread], просто явный, не через Main).
    /// </summary>
    public class EplanSession : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Thread _staThread;

        /// <summary>
        /// 11.09.2026: под Mono поймали TypeLoadException прямо на старте
        /// ("Could not load type of field '..._app'... Eplan.EplApi.Systemu") —
        /// поля с EPLAN-типами, объявленные НАПРЯМУЮ на EplanSession (которая
        /// создаётся в Program.Main СРАЗУ, ещё до первого HTTP-запроса), заставляли
        /// рантайм резолвить их сборки раньше, чем AssemblyResolver.PinToEplan()
        /// вообще успевал сказать, откуда их брать (тот вызывается только внутри
        /// Connect()). Тот же фикс, что и в EplanCipMvp.Gui/MainForm.cs (см. там
        /// класс EplanState) — все EPLAN-типизированные поля, включая
        /// Dictionary&lt;string, List&lt;Page&gt;&gt; (Page — тоже EPLAN-тип, тот же
        /// риск), вынесены в отдельный класс, создаваемый ТОЛЬКО внутри Connect(),
        /// сразу после PinToEplan().
        /// </summary>
        private class EplanState
        {
            public EplApplication App;
            public LockingStep LockingStep;
            public ProjectManager ProjectManager;
            public Project SourceProject;
            public Project SourceProject2;
            public Project TargetProject;

            /// <summary>Последний список кандидатов, отданный по каждой группе через
            /// GetDonorPages — нужен, чтобы на Copy() найти реальный Page-объект по
            /// имени, присланному фронтендом (сам Page через HTTP не передать).</summary>
            public readonly Dictionary<string, List<Page>> CandidatesByGroup = new Dictionary<string, List<Page>>();

            /// <summary>23.09.2026: нумерация кабелей (последнее «Считать») — общая с Gui логика.</summary>
            public readonly CableNumberingSession Cables = new CableNumberingSession();

            /// <summary>24.09.2026: нумерация жил и проводов.</summary>
            public readonly WireNumberingSession Wires = new WireNumberingSession();
        }

        /// <summary>null, пока не вызван Connect() — создаётся ПОСЛЕ PinToEplan(),
        /// см. комментарий у EplanState.</summary>
        private EplanState _eplan;
        private string _xlsxPath;

        /// <summary>12.09.2026: результат последнего чтения таблицы параметров ПЧ —
        /// задел под будущий автоподбор донор-страницы (см. план
        /// eventual-humming-charm.md). НЕ EPLAN-тип, поэтому, в отличие от полей
        /// EplanState, безопасно жить здесь как обычное поле с самого старта — и
        /// не зависит от того, подключилась ли вообще EPLAN (см. Connect()).</summary>
        private List<PumpParameterRow> _pumpParameterRows = new List<PumpParameterRow>();


        public EplanSession()
        {
            _staThread = new Thread(RunQueue) { IsBackground = true, Name = "EplanCipMvp-STA" };
            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.Start();
        }

        private void RunQueue()
        {
            foreach (var action in _queue.GetConsumingEnumerable())
                action();
        }

        private T Invoke<T>(Func<T> func)
        {
            T result = default(T);
            Exception error = null;
            using (var done = new ManualResetEventSlim(false))
            {
                _queue.Add(() =>
                {
                    try { result = func(); }
                    catch (Exception ex) { error = ex; }
                    finally { done.Set(); }
                });
                done.Wait();
            }
            if (error != null)
                throw new EplanSessionException(error.Message, error);
            return result;
        }

        private void Invoke(Action action) => Invoke<object>(() => { action(); return null; });

        public void Dispose()
        {
            try
            {
                Invoke(() =>
                {
                    if (_eplan == null) return; // Connect() ни разу не вызывался
                    try { _eplan.LockingStep?.Dispose(); } catch { /* закрываем в любом случае */ }
                    try { _eplan.App?.Exit(); } catch { /* закрываем в любом случае */ }
                });
            }
            catch { /* при выходе не критично */ }
            _queue.CompleteAdding();
        }

        // ------------------------------------------------------------------
        // Группы: null(=VFD, "Насосы с ПЧ") + DeviceGroupCatalog.All + "DP"
        // — тот же список, что раньше строился в _deviceGroups/_clbGroups.Items
        // (MainForm.cs).
        // ------------------------------------------------------------------
        private List<GroupSummary> AllGroupSummaries()
        {
            var result = new List<GroupSummary>
            {
                new GroupSummary { Key = "VFD", DisplayName = "Насосы с ПЧ (готово, проверено)" },
            };
            foreach (var g in DeviceGroupCatalog.All)
            {
                result.Add(new GroupSummary
                {
                    Key = g.Key,
                    DisplayName = g.DisplayName,
                    IsAnalogy = g.IsAnalogy,
                    AnalogyNote = g.AnalogyNote,
                });
            }
            result.Add(new GroupSummary
            {
                Key = "DP",
                DisplayName = "Модули ПЛК (DP: обзор + детализация)",
                IsDp = true,
            });
            return result;
        }

        public ConnectResult Connect(ConnectRequest req)
        {
            return Invoke(() =>
            {
                var result = new ConnectResult();
                void Log(string s) => result.Log.Add(s);

                // 12.09.2026: таблицу параметров ПЧ читаем ПЕРВОЙ и ВНЕ общего
                // try/catch подключения к EPLAN ниже — задел на будущее (см. план
                // eventual-humming-charm.md), пока только чтение/отображение, на
                // выбор донор-страниц не влияет. Специально независимо от EPLAN:
                // и чтобы это можно было проверить даже без живой EPLAN, и чтобы
                // ошибка в самой EPLAN не отбирала уже прочитанные строки.
                if (!string.IsNullOrWhiteSpace(req.PumpParamsXlsxPath))
                {
                    try
                    {
                        _pumpParameterRows = PumpParameterReader.ReadRows(req.PumpParamsXlsxPath);
                        Log($"Таблица параметров ПЧ: прочитано {_pumpParameterRows.Count} строк(и).");
                    }
                    catch (Exception ex)
                    {
                        _pumpParameterRows = new List<PumpParameterRow>();
                        DiagnosticLog.Error("таблица параметров ПЧ", ex);
                        Log($"Таблица параметров ПЧ: не удалось прочитать ({ex.Message}).");
                    }
                }

                if (string.IsNullOrWhiteSpace(req.TargetProject))
                {
                    Log("Не указан проект (.elk) — укажите путь к проекту и подключитесь снова.");
                    return result;
                }

                // 23.09.2026: папка Bin должна быть папкой варианта (где W3u.exe) — см. EplanBootstrap.
                string binError = EplanBootstrap.CheckBinPath(req.BinPath);
                if (binError != null)
                {
                    Log(binError);
                    return result;
                }

                Log("Подключаюсь к EPLAN...");

                try
                {
                    // 12.09.2026: вызов через ОТДЕЛЬНЫЙ метод (ConnectToEplan), а не
                    // инлайн здесь — важно не косметически, а функционально. Мы
                    // экспериментально обнаружили под Mono: если EPLAN-типизированные
                    // локальные переменные (EplApplication/LockingStep/...) объявлены
                    // В ТОЙ ЖЕ лямбде/методе, что и код ДО них (например, чтение
                    // таблицы параметров выше) — при невозможности резолвнуть сборки
                    // Eplan.EplApi.* падает JIT-компиляция ВСЕГО метода целиком, ДО
                    // выполнения хоть одной его строки, включая try/catch внутри
                    // него самого (это другое явление, чем TypeLoadException на
                    // полях объекта, из-за которого появился класс EplanState —
                    // тут дело в теле МЕТОДА, а не в конструкторе). В результате
                    // строка лога о таблице параметров тоже терялась, хотя стояла
                    // текстово раньше и вне этого try. Вынос в отдельный метод
                    // решает: его JIT происходит отдельно, и исключение при вызове
                    // ловится этим try как обычное исключение в точке вызова — тот
                    // же принцип, что уже применялся для CopyVfdPumps/CopyDeviceGroup/
                    // CopyPlcModules ниже.
                    // PinToEplan — отдельным шагом ДО JIT ConnectToEplan (см. EplanBootstrap).
                    EplanBootstrap.PinOnce(req.BinPath);
                    ConnectToEplan(req, result, Log);
                }
                catch (Exception ex)
                {
                    // Раньше исключение здесь пробрасывалось до ApiServer.HandleSafe
                    // и превращалось в голый 500 без лога — пользователь терял весь
                    // накопленный Log (включая строку про таблицу параметров выше).
                    // Теперь — как и во всех Copy*-методах ниже — ошибка просто
                    // дописывается в лог, результат всё равно возвращается.
                    DiagnosticLog.Error("подключение к EPLAN", ex);
                    Log($"ОШИБКА подключения к EPLAN: {ex.GetType().Name}: {ex.Message} (подробности — в журнале {DiagnosticLog.FilePath})");
                }

                return result;
            });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ConnectToEplan(ConnectRequest req, ConnectResult result, Action<string> Log)
        {
            if (_eplan == null)
            {
                // PinToEplan уже выполнен — см. EplanBootstrap.PinOnce в Connect.
                // См. комментарий у класса EplanState — создаём ТОЛЬКО ЗДЕСЬ,
                // сразу после PinToEplan(), а не как поле EplanSession.
                //
                // 23.09.2026: живой запуск — после неудачной первой попытки все
                // следующие падали с NullReferenceException на OpenProject: _eplan
                // присваивался ДО Init, и при сбое Init оставался «полуживым»
                // (ProjectManager == null), а повторное подключение init пропускало.
                // Теперь _eplan присваивается только после полной инициализации.
                var state = new EplanState();
                try
                {
                    Log("Запускаю EPLAN API (Init)...");
                    state.App = new EplApplication();
                    state.App.EplanBinFolder = req.BinPath;
                    state.App.Init("", true, true);
                    state.App.ResetQuietMode();

                    state.LockingStep = new LockingStep();
                    state.ProjectManager = new ProjectManager();
                }
                catch
                {
                    try { state.LockingStep?.Dispose(); } catch { /* уже сбой — главное исходное исключение */ }
                    try { state.App?.Exit(); } catch { /* то же */ }
                    throw;
                }
                _eplan = state;
                Log("EPLAN инициализирована.");
            }

            _xlsxPath = req.XlsxPath;

            // 12.09.2026: "Шаг 0" — целевой проект как ПОЛНАЯ копия донора 1, вместо
            // пустого проекта из шаблона (см. план eventual-humming-charm.md). До
            // открытия каких-либо проектов, т.к. CopyProject работает по путям
            // файлов, не по открытым Project-объектам. CopyMode.Snapshot — донор при
            // этом не блокируется монопольно (тот же принцип, что и ReadOnly ниже).
            // Не перезаписываем существующий файл без явного намерения — тот же
            // консервативный принцип, что и везде в этом проекте.
            if (req.CreateTargetAsFullCopyOfDonor1)
            {
                if (string.IsNullOrWhiteSpace(req.TargetProject))
                {
                    Log("Полное копирование не выполнено: не указан путь для нового целевого проекта.");
                }
                else if (System.IO.File.Exists(req.TargetProject))
                {
                    Log($"Целевой проект уже существует по пути {req.TargetProject} — полное копирование пропущено, открываю как есть.");
                }
                else
                {
                    Log($"Копирую донор 1 целиком как основу целевого проекта: {req.SourceProject1} -> {req.TargetProject} (CopyMode.Snapshot)...");
                    try
                    {
                        _eplan.ProjectManager.CopyProject(req.SourceProject1, req.TargetProject, ProjectManager.CopyMode.Snapshot);
                        Log("Полное копирование завершено.");
                    }
                    catch (ProjectCopyException ex) { Log("ОШИБКА полного копирования: " + ex.Message); }
                    catch (ProjectNeedsUpgradeException ex) { Log("Донор нужно сначала обновить в самой EPLAN (старая структура БД): " + ex.Message); }
                    catch (IncompatibleDatabaseException ex) { Log("Несовместимая структура БД донора: " + ex.Message); }
                    catch (ArgumentException ex) { Log("ОШИБКА полного копирования (неверный путь?): " + ex.Message); }
                }
            }

            // 23.09.2026: доноры нужны только для генерации страниц. Для нумерации
            // кабелей/жил достаточно папки EPLAN и самого проекта.
            bool donorsGiven = !string.IsNullOrWhiteSpace(req.SourceProject1);
            if (donorsGiven)
            {
                Log($"Открываю проект-донор 1 (только чтение): {req.SourceProject1}");
                _eplan.SourceProject = _eplan.ProjectManager.OpenProject(req.SourceProject1, ProjectManager.OpenMode.ReadOnly);
            }
            else
            {
                Log("Доноры не указаны — открываю только проект (режим нумерации).");
            }

            if (donorsGiven && !string.IsNullOrWhiteSpace(req.SourceProject2))
            {
                try
                {
                    Log($"Открываю проект-донор 2 (только чтение): {req.SourceProject2}");
                    _eplan.SourceProject2 = _eplan.ProjectManager.OpenProject(req.SourceProject2, ProjectManager.OpenMode.ReadOnly);
                    result.Donor2Connected = true;
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error("открытие донора 2", ex);
                    Log($"Донор 2 не открылся ({ex.Message}) — группы, для которых он нужен (GS), будут недоступны.");
                }
            }
            else if (donorsGiven)
            {
                Log("Донор 2 не указан — группы, для которых он нужен (GS), будут недоступны, пока не заполните поле.");
            }

            Log($"Открываю проект: {req.TargetProject}");
            _eplan.TargetProject = _eplan.ProjectManager.OpenProject(req.TargetProject);

            Log("Подключено.");
            result.Connected = true;
            result.Groups = AllGroupSummaries();
        }

        /// <summary>12.09.2026: строки последней прочитанной таблицы параметров ПЧ
        /// (пустой список, если файл не указывался/не прочитался) — задел под
        /// будущий автоподбор донор-страницы, см. ConnectRequest.PumpParamsXlsxPath.</summary>
        public List<PumpParameterRow> GetPumpParameters() => Invoke(() => _pumpParameterRows);

        /// <summary>12.09.2026: список целевых тегов устройств группы, читая ту же
        /// спецификацию тем же способом, что уже делают CopyVfdPumps/CopyDeviceGroup —
        /// общий помощник для ReconcilePreview/ReconcileApply, чтобы не дублировать.</summary>
        private List<string> ReadTargetTagsForGroup(DeviceGroupConfig config)
        {
            var devices = SignalListReader.ReadDevices(_xlsxPath);
            return devices
                .Select(d => d.Tag)
                .Where(t => config.TagTypeCodes.Contains(TagParser.TypeCode(t)))
                .OrderBy(t => t, NaturalStringComparer.Instance)
                .ToList();
        }

        /// <summary>12.09.2026: расчёт плана реконсиляции (см. PageReconciler) для
        /// каждой запрошенной группы — БЕЗ побочных эффектов, ничего не меняет в
        /// проекте. Модули ПЛК (DP) сюда не входят — см. комментарий у
        /// PageReconciler о том, почему титул/детализация не разделяются программно.</summary>
        public List<ReconcileGroupPlan> ReconcilePreview(ReconcileRequest req)
        {
            return Invoke(() =>
            {
                // 12.09.2026: НАРОЧНО только "_eplan == null" здесь, без обращения к
                // _eplan.TargetProject — иначе (см. ConnectToEplan выше) JIT этой самой
                // лямбды потребует резолвнуть все поля EplanState (в т.ч. EPLAN-типы)
                // ДО выполнения хоть одной строки, даже до проверки на null. Обращение
                // к _eplan.TargetProject вынесено в отдельный метод ниже — его JIT
                // происходит отдельно и уже ПОСЛЕ гарантии, что PinToEplan() отработал
                // (раз мы вообще сюда дошли, _eplan != null, т.е. Connect() успел).
                if (_eplan == null)
                    return new List<ReconcileGroupPlan> { new ReconcileGroupPlan { GroupKey = "", DisplayName = "Сначала вызовите /api/connect." } };
                return ReconcilePreviewCore(req);
            });
        }

        private List<ReconcileGroupPlan> ReconcilePreviewCore(ReconcileRequest req)
        {
            var plans = new List<ReconcileGroupPlan>();
            if (_eplan.TargetProject == null)
            {
                plans.Add(new ReconcileGroupPlan { GroupKey = "", DisplayName = "Целевой проект не открыт — сначала вызовите /api/connect." });
                return plans;
            }

            foreach (var key in req.Groups ?? new List<string>())
            {
                try
                {
                    if (key == "VFD")
                    {
                        var circuits = VfdCircuitBuilder.BuildAll(SignalListReader.ReadDevices(_xlsxPath));
                        plans.Add(PageReconciler.PlanVfd(_eplan.TargetProject, circuits.Select(c => c.PumpTag).ToList()));
                    }
                    else
                    {
                        var config = DeviceGroupCatalog.All.FirstOrDefault(g => g.Key == key);
                        if (config == null)
                        {
                            plans.Add(new ReconcileGroupPlan { GroupKey = key, DisplayName = key + ": группа не поддерживает реконсиляцию (см. PageReconciler)." });
                            continue;
                        }
                        plans.Add(PageReconciler.PlanDeviceGroup(_eplan.TargetProject, config, ReadTargetTagsForGroup(config)));
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error($"реконсиляция, план [{key}]", ex);
                    plans.Add(new ReconcileGroupPlan { GroupKey = key, DisplayName = key + ": ОШИБКА расчёта — " + ex.Message });
                }
            }

            return plans;
        }

        /// <summary>12.09.2026: реально выполняет реконсиляцию (дублирование/УДАЛЕНИЕ/
        /// переименование, необратимо) для каждой запрошенной группы — пользователь
        /// должен был сначала увидеть ReconcilePreview для тех же групп (кнопка
        /// "Применить" в UI активна только после предпросмотра).</summary>
        public List<string> ReconcileApply(ReconcileRequest req)
        {
            return Invoke(() =>
            {
                // См. комментарий в ReconcilePreview — только "_eplan == null" здесь.
                if (_eplan == null)
                    return new List<string> { "Сначала вызовите /api/connect." };
                return ReconcileApplyCore(req);
            });
        }

        private List<string> ReconcileApplyCore(ReconcileRequest req)
        {
            var log = new List<string>();
            void Log(string s) => log.Add(s);

            if (_eplan.TargetProject == null)
            {
                Log("Целевой проект не открыт — сначала вызовите /api/connect.");
                return log;
            }

            foreach (var key in req.Groups ?? new List<string>())
            {
                try
                {
                    if (key == "VFD")
                    {
                        Log("[Насосы с ПЧ] Реконсиляция...");
                        var circuits = VfdCircuitBuilder.BuildAll(SignalListReader.ReadDevices(_xlsxPath));
                        log.AddRange(PageReconciler.ApplyVfd(_eplan.TargetProject,
                            circuits.Select(c => c.PumpTag).ToList(), VfdComponentDefaults.Default(), Log));
                    }
                    else
                    {
                        var config = DeviceGroupCatalog.All.FirstOrDefault(g => g.Key == key);
                        if (config == null)
                        {
                            Log($"[{key}] Группа не поддерживает реконсиляцию.");
                            continue;
                        }
                        Log($"[{config.DisplayName}] Реконсиляция...");
                        log.AddRange(PageReconciler.ApplyDeviceGroup(_eplan.TargetProject, config, ReadTargetTagsForGroup(config), Log));
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error($"реконсиляция, применение [{key}]", ex);
                    Log($"[{key}] ОШИБКА: {ex.Message}");
                }
            }

            return log;
        }

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
            result.Rows = _eplan.Cables.Read(_eplan.TargetProject, rules, result.Log.Add);
            return result;
        }

        /// <summary>Пересчёт предпросмотра по уже считанным кабелям (после правки правил).</summary>
        public List<CableNamingRow> PreviewCables(List<CableRule> rules)
        {
            return Invoke(() =>
            {
                // Только "_eplan == null" здесь — см. комментарий в ReconcilePreview.
                if (_eplan == null) return new List<CableNamingRow>();
                return PreviewCablesCore(rules);
            });
        }

        private List<CableNamingRow> PreviewCablesCore(List<CableRule> rules) => _eplan.Cables.Preview(rules);

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
            _eplan.Cables.Apply(_eplan.TargetProject, request, log.Add);
            return log;
        }

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

        /// <summary>24.09.2026: выгрузка последнего «Считать» (TSV); пусто — ещё не считывали.</summary>
        public string ExportCables() => Invoke(() => _eplan == null ? "" : ExportCablesCore());

        private string ExportCablesCore() => _eplan.Cables.ExportTsv();

        public string ExportWires() => Invoke(() => _eplan == null ? "" : ExportWiresCore());

        /// <summary>25.09.2026: откат номеров из резервной копии/выгрузки (TSV).</summary>
        public List<string> RestoreWires(string backupTsv)
        {
            return Invoke(() =>
            {
                if (_eplan == null) return new List<string> { "Сначала подключитесь (кнопка «Подключиться»)." };
                return RestoreWiresCore(backupTsv);
            });
        }

        private List<string> RestoreWiresCore(string backupTsv)
        {
            var log = new List<string>();
            if (_eplan.TargetProject == null)
            {
                log.Add("Целевой проект не открыт — сначала подключитесь.");
                return log;
            }
            _eplan.Wires.Restore(_eplan.TargetProject, backupTsv, log.Add);
            return log;
        }

        private string ExportWiresCore() => _eplan.Wires.ExportTsv();

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

        private static string DescribePage(Page page)
        {
            string description = null;
            try { description = page.Properties.PAGE_NOMINATIOMN; }
            catch { /* не критично — часть донорских страниц может не иметь описания */ }
            return string.IsNullOrWhiteSpace(description) ? page.Name : $"{page.Name}  —  {description}";
        }

        private static List<PageOption> ToOptions(List<Page> pages) =>
            pages.Select(p => new PageOption { Name = p.Name, Description = DescribePage(p) }).ToList();

        public List<PageOption> GetDonorPages(string groupKey)
        {
            return Invoke(() =>
            {
                // 12.09.2026: только "_eplan == null" здесь — см. комментарий в
                // ReconcilePreview выше про то, почему "_eplan.SourceProject" в ЭТОЙ
                // ЖЕ лямбде валит JIT всего метода целиком под Mono.
                if (_eplan == null)
                    return new List<PageOption>();
                return GetDonorPagesCore(groupKey);
            });
        }

        private List<PageOption> GetDonorPagesCore(string groupKey)
        {
            if (_eplan.SourceProject == null)
                return new List<PageOption>();

            List<Page> candidates;
            if (groupKey == "VFD")
            {
                candidates = VfdPageCopier.FindCandidateDonorPages(_eplan.SourceProject);
            }
            else if (groupKey == "DP")
            {
                candidates = DeviceGroupPageCopier.FindCandidateDonorPages(_eplan.SourceProject, ".DP");
            }
            else
            {
                var config = DeviceGroupCatalog.All.FirstOrDefault(g => g.Key == groupKey);
                if (config == null)
                    return new List<PageOption>();
                var donorProject = config.PreferredDonor == 2 ? _eplan.SourceProject2 : _eplan.SourceProject;
                if (donorProject == null)
                    return new List<PageOption>();
                candidates = DeviceGroupPageCopier.FindCandidateDonorPages(donorProject, config.DonorPageFilter);
            }

            _eplan.CandidatesByGroup[groupKey] = candidates;
            return ToOptions(candidates);
        }

        private Page FindCachedPage(string groupKey, string pageName)
        {
            if (string.IsNullOrEmpty(pageName)) return null;
            if (_eplan == null || !_eplan.CandidatesByGroup.TryGetValue(groupKey, out var candidates)) return null;
            return candidates.FirstOrDefault(p => p.Name == pageName);
        }

        public List<string> Copy(List<CopyItem> items)
        {
            return Invoke(() =>
            {
                // 12.09.2026: только "_eplan == null" здесь — см. комментарий в
                // ReconcilePreview выше про то, почему обращение к _eplan.SourceProject/
                // _eplan.TargetProject в ЭТОЙ ЖЕ лямбде валит JIT всего метода под Mono.
                if (_eplan == null)
                    return new List<string> { "Сначала вызовите /api/connect." };
                return CopyCore(items);
            });
        }

        private List<string> CopyCore(List<CopyItem> items)
        {
            var log = new List<string>();
            void Log(string s) => log.Add(s);

            if (_eplan.SourceProject == null || _eplan.TargetProject == null)
            {
                Log("Сначала вызовите /api/connect.");
                return log;
            }

            foreach (var item in items ?? new List<CopyItem>())
            {
                    if (item.Group == "DP")
                    {
                        var titlePage = FindCachedPage("DP", item.TitlePage);
                        var detailPage = FindCachedPage("DP", item.DetailPage);
                        if (titlePage == null || detailPage == null)
                        {
                            Log("[DP] Не найдена страница-донор (титул или деталь) — перечитайте список страниц.");
                            continue;
                        }
                        CopyPlcModules(titlePage, detailPage, Log);
                    }
                    else if (item.Group == "VFD")
                    {
                        var donorPage = FindCachedPage("VFD", item.DonorPage);
                        if (donorPage == null)
                        {
                            Log("[Насосы с ПЧ] Не найдена страница-донор — перечитайте список страниц.");
                            continue;
                        }
                        CopyVfdPumps(donorPage, Log);
                    }
                    else
                    {
                        var config = DeviceGroupCatalog.All.FirstOrDefault(g => g.Key == item.Group);
                        var donorPage = FindCachedPage(item.Group, item.DonorPage);
                        if (config == null || donorPage == null)
                        {
                            Log($"[{item.Group}] Не найдена группа или страница-донор — перечитайте список страниц.");
                            continue;
                        }
                        CopyDeviceGroup(config, donorPage, Log);
                    }
                }

            return log;
        }

        // ------------------------------------------------------------------
        // Ниже — дословный перенос CopyVfdPumps/CopyDeviceGroup/CopyPlcModules
        // из EplanCipMvp.Gui/MainForm.cs, только вместо вызова Log(...) на
        // текстовое поле — Action<string> log, переданный параметром.
        // ------------------------------------------------------------------

        private void CopyVfdPumps(Page donorPage, Action<string> log)
        {
            try
            {
                log("Читаю спецификацию, ищу насосы с ПЧ...");
                var devices = SignalListReader.ReadDevices(_xlsxPath);
                var circuits = VfdCircuitBuilder.BuildAll(devices);

                if (circuits.Count == 0)
                {
                    log("Насосов с ПЧ в спецификации не найдено — нечего копировать.");
                    return;
                }

                log($"Найдено {circuits.Count} насос(ов) с ПЧ: {string.Join(", ", circuits.Select(c => c.PumpTag))}");
                log($"Копирую страницу '{donorPage.Name}' для каждого...");

                var copier = new VfdPageCopier();
                var results = copier.CopyForAllPumps(donorPage, _eplan.TargetProject, circuits, VfdComponentDefaults.Default());

                foreach (var r in results)
                {
                    if (r.PageCopied)
                    {
                        log($"  {r.PumpTag}: OK, новая страница '{r.NewPageName}', {r.PlacementsTotal} объектов.");
                        log($"    Найдены теги: {string.Join(", ", r.FoundDeviceTags)}");
                        if (r.Retagged.Count > 0)
                            log($"    Переименовано: {string.Join(", ", r.Retagged)}");
                        if (r.ArticlesSwapped.Count > 0)
                            log($"    Артикул заменён на актуальный (1364): {string.Join(", ", r.ArticlesSwapped)}");
                        if (r.Removed.Count > 0)
                            log($"    Убрано со страницы (Profinet, не нужно): {string.Join(" | ", r.Removed)}");
                        if (r.NoSpecMatch.Count > 0)
                            log($"    ТРЕБУЕТ РЕШЕНИЯ ЧЕЛОВЕКА: {string.Join(" | ", r.NoSpecMatch)}");
                        if (r.Error != null)
                            log($"    Были ошибки на части объектов: {r.Error}");
                    }
                    else
                    {
                        log($"  {r.PumpTag}: ОШИБКА — {r.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                log("ОШИБКА (насосы с ПЧ): " + ex);
            }
        }

        private void CopyDeviceGroup(DeviceGroupConfig group, Page donorPage, Action<string> log)
        {
            try
            {
                if (group.IsAnalogy)
                    log($"[{group.DisplayName}] {group.AnalogyNote}");

                log($"[{group.DisplayName}] Читаю спецификацию, ищу устройства типа {string.Join("/", group.TagTypeCodes)}...");
                var devices = SignalListReader.ReadDevices(_xlsxPath);
                var tags = devices
                    .Select(d => d.Tag)
                    .Where(t => group.TagTypeCodes.Contains(TagParser.TypeCode(t)))
                    .OrderBy(t => t, NaturalStringComparer.Instance)
                    .ToList();

                if (tags.Count == 0)
                {
                    log($"[{group.DisplayName}] Устройств в спецификации не найдено — нечего копировать.");
                    return;
                }

                var batches = DeviceGroupPageCopier.BuildBatches(tags, group.DevicesPerDonorPage);
                log($"[{group.DisplayName}] Найдено {tags.Count} устройств(о): {string.Join(", ", tags)}");
                log($"[{group.DisplayName}] Плотность донора: {group.DevicesPerDonorPage}/лист -> {batches.Count} страниц. " +
                    "Копирую страницу '" + donorPage.Name + "' на каждую пачку...");

                var copier = new DeviceGroupPageCopier();
                var results = copier.CopyGroup(donorPage, _eplan.TargetProject, group, tags);

                foreach (var r in results)
                {
                    string batchLabel = string.Join(", ", r.BatchTags);
                    if (r.PageCopied)
                    {
                        log($"  {batchLabel}: OK, новая страница '{r.NewPageName}', {r.PlacementsTotal} объектов.");
                        log($"    Найдены теги донора: {string.Join(", ", r.FoundDeviceTags)}");
                        if (r.SlotsMatched)
                        {
                            if (r.Retagged.Count > 0)
                                log($"    Переименовано: {string.Join(", ", r.Retagged)}");
                        }
                        else
                        {
                            log("    СЛОТЫ НЕ РАЗОБРАНЫ — донорские теги оставлены как есть, поправьте вручную в EPLAN.");
                        }
                        if (r.Error != null)
                            log($"    Были ошибки: {r.Error}");
                    }
                    else
                    {
                        log($"  {batchLabel}: ОШИБКА — {r.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                log($"ОШИБКА ({group.DisplayName}): " + ex);
            }
        }

        private void CopyPlcModules(Page titlePage, Page detailPage, Action<string> log)
        {
            try
            {
                log("[DP] Читаю спецификацию, группирую сигналы по модулям ET200SP...");

                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
                var config = configBuilder.Build();
                var settings = new AppSettings();
                config.Bind(settings);
                settings.Grouping.CpuCabinet = settings.PlcHardware.CpuCabinet;

                var signals = SignalListReader.ReadDeviceList(_xlsxPath, settings.Spec.SheetName);
                var modules = ModuleGrouper.GroupIntoModules(signals, settings.Grouping, settings.PageStructure);

                if (modules.Count == 0)
                {
                    log("[DP] Модулей не найдено — нечего копировать.");
                    return;
                }

                var cabinets = modules.Select(m => m.Cabinet).Distinct().ToList();
                log($"[DP] Найдено {modules.Count} модул(ей) в {cabinets.Count} шкаф(ах): {string.Join(", ", cabinets)}");

                var copier = new PlcModulePageCopier();

                log($"[DP] Копирую титульные листы ('{titlePage.Name}') — по одному на шкаф.");
                var titleResults = copier.CopyTitlePages(titlePage, _eplan.TargetProject, cabinets);
                foreach (var r in titleResults)
                {
                    if (r.PageCopied)
                        log($"  {r.Cabinet}: OK, новая страница '{r.NewPageName}'." + (r.Error != null ? $" (были ошибки: {r.Error})" : ""));
                    else
                        log($"  {r.Cabinet}: ОШИБКА — {r.Error}");
                }

                log($"[DP] Копирую детализацию ('{detailPage.Name}') — по 2 модуля на лист.");
                var detailResults = copier.CopyDetailPages(detailPage, _eplan.TargetProject, modules);
                foreach (var r in detailResults)
                {
                    string batchLabel = string.Join(", ", r.BatchDesignations);
                    if (r.PageCopied)
                    {
                        log($"  {batchLabel}: OK, новая страница '{r.NewPageName}', {r.PlacementsTotal} объектов.");
                        log($"    Найдены теги донора: {string.Join(", ", r.FoundDeviceTags)}");
                        if (r.SlotsMatched)
                        {
                            if (r.Retagged.Count > 0)
                                log($"    Переименовано: {string.Join(", ", r.Retagged)}");
                            if (r.ArticlesSwapped.Count > 0)
                                log($"    Артикул заменён: {string.Join(", ", r.ArticlesSwapped)}");
                        }
                        else
                        {
                            log("    СЛОТЫ НЕ РАЗОБРАНЫ — донорские теги/артикул оставлены как есть, поправьте вручную в EPLAN.");
                        }
                        if (r.Error != null)
                            log($"    Были ошибки: {r.Error}");
                    }
                    else
                    {
                        log($"  {batchLabel}: ОШИБКА — {r.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                log("ОШИБКА (DP): " + ex);
            }
        }
    }

    public class EplanSessionException : Exception
    {
        public EplanSessionException(string message, Exception inner) : base(message, inner) { }
    }
}
