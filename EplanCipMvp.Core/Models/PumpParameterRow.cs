using System.Collections.Generic;

namespace EplanCipMvp.Core.Models
{
    /// <summary>
    /// 12.09.2026: одна строка таблицы параметров ПЧ (Google Sheet "Таблица Для
    /// Автоматизма Eplan") — задел под будущий автоподбор донор-страницы по
    /// параметрам насоса вместо ручного выбора. Таблица у пользователя пока не
    /// заполнена данными (только заголовки колонок), поэтому:
    ///  - все известные колонки хранятся как СЫРОЙ текст ячейки (string), без
    ///    попытки интерпретировать "+/-"/"да"/"нет" как bool — правило ещё не
    ///    известно, станет ясно только по реальным данным;
    ///  - RawColumns хранит ВСЕ колонки листа как есть, включая те, что появятся
    ///    позже и пока не имеют именованного свойства здесь.
    /// См. план: /home/node/.claude/plans/eventual-humming-charm.md
    /// </summary>
    public class PumpParameterRow
    {
        /// <summary>Номер строки в файле (с 1) — запасной идентификатор на случай,
        /// если колонки-тега нет (сейчас в таблице её нет).</summary>
        public int RowNumber { get; set; }

        /// <summary>Тег/номер насоса, если удалось распознать такую колонку
        /// (см. PumpParameterReader.TagColumnNames) — иначе null.</summary>
        public string Tag { get; set; }

        public string VfdModel { get; set; }
        public string VfdCharacteristics { get; set; }
        public string Breaker { get; set; }
        public string Starter { get; set; }
        public string StartDi { get; set; }
        public string FeedbackDo { get; set; }
        public string FrequencyRefAi { get; set; }
        public string FeedbackAo { get; set; }
        public string ProgrammableDi { get; set; }
        public string Profibus { get; set; }
        public string MotorModel { get; set; }
        public string MotorCharacteristics { get; set; }
        public string ThermalFeedback { get; set; }
        public string PhaseCount { get; set; }

        /// <summary>Все колонки листа как есть (заголовок -> значение ячейки) —
        /// включая колонки без именованного свойства выше, т.к. схема таблицы
        /// ещё будет меняться.</summary>
        public IReadOnlyDictionary<string, string> RawColumns { get; set; }
    }
}
