using System;
using Eplan.EplApi.ApplicationFramework;
using EplMenu = Eplan.EplApi.Gui.Menu;

namespace EplanCipMvp.AddIn
{
    /// <summary>
    /// 24.09.2026: надстройка EPLAN — пункт меню «CIP MVP → Нумерация кабелей…» открывает окно
    /// нумерации для проекта, выделенного в EPLAN (без запуска отдельной EPLAN и открытия проекта).
    /// Подключение: Utilities → API add-ins… → Load → EplanCipMvp.AddIn.dll.
    /// </summary>
    public class AddIn : IEplAddIn
    {
        public bool OnRegister(ref bool bLoadOnStart)
        {
            bLoadOnStart = true;
            return true;
        }

        public bool OnUnregister() => true;

        public bool OnInit() => true;

        public bool OnInitGui()
        {
            try
            {
                new EplMenu().AddMainMenu(
                    "CIP MVP",
                    EplMenu.MainMenuName.eMainMenuUtilities,
                    "Нумерация кабелей…",
                    CableNumberingAction.ActionName,
                    "Нумерация кабелей по правилам в текущем проекте",
                    1);
            }
            catch (Exception ex)
            {
                // Меню не создалось — надстройка всё равно работает, действие можно вызвать по имени.
                AddInLog.Write("Не удалось добавить пункт меню: " + ex);
            }
            return true;
        }

        public bool OnExit() => true;
    }
}
