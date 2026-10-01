using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanCipMvp.App;
using EplanCipMvp.Core.CableNaming;
using EplanCipMvp.Gui;

namespace EplanCipMvp.AddIn
{
    /// <summary>Действие EPLAN: открыть окно нумерации кабелей для выделенного проекта.</summary>
    public class CableNumberingAction : IEplAction
    {
        public const string ActionName = "EplanCipMvpCableNumbering";

        public bool OnRegister(ref string Name, ref int Ordinal)
        {
            Name = ActionName;
            Ordinal = 20;
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }

        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            try
            {
                Project project = new SelectionSet().GetCurrentProject(true);
                if (project == null)
                {
                    MessageBox.Show("Откройте проект и выделите его в навигаторе страниц, затем повторите.",
                                    "Нумерация кабелей", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return true;
                }

                var host = new ProjectCableHost(project);
                using (var dialog = new CableNumberingDialog(host.ProjectName, host.Read, host.Preview, host.Apply))
                    dialog.ShowDialog(new EplanWindow());
                return true;
            }
            catch (Exception ex)
            {
                AddInLog.Write("ОШИБКА действия: " + ex);
                MessageBox.Show(ex.ToString(), "Нумерация кабелей — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>Главное окно EPLAN как владелец диалога (диалог поверх EPLAN, не за ней).</summary>
        private class EplanWindow : IWin32Window
        {
            public IntPtr Handle => Process.GetCurrentProcess().MainWindowHandle;
        }
    }

    /// <summary>Чтение/пересчёт/запись для одного открытого проекта.</summary>
    internal class ProjectCableHost
    {
        private readonly Project _project;
        private readonly CableNumberingSession _session = new CableNumberingSession();

        public ProjectCableHost(Project project) { _project = project; }

        public string ProjectName => _project.ProjectName;

        public List<CableNamingRow> Read(IList<CableRule> rules, System.Action<string> log)
        {
            // LockingStep обязателен для обработчиков в диалогах (см. доку LockingStep); вложенность допустима.
            using (new LockingStep())
                return _session.Read(_project, rules, log);
        }

        public List<CableNamingRow> Preview(IList<CableRule> rules) => _session.Preview(rules);

        public void Apply(IDictionary<string, string> newNameById, System.Action<string> log)
        {
            using (new LockingStep())
            {
                UndoStep undo = null;
                try
                {
                    try
                    {
                        undo = new UndoManager().CreateUndoStep();
                        undo.SetUndoDescription("Нумерация кабелей (EplanCipMvp)");
                    }
                    catch (Exception ex)
                    {
                        undo = null;
                        log("Шаг отмены (Undo) не создан — запись без отмены: " + ex.Message);
                    }
                    _session.Apply(_project, newNameById, log);
                }
                finally
                {
                    undo?.Dispose();
                }
            }
        }
    }
}
