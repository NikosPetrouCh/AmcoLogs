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
const stepIps = document.querySelector("#step-ips");
const stepDownload = document.querySelector("#step-download");
const ipBody = document.querySelector("#ip-body");
const dlBody = document.querySelector("#dl-body");
const prBody = document.querySelector("#pr-body");
const stepProcess = document.querySelector("#step-process");

const KNOWN_RESULTS = ["01", "02", "03", "04"];
const STEP_COPY = {
  1: {
    kicker: "Βήμα 1 από 4",
    title: "Εισαγωγή Excel",
    lede: "Άσε το Excel. Η εφαρμογή κρατά Transaction ID, RRN, Amount, TransactionDate και αν η συσκευή είναι κινητή (ξεκινά από 4) ή σταθερή (ξεκινά από 36)."
  },
  2: {
    kicker: "Βήμα 2 από 4",
    title: "Εύρεση IP",
    lede: "Οι αντιστοιχίες CTVM → IP → όχημα έρχονται από τα δύο JSON. Ό,τι λείπει το γράφεις χειροκίνητα και μένει στη βάση CTVM IPs."
  },
  3: {
    kicker: "Βήμα 3 από 4",
    title: "Logs",
    lede: "Κατέβασμα με scp -P 1030 amco@IP:/opt/amco/middleware/TVMserver/nlog-all-ΗΜΕΡΟΜΗΝΙΑ.log στο Desktop\\Nikos. Αν αποτύχει, άσε το log μόνος σου."
  },
  4: {
    kicker: "Βήμα 4 από 4",
    title: "Πόρισμα",
    lede: "Ψάχνει μόνο τη συναλλαγή του Excel: Selecting product + Transaction ID, μόνο κάρτα, και μαζεύει ERROR σε αυτό το παράθυρο."
  }
};

const DB_COPY = {
  errors: {
    kicker: "Database",
    title: "Error reports",
    lede: "Πορίσματα ανά Transaction ID: ERROR μέσα στο παράθυρο της συναλλαγής του Excel."
  },
  ips: {
    kicker: "Database",
    title: "CTVM IPs",
    lede: "Αντιστοιχίες από Ips_TVM360.json, Ips_Mobile.json και ό,τι πέρασες χειροκίνητα (CTVM, IP, όχημα)."
  },
  general: {
    kicker: "Database",
    title: "General",
    lede: "Τα στοιχεία της συναλλαγής από το Excel: Transaction ID, RRN, Amount, ημερομηνία, κινητή/σταθερή."
  }
};

let currentStep = 1;
let currentView = "flow";
let importedRows = [];
let selectedResult = null;
let fileName = "";
let devices = [];
let reachedIps = false;
let lastFindings = [];

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
  reachedIps = false;
  lastFindings = [];

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
    <p>${countLabel(rows.length, "συναλλαγή", "συναλλαγές")} από ${countLabel(devices.length, "συσκευή", "συσκευές")}. Αποθηκεύονται Transaction ID, RRN, Amount, ημερομηνία και τύπος συσκευής.</p>
    <div class="actions">
      <button type="button" class="primary" id="to-ips" onclick="goToStep(2)">Συνέχεια στις IPs</button>
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
  if (step === 3 && !reachedIps) {
    return;
  }
  if (step === 4 && !hasAnyLog()) {
    return;
  }
  if (step >= 2) {
    await mergeCachedIps();
    reachedIps = true;
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
    stepIps.hidden = true;
    stepDownload.hidden = true;
    stepProcess.hidden = true;
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
  stepIps.hidden = currentStep !== 2;
  stepDownload.hidden = currentStep !== 3;
  stepProcess.hidden = currentStep !== 4;
  renderPipeline();
  if (currentStep === 2) {
    renderIps();
  }
  if (currentStep === 3) {
    renderDownload();
  }
  if (currentStep === 4) {
    renderProcess();
  }
}

function renderPipeline() {
  document.querySelectorAll(".pipeline li").forEach((item) => {
    const step = Number(item.dataset.step);
    item.classList.toggle("active", currentView === "flow" && step === currentStep);
    item.classList.toggle("done", currentView === "flow" && step < currentStep);
    const button = item.querySelector(".pipeline-btn");
    const locked = (step === 2 && (!selectedResult || devices.length === 0))
      || (step === 3 && !reachedIps)
      || (step === 4 && !hasAnyLog());
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
  } else if (currentView === "general") {
    await renderExcelArchive();
  }
}

async function renderErrorReports() {
  const stored = lastFindings.length ? lastFindings : await fetchJson("/api/logs/findings");
  dbBody.innerHTML = `
    <h2>Error reports</h2>
    <p class="hint">${stored.length ? `${stored.length} πορίσματα.` : "Δεν υπάρχουν ακόμα πορίσματα. Τρέξε το βήμα 4 στο flow."}</p>
    ${stored.length ? renderFindings(stored) : ""}
  `;
}

function renderFindings(findings) {
  return findings.map((item) => `
    <details ${item.verdict === "LocatedWithErrors" ? "open" : ""}>
      <summary class="mono">${escapeHtml(verdictLabel(item.verdict))} · ${escapeHtml(item.transactionId || "—")} · ${escapeHtml(item.serviceAccountId || "")} · ${item.errorCount || 0} ERROR</summary>
      <p class="hint">${escapeHtml(item.product || "")} · ${escapeHtml(item.rrn || "")} · ${item.amount ?? ""} · ${formatDate(item.transactionDate)} · ${escapeHtml(item.logFileName || "χωρίς log")}${item.lineStart ? ` · γραμμές ${item.lineStart}–${item.lineEnd}` : ""}</p>
      ${item.errors?.length ? renderTable(
        ["Line", "Time", "Logger", "Message"],
        item.errors.map((error) => [error.lineNumber, error.time, error.logger, error.message])
      ) : `<p class="hint">${hintForVerdict(item.verdict)}</p>`}
    </details>
  `).join("");
}

function verdictLabel(value) {
  if (value === "LocatedWithErrors") return "ERROR στη συναλλαγή";
  if (value === "LocatedNoErrors") return "Εντοπίστηκε, χωρίς ERROR";
  if (value === "NotFoundInLog") return "Δεν βρέθηκε στο log";
  return "Χωρίς log";
}

function hintForVerdict(value) {
  if (value === "LocatedNoErrors") return "Η card συναλλαγή του Excel βρέθηκε. Δεν υπάρχουν ERROR σε αυτό το παράθυρο.";
  if (value === "NotFoundInLog") return "Το Transaction ID του Excel δεν βρέθηκε ως card πώληση σε αυτό το log.";
  return "Δεν υπάρχει nlog για αυτή τη συσκευή / ημέρα.";
}

function verdictClass(value) {
  if (value === "LocatedWithErrors") return "verdict-bad";
  if (value === "LocatedNoErrors") return "verdict-ok";
  return "verdict-miss";
}

async function renderCtvmIps() {
  const mappings = await fetchJson("/api/devices");
  dbBody.innerHTML = `
    <h2>CTVM IPs</h2>
    <p class="hint">Η βάση ενώνει τα δύο JSON με τις χειροκίνητες εγγραφές. Αν λείπει IP, CTVM ή όχημα από το αρχείο, πέρασέ το εδώ.</p>
    <form id="manual-ip-form" class="catalog-form" onsubmit="return saveManualCatalog(event)">
      <label>CTVM / ServiceAccountID <input name="serviceAccountId" placeholder="CTVM46240918" autocomplete="off" required></label>
      <label>IP <input name="ipAddress" placeholder="10.249.19.21" autocomplete="off" required></label>
      <label>Όχημα <input name="busNo" placeholder="π.χ. 2216" autocomplete="off"></label>
      <button type="submit">Αποθήκευση χειροκίνητα</button>
    </form>
    <p id="manual-ip-status" class="hint"></p>
    <p class="hint">${mappings.length ? `${mappings.length} αντιστοιχίες (JSON + χειροκίνητα).` : "Άδειος κατάλογος — βάλε τα JSON στο data/ ή πέρασε μία γραμμή παραπάνω."}</p>
    ${renderTable(
      ["CTVM", "IP", "Όχημα", "Τύπος", "Πηγή", "Updated"],
      mappings.map((item) => [
        item.serviceAccountId,
        item.ipAddress,
        item.busNo || "",
        item.mobility,
        item.source,
        formatDate(item.updatedAt)
      ])
    )}
  `;
}

async function saveManualCatalog(event) {
  event.preventDefault();
  const form = event.target;
  const data = new FormData(form);
  const payload = {
    serviceAccountId: String(data.get("serviceAccountId") || "").trim(),
    ipAddress: String(data.get("ipAddress") || "").trim(),
    busNo: String(data.get("busNo") || "").trim()
  };
  const status = document.querySelector("#manual-ip-status");
  const response = await fetch("/api/devices/ip", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) {
    if (status) {
      status.textContent = body.message || "Δεν αποθηκεύτηκε.";
    }
    return false;
  }
  const device = devices.find((item) => digitsOf(item.serviceAccountId) === digitsOf(payload.serviceAccountId));
  if (device) {
    device.ip = payload.ipAddress;
    device.busNo = payload.busNo || device.busNo;
    device.ipSource = "manual";
  }
  const note = body.persisted
    ? "Γράφτηκε στη MySQL (πηγή: manual)."
    : (body.message || "Κρατήθηκε στη συνεδρία. Συνδέσου στη MySQL για μόνιμη αποθήκευση.");
  form.reset();
  await renderCtvmIps();
  const after = document.querySelector("#manual-ip-status");
  if (after) {
    after.textContent = note;
  }
  return false;
}

async function renderExcelArchive() {
  const rows = await fetchJson("/api/imports/rows");
  dbBody.innerHTML = `
    <h2>General</h2>
    <p class="hint">${rows.length ? `${rows.length} συναλλαγές από Excel.` : "Δεν υπάρχει ακόμα αρχείο Excel."}</p>
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

function renderIps() {
  ipBody.innerHTML = `
    <h2>Συσκευές από κατηγορία ${escapeHtml(selectedResult)}</h2>
    <p class="hint">IP και όχημα από Ips_TVM360.json / Ips_Mobile.json. Αν λείπει κάτι, γράψε το — πηγαίνει στη βάση CTVM IPs ως χειροκίνητο.</p>
    <div class="device-list" id="ip-devices">
      ${renderDeviceRows()}
    </div>
    <pre id="ip-log" class="run-log"></pre>
    <div class="actions">
      <button type="button" onclick="goToStep(1)">Πίσω στο Excel</button>
      <button type="button" class="primary" id="ip-lookup" onclick="resolveIpsFromFiles()">Φόρτωση IP από αρχεία</button>
      <button type="button" class="primary" onclick="goToStep(3)">Συνέχεια στα logs</button>
    </div>
  `;
  resolveIpsFromFiles();
}

function renderDeviceRows() {
  return `
    <div class="device-row ip-row" style="color:var(--muted); font-size:11px; letter-spacing:.04em; text-transform:uppercase;">
      <span>CTVM</span><span>Τύπος</span><span>IP</span><span>Όχημα</span>
    </div>
    ${devices.map((device) => `
      <div class="device-row ip-row">
        <span class="mono">${escapeHtml(device.serviceAccountId)}</span>
        <span>${mobilityLabel(device.mobility)}</span>
        <input class="ip-edit" data-id="${escapeHtml(device.serviceAccountId)}" value="${escapeHtml(device.ip || "")}" placeholder="π.χ. 10.249.19.21" onchange="saveDeviceMapping(this)">
        <input class="ip-edit bus-edit" data-id="${escapeHtml(device.serviceAccountId)}" value="${escapeHtml(device.busNo || "")}" placeholder="π.χ. 2216" onchange="saveDeviceMapping(this)">
      </div>
    `).join("")}
  `;
}

async function resolveIpsFromFiles() {
  const button = document.querySelector("#ip-lookup");
  if (button) {
    button.disabled = true;
  }
  setIpLog("Διάβασμα JSON + χειροκίνητων εγγραφών από τη βάση…");
  const response = await fetch("/api/devices/resolve", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      serviceAccountIds: devices.map((device) => device.serviceAccountId)
    })
  });
  const payload = await response.json().catch(() => ({}));
  if (button) {
    button.disabled = false;
  }
  if (!response.ok) {
    setIpLog(payload.message || "Δεν διαβάστηκαν τα αρχεία IP.");
    return;
  }
  for (const hit of payload.hits || []) {
    const device = devices.find((item) => item.serviceAccountId === hit.serviceAccountId);
    if (!device) {
      continue;
    }
    device.ip = hit.ipAddress;
    device.busNo = hit.busNo || device.busNo;
    device.ipSource = hit.source;
  }
  const list = document.querySelector("#ip-devices");
  if (list) {
    list.innerHTML = renderDeviceRows();
  }
  const missing = payload.missing || [];
  setIpLog([
    `JSON κατάλογος: ${payload.catalogCount} συσκευές.`,
    `Βρέθηκαν (JSON ή χειροκίνητα): ${payload.matchedCount}.`,
    missing.length ? `Χωρίς IP — γράψε την χειροκίνητα: ${missing.join(", ")}` : "Όλες οι συσκευές του Excel έχουν IP."
  ].join("\n"));
}

function setIpLog(text) {
  const log = document.querySelector("#ip-log");
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
    <h2>Logs για κατηγορία ${escapeHtml(selectedResult)}</h2>
    <p class="hint">${ready} από ${devices.length} έχουν IP. Κατέβασμα: scp -P 1030 amco@IP:/opt/amco/middleware/TVMserver/nlog-all-ΗΜΕΡΟΜΗΝΙΑ.log στο Desktop\\Nikos\\ΣΥΣΚΕΥΗ. Αν αποτύχει, διάλεξε το nlog στη συσκευή.</p>
    <div class="split">
      <div>
        <h3>Σταθερά</h3>
        ${deviceMiniList(fixed)}
      </div>
      <div>
        <h3>Κινητά</h3>
        ${deviceMiniList(mobile)}
      </div>
    </div>
    ${other.length ? `<p class="hint">Άγνωστος τύπος: ${other.map((device) => device.serviceAccountId).join(", ")}</p>` : ""}
    <pre id="dl-log" class="run-log"></pre>
    <div class="actions">
      <button type="button" id="back-tb" onclick="goToStep(2)">Πίσω στις IPs</button>
      <button type="button" class="primary" id="start-download" onclick="startLogDownload()">Κατέβασμα με scp</button>
      <button type="button" class="primary" id="to-process" ${hasAnyLog() ? "" : "disabled"} onclick="goToStep(4)">Συνέχεια στο πόρισμα</button>
    </div>
  `;
}

function deviceMiniList(list) {
  if (!list.length) {
    return `<p class="hint">Καμία συσκευή σε αυτή την ομάδα.</p>`;
  }
  return `<div class="device-list">${list.map((device) => `
    <div class="device-row dl-row">
      <span class="mono">${escapeHtml(device.serviceAccountId)}</span>
      <span>${mobilityLabel(device.mobility)} · ${(device.logDates || []).join(", ") || "χωρίς ημερομηνία"}</span>
      <input class="ip-edit" data-id="${escapeHtml(device.serviceAccountId)}" value="${escapeHtml(device.ip || "")}" placeholder="IP" onchange="saveDeviceMapping(this)">
      <label class="log-drop">Drop / επιλογή nlog
        <input type="file" accept=".log,.zip,.txt" data-id="${escapeHtml(device.serviceAccountId)}" onchange="uploadManualLog(this)">
      </label>
      <span class="mono dl-status">${escapeHtml(device.downloadStatus || "—")}</span>
    </div>
  `).join("")}</div>`;
}

async function saveDeviceMapping(input) {
  const id = input.dataset.id;
  const device = devices.find((item) => item.serviceAccountId === id);
  if (!device) {
    return;
  }
  const row = input.closest(".device-row");
  const ip = (row?.querySelector(".ip-edit:not(.bus-edit)")?.value || device.ip || "").trim();
  const bus = (row?.querySelector(".bus-edit")?.value || device.busNo || "").trim();
  device.ip = ip || null;
  device.busNo = bus || null;
  if (!ip) {
    return;
  }
  const response = await fetch("/api/devices/ip", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      serviceAccountId: device.serviceAccountId,
      ipAddress: ip,
      busNo: bus,
      mobility: device.mobility
    })
  });
  if (!response.ok) {
    const payload = await response.json().catch(() => ({}));
    setIpLog(payload.message || "Invalid IP.");
    setDlLog(payload.message || "Invalid IP.");
  } else {
    device.ipSource = "manual";
  }
}

async function startLogDownload() {
  const target = devices.filter((device) => device.ip);
  if (!target.length) {
    setDlLog("Write at least one IP first.");
    return;
  }
  const button = document.querySelector("#start-download");
  if (button) {
    button.disabled = true;
  }
  setDlLog("Starting scp -P 1030…");
  const response = await fetch("/api/logs/download", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      devices: target.map((device) => ({
        serviceAccountId: device.serviceAccountId,
        mobility: device.mobility,
        ipAddress: device.ip,
        logDates: device.logDates || []
      }))
    })
  });
  if (!response.ok && response.status !== 202) {
    const payload = await response.json().catch(() => ({}));
    setDlLog(payload.message || "Could not start download.");
    if (button) {
      button.disabled = false;
    }
    return;
  }
  pollLogDownload();
}

function pollLogDownload() {
  if (window.__dlTimer) {
    clearInterval(window.__dlTimer);
  }
  const tick = async () => {
    try {
      const response = await fetch("/api/logs/status");
      if (!response.ok) {
        return;
      }
      const status = await response.json();
      applyDownloadStatus(status);
      if (!status.running) {
        clearInterval(window.__dlTimer);
        const button = document.querySelector("#start-download");
        if (button) {
          button.disabled = false;
        }
      }
    } catch {
      // Keep polling.
    }
  };
  tick();
  window.__dlTimer = setInterval(tick, 1500);
}

function applyDownloadStatus(status) {
  for (const result of status.results || []) {
    const device = devices.find((item) => item.serviceAccountId === result.serviceAccountId);
    if (!device) {
      continue;
    }
    device.downloadStatus = result.message || result.status;
    if (result.localPath) {
      device.localPath = result.localPath;
    }
  }
  document.querySelectorAll(".dl-row").forEach((row) => {
    const id = row.querySelector(".ip-edit")?.dataset.id;
    const device = devices.find((item) => item.serviceAccountId === id);
    const cell = row.querySelector(".dl-status");
    if (device && cell) {
      cell.textContent = device.downloadStatus || "—";
    }
  });
  setDlLog((status.log || []).join("\n"));
  if (!status.running) {
    renderPipeline();
    const next = document.querySelector("#to-process");
    if (next && hasAnyLog()) {
      next.disabled = false;
    }
  }
}

function setDlLog(text) {
  const log = document.querySelector("#dl-log");
  if (log) {
    log.textContent = text;
    log.scrollTop = log.scrollHeight;
  }
}

async function uploadManualLog(input) {
  const file = input.files?.[0];
  const id = input.dataset.id;
  if (!file || !id) {
    return;
  }
  const body = new FormData();
  body.append("file", file);
  body.append("serviceAccountId", id);
  setDlLog("Αποθήκευση " + file.name + " για " + id + "…");
  const response = await fetch("/api/logs/upload", { method: "POST", body });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    setDlLog(payload.message || "Το αρχείο δεν ανέβηκε.");
    return;
  }
  const device = devices.find((item) => item.serviceAccountId === id);
  if (device) {
    device.localPath = payload.localPath;
    device.downloadStatus = "χειροκίνητα · " + payload.fileName;
  }
  setDlLog("Το " + payload.fileName + " μπήκε χειροκίνητα για " + id + ".");
  renderDownload();
  renderPipeline();
}

function renderProcess() {
  const rows = rowsFor(selectedResult);
  const logs = devices.filter((device) => device.localPath);
  prBody.innerHTML = `
    <h2>Πόρισμα κατηγορίας ${escapeHtml(selectedResult)}</h2>
    <p class="hint">${countLabel(rows.length, "συναλλαγή", "συναλλαγές")} από το Excel · ${countLabel(logs.length, "log", "logs")}. Ψάχνει μόνο αυτά τα Transaction ID, μόνο κάρτα.</p>
    ${renderTable(
      ["ServiceAccountID", "TransactionID", "RRN", "Amount", "Ημερομηνία", "Τύπος"],
      rows.map((row) => [
        row.immediate.serviceAccountId || "",
        row.immediate.transactionId || "",
        row.immediate.rrn || "",
        row.immediate.amount ?? "",
        formatDate(row.immediate.transactionDate),
        mobilityLabel(row.mobility)
      ])
    )}
    <p class="hint" style="margin-top:16px;">Logs: ${logs.map((device) => device.serviceAccountId + " → " + (device.downloadStatus || device.localPath)).join(" · ") || "κανένα"}</p>
    <pre id="pr-log" class="run-log">${lastFindings.length ? "" : "Πάτα επεξεργασία για να βγει το πόρισμα."}</pre>
    <div id="pr-results">${lastFindings.length ? renderFindings(lastFindings) : ""}</div>
    <div class="actions">
      <button type="button" onclick="goToStep(3)">Πίσω στα logs</button>
      <button type="button" class="primary" id="run-process" onclick="runProcess()">Επεξεργασία και αποθήκευση</button>
    </div>
  `;
}

async function runProcess() {
  const button = document.querySelector("#run-process");
  const log = document.querySelector("#pr-log");
  if (button) {
    button.disabled = true;
  }
  if (log) {
    log.textContent = "Εντοπισμός συναλλαγών του Excel μέσα στα nlog…";
  }
  const response = await fetch("/api/logs/process", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      transactions: rowsFor(selectedResult).map((row) => ({
        transactionId: row.immediate.transactionId,
        rrn: row.immediate.rrn,
        amount: row.immediate.amount,
        transactionDate: row.immediate.transactionDate,
        serviceAccountId: row.immediate.serviceAccountId,
        mobility: row.mobility
      })),
      logs: devices.filter((device) => device.localPath).map((device) => ({
        serviceAccountId: device.serviceAccountId,
        path: device.localPath
      }))
    })
  });
  const payload = await response.json().catch(() => ({}));
  if (button) {
    button.disabled = false;
  }
  if (!response.ok) {
    if (log) {
      log.textContent = payload.message || "Η επεξεργασία απέτυχε.";
    }
    return;
  }
  lastFindings = payload.findings || [];
  if (log) {
    log.textContent = [
      `Συναλλαγές Excel: ${payload.subjectCount}`,
      `Αρχεία log: ${payload.logFileCount}`,
      `Εντοπίστηκαν: ${payload.locatedCount}`,
      `Με ERROR: ${payload.errorSaleCount}`,
      `Δεν βρέθηκαν στο log: ${payload.notFoundCount}`,
      `Χωρίς log: ${payload.missingLogCount}`,
      payload.persisted ? "Αποθηκεύτηκαν στη MySQL (Error reports, CTVM IPs, General)." : (payload.persistMessage || "")
    ].filter(Boolean).join("\n");
  }
  const results = document.querySelector("#pr-results");
  if (results) {
    results.innerHTML = renderFindings(lastFindings);
  }
}

function refreshDevices() {
  const previous = new Map(devices.map((device) => [device.serviceAccountId, device]));
  const map = new Map();
  for (const row of rowsFor(selectedResult)) {
    const id = row.immediate.serviceAccountId;
    if (!id) {
      continue;
    }
    const prior = previous.get(id);
    const current = map.get(id) || {
      serviceAccountId: id,
      mobility: row.mobility,
      transactions: 0,
      ip: prior?.ip || null,
      busNo: prior?.busNo || null,
      localPath: prior?.localPath || null,
      downloadStatus: prior?.downloadStatus || null,
      logDates: []
    };
    current.transactions += 1;
    const day = dayFromTransaction(row.immediate.transactionDate);
    if (day && !current.logDates.includes(day)) {
      current.logDates.push(day);
    }
    map.set(id, current);
  }
  devices = [...map.values()];
}

function hasAnyLog() {
  return devices.some((device) => device.localPath);
}

async function mergeCachedIps() {
  try {
    const response = await fetch("/api/devices");
    if (!response.ok) {
      return;
    }
    const mappings = await response.json();
    const byDigits = new Map(mappings.map((item) => [digitsOf(item.serviceAccountId), item]));
    devices = devices.map((device) => {
      const cached = byDigits.get(digitsOf(device.serviceAccountId));
      if (!cached) {
        return device;
      }
      return {
        ...device,
        ip: cached.ipAddress || device.ip,
        busNo: cached.busNo || device.busNo,
        ipSource: cached.source || device.ipSource
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

function dayFromTransaction(value) {
  if (!value) {
    return null;
  }
  return String(value).slice(0, 10);
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

function uniqueIds(values) {
  return [...new Set((values || []).filter(Boolean).map((id) => String(id).trim()).filter(Boolean))];
}

function digitsOf(value) {
  return String(value || "").replace(/\D/g, "");
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
