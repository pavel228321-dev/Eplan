using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using EplanCipMvp.Core.Models;

namespace EplanCipMvp.Core
{
    /// <summary>
    /// 12.09.2026: читает таблицу параметров ПЧ — задел под будущий автоподбор
    /// донор-страницы по параметрам насоса (см. план
    /// /home/node/.claude/plans/eventual-humming-charm.md). В отличие от
    /// SignalListReader — сознательно МЯГКИЙ к структуре листа: пользователь ещё
    /// наполняет таблицу и, вероятно, будет менять состав колонок, поэтому
    /// отсутствующая колонка не бросает исключение, а лист берётся первый в
    /// книге, если имя не задано явно (имя вкладки реального экспортированного
    /// файла нам заранее неизвестно).
    /// </summary>
    public static class PumpParameterReader
    {
        private static readonly string[] TagColumnNames =
            { "Тег", "Насос", "№", "N", "Позиция", "Tag", "Pump" };

        public static List<PumpParameterRow> ReadRows(string xlsxPath, string sheetName = null)
        {
            var result = new List<PumpParameterRow>();

            using (var wb = new XLWorkbook(xlsxPath))
            {
                var ws = string.IsNullOrWhiteSpace(sheetName) ? wb.Worksheets.First() : wb.Worksheet(sheetName);
                var lastRowUsed = ws.LastRowUsed();
                if (lastRowUsed == null)
                    return result; // пустой лист — заголовков тоже нет

                var headerRow = ws.Row(1);
                var headers = new Dictionary<int, string>();
                foreach (var cell in headerRow.CellsUsed())
                {
                    var name = cell.GetString().Trim();
                    if (!string.IsNullOrEmpty(name))
                        headers[cell.Address.ColumnNumber] = name;
                }

                int colTag = 0;
                foreach (var h in headers)
                {
                    if (TagColumnNames.Any(n => n.Equals(h.Value, StringComparison.OrdinalIgnoreCase)))
                    {
                        colTag = h.Key;
                        break;
                    }
                }

                int lastRow = lastRowUsed.RowNumber();
                for (int r = 2; r <= lastRow; r++)
                {
                    var row = ws.Row(r);
                    var raw = new Dictionary<string, string>();
                    bool anyValue = false;
                    foreach (var kv in headers)
                    {
                        var value = row.Cell(kv.Key).GetString().Trim();
                        raw[kv.Value] = value;
                        if (!string.IsNullOrEmpty(value))
                            anyValue = true;
                    }
                    if (!anyValue)
                        continue; // полностью пустая строка — пропускаем

                    string Get(string header) => raw.TryGetValue(header, out var v) && !string.IsNullOrEmpty(v) ? v : null;
                    string tagValue = colTag > 0 ? row.Cell(colTag).GetString().Trim() : null;

                    result.Add(new PumpParameterRow
                    {
                        RowNumber = r,
                        Tag = string.IsNullOrEmpty(tagValue) ? null : tagValue,
                        VfdModel = Get("Модель ПЧ"),
                        VfdCharacteristics = Get("Характеристики технические"),
                        Breaker = Get("Автоматический выключатель ОС"),
                        Starter = Get("Магнитный пускатель"),
                        StartDi = Get("Пуск DI"),
                        FeedbackDo = Get("Обратная связь DO"),
                        FrequencyRefAi = Get("Задание частоты AI"),
                        FeedbackAo = Get("Обратная связь AO"),
                        ProgrammableDi = Get("Программируемый вход DI"),
                        Profibus = Get("Profibus"),
                        MotorModel = Get("Модель двигателя"),
                        MotorCharacteristics = Get("Характеристики двигателя"),
                        ThermalFeedback = Get("Температурная обратная связь"),
                        PhaseCount = Get("Количество Фаз"),
                        RawColumns = raw,
                    });
                }
            }

            return result;
        }
    }
}
