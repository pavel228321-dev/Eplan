using System.Collections.Generic;
using System.Linq;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.Core
{
    /// <summary>
    /// Собирает список силовых контуров ПЧ из "Перечня устройств" — по одному на
    /// каждое устройство с BaseDeviceType == VfdComponentDefaults.VfdDeviceType.
    /// Первая "готовая к работе" сущность именно про обвязку ПЧ (см. чат 09.09.2026,
    /// "начать с обвязки ПЧ") — не рисует лист, но собирает всё, что для этого нужно.
    /// </summary>
    public static class VfdCircuitBuilder
    {
        public static List<VfdCircuit> BuildAll(List<DeviceInfo> devices, VfdComponentDefaults defaults = null)
        {
            defaults = defaults ?? VfdComponentDefaults.Default();

            return devices
                .Where(d => d.BaseDeviceType == defaults.VfdDeviceType)
                .OrderBy(d => d.Tag, NaturalStringComparer.Instance)
                .Select(d => new VfdCircuit
                {
                    PumpTag = d.Tag,
                    Cabinet = d.Cabinet,
                    Power = d.Power,
                    VfdVendor = defaults.VfdVendor,
                    VfdArticle = defaults.VfdArticle,
                    VfdIsDiscontinued = defaults.VfdIsDiscontinued,
                    VfdNote = defaults.VfdNote,
                    ControlPanelArticle = defaults.ControlPanelArticle,
                    BreakerVendor = defaults.BreakerVendor,
                    BreakerArticle = defaults.BreakerArticle,
                    AuxContactArticle = defaults.AuxContactArticle,
                    AuxContactPurpose = defaults.AuxContactPurpose,
                })
                .ToList();
        }
    }
}
