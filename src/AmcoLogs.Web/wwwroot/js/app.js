const dropzone = document.querySelector("#dropzone");
const fileInput = document.querySelector("#file-input");
const browse = document.querySelector("#browse");
const statusLine = document.querySelector("#status-line");
const summary = document.querySelector("#summary");
const categories = document.querySelector("#categories");
const selection = document.querySelector("#selection");
const dbForm = document.querySelector("#db-form");
const dbStatus = document.querySelector("#db-status");
const dbDot = document.querySelector("#db-dot");
const dbView = document.querySelector("#db-view");
const dbBody = document.querySelector("#db-body");
const kicker = document.querySelector("#kicker");
const stageTitle = document.querySelector("#stage-title");
const stageLede = document.querySelector("#stage-lede");
const stepExcel = document.querySelector("#step-excel");
const stepThingsboard = document.querySelector("#step-thingsboard");
const stepDownload = document.querySelector("#step-download");
const tbBody = document.querySelector("#tb-body");
const dlBody = document.querySelector("#dl-body");

const KNOWN_RESULTS = ["01", "02", "03", "04"];
const STEP_COPY = {
  1: {
    kicker: "Βήμα 1 από 3",
    title: "Εισαγωγή Excel",
    lede: "Άσε το Excel. Διάλεξε κατηγορία 01–04 και συνέχισε με τις ίδιες συσκευές."
  },
  2: {
    kicker: "Βήμα 2 από 3",
    title: "ThingsBoard",
    lede: "Ανοίγει browser στο laptop. Κάνε login όπως πάντα. Η εφαρμογή πάει κατευθείαν στα δύο dashboards: ServiceAccountID → BUS no → WAN IP."
  },
  3: {
    kicker: "Βήμα 3 από 3",
    title: "Κατέβασμα logs",
    lede: "Σταθερά (36…) κατεβαίνουν απευθείας. Κινητά (4…) χρειάζονται ping και zip."
  }
};

const DB_COPY = {
  errors: {
    kicker: "Database",
    title: "Error reports",
    lede: "Imported transactions grouped by EticketTransactionResult."
  },
  ips: {
    kicker: "Database",
    title: "CTVM IPs",
    lede: "Cached ServiceAccountID to IP mappings from ThingsBoard."
  },
  excel: {
    kicker: "Database",
    title: "Excel archive",
    lede: "Every imported Excel field, including extra columns."
  }
};

let currentStep = 1;
let currentView = "flow";
let importedRows = [];
let selectedResult = null;
let fileName = "";
let devices = [];
let reachedThingsboard = false;

browse.addEventListener("click", () => fileInput.click());
fileInput.addEventListener("change", () => {
  if (fileInput.files[0]) {
    uploadExcel(fileInput.files[0]);
  }
});

["dragenter", "dragover"].forEach((eventName) => {
  dropzone.addEventListener(eventName, (event) => {
    event.preventDefault();
    dropzone.classList.add("hover");
  });
});

["dragleave", "drop"].forEach((eventName) => {
  dropzone.addEventListener(eventName, (event) => {
    event.preventDefault();
    dropzone.classList.remove("hover");
  });
});

dropzone.addEventListener("drop", (event) => {
  const file = event.dataTransfer.files[0];
  if (file) {
    uploadExcel(file);
  }
});

categories.addEventListener("click", (event) => {
  const card = event.target.closest(".result-card");
  if (!card || card.disabled) {
    return;
  }
  selectedResult = card.dataset.result;
  refreshDevices();
  renderCategories();
  renderSelection();
  renderPipeline();
});

document.querySelector(".pipeline").addEventListener("click", (event) => {
  const button = event.target.closest(".pipeline-btn");
  if (!button || button.disabled) {
    return;
  }
  currentView = "flow";
  goToStep(Number(button.dataset.step));
});

document.querySelector(".db-nav").addEventListener("click", (event) => {
  const button = event.target.closest(".db-link");
  if (!button) {
    return;
  }
  openDatabase(button.dataset.db);
});

dbForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const data = Object.fromEntries(new FormData(dbForm).entries());
  setDbStatus("pending", "Σύνδεση...");
  const response = await fetch("/api/database/connect", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      host: data.host,
      port: Number(data.port),
      user: data.user,
      password: data.password,
      database: data.database
    })
  });
  const payload = await response.json();
  renderDbStatus(payload);
  if (currentView !== "flow") {
    renderDatabase();
  }
});

async function uploadExcel(file) {
  setStatus("Διάβασμα " + file.name + "...");
  const body = new FormData();
  body.append("file", file);
  const response = await fetch("/api/imports/excel?persist=true", {
    method: "POST",
    body
  });
  const payload = await response.json();
  if (!response.ok) {
    setStatus(payload.message || "Αποτυχία εισαγωγής.", true);
    return;
  }
  renderImport(payload);
}

function renderImport(payload) {
  const importData = payload.import;
  importedRows = importData.rows || [];
  fileName = importData.fileName;
  selectedResult = firstPopulatedResult();
  refreshDevices();
  dropzone.classList.add("compact");
  currentStep = 1;
  currentView = "flow";
  reachedThingsboard = false;

  const savedNote = payload.saved
    ? `Αποθηκεύτηκαν ${payload.saved.rowCount} γραμμές στη MySQL.`
    : (payload.persistMessage || "Το αρχείο διαβάστηκε.");
  const warnings = (importData.warnings || []).join(" ");
  setStatus(savedNote + (warnings ? " " + warnings : ""));

  summary.hidden = false;
  summary.innerHTML = `
    <h2>${escapeHtml(fileName)}</h2>
    <p>${countLabel(importedRows.length, "συναλλαγή", "συναλλαγές")} · διάλεξε κατηγορία για να περάσει στα επόμενα βήματα</p>
  `;

  renderCategories();
  renderSelection();
  renderApp();
}

function renderCategories() {
  const groups = groupByResult(importedRows);
  const extra = [...groups.keys()]
    .filter((code) => !KNOWN_RESULTS.includes(code) && code !== "—")
    .sort();
  const codes = [...KNOWN_RESULTS, ...extra];

  categories.hidden = false;
  categories.innerHTML = codes.map((code) => {
    const rows = groups.get(code) || [];
    const empty = rows.length === 0;
    const selected = selectedResult === code;
    return `
      <button type="button" class="result-card${selected ? " selected" : ""}" data-result="${escapeHtml(code)}" ${empty ? "disabled" : ""}>
        <div class="result-code">${escapeHtml(code)}</div>
        <p class="result-meta">${countLabel(rows.length, "συναλλαγή", "συναλλαγές")} · ${countLabel(countDevices(rows), "συσκευή", "συσκευές")}</p>
      </button>
    `;
  }).join("");
}

function renderSelection() {
  if (!selectedResult) {
    selection.hidden = true;
    return;
  }

  const rows = rowsFor(selectedResult);
  selection.hidden = false;
  selection.innerHTML = `
    <h2>Κατηγορία ${escapeHtml(selectedResult)}</h2>
    <p>${countLabel(rows.length, "συναλλαγή", "συναλλαγές")} από ${countLabel(devices.length, "συσκευή", "συσκευές")} θα περάσουν στο ThingsBoard.</p>
    <div class="actions">
      <button type="button" class="primary" id="to-thingsboard" onclick="goToStep(2)">Συνέχεια στο ThingsBoard</button>
    </div>
    <details>
      <summary>Προβολή γραμμών</summary>
      ${renderTable(
        ["ServiceAccountID", "RRN", "Amount", "TransactionID", "Ημερομηνία"],
        rows.map((row) => [
          row.immediate.serviceAccountId || "",
          row.immediate.rrn || "",
          row.immediate.amount ?? "",
          row.immediate.transactionId || "",
          formatDate(row.immediate.transactionDate)
        ])
      )}
    </details>
  `;
}

async function goToStep(step) {
  if (step >= 2 && (!selectedResult || devices.length === 0)) {
    return;
  }
  if (step === 3 && !reachedThingsboard) {
    return;
  }
  if (step >= 2) {
    await mergeCachedIps();
    reachedThingsboard = true;
  }
  currentStep = step;
  currentView = "flow";
  renderApp();
}

function renderApp() {
  document.querySelectorAll(".db-link").forEach((link) => {
    link.classList.toggle("active", currentView === link.dataset.db);
  });

  if (currentView !== "flow") {
    const copy = DB_COPY[currentView];
    kicker.textContent = copy.kicker;
    stageTitle.textContent = copy.title;
    stageLede.textContent = copy.lede;
    stepExcel.hidden = true;
    stepThingsboard.hidden = true;
    stepDownload.hidden = true;
    dbView.hidden = false;
    renderPipeline();
    renderDatabase();
    return;
  }

  const copy = STEP_COPY[currentStep];
  kicker.textContent = copy.kicker;
  stageTitle.textContent = copy.title;
  stageLede.textContent = copy.lede;
  dbView.hidden = true;
  stepExcel.hidden = currentStep !== 1;
  stepThingsboard.hidden = currentStep !== 2;
  stepDownload.hidden = currentStep !== 3;
  renderPipeline();
  if (currentStep === 2) {
    renderThingsboard();
  }
  if (currentStep === 3) {
    renderDownload();
  }
}

function renderPipeline() {
  document.querySelectorAll(".pipeline li").forEach((item) => {
    const step = Number(item.dataset.step);
    item.classList.toggle("active", currentView === "flow" && step === currentStep);
    item.classList.toggle("done", currentView === "flow" && step < currentStep);
    const button = item.querySelector(".pipeline-btn");
    const locked = (step === 2 && (!selectedResult || devices.length === 0))
      || (step === 3 && !reachedThingsboard);
    button.disabled = locked;
  });
}

async function openDatabase(section) {
  currentView = section;
  renderApp();
}

async function renderDatabase() {
  if (currentView === "errors") {
    await renderErrorReports();
  } else if (currentView === "ips") {
    await renderCtvmIps();
  } else if (currentView === "excel") {
    await renderExcelArchive();
  }
}

async function renderErrorReports() {
  const rows = await fetchJson("/api/imports/rows");
  const groups = new Map();
  for (const row of rows) {
    const code = normalizeResult(row.eticketTransactionResult);
    const current = groups.get(code) || [];
    current.push(row);
    groups.set(code, current);
  }
  const codes = [...KNOWN_RESULTS, ...[...groups.keys()].filter((code) => !KNOWN_RESULTS.includes(code)).sort()];
  dbBody.innerHTML = `
    <h2>Error reports</h2>
    <p class="hint">${rows.length ? `${rows.length} stored transactions.` : "No stored Excel data yet. Connect MySQL and import a file."}</p>
    ${codes.map((code) => {
      const items = groups.get(code) || [];
      return `
        <details ${items.length ? "" : "hidden"}>
          <summary>${escapeHtml(code)} · ${items.length}</summary>
          ${renderTable(
            ["File", "ServiceAccountID", "RRN", "Amount", "TransactionID", "Date"],
            items.map((row) => [
              row.fileName,
              row.serviceAccountId || "",
              row.rrn || "",
              row.amount ?? "",
              row.transactionId || "",
              formatDate(row.transactionDate)
            ])
          )}
        </details>
      `;
    }).join("")}
  `;
}

async function renderCtvmIps() {
  const mappings = await fetchJson("/api/devices");
  dbBody.innerHTML = `
    <h2>CTVM IPs</h2>
    <p class="hint">${mappings.length ? `${mappings.length} cached mappings.` : "No IPs stored yet. They will appear after ThingsBoard lookups."}</p>
    ${renderTable(
      ["ServiceAccountID", "IP", "Type", "Source", "Updated"],
      mappings.map((item) => [
        item.serviceAccountId,
        item.ipAddress,
        item.mobility,
        item.source,
        formatDate(item.updatedAt)
      ])
    )}
  `;
}

async function renderExcelArchive() {
  const rows = await fetchJson("/api/imports/rows");
  dbBody.innerHTML = `
    <h2>Excel archive</h2>
    <p class="hint">${rows.length ? `${rows.length} rows with all imported fields.` : "No Excel archive yet."}</p>
    ${renderTable(
      ["File", "ServiceAccountID", "RRN", "Amount", "EticketTransactionResult", "TransactionID", "TransactionDate", "Type", "Extra"],
      rows.map((row) => [
        row.fileName,
        row.serviceAccountId || "",
        row.rrn || "",
        row.amount ?? "",
        row.eticketTransactionResult || "",
        row.transactionId || "",
        formatDate(row.transactionDate),
        row.mobility,
        row.extraJson || ""
      ])
    )}
  `;
}

async function fetchJson(url) {
  try {
    const response = await fetch(url);
    if (!response.ok) {
      return [];
    }
    return await response.json();
  } catch {
    return [];
  }
}

function renderThingsboard() {
  tbBody.innerHTML = `
    <h2>Συσκευές από κατηγορία ${escapeHtml(selectedResult)}</h2>
    <p class="hint">Δεν μπαίνει στο Home ούτε στη λίστα Dashboards. Ανοίγει το dashboard συσκευών, βρίσκει BUS no, μετά το dashboard οχημάτων για WAN IP. Αν εμφανιστεί login, μπες όπως στο εταιρικό laptop.</p>
    <div class="device-list" id="tb-devices">
      ${renderDeviceRows()}
    </div>
    <pre id="tb-log" class="tb-log"></pre>
    <div class="actions">
      <button type="button" onclick="goToStep(1)">Πίσω στο Excel</button>
      <button type="button" class="primary" id="tb-lookup" onclick="startThingsBoardLookup()">Αναζήτηση IP</button>
      <button type="button" class="primary" onclick="goToStep(3)">Συνέχεια στο κατέβασμα</button>
    </div>
  `;
}

function renderDeviceRows() {
  return `
    <div class="device-row" style="color:var(--muted); font-size:11px; letter-spacing:.04em; text-transform:uppercase;">
      <span>ServiceAccountID</span><span>Type</span><span>IP / BUS</span>
    </div>
    ${devices.map((device) => `
      <div class="device-row">
        <span class="mono">${escapeHtml(device.serviceAccountId)}</span>
        <span>${mobilityLabel(device.mobility)}</span>
        <span class="mono">${escapeHtml(device.ip || "pending")}${device.busNo ? " · " + escapeHtml(device.busNo) : ""}</span>
      </div>
    `).join("")}
  `;
}

async function startThingsBoardLookup() {
  const pending = devices.filter((device) => !device.ip);
  const target = pending.length ? pending : devices;
  const button = document.querySelector("#tb-lookup");
  if (button) {
    button.disabled = true;
  }
  setTbLog("Opening ThingsBoard. Log in in the Chrome window if asked.");
  const response = await fetch("/api/thingsboard/lookup", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      devices: target.map((device) => ({
        serviceAccountId: device.serviceAccountId,
        mobility: device.mobility
      }))
    })
  });
  if (!response.ok && response.status !== 202) {
    const payload = await response.json().catch(() => ({}));
    setTbLog(payload.message || "Could not start lookup.");
    if (button) {
      button.disabled = false;
    }
    return;
  }
  pollThingsBoard();
}

async function pollThingsBoard() {
  if (window.__tbTimer) {
    clearInterval(window.__tbTimer);
  }

  const tick = async () => {
    try {
      const response = await fetch("/api/thingsboard/status");
      if (!response.ok) {
        return;
      }
      const status = await response.json();
      applyThingsBoardStatus(status);
      if (!status.running) {
        clearInterval(window.__tbTimer);
        const button = document.querySelector("#tb-lookup");
        if (button) {
          button.disabled = false;
        }
      }
    } catch {
      // Keep polling until the lookup process finishes.
    }
  };

  await tick();
  window.__tbTimer = setInterval(tick, 1500);
}

function applyThingsBoardStatus(status) {
  for (const result of status.results || []) {
    const device = devices.find((item) => item.serviceAccountId === result.serviceAccountId);
    if (!device) {
      continue;
    }
    if (result.ipAddress) {
      device.ip = result.ipAddress;
    }
    if (result.busNo) {
      device.busNo = result.busNo;
    }
  }
  const list = document.querySelector("#tb-devices");
  if (list) {
    list.innerHTML = renderDeviceRows();
  }
  setTbLog((status.log || []).join("\n"));
}

function setTbLog(text) {
  const log = document.querySelector("#tb-log");
  if (log) {
    log.textContent = text;
    log.scrollTop = log.scrollHeight;
  }
}

function renderDownload() {
  const fixed = devices.filter((device) => device.mobility === "Fixed");
  const mobile = devices.filter((device) => device.mobility === "Mobile");
  const other = devices.filter((device) => device.mobility !== "Fixed" && device.mobility !== "Mobile");
  const ready = devices.filter((device) => device.ip).length;

  dlBody.innerHTML = `
    <h2>Κατέβασμα για κατηγορία ${escapeHtml(selectedResult)}</h2>
    <p class="hint">${ready} από ${devices.length} συσκευές έχουν IP. Το πραγματικό WinSCP / ping θα μπει αφού δέσουμε το ThingsBoard.</p>
    <div class="split">
      <div>
        <h3>Σταθερά · WinSCP</h3>
        ${deviceMiniList(fixed)}
      </div>
      <div>
        <h3>Κινητά · ping + zip</h3>
        ${deviceMiniList(mobile)}
      </div>
    </div>
    ${other.length ? `<p class="hint">Άγνωστος τύπος: ${other.map((device) => device.serviceAccountId).join(", ")}</p>` : ""}
    <div class="actions">
      <button type="button" id="back-tb" onclick="goToStep(2)">Πίσω στο ThingsBoard</button>
      <button type="button" class="primary" id="start-download" ${ready ? "" : "disabled"}>Κατέβασμα logs</button>
    </div>
  `;
  dlBody.querySelector("#start-download").addEventListener("click", () => {
    setStatus("Το κατέβασμα θα ενεργοποιηθεί όταν υπάρχουν IP από το ThingsBoard.", true);
  });
}

function deviceMiniList(list) {
  if (!list.length) {
    return `<p class="hint">Καμία συσκευή σε αυτή την ομάδα.</p>`;
  }
  return `<div class="device-list">${list.map((device) => `
    <div class="device-row">
      <span class="mono">${escapeHtml(device.serviceAccountId)}</span>
      <span>${device.transactions}</span>
      <span class="mono">${escapeHtml(device.ip || "χωρίς IP")}</span>
    </div>
  `).join("")}</div>`;
}

function refreshDevices() {
  const map = new Map();
  for (const row of rowsFor(selectedResult)) {
    const id = row.immediate.serviceAccountId;
    if (!id) {
      continue;
    }
    const current = map.get(id) || {
      serviceAccountId: id,
      mobility: row.mobility,
      transactions: 0,
      ip: null
    };
    current.transactions += 1;
    map.set(id, current);
  }
  devices = [...map.values()];
}

async function mergeCachedIps() {
  try {
    const response = await fetch("/api/devices");
    if (!response.ok) {
      return;
    }
    const mappings = await response.json();
    const byId = new Map(mappings.map((item) => [item.serviceAccountId, item]));
    devices = devices.map((device) => {
      const cached = byId.get(device.serviceAccountId);
      if (!cached) {
        return device;
      }
      return {
        ...device,
        ip: cached.ipAddress || device.ip,
        busNo: cached.busNo || device.busNo
      };
    });
  } catch {
    // Cache is optional until MySQL is connected.
  }
}

function groupByResult(rows) {
  const map = new Map();
  for (const row of rows) {
    const code = normalizeResult(row.immediate?.eticketTransactionResult);
    const current = map.get(code) || [];
    current.push(row);
    map.set(code, current);
  }
  return map;
}

function firstPopulatedResult() {
  const groups = groupByResult(importedRows);
  return KNOWN_RESULTS.find((code) => (groups.get(code) || []).length > 0)
    || [...groups.keys()].find((code) => (groups.get(code) || []).length > 0)
    || null;
}

function rowsFor(code) {
  return groupByResult(importedRows).get(code) || [];
}

function countDevices(rows) {
  return new Set(rows.map((row) => row.immediate.serviceAccountId).filter(Boolean)).size;
}

function countLabel(count, singular, plural) {
  return `${count} ${count === 1 ? singular : plural}`;
}

function mobilityLabel(value) {
  if (value === "Fixed") return "Σταθερό";
  if (value === "Mobile") return "Κινητό";
  return "Άγνωστο";
}

function normalizeResult(value) {
  if (value == null || String(value).trim() === "") {
    return "—";
  }
  const text = String(value).trim();
  if (/^\d+$/.test(text)) {
    return text.padStart(2, "0");
  }
  return text;
}

function renderTable(headers, rows) {
  return `
    <div class="table-wrap">
      <table>
        <thead><tr>${headers.map((header) => `<th>${escapeHtml(header)}</th>`).join("")}</tr></thead>
        <tbody>
          ${rows.map((cells) => `
            <tr>${cells.map((cell, index) => `<td class="${index === 0 ? "mono" : ""}">${escapeHtml(String(cell))}</td>`).join("")}</tr>
          `).join("")}
        </tbody>
      </table>
    </div>
  `;
}

function formatDate(value) {
  if (!value) return "";
  return new Date(value).toLocaleString("el-GR");
}

function setStatus(text, isError = false) {
  statusLine.hidden = false;
  statusLine.textContent = text;
  statusLine.classList.toggle("error", isError);
}

function setDbStatus(kind, text) {
  dbStatus.className = "db-status " + kind;
  dbStatus.textContent = text;
}

function renderDbStatus(status) {
  if (status.connected) {
    setDbStatus("ok", `Connected · ${status.database} @ ${status.host} · MySQL ${status.serverVersion}`);
    dbDot.textContent = `MySQL · ${status.database}`;
  } else {
    setDbStatus("bad", status.message || "Not connected.");
    dbDot.textContent = "MySQL offline";
  }
}

async function loadStatus() {
  const response = await fetch("/api/database/status");
  renderDbStatus(await response.json());
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

loadStatus();
renderPipeline();
