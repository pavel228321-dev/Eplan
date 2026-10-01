using System;
using System.Collections.Generic;
using ClosedXML.Excel;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.Core
{
    /// <summary>
    /// Читает лист "Перечень устройств" из файла 1364-Сводная_спецификация_изделий.xlsx
    /// и разворачивает каждую строку устройства в отдельные DeviceSignal (по одному на канал).
    /// Устойчиво к порядку колонок — ищет их по заголовку, а не по номеру.
    /// </summary>
    public static class SignalListReader
    {
        public static List<DeviceSignal> ReadDeviceList(string xlsxPath, string sheetName = "Перечень устройств")
        {
            var result = new List<DeviceSignal>();

            using (var wb = new XLWorkbook(xlsxPath))
            {
                var ws = wb.Worksheet(sheetName);
                var headerRow = ws.Row(1);

                int colIdent = FindColumn(headerRow, "Идентификатор");
                int colType = FindColumn(headerRow, "Тип");
                int colCabinet = FindColumn(headerRow, "Шкаф");
                int colDI = FindColumn(headerRow, "DI");
                int colDO = FindColumn(headerRow, "DO");
                int colAI = FindColumn(headerRow, "AI");
                int colAO = FindColumn(headerRow, "AO");

                int lastRow = ws.LastRowUsed().RowNumber();
                for (int r = 2; r <= lastRow; r++)
                {
                    var row = ws.Row(r);
                    string tag = row.Cell(colIdent).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(tag))
                        continue;

                    string deviceType = row.Cell(colType).GetString().Trim();
                    string cabinet = row.Cell(colCabinet).GetString().Trim();

                    AddChannels(result, tag, deviceType, cabinet, "DI", row.Cell(colDI));
                    AddChannels(result, tag, deviceType, cabinet, "DO", row.Cell(colDO));
                    AddChannels(result, tag, deviceType, cabinet, "AI", row.Cell(colAI));
                    AddChannels(result, tag, deviceType, cabinet, "AO", row.Cell(colAO));
                }
            }

            return result;
        }

        /// <summary>
        /// Читает тот же лист, но на уровне устройства (не канала) — с производителем
        /// и заказным номером. Нужно для ArticleResolver (сверка TBD-позиций) и подбора
        /// символа-макроса по типу устройства.
        /// </summary>
        public static List<DeviceInfo> ReadDevices(string xlsxPath, string sheetName = "Перечень устройств")
        {
            var result = new List<DeviceInfo>();

            using (var wb = new XLWorkbook(xlsxPath))
            {
                var ws = wb.Worksheet(sheetName);
                var headerRow = ws.Row(1);

                int colIdent = FindColumn(headerRow, "Идентификатор");
                int colType = FindColumn(headerRow, "Тип");
                int colCabinet = FindColumn(headerRow, "Шкаф");
                int colManufacturer = FindColumn(headerRow, "Производитель");
                int colOrderNumber = FindColumn(headerRow, "Заказной номер");
                // 10.09.2026: Power/Voltage — не обязательные (не у всех типов устройств в
                // таблице они заполнены осмысленно), поэтому не через FindColumn (который
                // бросает исключение, если колонки нет) — через TryFindColumn.
                int colPower = TryFindColumn(headerRow, "Power");
                int colVoltage = TryFindColumn(headerRow, "Voltage");

                int lastRow = ws.LastRowUsed().RowNumber();
                for (int r = 2; r <= lastRow; r++)
                {
                    var row = ws.Row(r);
                    string tag = row.Cell(colIdent).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(tag))
                        continue;

                    result.Add(new DeviceInfo
                    {
                        Tag = tag,
                        DeviceType = row.Cell(colType).GetString().Trim(),
                        Cabinet = row.Cell(colCabinet).GetString().Trim(),
                        Manufacturer = row.Cell(colManufacturer).GetString().Trim(),
                        OrderNumber = row.Cell(colOrderNumber).GetString().Trim(),
                        Power = colPower > 0 ? row.Cell(colPower).GetString().Trim() : null,
                        Voltage = colVoltage > 0 ? row.Cell(colVoltage).GetString().Trim() : null,
                    });
                }
            }

            return result;
        }

        private static void AddChannels(List<DeviceSignal> result, string tag, string deviceType,
            string cabinet, string channelType, IXLCell countCell)
        {
            int count = 0;
            if (!countCell.IsEmpty())
                int.TryParse(countCell.GetString(), out count);

            for (int i = 0; i < count; i++)
            {
                result.Add(new DeviceSignal
                {
                    Tag = tag,
                    DeviceType = deviceType,
                    Cabinet = cabinet,
                    ChannelType = channelType
                });
            }
        }

        private static int FindColumn(IXLRow headerRow, string name)
        {
            foreach (var cell in headerRow.CellsUsed())
            {
                if (cell.GetString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return cell.Address.ColumnNumber;
            }
            throw new InvalidOperationException(
                $"Не найдена колонка \"{name}\" на листе \"{headerRow.Worksheet.Name}\". " +
                "Проверьте, что имя листа/заголовки не изменились в файле спецификации.");
        }

        /// <summary>Как FindColumn, но не бросает исключение, если колонки нет — возвращает 0.
        /// Для необязательных колонок (Power/Voltage), которые заполнены не для всех типов
        /// устройств в таблице.</summary>
        private static int TryFindColumn(IXLRow headerRow, string name)
        {
            foreach (var cell in headerRow.CellsUsed())
            {
                if (cell.GetString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return cell.Address.ColumnNumber;
            }
            return 0;
        }
    }
}
