using System.Collections.Generic;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.Core
{
    public class ArticleResolution
    {
        public string Tag { get; set; }
        public string DeviceType { get; set; }
        public bool WasTbd { get; set; }
        public string ResolvedVendor { get; set; }
        public string ResolvedArticle { get; set; }
        /// <summary>"исходные данные" / "настройка по умолчанию (требует подтверждения)" / "не определено"</summary>
        public string Source { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// Подставляет вендора/артикул "по умолчанию" туда, где в спецификации TBD —
    /// на основе EplanCipMvp.App/appsettings.json -> DeviceDefaults.ArticleDefaults.
    /// Сопоставление — по DeviceInfo.BaseDeviceType (без уточнения в скобках), т.к.
    /// один тип устройства в файле встречается с разными уточнениями
    /// ("Клапан пневматический" / "Клапан пневматический (моющая головка)").
    /// </summary>
    public static class ArticleResolver
    {
        public static ArticleResolution Resolve(DeviceInfo device, Dictionary<string, ArticleDefault> defaults)
        {
            bool isTbd = device.IsManufacturerTbd || device.IsOrderNumberTbd;

            if (!isTbd)
            {
                return new ArticleResolution
                {
                    Tag = device.Tag,
                    DeviceType = device.DeviceType,
                    WasTbd = false,
                    ResolvedVendor = device.Manufacturer,
                    ResolvedArticle = device.OrderNumber,
                    Source = "исходные данные",
                };
            }

            if (defaults != null && defaults.TryGetValue(device.BaseDeviceType, out var def))
            {
                return new ArticleResolution
                {
                    Tag = device.Tag,
                    DeviceType = device.DeviceType,
                    WasTbd = true,
                    ResolvedVendor = def.Vendor,
                    ResolvedArticle = def.Article,
                    Source = "настройка по умолчанию (требует подтверждения)",
                    Note = def.Note,
                };
            }

            return new ArticleResolution
            {
                Tag = device.Tag,
                DeviceType = device.DeviceType,
                WasTbd = true,
                ResolvedVendor = null,
                ResolvedArticle = null,
                Source = "не определено — нет ни данных, ни настройки по умолчанию",
            };
        }

        public static List<ArticleResolution> ResolveAll(List<DeviceInfo> devices, Dictionary<string, ArticleDefault> defaults)
        {
            var result = new List<ArticleResolution>();
            foreach (var device in devices)
                result.Add(Resolve(device, defaults));
            return result;
        }
    }
}
