// 11.09.2026: фронтенд для EplanCipMvp.Web — тот же сценарий, что раньше был в
// EplanCipMvp.Gui/MainForm.cs (WinForms), просто через fetch() на локальный
// HTTP API вместо прямых вызовов EPLAN API из формы. Работает ТОЛЬКО с той же
// машины, где запущен сервер (см. план) — без аутентификации.

const NO_DONOR_TYPES = [
  "VC — клапан регулирующий (DI+AO)",
  "FQT — расходомер (DI+AI, смешанный тип)",
  "Насосы дозирования (M2, мембранные SMC)",
];

// 11.09.2026: JavaScriptSerializer (сервер) сериализует свойства C# КАК ЕСТЬ,
// с большой буквы (Key/DisplayName/IsAnalogy/...), camelCase не применяет —
// нашлась и исправлена ошибка, где здесь читалось g.key/g.displayName и т.п.
// (было бы undefined). Все обращения к ответам сервера — строго с большой буквы.
let groups = [];               // ответ /api/connect: [{Key, DisplayName, IsAnalogy, AnalogyNote, IsDp}]
const checkedGroups = new Set();
const donorSelections = {};    // groupKey -> {donorPage} | {titlePage, detailPage} (DP)

const $ = (id) => document.getElementById(id);

function logLines(lines) {
  const el = $("log");
  for (const line of lines) el.textContent += line + "\n";
  el.scrollTop = el.scrollHeight;
}

function logLine(line) { logLines([line]); }

// 23.09.2026: сервер при сбое отвечает {error, logFile} — показываем понятный текст
// и путь к журналу EplanCipMvp.Web.log, где лежит полная ошибка со stack trace.
async function errorFromResponse(resp) {
  const text = await resp.text();
  let message = text;
  try {
    const data = JSON.parse(text);
    if (data && data.error) {
      message = data.error + (data.logFile ? ` (подробности — в журнале ${data.logFile})` : "");
    }
  } catch { /* не JSON — показываем как есть */ }
  return new Error(`HTTP ${resp.status}: ${message}`);
}

async function postJson(url, body) {
  const resp = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!resp.ok) throw await errorFromResponse(resp);
  return resp.json();
}

async function getJson(url) {
  const resp = await fetch(url);
  if (!resp.ok) throw await errorFromResponse(resp);
  return resp.json();
}

// Ошибки самой страницы (JavaScript) — в «Лог» и в журнал на сервере.
function reportClientError(text) {
  logLine("ОШИБКА в браузере: " + text);
  fetch("/api/client-log", { method: "POST", body: text }).catch(() => {});
}
window.addEventListener("error", (e) => reportClientError(`${e.message} (${e.filename}:${e.lineno})`));
window.addEventListener("unhandledrejection", (e) => reportClientError(String(e.reason && e.reason.stack || e.reason)));

// 11.09.2026: "Обзор..." — сервер и браузер на одной машине (см. план), поэтому
// сервер сам открывает системный диалог Windows (см. DialogService.cs) и
// возвращает путь — браузер сам по себе такого пути дать не может.
document.querySelectorAll("button.browse").forEach((btn) => {
  btn.addEventListener("click", async () => {
    const targetId = btn.dataset.target;
    const type = btn.dataset.type; // "file" | "folder" | "save-file"
    const filter = btn.dataset.filter || "";
    btn.disabled = true;
    const label = btn.textContent;
    btn.textContent = "Окно выбора открыто…";
    try {
      const url = type === "folder"
        ? "/api/browse/folder"
        : type === "save-file"
        ? `/api/browse/save-file?filter=${encodeURIComponent(filter)}`
        : `/api/browse/file?filter=${encodeURIComponent(filter)}`;
      const result = await getJson(url);
      if (result.Error) {
        logLine("ОШИБКА диалога выбора: " + result.Error);
      } else if (result.Path) {
        $(targetId).value = result.Path;
      }
      // result.Path == null и без Error — пользователь просто нажал "Отмена", это нормально.
    } catch (err) {
      logLine("ОШИБКА диалога выбора: " + err.message);
    } finally {
      btn.disabled = false;
      btn.textContent = label;
    }
  });
});

// 12.09.2026: "Шаг 0" — если включено, поле "Целевой проект" задаёт путь для
// ЕЩЁ НЕ существующего файла (полная копия донора 1, см. план
// eventual-humming-charm.md) — переключаем кнопку "Обзор..." на SaveFileDialog.
$("createAsFullCopy").addEventListener("change", () => {
  const on = $("createAsFullCopy").checked;
  $("btnBrowseTarget").dataset.type = on ? "save-file" : "file";
  $("targetProjectNote").textContent = on
    ? "Укажите путь для НОВОГО файла — если по этому пути ничего нет, он будет создан как полная копия донора 1 (CopyMode.Snapshot). Если файл уже существует, копирование пропускается, он просто открывается как есть."
    : "Существующий проект. Путь можно вставить и вручную. Первый прогон нумерации — на копии проекта.";
});

$("btnHelp").addEventListener("click", () => $("helpModal").classList.remove("hidden"));
$("btnHelpClose").addEventListener("click", () => $("helpModal").classList.add("hidden"));
$("helpModal").addEventListener("click", (e) => {
  if (e.target === $("helpModal")) $("helpModal").classList.add("hidden");
});

$("btnConnect").addEventListener("click", async () => {
  const body = {
    BinPath: $("binPath").value,
    XlsxPath: $("xlsxPath").value,
    SourceProject1: $("sourceProject1").value,
    SourceProject2: $("sourceProject2").value,
    TargetProject: $("targetProject").value,
    PumpParamsXlsxPath: $("pumpParamsXlsxPath").value,
    CreateTargetAsFullCopyOfDonor1: $("createAsFullCopy").checked,
  };
  const btn = $("btnConnect");
  btn.disabled = true;
  btn.textContent = "Подключаюсь…";
  try {
    const result = await postJson("/api/connect", body);
    logLines(result.Log || []);
    if (!result.Connected) {
      logLine("Не подключено — см. сообщения выше.");
      return;
    }
    $("cablesCard").classList.remove("hidden");
    await loadCableRules();
    $("wiresCard").classList.remove("hidden");
    await loadWireRules();

    // 23.09.2026: генерация страниц — только если указан донор 1 (иначе режим нумерации).
    if ($("sourceProject1").value.trim()) {
      groups = result.Groups || [];
      renderGroupCheckboxes();
      $("groupsBlock").classList.remove("hidden");
      $("noDonorNote").textContent =
        "Без донора ни в 1260, ни в 1166 (в списке выше их нет, программа их не копирует): " +
        NO_DONOR_TYPES.join("; ") + " — чертить вручную.";
      await renderPumpParameters();
      renderReconcileGroupCheckboxes();
      $("reconcileCard").classList.remove("hidden");
      $("legacyBlock").classList.remove("hidden");
    }
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Подключиться";
  }
});

function renderGroupCheckboxes() {
  const list = $("groupsList");
  list.innerHTML = "";
  for (const g of groups) {
    const wrap = document.createElement("label");
    wrap.className = "group-check";

    const cb = document.createElement("input");
    cb.type = "checkbox";
    cb.value = g.Key;
    cb.addEventListener("change", () => onGroupToggle(g, cb.checked));

    const textWrap = document.createElement("span");
    textWrap.textContent = g.DisplayName;
    if (g.IsAnalogy) {
      const small = document.createElement("small");
      small.className = "analogy";
      small.textContent = "⚠ по аналогии, ниже уверенность";
      textWrap.appendChild(small);
    }

    wrap.appendChild(cb);
    wrap.appendChild(textWrap);
    list.appendChild(wrap);
  }
}

// 12.09.2026: задел на будущее (см. план eventual-humming-charm.md) — таблица
// параметров ПЧ пока только читается и показывается для проверки, на выбор
// донор-страниц не влияет.
async function renderPumpParameters() {
  let rows;
  try {
    rows = await getJson("/api/pump-parameters");
  } catch (err) {
    return; // не критично — просто не показываем таблицу
  }
  const card = $("pumpParamsCard");
  if (!rows || rows.length === 0) {
    card.classList.add("hidden");
    return;
  }
  card.classList.remove("hidden");
  const tbody = $("pumpParamsBody");
  tbody.innerHTML = "";
  for (const r of rows) {
    const tr = document.createElement("tr");
    const cells = [
      r.RowNumber, r.Tag, r.VfdModel, r.VfdCharacteristics, r.Breaker, r.Starter,
      r.StartDi, r.FeedbackDo, r.FrequencyRefAi, r.FeedbackAo, r.ProgrammableDi,
      r.Profibus, r.MotorModel, r.MotorCharacteristics, r.ThermalFeedback, r.PhaseCount,
    ];
    for (const c of cells) {
      const td = document.createElement("td");
      td.textContent = (c === null || c === undefined || c === "") ? "—" : c;
      tr.appendChild(td);
    }
    tbody.appendChild(tr);
  }
}

async function onGroupToggle(group, checked) {
  if (checked) {
    checkedGroups.add(group.Key);
  } else {
    checkedGroups.delete(group.Key);
    delete donorSelections[group.Key];
  }
  await rebuildDonorRows();
}

async function rebuildDonorRows() {
  const container = $("donorRows");
  container.innerHTML = "";

  for (const key of checkedGroups) {
    const group = groups.find((g) => g.Key === key);
    let pages;
    try {
      pages = await getJson(`/api/groups/${encodeURIComponent(key)}/donor-pages`);
    } catch (err) {
      pages = [];
      logLine(`ОШИБКА при чтении страниц для "${group.DisplayName}": ${err.message}`);
    }

    const row = document.createElement("div");
    row.className = "donor-row";

    const labelEl = document.createElement("b");
    labelEl.textContent = group.DisplayName;
    row.appendChild(labelEl);

    if (pages.length === 0) {
      const warn = document.createElement("span");
      warn.className = "warn";
      warn.textContent = "страниц-кандидатов не найдено — проверьте подключение донора / фильтр";
      row.appendChild(warn);
      container.appendChild(row);
      continue;
    }

    if (group.IsDp) {
      const titleSelect = buildSelect(pages, `dp-title-${key}`);
      const detailSelect = buildSelect(pages, `dp-detail-${key}`);
      if (pages.length > 1) detailSelect.selectedIndex = 1; // деталь почти наверняка не та же страница, что титул
      donorSelections[key] = { titlePage: titleSelect.value, detailPage: detailSelect.value };
      titleSelect.addEventListener("change", () => { donorSelections[key].titlePage = titleSelect.value; });
      detailSelect.addEventListener("change", () => { donorSelections[key].detailPage = detailSelect.value; });

      const titleLabel = document.createElement("span"); titleLabel.className = "sub-label"; titleLabel.textContent = "титул";
      const detailLabel = document.createElement("span"); detailLabel.className = "sub-label"; detailLabel.textContent = "деталь";
      row.appendChild(titleLabel); row.appendChild(titleSelect);
      row.appendChild(detailLabel); row.appendChild(detailSelect);
    } else {
      const select = buildSelect(pages, `donor-${key}`);
      donorSelections[key] = { donorPage: select.value };
      select.addEventListener("change", () => { donorSelections[key].donorPage = select.value; });
      row.appendChild(select);
    }

    container.appendChild(row);
  }

  $("btnCopy").disabled = checkedGroups.size === 0;
}

function buildSelect(pages, id) {
  const select = document.createElement("select");
  select.id = id;
  for (const p of pages) {
    const opt = document.createElement("option");
    opt.value = p.Name;
    opt.textContent = p.Description || p.Name;
    select.appendChild(opt);
  }
  return select;
}

$("btnCopy").addEventListener("click", async () => {
  const items = [];
  for (const key of checkedGroups) {
    const sel = donorSelections[key];
    if (!sel) continue;
    if (sel.titlePage !== undefined) {
      items.push({ Group: key, TitlePage: sel.titlePage, DetailPage: sel.detailPage });
    } else {
      items.push({ Group: key, DonorPage: sel.donorPage });
    }
  }
  if (items.length === 0) {
    logLine("Нет ни одной готовой к копированию группы.");
    return;
  }

  logLine(`Запускаю копирование для ${items.length} тип(ов) устройств...`);
  const btn = $("btnCopy");
  btn.disabled = true;
  btn.textContent = "Копирую…";
  try {
    const log = await postJson("/api/copy", items);
    logLines(log);
    logLine("Готово. Проверьте результат в самой EPLAN.");
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Скопировать для всех отмеченных типов";
  }
});

// 12.09.2026: Шаг 3 — реконсиляция (см. план eventual-humming-charm.md). DP
// (модули ПЛК) сюда не входит — см. комментарий в PageReconciler.cs о том,
// почему титул/детализация не разделяются программно.
const reconcileCheckedGroups = new Set();
let lastPreviewedGroupsKey = null; // строка-снимок отмеченных групп на момент последнего успешного предпросмотра

function currentReconcileGroupsKey() {
  return Array.from(reconcileCheckedGroups).sort().join(",");
}

function renderReconcileGroupCheckboxes() {
  const list = $("reconcileGroupsList");
  list.innerHTML = "";
  reconcileCheckedGroups.clear();
  lastPreviewedGroupsKey = null;
  $("btnReconcileApply").disabled = true;

  for (const g of groups) {
    if (g.IsDp) continue; // не поддерживается реконсиляцией, см. класс PageReconciler
    const wrap = document.createElement("label");
    wrap.className = "group-check";

    const cb = document.createElement("input");
    cb.type = "checkbox";
    cb.value = g.Key;
    cb.addEventListener("change", () => {
      if (cb.checked) reconcileCheckedGroups.add(g.Key);
      else reconcileCheckedGroups.delete(g.Key);
      $("btnReconcileApply").disabled = true; // список групп поменялся — план устарел
    });

    const textWrap = document.createElement("span");
    textWrap.textContent = g.DisplayName;

    wrap.appendChild(cb);
    wrap.appendChild(textWrap);
    list.appendChild(wrap);
  }
}

function renderReconcilePlan(plans) {
  const el = $("reconcilePlanOutput");
  el.innerHTML = "";
  for (const p of plans) {
    const box = document.createElement("div");
    box.className = "reconcile-plan-item";

    const title = document.createElement("b");
    title.textContent = p.DisplayName || p.GroupKey;
    box.appendChild(title);

    const summary = document.createElement("div");
    summary.textContent = `Есть страниц: ${p.ExistingCount ?? "—"}, нужно по спецификации: ${p.NeededCount ?? "—"}.`;
    box.appendChild(summary);

    if (p.ToDuplicateCount) {
      const dup = document.createElement("div");
      dup.textContent = `Будет продублировано внутри проекта: ${p.ToDuplicateCount} страниц(а).`;
      box.appendChild(dup);
    }
    if (p.ToDeletePageNames && p.ToDeletePageNames.length > 0) {
      const del = document.createElement("div");
      del.className = "delete-line";
      del.textContent = `Будет УДАЛЕНО (необратимо): ${p.ToDeletePageNames.join(", ")}`;
      box.appendChild(del);
    }
    el.appendChild(box);
  }
}

$("btnReconcilePreview").addEventListener("click", async () => {
  if (reconcileCheckedGroups.size === 0) {
    logLine("Отметьте хотя бы одну группу для реконсиляции.");
    return;
  }
  const btn = $("btnReconcilePreview");
  btn.disabled = true;
  btn.textContent = "Считаю…";
  try {
    const plans = await postJson("/api/reconcile/preview", { Groups: Array.from(reconcileCheckedGroups) });
    renderReconcilePlan(plans);
    lastPreviewedGroupsKey = currentReconcileGroupsKey();
    $("btnReconcileApply").disabled = false;
    logLine("План реконсиляции показан ниже — проверьте список удаляемых страниц перед тем, как жать «Применить».");
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Показать план";
  }
});

$("btnReconcileApply").addEventListener("click", async () => {
  if (currentReconcileGroupsKey() !== lastPreviewedGroupsKey) {
    logLine("Список групп изменился с момента предпросмотра — сначала «Показать план» ещё раз.");
    return;
  }
  if (!confirm("Это необратимо удалит лишние страницы (Page.Remove) в целевом проекте, если план это предполагает. Продолжить?")) {
    return;
  }
  const btn = $("btnReconcileApply");
  btn.disabled = true;
  btn.textContent = "Применяю…";
  try {
    const log = await postJson("/api/reconcile/apply", { Groups: Array.from(reconcileCheckedGroups) });
    logLines(log);
    logLine("Реконсиляция завершена. Проверьте результат в самой EPLAN.");
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.textContent = "Применить план";
    // Нарочно НЕ включаем btn обратно автоматически — план мог устареть после
    // применения (страницы физически изменились), заставляем сначала обновить
    // предпросмотр перед повторным применением.
  }
});

// 23.09.2026: нумерация кабелей по правилам. Все данные выводятся через
// textContent/value (не innerHTML) — имена и типы приходят из проекта EPLAN.
let cableRules = [];
let cableRows = [];

const RULE_FIELDS = ["Name", "SourceLocation", "TargetLocation", "TypeContains", "CoresTotal", "TargetDevice", "Template"];

function textInput(value, onChange) {
  const input = document.createElement("input");
  input.type = "text";
  input.value = value ?? "";
  input.addEventListener("input", () => onChange(input.value));
  return input;
}

function cell(tr, content) {
  const td = document.createElement("td");
  if (content instanceof Node) td.appendChild(content);
  else td.textContent = content ?? "";
  tr.appendChild(td);
  return td;
}

function renderCableRules() {
  const tbody = $("rulesTable").querySelector("tbody");
  tbody.innerHTML = "";
  cableRules.forEach((rule, index) => {
    const tr = document.createElement("tr");
    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = rule.Enabled !== false;
    enabled.addEventListener("change", () => { rule.Enabled = enabled.checked; });
    cell(tr, enabled);
    for (const field of RULE_FIELDS) {
      cell(tr, textInput(rule[field], (v) => {
        rule[field] = field === "CoresTotal" ? (v.trim() === "" ? null : parseInt(v, 10)) : v;
      }));
    }
    const tools = document.createElement("span");
    for (const [label, action] of [["↑", -1], ["↓", 1], ["✕", 0]]) {
      const b = document.createElement("button");
      b.type = "button";
      b.textContent = label;
      b.addEventListener("click", () => {
        if (action === 0) cableRules.splice(index, 1);
        else {
          const j = index + action;
          if (j < 0 || j >= cableRules.length) return;
          [cableRules[index], cableRules[j]] = [cableRules[j], cableRules[index]];
        }
        renderCableRules();
      });
      tools.appendChild(b);
    }
    cell(tr, tools);
    tbody.appendChild(tr);
  });
}

async function loadCableRules() {
  try {
    cableRules = await getJson("/api/cables/rules");
    renderCableRules();
  } catch (err) {
    logLine("ОШИБКА загрузки правил: " + err.message);
  }
}

$("btnRuleAdd").addEventListener("click", () => {
  cableRules.push({ Name: "Новое правило", Enabled: true, SourceLocation: "", TargetLocation: "", TypeContains: "", CoresTotal: null, TargetDevice: "", Template: "" });
  renderCableRules();
});

$("btnRulesSave").addEventListener("click", async () => {
  try {
    cableRules = await postJson("/api/cables/rules", cableRules);
    renderCableRules();
    logLine("Правила сохранены.");
    if (cableRows.length > 0) await previewCables();
  } catch (err) {
    logLine("ОШИБКА сохранения правил: " + err.message);
  }
});

$("btnRulesReset").addEventListener("click", async () => {
  if (!confirm("Заменить текущие правила пресетом «Как 1260»?")) return;
  try {
    cableRules = await postJson("/api/cables/rules/reset", {});
    renderCableRules();
    logLine("Правила сброшены к пресету «Как 1260».");
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  }
});

function describeEnds(ends) {
  return (ends || []).map((e) => `${e.Location}-${e.Device}`).join("; ");
}

function effectiveName(row) {
  return row.Apply ? (row.ProposedName || "").trim() : (row.Cable.CurrentName || "");
}

function highlightDuplicates() {
  const counts = new Map();
  for (const row of cableRows) {
    const name = effectiveName(row).toUpperCase();
    if (name) counts.set(name, (counts.get(name) || 0) + 1);
  }
  const trs = $("cablesTable").querySelectorAll("tbody tr");
  cableRows.forEach((row, i) => {
    const dup = row.Apply && (counts.get(effectiveName(row).toUpperCase()) || 0) > 1;
    trs[i].classList.toggle("conflict", dup || row.Status === "Конфликт");
  });
}

function renderCableRows() {
  const tbody = $("cablesTable").querySelector("tbody");
  tbody.innerHTML = "";
  const counts = {};
  for (const row of cableRows) {
    counts[row.Status] = (counts[row.Status] || 0) + 1;
    const tr = document.createElement("tr");
    if (row.Status === "Нет правила") tr.classList.add("norule");
    const apply = document.createElement("input");
    apply.type = "checkbox";
    apply.checked = row.Apply;
    apply.disabled = row.Status === "Конфликт";
    apply.addEventListener("change", () => { row.Apply = apply.checked; highlightDuplicates(); });
    cell(tr, apply);
    cell(tr, row.Cable.CurrentName || "(пусто)");
    cell(tr, describeEnds(row.Cable.Sources));
    cell(tr, describeEnds(row.Cable.Targets));
    cell(tr, `${row.Cable.Type || ""} / ${row.Cable.CoresTotal}`);
    cell(tr, row.RuleName || "—");
    cell(tr, textInput(row.ProposedName, (v) => {
      row.ProposedName = v;
      if (row.Status === "Нет правила" || row.Status === "Без изменений") {
        row.Apply = v.trim() !== "" && v.trim() !== (row.Cable.CurrentName || "");
        apply.checked = row.Apply;
      }
      highlightDuplicates();
    }));
    const status = cell(tr, row.Status + (row.Note ? " — " + row.Note : ""));
    if (row.Note) status.classList.add("note");
    tbody.appendChild(tr);
  }
  $("cablesSummary").textContent = "Итого: " + Object.entries(counts).map(([k, v]) => `${k}: ${v}`).join(", ");
  highlightDuplicates();
}

async function previewCables() {
  cableRows = await postJson("/api/cables/preview", {});
  renderCableRows();
}

$("btnCablesRead").addEventListener("click", async () => {
  const btn = $("btnCablesRead");
  btn.disabled = true;
  btn.textContent = "Читаю…";
  try {
    const result = await postJson("/api/cables/read", {});
    logLines(result.Log || []);
    cableRows = result.Rows || [];
    renderCableRows();
    $("btnCablesPreview").disabled = false;
    $("btnCablesApply").disabled = cableRows.length === 0;
    $("btnCablesExport").disabled = cableRows.length === 0;
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Считать кабели";
  }
});

$("btnCablesPreview").addEventListener("click", async () => {
  try { await previewCables(); } catch (err) { logLine("ОШИБКА: " + err.message); }
});

$("btnCablesApply").addEventListener("click", async () => {
  const items = cableRows.filter((r) => r.Apply).map((r) => ({ Id: r.Cable.Id, NewName: (r.ProposedName || "").trim() }));
  if (items.length === 0) { logLine("Нет отмеченных кабелей."); return; }
  if ($("cablesTable").querySelector("tbody tr.conflict input[type=checkbox]:checked")) {
    logLine("Есть отмеченные строки с одинаковыми именами (подсвечены красным) — исправьте перед записью.");
    return;
  }
  if (!confirm(`Переименовать ${items.length} кабел(ей) в целевом проекте EPLAN? Рекомендуется делать на копии проекта.`)) return;
  const btn = $("btnCablesApply");
  btn.disabled = true;
  btn.textContent = "Записываю…";
  try {
    logLines(await postJson("/api/cables/apply", items));
    await previewCables();
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Записать отмеченные";
  }
});

// 24.09.2026: нумерация жил и проводов. Данные — только через textContent/value.
let wireRules = [];
let wireRows = [];

const WIRE_RULE_FIELDS = ["Name", "Location", "Device", "Terminal", "OtherLocation", "ExcludeDevice", "Potential", "InCable", "SourceDevice", "Order", "CounterStart", "AutoCheck", "Template"];

function renderWireRules() {
  const tbody = $("wireRulesTable").querySelector("tbody");
  tbody.innerHTML = "";
  wireRules.forEach((rule, index) => {
    const tr = document.createElement("tr");
    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = rule.Enabled !== false;
    enabled.addEventListener("change", () => { rule.Enabled = enabled.checked; });
    cell(tr, enabled);
    for (const field of WIRE_RULE_FIELDS) {
      cell(tr, textInput(rule[field], (v) => {
        rule[field] = field === "CounterStart" ? (v.trim() === "" ? null : parseInt(v, 10)) : v;
      }));
    }
    const tools = document.createElement("span");
    for (const [label, action] of [["↑", -1], ["↓", 1], ["✕", 0]]) {
      const b = document.createElement("button");
      b.type = "button";
      b.textContent = label;
      b.addEventListener("click", () => {
        if (action === 0) wireRules.splice(index, 1);
        else {
          const j = index + action;
          if (j < 0 || j >= wireRules.length) return;
          [wireRules[index], wireRules[j]] = [wireRules[j], wireRules[index]];
        }
        renderWireRules();
      });
      tools.appendChild(b);
    }
    cell(tr, tools);
    tbody.appendChild(tr);
  });
}

async function loadWireRules() {
  try {
    wireRules = await getJson("/api/wires/rules");
    renderWireRules();
  } catch (err) {
    logLine("ОШИБКА загрузки правил проводов: " + err.message);
  }
}

$("btnWireRuleAdd").addEventListener("click", () => {
  wireRules.push({ Name: "Новое правило", Enabled: true, Location: "", Device: "", Terminal: "", OtherLocation: "", ExcludeDevice: "",
                   Potential: "", InCable: "", SourceDevice: "", Order: "X", CounterStart: null, AutoCheck: "нет", Template: "" });
  renderWireRules();
});

$("btnWireRulesSave").addEventListener("click", async () => {
  try {
    wireRules = await postJson("/api/wires/rules", wireRules);
    renderWireRules();
    logLine("Правила нумерации проводов сохранены.");
    if (wireRows.length > 0) await previewWires();
  } catch (err) {
    logLine("ОШИБКА сохранения правил: " + err.message);
  }
});

$("btnWireRulesReset").addEventListener("click", async () => {
  if (!confirm("Заменить правила нумерации проводов пресетом «Как 1260»?")) return;
  try {
    wireRules = await postJson("/api/wires/rules/reset", {});
    renderWireRules();
    logLine("Правила нумерации проводов сброшены к пресету «Как 1260».");
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  }
});

function describeWireEnds(ends) {
  return (ends || []).map((e) => `${e.Location}-${e.Device}:${e.Terminal}`).join("  ↔  ");
}

function describeWireCable(row) {
  const cores = (row.Cores || []).map((c) => "→ " + c.Core).join(", ");
  if (!row.Cable) return "";
  return cores ? `${row.Cable} ${cores}` : row.Cable;
}

function wireEffective(row) {
  const proposed = (row.ProposedName || "").trim();
  if (row.Apply && proposed) return proposed;
  return (row.CurrentName || "").includes(" / ") ? "" : (row.CurrentName || "");
}

function highlightWireDuplicates() {
  const groups = new Map();
  for (const row of wireRows) {
    const name = wireEffective(row).toUpperCase();
    if (!name) continue;
    if (!groups.has(name)) groups.set(name, []);
    groups.get(name).push(row);
  }
  const dup = new Set();
  for (const rows of groups.values()) {
    if (rows.length > 1 && !rows.every((r) => r.SharedName)) rows.forEach((r) => dup.add(r));
  }
  const trs = $("wiresTable").querySelectorAll("tbody tr");
  wireRows.forEach((row, i) => {
    trs[i].classList.toggle("conflict", (row.Apply && dup.has(row)) || row.Status === "Конфликт");
  });
}

function renderWireRows() {
  // 25.09.2026: строки «на схеме нет точки номера» отмечаются сами при «Ставить новые точки».
  if ($("chkPlacePoints").checked) wireRows.forEach((r) => { if (r.AutoWithPoints) r.Apply = true; });
  const tbody = $("wiresTable").querySelector("tbody");
  tbody.innerHTML = "";
  const counts = {};
  let cores = 0;
  for (const row of wireRows) {
    counts[row.Status] = (counts[row.Status] || 0) + 1;
    cores += (row.Cores || []).length;
    const tr = document.createElement("tr");
    if (row.Status === "Нет правила" && !(row.Cores || []).length) tr.classList.add("norule");
    const apply = document.createElement("input");
    apply.type = "checkbox";
    apply.checked = row.Apply;
    apply.disabled = row.Status === "Конфликт";
    apply.addEventListener("change", () => { row.Apply = apply.checked; highlightWireDuplicates(); });
    cell(tr, apply);
    cell(tr, String(row.Page + 1));
    cell(tr, describeWireEnds(row.Ends));
    cell(tr, row.Potential || "");
    cell(tr, describeWireCable(row));
    cell(tr, row.CurrentName || "(пусто)");
    cell(tr, row.RuleName || "—");
    cell(tr, textInput(row.ProposedName, (v) => {
      row.ProposedName = v;
      if (row.Status === "Нет правила" || row.Status === "Без изменений") {
        row.Apply = (v.trim() !== "" && v.trim() !== (row.CurrentName || "")) || (row.Cores || []).length > 0;
        apply.checked = row.Apply;
      }
      highlightWireDuplicates();
    }));
    const status = cell(tr, row.Status + (row.Note ? " — " + row.Note : ""));
    if (row.Note) status.classList.add("note");
    tbody.appendChild(tr);
  }
  $("wiresSummary").textContent = "Итого: " + Object.entries(counts).map(([k, v]) => `${k}: ${v}`).join(", ") + `; жил к назначению: ${cores}`;
  highlightWireDuplicates();
}

async function previewWires() {
  wireRows = await postJson("/api/wires/preview", {});
  renderWireRows();
}

$("btnWiresRead").addEventListener("click", async () => {
  const btn = $("btnWiresRead");
  btn.disabled = true;
  btn.textContent = "Читаю…";
  try {
    const result = await postJson("/api/wires/read", {});
    logLines(result.Log || []);
    wireRows = result.Rows || [];
    renderWireRows();
    $("btnWiresPreview").disabled = false;
    $("btnWiresApply").disabled = wireRows.length === 0;
    $("btnWiresExport").disabled = wireRows.length === 0;
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Считать провода";
  }
});

$("btnWiresPreview").addEventListener("click", async () => {
  try { await previewWires(); } catch (err) { logLine("ОШИБКА: " + err.message); }
});

$("btnWiresApply").addEventListener("click", async () => {
  const items = wireRows.filter((r) => r.Apply).map((r) => ({ NetId: r.NetId, NewName: (r.ProposedName || "").trim() }));
  if (items.length === 0) { logLine("Нет отмеченных строк."); return; }
  if ($("wiresTable").querySelector("tbody tr.conflict input[type=checkbox]:checked")) {
    logLine("Есть отмеченные строки с одинаковыми номерами (подсвечены красным) — исправьте перед записью.");
    return;
  }
  const cores = wireRows.filter((r) => r.Apply).reduce((n, r) => n + (r.Cores || []).length, 0);
  if (!confirm(`Записать номера в ${items.length} узл(ах) и назначить ${cores} жил(ы) в целевом проекте EPLAN? Рекомендуется делать на копии проекта.`)) return;
  const btn = $("btnWiresApply");
  btn.disabled = true;
  btn.textContent = "Записываю…";
  try {
    logLines(await postJson("/api/wires/apply", { Items: items, PlaceDefinitionPoints: $("chkPlacePoints").checked }));
    await previewWires();
  } catch (err) {
    logLine("ОШИБКА: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Записать отмеченные";
  }
});

// 24.09.2026: «Сохранить выгрузку…» — файл TSV последнего чтения скачивается браузером (папка «Загрузки»).
async function downloadExport(url, fileName) {
  try {
    const resp = await fetch(url);
    if (!resp.ok) throw await errorFromResponse(resp);
    const blob = await resp.blob();
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(link.href), 10000);
    logLine(`Выгрузка ${fileName} сохранена в папку загрузок браузера.`);
  } catch (err) {
    logLine("ОШИБКА выгрузки: " + err.message);
  }
}

$("btnCablesExport").addEventListener("click", () => downloadExport("/api/cables/export", "export-cables.tsv"));
$("btnWiresExport").addEventListener("click", () => downloadExport("/api/wires/export", "export-wires.tsv"));

// 25.09.2026: откат номеров — резервная копия backup-wires-*.tsv (пишется перед каждой записью в %APPDATA%\EplanCipMvp)
// или выгрузка export-wires.tsv. Файл читается в браузере и отправляется на сервер текстом.
$("btnWiresRestore").addEventListener("click", () => $("fileWiresRestore").click());
$("fileWiresRestore").addEventListener("change", async () => {
  const file = $("fileWiresRestore").files[0];
  $("fileWiresRestore").value = "";
  if (!file) return;
  if (!confirm(`Вернуть проводам номера из «${file.name}»? Провода, которых в файле нет, не меняются.`)) return;
  const btn = $("btnWiresRestore");
  btn.disabled = true;
  btn.textContent = "Восстанавливаю…";
  try {
    const text = await file.text();
    const resp = await fetch("/api/wires/restore", { method: "POST", body: text });
    if (!resp.ok) throw await errorFromResponse(resp);
    logLines(await resp.json());
    wireRows = [];
    renderWireRows();
    $("btnWiresApply").disabled = true;
  } catch (err) {
    logLine("ОШИБКА восстановления: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Восстановить номера из файла…";
  }
});

$("chkPlacePoints").addEventListener("change", () => {
  const on = $("chkPlacePoints").checked;
  wireRows.forEach((r) => { if (r.AutoWithPoints) r.Apply = on; });
  renderWireRows();
});
