---
id: 1
title: Автоподбор сечения/типа/цвета провода (part number) по правилам, отдельно от нумерации
priority: medium
status: open
created: 2026-10-01T13:03:51Z
updated: 2026-10-01T13:03:51Z
---
Что сделать:
Добавить отдельный модуль автоматического подбора сечения/типа/цвета провода (EPLAN "Part number" точки соединения, Connection definition point), который пишет это значение по правилам (например, по номинальному току/напряжению/типу цепи устройства) — по аналогии с тем, как EplanCipMvp.Core/VfdComponentDefaults.cs задаёт артикулы компонентов силового контура.

Почему:
Обнаружено 2026-10-01 при разборе скриншота EPLAN (Connection definition point -> Part number: "ПВ-3, х2,5 ВК") — сейчас движок нумерации вообще не касается сечения/типа/цвета провода:
- EplanCipMvp.Core/WireNaming/WireModels.cs:16-34 — модель WireInfo не хранит сечение/тип/цвет провода вообще.
- EplanCipMvp.Core/WireNaming/WireNets.cs:17-41 — идентичность провода ("один и тот же провод/сеть") определяется чисто топологически через union-find по WireEnd.PinKey, сечение не участвует — и не должно: один и тот же потенциал может физически идти разным сечением на разных участках (например, после предохранителя), но номер провода должен остаться одним. Смешивать сечение с логикой нумерации нельзя, сломает корректность.
- EplanCipMvp.App/WireNumbering.cs:53-66 — чтение данных из EPLAN API тянет только Properties.Connection.POTENTIAL_NAME и CONNECTION_WIRENUMBER; свойство "Part number" у Connection definition point сейчас не читается и не пишется вообще.

Подбор сечения/артикула провода — отдельная задача (спецификация/заказ, а не нумерация), и её можно реализовать отдельным модулем, не трогая WireNets/WireNamingEngine.

Где смотреть:
- Прецедент по структуре данных (POCO с реальными артикулами, не гипотезами): EplanCipMvp.Core/VfdComponentDefaults.cs
- Точка записи в EPLAN API (где читается HasDefinitionPoint): EplanCipMvp.App/WireNumbering.cs:63
- Контекст обсуждения: EPLAN Connection definition point, пример "ПВ-3, х2,5 ВК" (тип ПВ-3, сечение 2,5мм², цвет ВК)

Проект: eplan-cip-mvp
