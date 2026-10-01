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
