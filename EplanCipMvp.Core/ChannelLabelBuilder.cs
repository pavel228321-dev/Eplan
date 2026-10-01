using System.Text.RegularExpressions;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.Core
{
    /// <summary>
    /// Строит подпись канала в формате, похожем на реальные страницы МСА
    /// ("+LINE3-V0 Вода в бачок"), из плоского тега спецификации ("L3V0" + описание).
    ///
    /// Это чисто текстовая логика — не зависит от EPLAN API, поэтому её можно
    /// проверить прямо сейчас (Core.SelfTest), в отличие от самой вставки текста
    /// в EPLAN (PlcPageBuilder.FillChannelTexts — всё ещё заглушка, ждёт Этапа 0).
    /// </summary>
    public static class ChannelLabelBuilder
    {
        private static readonly Regex LineTagPattern = new Regex(@"^L(\d+)(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string BuildLabel(DeviceSignal signal, NamingSettings settings = null)
        {
            settings = settings ?? NamingSettings.Default();

            var match = LineTagPattern.Match(signal.Tag);
            string structurePrefix;
            string shortCode;

            if (match.Success)
            {
                string lineNumber = match.Groups[1].Value;
                shortCode = match.Groups[2].Value;
                structurePrefix = string.Format(settings.LineStructureTemplate, lineNumber);
            }
            else
            {
                shortCode = signal.Tag;
                structurePrefix = settings.CommonStructurePrefix;
            }

            return string.Format(settings.LabelTemplate, structurePrefix, shortCode, signal.DeviceType);
        }
    }
}
