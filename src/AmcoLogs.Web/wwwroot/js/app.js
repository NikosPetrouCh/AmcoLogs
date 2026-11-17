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
dbBody.addEventListener("click", (event) => {
  const day = event.target.closest("[data-general-day]");
  if (day) {
    generalDay = day.dataset.generalDay;
    generalBatchId = null;
    renderExcelArchive();
    return;
  }
  const batch = event.target.closest("[data-general-batch]");
  if (batch) {
    generalBatchId = batch.dataset.generalBatch;
    renderExcelArchive();
    return;
  }
  const back = event.target.closest("[data-general-back]");
  if (back) {
    if (back.dataset.generalBack === "days") {
      generalDay = null;
    }
    generalBatchId = null;
    renderExcelArchive();
    return;
  }
  const resume = event.target.closest("[data-resume-batch]");
  if (resume) {
    resumeBatch(resume.dataset.resumeBatch);
    return;
  }
  const up = event.target.closest("[data-desk-up]");
  if (up) {
    if (currentView === "general" && generalMode) {
      generalMode = null;
    } else if (currentView === "errors" && errorView) {
      errorView = null;
    } else if (deskOperator === "OASA" && deskNetwork) {
      deskNetwork = null;
      generalMode = null;
      errorView = null;
    } else {
      deskOperator = null;
      deskNetwork = null;
      generalMode = null;
      errorView = null;
    }
    renderDatabase();
    return;
  }
  const operator = event.target.closest("[data-desk-operator]");
  if (operator) {
    deskOperator = operator.dataset.deskOperator;
    deskNetwork = null;
    generalMode = null;
    errorView = null;
    renderDatabase();
    return;
  }
  const network = event.target.closest("[data-desk-network]");
  if (network) {
    deskNetwork = network.dataset.deskNetwork;
    generalMode = null;
    errorView = null;
    renderDatabase();
    return;
  }
  const mode = event.target.closest("[data-general-mode]");
  if (mode) {
    generalMode = mode.dataset.generalMode;
    renderExcelArchive();
    return;
  }
  const errorMode = event.target.closest("[data-error-view]");
  if (errorMode) {
    errorView = errorMode.dataset.errorView;
    renderErrorReports();
    return;
  }
  const openCase = event.target.closest("[data-desk-resume]");
  if (openCase) {
    resumeDeskCase(openCase.dataset.deskResume);
  }
});
dbBody.addEventListener("change", (event) => {
  const target = event.target;
  if (target.dataset.caseDate) {
    patchDeskCase(target.dataset.caseDate, { hasFileDate: true, fileDate: target.value || null });
  } else if (target.dataset.caseFault) {
    patchDeskCase(target.dataset.caseFault, { hasFaultType: true, faultType: target.value });
  } else if (target.dataset.caseConclusion) {
    patchDeskCase(target.dataset.caseConclusion, { hasConclusion: true, conclusion: target.value });
  } else if (target.dataset.lineLogs) {
    patchDeskLine(target.dataset.lineLogs, { logsDownloaded: target.checked });
  } else if (target.dataset.lineFault) {
    patchDeskLine(target.dataset.lineFault, { hasFaultType: true, faultType: target.value });
  } else if (target.dataset.lineConclusion) {
    patchDeskLine(target.dataset.lineConclusion, { hasConclusion: true, conclusion: target.value });
  }
});
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
const deviceEntry = document.querySelector("#device-entry");
const deviceEntryError = document.querySelector("#device-entry-error");
const deviceNetwork = document.querySelector("#device-network");
const deviceOperator = deviceEntry.querySelector("[name=operator]");
deviceOperator.addEventListener("change", syncEntryNetwork);
syncEntryNetwork();
deviceEntry.querySelector("#device-search-mode").addEventListener("change", syncDeviceSearch);
deviceEntry.querySelector("#device-log-service").addEventListener("change", syncDeviceSearch);
syncDeviceSearch();
document.querySelector("#entry-choice").addEventListener("click", (event) => {
  const card = event.target.closest("[data-entry]");
  if (card) {
    showEntryKind(card.dataset.entry);
  }
});
document.querySelector("#entry-back").addEventListener("click", () => showEntryKind(null));

function showEntryKind(kind) {
  const choice = document.querySelector("#entry-choice");
  const work = document.querySelector("#entry-work");
  choice.hidden = Boolean(kind);
  work.hidden = !kind;
  document.querySelector("#entry-device").hidden = kind !== "device";
  document.querySelector("#entry-file").hidden = kind !== "exceptions";
}

const STEP_COPY = {
  1: {
    kicker: "Βήμα 1 από 4",
    title: "Εισαγωγή",
    lede: ""
  },
  2: {
    kicker: "Βήμα 2 από 4",
    title: "Εύρεση IP",
    lede: "Οι αντιστοιχίες CTVM → IP → όχημα έρχονται από τα δύο JSON. Ό,τι λείπει το γράφεις χειροκίνητα και μένει στη βάση CTVM IPs."
  },
  3: {
    kicker: "Βήμα 3 από 4",
    title: "Logs",
    lede: "Κατέβασμα μόνο για συσκευές που απάντησαν στο ping. Σταθερά έρχεται το nlog. Κινητά κάνουν sudo zip και κατεβαίνει το zip, στο Desktop\\Amco files\\κωδικός συσκευής."
  },
  4: {
    kicker: "Βήμα 4 από 4",
    title: "Πόρισμα",
    lede: "Εδώ κλείνει το αρχείο: YES ή NO. Αυτόματο YES είναι ο τύπος 03 και ο τύπος 02 με ποσό 0,02."
  }
};

const DB_COPY = {
  errors: {
    kicker: "Database",
    title: "Error reports",
    lede: "Κάθε καταχώρηση είναι μία συσκευή, ένας τύπος σφάλματος και η ημερομηνία της. Φαίνεται ανά συσκευή ή ανά τύπο."
  },
  ips: {
    kicker: "Database",
    title: "CTVM IPs",
    lede: "Αντιστοιχίες από Ips_TVM360.json, Ips_Mobile.json και ό,τι πέρασες χειροκίνητα (CTVM, IP, όχημα)."
  },
  general: {
    kicker: "Database",
    title: "General",
    lede: "Μπαίνεις στο OSETH ή στον OASA. Μέσα στον OASA είναι το TRAM και το METRO. Το Excel είναι τα Daily Exceptions. Το τραμ έχει μόνο Devices."
  }
};

let currentStep = 1;
let currentView = "flow";
let importedRows = [];
let fileName = "";
let importProfile = "OSETH";
let entryOperator = null;
let entryNetwork = null;
let deskOperator = null;
let deskNetwork = null;
let generalMode = null;
let errorView = null;
let activeCaseId = null;
let activeCaseLines = [];
let devices = [];
let reachedIps = false;
let lastFindings = [];
let generalDay = null;
let generalBatchId = null;

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

deviceEntry.addEventListener("submit", (event) => {
  event.preventDefault();
  submitDeviceEntry(new FormData(deviceEntry));
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
  importProfile = importData.profile || "OSETH";
  fileName = importData.fileName;
  refreshDevices();
  dropzone.classList.add("compact");
  currentStep = 1;
  currentView = "flow";
  reachedIps = false;
  lastFindings = [];

  const savedNote = payload.saved
    ? `Στο General: ${payload.saved.title}. ${payload.saved.yesCount} YES, ${payload.saved.pendingCount} εκκρεμεί. ${payload.persistMessage || ""}`
    : (payload.persistMessage || "Το αρχείο διαβάστηκε.");
  const warnings = (importData.warnings || []).join(" ");
  setStatus(savedNote + (warnings ? " " + warnings : ""));

  summary.hidden = false;
  const profileLabel = importProfile === "OASA" ? "ΟΑΣΑ" : "OSETH";
  summary.innerHTML = `
    <h2>${escapeHtml(fileName)}</h2>
    <p>${profileLabel} · ${countLabel(importedRows.length, "γραμμή", "γραμμές")}.${importProfile === "OASA" ? " Το CTVM και η IP βγαίνουν από τον κωδικό τερματικού." : " Η απόφαση YES ή NO φαίνεται στο πόρισμα."}</p>
  `;

  if (importProfile === "OASA") {
    renderOasaScreen();
  } else {
    renderScreen();
  }
  renderApp();
}

async function submitDeviceEntry(data) {
  const ctvm = String(data.get("ctvm") || "").trim().toUpperCase();
  const logDate = String(data.get("logDate") || "").trim();
  const operator = String(data.get("operator") || "OSETH");
  const network = operator === "OASA" ? String(data.get("network") || "TRAM") : null;
  const faultType = String(data.get("faultType") || "").trim();
  const searchMode = String(data.get("searchMode") || "transaction");
  const logService = String(data.get("logService") || "middleware");
  const clockTime = String(data.get("clockTime") || "").trim();
  const rangeFrom = String(data.get("rangeFrom") || "").trim();
  const rangeUntil = String(data.get("rangeUntil") || "").trim();
  const grepText = String(data.get("grepText") || "").trim();
  if (!/^CTVM\d{8}$/.test(ctvm)) {
    setDeviceError("Το CTVM πρέπει να είναι CTVM και 8 ψηφία.");
    return;
  }
  if (searchMode === "transaction" && logService === "middleware" && !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(clockTime)) {
    setDeviceError("Η ώρα της συναλλαγής γράφεται 08:23.");
    return;
  }
  if (searchMode === "transaction" && !logDate) {
    setDeviceError("Χρειάζεται η ημερομηνία του log.");
    return;
  }
  if (searchMode === "range" && (!rangeFrom || !rangeUntil || rangeUntil < rangeFrom)) {
    setDeviceError("Το εύρος χρειάζεται από και έως, με το έως μετά το από.");
    return;
  }
  if (searchMode === "range" && daysBetween(rangeFrom, rangeUntil).length > 31) {
    setDeviceError("Το εύρος είναι πάνω από 31 ημέρες.");
    return;
  }
  if (searchMode === "range" && !grepText) {
    setDeviceError("Στο εύρος ημερομηνιών γράψε το log που θα γίνει grep.");
    return;
  }
  setDeviceError("");
  const lookedUp = await fetch("/api/devices/resolve", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ serviceAccountIds: [ctvm] })
  }).then((response) => response.ok ? response.json() : { hits: [] }).catch(() => ({ hits: [] }));
  const hit = (lookedUp.hits || [])[0] || {};
  entryOperator = operator;
  entryNetwork = network;
  importProfile = "DEVICE";
  fileName = ctvm;
  importedRows = [{
    rowNumber: 1,
    mobility: classifyMobility(ctvm),
    disposition: "NeedsIp",
    decision: null,
    screenReason: "Καταχώρηση μίας συσκευής.",
    oasaTerminal: hit.terminal || null,
    stationName: hit.station || null,
    logService,
    operator,
    clockTime: searchMode === "transaction" && logService === "middleware" ? clockTime : null,
    searchMode,
    rangeFrom: searchMode === "range" ? rangeFrom : null,
    rangeUntil: searchMode === "range" ? rangeUntil : null,
    grepText: searchMode === "range" ? grepText : null,
    immediate: {
      serviceAccountId: ctvm,
      transactionDate: searchMode === "range" ? rangeFrom : logDate,
      amount: null,
      eticketTransactionResult: null,
      rrn: null,
      transactionId: null
    }
  }];
  refreshDevices();
  dropzone.classList.remove("compact");
  currentStep = 1;
  currentView = "flow";
  reachedIps = false;
  lastFindings = [];
  const filed = await postDeskCase({
    operator,
    network,
    origin: "device",
    fileName: ctvm,
    fileDate: logDate || null,
    faultType: faultType || null,
    serviceAccountId: ctvm,
    lines: [{
      rowNumber: 1,
      serviceAccountId: ctvm,
      faultType: faultType || null,
      terminalCode: hit.terminal || null,
      stationName: hit.station || null,
      transactionDate: logDate ? `${logDate}T00:00:00` : null
    }]
  });
  const place = operator === "OASA" ? `OASA · ${network}` : "OSETH";
  const stationNote = hit.station ? ` Σταθμός: ${hit.station}.` : "";
  setStatus(`${ctvm}.${stationNote} ${filed && filed.error ? filed.error : `Μπήκε στις συσκευές του ${place}. Η IP είναι στο επόμενο βήμα.`}`);
  summary.hidden = false;
  summary.innerHTML = `
    <h2>${escapeHtml(ctvm)}</h2>
    <p>Μία συσκευή.${hit.station ? ` Σταθμός ${escapeHtml(hit.station)}.` : ""}${hit.terminal ? ` Τερματικό ${escapeHtml(hit.terminal)}.` : ""} Η IP συμπληρώνεται στο επόμενο βήμα.${logDate ? ` Ημερομηνία log: ${escapeHtml(logDate)}.` : " Χωρίς ημερομηνία log."}</p>
  `;
  renderDeviceScreen();
  renderApp();
}

function setDeviceError(text) {
  deviceEntryError.hidden = !text;
  deviceEntryError.textContent = text;
}

function classifyMobility(id) {
  const digits = String(id || "").replace(/\D/g, "");
  if (digits.startsWith("36")) return "Fixed";
  if (digits.startsWith("4")) return "Mobile";
  return "Unknown";
}

function renderDeviceScreen() {
  const device = devices[0];
  categories.hidden = false;
  categories.innerHTML = `
    <div class="result-card ip">
      <div class="result-code word">${escapeHtml(device?.serviceAccountId || "")}</div>
      <p class="result-meta">${escapeHtml(device?.stationName || device?.station || "χωρίς σταθμό")} · ${mobilityLabel(device?.mobility)}</p>
    </div>
  `;
  selection.hidden = false;
  selection.innerHTML = `
    <h2>Καταχωρήθηκε</h2>
    <p>Η IP δεν μπαίνει εδώ. Στο επόμενο βήμα τη φέρνει η βάση ή τη γράφεις εσύ, και μετά γίνονται ping και logs όπως σε κάθε συσκευή.</p>
    <div class="actions">
      <button type="button" class="primary" id="to-ips" onclick="goToStep(2)">Συνέχεια στις IPs</button>
    </div>
  `;
}

function renderOasaScreen() {
  const ready = importedRows.filter((row) => row.disposition === "NeedsIp" && row.resolvedIp);
  const needsIp = importedRows.filter((row) => row.disposition === "NeedsIp" && !row.resolvedIp);
  const unmatched = importedRows.filter((row) => row.disposition !== "NeedsIp");
  categories.hidden = false;
  categories.innerHTML = `
    <div class="result-card yes">
      <div class="result-code">${ready.length}</div>
      <p class="result-meta">με CTVM και IP</p>
    </div>
    <div class="result-card ip">
      <div class="result-code">${needsIp.length}</div>
      <p class="result-meta">με CTVM, χωρίς IP</p>
    </div>
    <div class="result-card out">
      <div class="result-code">${unmatched.length}</div>
      <p class="result-meta">χωρίς αντιστοιχία</p>
    </div>
  `;
  selection.hidden = false;
  selection.innerHTML = `
    <h2>ΟΑΣΑ</h2>
    <p>Ο κωδικός τερματικού ψάχνει στο Ips_Oasa.json το CTVM, την IP, τον αριθμό τερματικού και το όνομα σταθμού. Το ping και τα logs είναι τα ίδια με τον OSETH.</p>
    ${importedRows.length ? `<details open><summary>Γραμμές · ${importedRows.length}</summary>${oasaTable(importedRows)}</details>` : ""}
    <div class="actions">
      <button type="button" class="primary" id="to-ips" onclick="goToStep(${devices.length ? 2 : 4})" ${importedRows.length ? "" : "disabled"}>${devices.length ? "Συνέχεια στις IPs" : "Συνέχεια στο πόρισμα"}</button>
      <button type="button" onclick="fileOasaCase('METRO')">Καταχώρηση Daily Exceptions · METRO</button>
    </div>
  `;
}

function oasaTable(rows) {
  return renderTable(
    ["Τερματικό", "CTVM", "IP", "Ημερομηνία", "Ποσό", "Κωδ. κατάστασης", "Λόγος"],
    rows.map((row) => [
      row.oasaTerminal || "",
      row.immediate?.serviceAccountId || "",
      row.resolvedIp || "",
      row.immediate?.transactionDate ? String(row.immediate.transactionDate).slice(0, 16).replace("T", " ") : "",
      row.immediate?.amount ?? "",
      row.statusCode || "",
      row.screenReason || ""
    ])
  );
}

function renderScreen() {
  const yesType03 = rowsByDisposition("AutomaticYes").filter((row) => resultCode(row) === "03");
  const yesTwoCents = rowsByDisposition("AutomaticYes").filter((row) => resultCode(row) !== "03");
  const pending = needsIpRows();
  const excludedId = rowsByDisposition("ExcludedInvalidServiceAccount");
  const excludedType = rowsByDisposition("ExcludedType04");
  const other = rowsByDisposition("Unclassified");
  const excluded = excludedId.length + excludedType.length;
  const continueLabel = pending.length ? "Συνέχεια στις IPs" : "Συνέχεια στο πόρισμα";
  const continueStep = pending.length ? 2 : 4;

  categories.hidden = false;
  categories.innerHTML = `
    <div class="result-card yes">
      <div class="result-code">03</div>
      <p class="result-meta">${countLabel(yesType03.length, "συναλλαγή", "συναλλαγές")} · YES</p>
    </div>
    <div class="result-card yes">
      <div class="result-code">0,02</div>
      <p class="result-meta">${countLabel(yesTwoCents.length, "συναλλαγή", "συναλλαγές")} · τύπος 02, YES</p>
    </div>
    <div class="result-card ip">
      <div class="result-code">02</div>
      <p class="result-meta">${countLabel(pending.length, "συναλλαγή", "συναλλαγές")} · μένουν για εύρεση IP</p>
    </div>
    <div class="result-card out">
      <div class="result-code word">ΕΚΤΟΣ</div>
      <p class="result-meta">${countLabel(excluded, "συναλλαγή", "συναλλαγές")} · άκυρο CTVM ή τύπος 04</p>
    </div>
  `;

  selection.hidden = false;
  selection.innerHTML = `
    <h2>YES</h2>
    <p>Φαίνονται εδώ και κλείνουν ως YES στο πόρισμα. Δεν πάνε για IP.</p>
    <details>
      <summary>Τύπος 03 · ${yesType03.length}</summary>
      ${yesType03.length ? screenTable(yesType03, true) : "<p>Καμία γραμμή.</p>"}
    </details>
    <details>
      <summary>Τύπος 02 με ποσό 0,02 · ${yesTwoCents.length}</summary>
      ${yesTwoCents.length ? screenTable(yesTwoCents, true) : "<p>Καμία γραμμή.</p>"}
    </details>
    <h2 class="screen-next">Τύπος 02 για IP</h2>
    <p>${countLabel(pending.length, "συναλλαγή", "συναλλαγές")} από ${countLabel(devices.length, "συσκευή", "συσκευές")}. Ποσό διαφορετικό από 0,02. Η απόφασή τους βγαίνει στο πόρισμα.</p>
    ${pending.length ? `<details><summary>Προβολή γραμμών</summary>${screenTable(pending, false)}</details>` : ""}
    <div class="actions">
      <button type="button" class="primary" id="to-ips" onclick="goToStep(${continueStep})" ${importedRows.length ? "" : "disabled"}>${continueLabel}</button>
    </div>
    <details>
      <summary>Εξαιρέσεις · ${excluded}</summary>
      <p>Άκυρο ServiceAccountID (όχι CTVM και 8 ψηφία): ${excludedId.length}. Τύπος 04: ${excludedType.length}.</p>
      ${excluded ? screenTable([...excludedId, ...excludedType], false) : ""}
    </details>
    ${other.length ? `
      <details>
        <summary>Χωρίς κανόνα · ${other.length}</summary>
        <p>Τύπος 01 και ό,τι άλλο δεν είναι 02, 03 ή 04 μένει εδώ. Δεν γίνεται YES και δεν πάει σε IP.</p>
        ${screenTable(other, false)}
      </details>` : ""}
  `;
}

function screenTable(rows, showDecision) {
  const headers = ["ServiceAccountID", "Τύπος", "Amount", "Λόγος", "TransactionID"];
  if (showDecision) {
    headers.push("Απόφαση");
  }
  return renderTable(
    headers,
    rows.map((row) => {
      const cells = [
        row.immediate.serviceAccountId || "",
        row.immediate.eticketTransactionResult || "",
        row.immediate.amount ?? "",
        row.screenReason || "",
        row.immediate.transactionId || ""
      ];
      if (showDecision) {
        cells.push(row.decision || "");
      }
      return cells;
    })
  );
}

async function goToStep(step) {
  if ((step === 2 || step === 3) && devices.length === 0) {
    return;
  }
  if (step === 3 && !reachedIps) {
    return;
  }
  if (step === 4 && importedRows.length === 0) {
    return;
  }
  if (step === 2 || step === 3) {
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
    stageLede.hidden = !copy.lede;
    stepExcel.hidden = true;
    stepIps.hidden = true;
    stepDownload.hidden = true;
    stepProcess.hidden = true;
    dbView.hidden = false;
    renderPipeline();
    renderDatabase();
    return;
  }

  const copy = stageCopy();
  kicker.textContent = copy.kicker;
  stageTitle.textContent = copy.title;
  stageLede.textContent = copy.lede;
  stageLede.hidden = !copy.lede;
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

function stageCopy() {
  if (currentStep !== 1) {
    return STEP_COPY[currentStep];
  }
  if (importProfile === "OASA" && importedRows.length) {
    return {
      ...STEP_COPY[1],
      title: "Αρχείο ΟΑΣΑ",
      lede: ""
    };
  }
  if (importProfile === "DEVICE" && importedRows.length) {
    return {
      ...STEP_COPY[1],
      title: "Μία συσκευή",
      lede: ""
    };
  }
  return STEP_COPY[1];
}

function renderPipeline() {
  document.querySelectorAll(".pipeline li").forEach((item) => {
    const step = Number(item.dataset.step);
    item.classList.toggle("active", currentView === "flow" && step === currentStep);
    item.classList.toggle("done", currentView === "flow" && step < currentStep);
    const button = item.querySelector(".pipeline-btn");
    const locked = (step === 2 && devices.length === 0)
      || (step === 3 && !reachedIps)
      || (step === 4 && importedRows.length === 0);
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

const ERROR_KINDS = [
  ["restart", "Restart"],
  ["printer", "Not Operational Printer"],
  ["database_error", "Database error"],
  ["customer_mistake", "Mistake of a customer"],
  ["card_reader", "Card reader"],
  ["network", "Network"],
  ["power", "Power"],
  ["amount_mismatch", "Amount mismatch"],
  ["other", "Other"]
];

function errorKindLabel(code) {
  return ERROR_KINDS.find(([kind]) => kind === code)?.[1] || code || "";
}

function registrationsLabel(count) {
  return count === 1 ? "1 καταχώρηση" : `${count} καταχωρήσεις`;
}

function todayInput() {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${now.getFullYear()}-${month}-${day}`;
}

function isOasaFlow() {
  return importProfile === "OASA" || (importProfile === "DEVICE" && entryOperator === "OASA");
}

function scopePlace() {
  if (deskOperator === "OSETH") {
    return "OSETH";
  }
  return deskNetwork ? `OASA / ${deskNetwork}` : "OASA";
}

function folderCard(attr, title, meta) {
  return `<button type="button" class="result-card" ${attr}><strong>${escapeHtml(title)}</strong><p class="result-meta">${escapeHtml(meta)}</p></button>`;
}

function deskCrumb() {
  const parts = [];
  if (deskOperator) {
    parts.push(deskOperator);
  }
  if (deskOperator === "OASA" && deskNetwork) {
    parts.push(deskNetwork);
  }
  if (currentView === "general" && generalMode === "exceptions") {
    parts.push("Daily Exceptions");
  }
  if (currentView === "general" && generalMode === "devices") {
    parts.push("Devices");
  }
  if (currentView === "errors" && errorView === "device") {
    parts.push("Κατά συσκευή");
  }
  if (currentView === "errors" && errorView === "kind") {
    parts.push("Κατά τύπο σφάλματος");
  }
  return parts.join(" / ");
}

function deskAtContent() {
  if (!deskOperator) {
    return false;
  }
  if (deskOperator === "OASA" && !deskNetwork) {
    return false;
  }
  if (currentView === "general" && generalMode !== "exceptions" && generalMode !== "devices") {
    return false;
  }
  if (currentView === "general" && generalMode === "exceptions" && deskNetwork === "TRAM") {
    return false;
  }
  if (currentView === "errors" && errorView !== "device" && errorView !== "kind") {
    return false;
  }
  return true;
}

function networkCards() {
  const tram = folderCard('data-desk-network="TRAM"', "TRAM", currentView === "general" ? "Devices" : "Οι συσκευές");
  const metro = folderCard('data-desk-network="METRO"', "METRO", currentView === "general" ? "Daily Exceptions και Devices" : "Οι συσκευές");
  return currentView === "general" ? `${metro}${tram}` : `${tram}${metro}`;
}

function renderDeskFolders() {
  if (!deskOperator) {
    return `
      <div class="categories">
        ${folderCard('data-desk-operator="OSETH"', "OSETH", currentView === "general" ? "Daily Exceptions και Devices" : "Οι συσκευές")}
        ${folderCard('data-desk-operator="OASA"', "OASA", "TRAM και METRO")}
      </div>`;
  }
  const back = `<button type="button" class="desk-up" data-desk-up="1">Πίσω</button>`;
  if (deskOperator === "OASA" && !deskNetwork) {
    return `
      ${back}
      <p class="scope-current">OASA</p>
      <div class="categories">
        ${networkCards()}
      </div>`;
  }
  if (currentView === "general" && !generalMode) {
    const choices = deskNetwork === "TRAM"
      ? folderCard('data-general-mode="devices"', "Devices", "Κάθε συσκευή χωριστά")
      : `
        ${folderCard('data-general-mode="exceptions"', "Daily Exceptions", "Αρχείο Excel")}
        ${folderCard('data-general-mode="devices"', "Devices", "Κάθε συσκευή χωριστά")}
      `;
    return `
      ${back}
      <p class="scope-current">${escapeHtml(deskCrumb())}</p>
      <div class="categories">${choices}</div>`;
  }
  if (currentView === "errors" && !errorView) {
    return `
      ${back}
      <p class="scope-current">${escapeHtml(deskCrumb())}</p>
      <div class="categories">
        ${folderCard('data-error-view="device"', "Κατά συσκευή", "Κάθε καταχώρηση πάνω στη συσκευή")}
        ${folderCard('data-error-view="kind"', "Κατά τύπο σφάλματος", "Οι συσκευές με το ίδιο πρόβλημα")}
      </div>`;
  }
  return `
    ${back}
    <p class="scope-current">${escapeHtml(deskCrumb())}</p>`;
}

function syncEntryNetwork() {
  if (deviceNetwork) {
    deviceNetwork.hidden = deviceOperator.value !== "OASA";
  }
}

function syncDeviceSearch() {
  const mode = deviceEntry.querySelector("#device-search-mode").value;
  const service = deviceEntry.querySelector("#device-log-service").value;
  const range = mode === "range";
  const clock = mode === "transaction" && service === "middleware";
  deviceEntry.querySelector("#device-log-date").hidden = range;
  deviceEntry.querySelector("#device-clock").hidden = !clock;
  deviceEntry.querySelector("#device-range-from").hidden = !range;
  deviceEntry.querySelector("#device-range-until").hidden = !range;
  deviceEntry.querySelector("#device-grep").hidden = !range;
}


function deskQuery() {
  const network = deskOperator === "OASA" ? `&network=${deskNetwork}` : "";
  return `operator=${deskOperator}${network}`;
}

function deskScopeBody(extra) {
  return {
    operator: deskOperator,
    network: deskOperator === "OASA" ? deskNetwork : null,
    ...extra
  };
}

async function deskFetch(url, options) {
  try {
    const response = await fetch(url, options);
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      return { error: payload.message || "Η MySQL δεν είναι συνδεδεμένη.", payload };
    }
    return { error: null, payload };
  } catch {
    return { error: "Η MySQL δεν απάντησε.", payload: null };
  }
}

async function postDeskCase(body) {
  const result = await deskFetch("/api/desk/cases", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  if (!result.error && result.payload?.id) {
    activeCaseId = result.payload.id;
  }
  return result;
}

async function fileOasaCase(network) {
  if (network !== "METRO") {
    setStatus("Το τραμ δεν παίρνει Excel. Το αρχείο ΟΑΣΑ μπαίνει στα Daily Exceptions του METRO.");
    return;
  }
  deskOperator = "OASA";
  deskNetwork = network;
  generalMode = "exceptions";
  const lines = importedRows
    .filter((row) => row.disposition === "NeedsIp" || row.disposition === "AutomaticYes")
    .map((row) => ({
      rowNumber: row.rowNumber,
      serviceAccountId: row.immediate?.serviceAccountId,
      resultCode: row.statusCode || row.immediate?.eticketTransactionResult,
      decision: row.decision,
      transactionId: row.immediate?.transactionId,
      transactionDate: row.immediate?.transactionDate,
      amount: row.immediate?.amount,
      faultType: row.statusCode || null,
      terminalCode: row.oasaTerminal || null,
      stationName: row.stationName || null
    }));
  const result = await postDeskCase({
    operator: "OASA",
    network,
    origin: "excel",
    fileName,
    lines
  });
  setStatus(result.error || `Το αρχείο μπήκε στο General ΟΑΣΑ ${network}.`);
}

async function patchDeskCase(id, fields) {
  await deskFetch(`/api/desk/cases/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(deskScopeBody(fields))
  });
}

async function patchDeskLine(id, fields) {
  await deskFetch(`/api/desk/lines/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(deskScopeBody(fields))
  });
}

function noteDeskLogs(serviceAccountId) {
  activeCaseLines
    .filter((line) => line.serviceAccountId === serviceAccountId && !line.logsDownloaded)
    .forEach((line) => {
      line.logsDownloaded = true;
      patchDeskLine(line.id, { logsDownloaded: true, hasInvestigationStatus: true, investigationStatus: "logs" });
    });
}

async function resumeDeskCase(caseId) {
  const result = await deskFetch(`/api/desk/lines?${deskQuery()}&caseId=${caseId}`);
  const lines = Array.isArray(result.payload) ? result.payload : [];
  if (!lines.length) {
    return;
  }
  activeCaseId = caseId;
  activeCaseLines = lines;
  fileName = lines[0].fileName || lines[0].serviceAccountId || "";
  importProfile = lines[0].origin === "device" ? "DEVICE" : deskOperator;
  importedRows = lines.map((line) => ({
    rowNumber: line.rowNumber,
    mobility: classifyMobility(line.serviceAccountId),
    disposition: line.decision === "YES" ? "AutomaticYes" : "NeedsIp",
    decision: line.decision === "YES" || line.decision === "NO" ? line.decision : null,
    screenReason: line.faultType || "Από το General.",
    operator: deskOperator === "OASA" ? "OASA" : "OSETH",
    immediate: {
      serviceAccountId: line.serviceAccountId,
      amount: line.amount,
      eticketTransactionResult: line.resultCode,
      transactionId: line.transactionId,
      transactionDate: line.transactionDate,
      rrn: null
    }
  }));
  currentView = "flow";
  reachedIps = false;
  lastFindings = [];
  refreshDevices();
  if (devices.length) {
    await goToStep(2);
    return;
  }
  currentStep = 4;
  renderApp();
}

async function renderErrorReports() {
  if (!deskAtContent()) {
    dbBody.innerHTML = `<h2>Error reports</h2>${renderDeskFolders()}`;
    return;
  }
  const result = await deskFetch(`/api/desk/errors?${deskQuery()}`);
  const rows = Array.isArray(result.payload) ? result.payload : [];
  const hint = result.error || (errorView === "kind"
    ? "Κάθε γραμμή είναι μία συσκευή με αυτό το σφάλμα, και η ημερομηνία της καταχώρησης. Η ίδια συσκευή ξαναφαίνεται αν το σφάλμα καταχωρήθηκε δεύτερη φορά."
    : "Κάθε γραμμή είναι μία καταχώρηση. Δύο γραμμές στην ίδια συσκευή σημαίνουν δύο καταχωρήσεις σφάλματος.");
  dbBody.innerHTML = `
    <h2>Error reports</h2>
    ${renderDeskFolders()}
    <p class="hint">${escapeHtml(hint)}</p>
    <form class="catalog-form" onsubmit="return addErrorEntry(event)">
      <label>CTVM <input name="serviceAccountId" placeholder="CTVM36246554" required></label>
      <label>Τύπος σφάλματος
        <select name="errorKind">
          ${ERROR_KINDS.map(([code, label]) => `<option value="${code}">${escapeHtml(label)}</option>`).join("")}
        </select>
      </label>
      <label>Ημερομηνία <input name="occurredOn" type="date" value="${todayInput()}" required></label>
      <button type="submit">Καταχώρηση</button>
    </form>
    <p id="error-entry-status" class="hint"></p>
    ${result.error ? "" : (rows.length ? (errorView === "kind" ? renderErrorsByKind(rows) : renderErrorsByDevice(rows)) : `<p class="hint">Καμία καταχώρηση ακόμη μέσα στο ${scopePlace()}.</p>`)}
  `;
}

async function addErrorEntry(event) {
  event.preventDefault();
  const data = new FormData(event.target);
  const id = String(data.get("serviceAccountId") || "").trim().toUpperCase();
  const errorKind = String(data.get("errorKind") || "");
  const occurredOn = String(data.get("occurredOn") || "");
  const status = document.querySelector("#error-entry-status");
  if (!/^CTVM\d{8}$/.test(id)) {
    if (status) {
      status.textContent = "Το CTVM πρέπει να είναι CTVM και 8 ψηφία.";
    }
    return false;
  }
  const result = await deskFetch("/api/desk/errors", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(deskScopeBody({ serviceAccountId: id, errorKind, occurredOn }))
  });
  if (result.error) {
    if (status) {
      status.textContent = result.error;
    }
    return false;
  }
  renderErrorReports();
  return false;
}

function renderErrorsByDevice(rows) {
  const groups = new Map();
  rows.forEach((row) => {
    const id = row.serviceAccountId || "—";
    const group = groups.get(id) || [];
    group.push(row);
    groups.set(id, group);
  });
  return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([id, entries]) => `
    <details open>
      <summary class="mono">${escapeHtml(id)} · ${escapeHtml(registrationsLabel(entries.length))}</summary>
      <div class="table-wrap desk-table">
        <table>
          <thead><tr><th>Ημερομηνία</th><th>Τύπος σφάλματος</th></tr></thead>
          <tbody>
            ${entries.map((entry) => `
              <tr>
                <td>${escapeHtml(formatFileDay(entry.occurredOn))}</td>
                <td>${escapeHtml(errorKindLabel(entry.errorKind))}</td>
              </tr>`).join("")}
          </tbody>
        </table>
      </div>
    </details>
  `).join("");
}

function renderErrorsByKind(rows) {
  return ERROR_KINDS.map(([code, label]) => {
    const entries = rows.filter((row) => row.errorKind === code);
    if (!entries.length) {
      return "";
    }
    return `
      <details open>
        <summary>${escapeHtml(label)} · ${escapeHtml(registrationsLabel(entries.length))}</summary>
        <div class="table-wrap desk-table">
          <table>
            <thead><tr><th>CTVM</th><th>Ημερομηνία</th></tr></thead>
            <tbody>
              ${entries.map((entry) => `
                <tr>
                  <td class="mono">${escapeHtml(entry.serviceAccountId || "")}</td>
                  <td>${escapeHtml(formatFileDay(entry.occurredOn))}</td>
                </tr>`).join("")}
            </tbody>
          </table>
        </div>
      </details>`;
  }).join("");
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
  if (!deskAtContent()) {
    dbBody.innerHTML = `<h2>CTVM IPs</h2>${renderDeskFolders()}`;
    return;
  }
  const result = await deskFetch(`/api/desk/ips?${deskQuery()}`);
  const mappings = Array.isArray(result.payload) ? result.payload : [];
  dbBody.innerHTML = `
    <h2>CTVM IPs</h2>
    ${renderDeskFolders()}
    <p class="hint">${result.error || `Κάθε συσκευή του ${scopePlace()} με τη δική της IP. Η χειροκίνητη γραμμή γράφεται και στη ροή της διερεύνησης.`}</p>
    <form id="manual-ip-form" class="catalog-form" onsubmit="return saveManualCatalog(event)">
      <label>CTVM / ServiceAccountID <input name="serviceAccountId" placeholder="CTVM46240918" autocomplete="off" required></label>
      <label>IP <input name="ipAddress" placeholder="10.249.19.21" autocomplete="off" required></label>
      ${deskOperator === "OASA"
        ? `<label>Τερματικό <input name="terminalCode" placeholder="αριθμός" autocomplete="off"></label>
           <label>Σταθμός <input name="stationName" placeholder="όνομα σταθμού" autocomplete="off"></label>`
        : `<label>Όχημα <input name="busNo" placeholder="π.χ. 2216" autocomplete="off"></label>`}
      <button type="submit">Αποθήκευση χειροκίνητα</button>
    </form>
    <p id="manual-ip-status" class="hint"></p>
    <p class="hint">${mappings.length ? `${mappings.length} συσκευές μέσα στο ${scopePlace()}.` : `Καμία συσκευή ακόμη μέσα στο ${scopePlace()}.`}</p>
    ${renderTable(
      deskOperator === "OASA" ? ["CTVM", "IP", "Τερματικό", "Σταθμός", "Τύπος", "Πηγή", "Updated"] : ["CTVM", "IP", "Όχημα", "Τύπος", "Πηγή", "Updated"],
      mappings.map((item) => deskOperator === "OASA" ? [
        item.serviceAccountId,
        item.ipAddress,
        item.terminalCode || "",
        item.stationName || "",
        item.mobility,
        item.source,
        formatDate(item.updatedAt)
      ] : [
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
    busNo: String(data.get("busNo") || "").trim(),
    terminalCode: String(data.get("terminalCode") || "").trim(),
    stationName: String(data.get("stationName") || "").trim()
  };
  const status = document.querySelector("#manual-ip-status");
  const response = await fetch("/api/devices/ip", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  await deskFetch("/api/desk/ips", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(deskScopeBody({
      serviceAccountId: payload.serviceAccountId,
      ipAddress: payload.ipAddress,
      busNo: payload.busNo,
      terminalCode: payload.terminalCode,
      stationName: payload.stationName,
      source: "manual"
    }))
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
  if (!deskAtContent()) {
    dbBody.innerHTML = `<h2>General</h2>${renderDeskFolders()}`;
    return;
  }
  const casesResult = generalMode === "exceptions" ? await deskFetch(`/api/desk/cases?${deskQuery()}`) : { payload: [], error: null };
  const cases = Array.isArray(casesResult.payload) ? casesResult.payload : [];
  const linesResult = generalMode === "devices" ? await deskFetch(`/api/desk/lines?${deskQuery()}`) : { payload: [], error: null };
  const lines = Array.isArray(linesResult.payload) ? linesResult.payload : [];
  const legacy = deskOperator === "OSETH" && generalMode === "exceptions" ? await fetchJson("/api/imports") : [];
  dbBody.innerHTML = `
    <h2>General</h2>
    ${renderDeskFolders()}
    <p class="hint">${casesResult.error || linesResult.error || (generalMode === "exceptions"
      ? "Τα Excel που τάισαν αυτές τις συσκευές. Η ημερομηνία αλλάζει αν διαβάστηκε λάθος. Το 02 και το 03 μετριούνται χωριστά. YES είναι επιστροφή, NO είναι μη επιστροφή."
      : "Κάθε CTVM μένει χωριστά, με το δικό του ιστορικό, είτε ήρθε από Excel είτε καταχωρήθηκε μόνη της.")}</p>
    ${generalMode === "exceptions" ? renderDeskFiles(cases) : renderDeskDevices(lines)}
    ${deskOperator === "OSETH" && generalMode === "exceptions" ? renderLegacyFiles(legacy) : ""}
  `;
}

function renderDeskFiles(cases) {
  if (!cases.length) {
    return `<p class="hint">Δεν έχει γίνει ακόμη drop μέσα στο ${scopePlace()}.</p>`;
  }
  return `
    <div class="table-wrap desk-table">
      <table>
        <thead>
          <tr>
            <th>Ημερομηνία</th><th>Αρχείο / συσκευή</th><th>02</th><th>03</th><th>Τύπος σφάλματος</th><th>YES</th><th>NO</th><th>Logs</th><th>Κατάσταση</th><th>Συμπέρασμα</th><th></th>
          </tr>
        </thead>
        <tbody>
          ${cases.map((item) => `
            <tr>
              <td><input type="date" data-case-date="${item.id}" value="${item.fileDate ? String(item.fileDate).slice(0, 10) : ""}"></td>
              <td>${escapeHtml(item.origin === "device" ? (item.serviceAccountId || "Συσκευή") : (item.fileName || ""))}</td>
              <td>${item.code02Count}</td>
              <td>${item.code03Count}</td>
              <td><input data-case-fault="${item.id}" value="${escapeHtml(item.faultType || "")}"></td>
              <td>${item.yesCount}</td>
              <td>${item.noCount}</td>
              <td>${item.logsDownloaded ? "ναι" : "όχι"}</td>
              <td>${escapeHtml(statusText(item.investigationStatus))}</td>
              <td>
                <select data-case-conclusion="${item.id}">
                  <option value="" ${item.conclusion ? "" : "selected"}>—</option>
                  <option value="YES" ${item.conclusion === "YES" ? "selected" : ""}>YES επιστροφή</option>
                  <option value="NO" ${item.conclusion === "NO" ? "selected" : ""}>NO όχι</option>
                </select>
              </td>
              <td><button type="button" data-desk-resume="${item.id}">Άνοιγμα</button></td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>`;
}

function renderDeskDevices(lines) {
  const groups = new Map();
  lines.forEach((line) => {
    const id = line.serviceAccountId || "—";
    const group = groups.get(id) || [];
    group.push(line);
    groups.set(id, group);
  });
  if (!groups.size) {
    return `<p class="hint">Καμία συσκευή ακόμη μέσα στο ${scopePlace()}.</p>`;
  }
  return [...groups.entries()].map(([id, history]) => `
    <details open>
      <summary class="mono">${escapeHtml(id)}${deskOperator === "OASA" ? ` · ${escapeHtml(history.find((line) => line.stationName)?.stationName || "χωρίς σταθμό")}${history.find((line) => line.terminalCode) ? ` · τερματικό ${escapeHtml(history.find((line) => line.terminalCode).terminalCode)}` : ""}` : ""} · ${escapeHtml(history[0].faultType || "χωρίς τύπο σφάλματος")}</summary>
      <div class="table-wrap desk-table">
        <table>
          <thead><tr><th>Ημερομηνία</th>${deskOperator === "OASA" ? "<th>Τερματικό</th><th>Σταθμός</th>" : ""}<th>Προέλευση</th><th>Κωδικός</th><th>Τύπος</th><th>Logs</th><th>Κατάσταση</th><th>Συμπέρασμα</th><th></th></tr></thead>
          <tbody>
            ${history.map((line) => `
              <tr>
                <td>${escapeHtml(line.fileDate ? formatFileDay(line.fileDate) : "")}</td>
                ${deskOperator === "OASA" ? `<td>${escapeHtml(line.terminalCode || "")}</td><td>${escapeHtml(line.stationName || "")}</td>` : ""}
                <td>${escapeHtml(line.origin === "device" ? "συσκευή" : (line.fileName || "excel"))}</td>
                <td>${escapeHtml(line.resultCode || "")}</td>
                <td><input data-line-fault="${line.id}" value="${escapeHtml(line.faultType || "")}"></td>
                <td><input type="checkbox" data-line-logs="${line.id}" ${line.logsDownloaded ? "checked" : ""}></td>
                <td>${escapeHtml(statusText(line.investigationStatus))}</td>
                <td>
                  <select data-line-conclusion="${line.id}">
                    <option value="" ${line.conclusion ? "" : "selected"}>—</option>
                    <option value="YES" ${line.conclusion === "YES" ? "selected" : ""}>YES</option>
                    <option value="NO" ${line.conclusion === "NO" ? "selected" : ""}>NO</option>
                  </select>
                </td>
                <td><button type="button" data-desk-resume="${line.caseId}">Άνοιγμα</button></td>
              </tr>`).join("")}
          </tbody>
        </table>
      </div>
    </details>
  `).join("");
}

function renderLegacyFiles(batches) {
  const dated = (Array.isArray(batches) ? batches : []).map((batch) => ({ ...batch, day: formatFileDay(batch.fileDate) }));
  if (!dated.length) {
    return "";
  }
  return `
    <h3 class="screen-next">Παλιότερα Excel OSETH</h3>
    <p class="hint">Αρχεία που είχαν ήδη γραφτεί πριν τα χωριστά ράφια. Ανοίγουν από εκεί που έμειναν.</p>
    ${renderTable(
      ["Ημέρα", "Αρχείο", "YES", "NO", "Εκκρεμεί", ""],
      dated.map((file) => [
        file.day,
        file.title || file.fileName,
        file.yesCount,
        file.noCount,
        file.pendingCount,
        ""
      ])
    )}
    <div class="categories">
      ${dated.map((file) => `
        <button type="button" class="result-card" data-resume-batch="${escapeHtml(file.batchId)}">
          <strong>${escapeHtml(file.title || file.fileName)}</strong>
          <p class="result-meta">${escapeHtml(formatFileDay(file.fileDate))} · εκκρεμεί ${file.pendingCount}</p>
        </button>`).join("")}
    </div>`;
}

function statusText(value) {
  if (value === "logs") return "έχουν κατέβει logs";
  if (value === "concluded") return "συμπέρασμα";
  return "ανοιχτή";
}

function formatFileDay(value) {
  if (!value) {
    return "Χωρίς ημερομηνία";
  }
  const [year, month, day] = String(value).slice(0, 10).split("-");
  if (!year || !month || !day) {
    return String(value);
  }
  return `${day}/${month}/${year}`;
}

function statusLabel(decision) {
  if (decision === "YES") return "YES";
  if (decision === "NO") return "NO";
  return "Εκκρεμεί";
}

async function resumeBatch(batchId) {
  const rows = await fetchJson(`/api/imports/${batchId}/rows`);
  const stored = Array.isArray(rows) ? rows : [];
  if (!stored.length) {
    return;
  }
  fileName = stored[0].fileName || "";
  importedRows = stored.map(storedToWorkingRow);
  currentView = "flow";
  reachedIps = false;
  lastFindings = [];
  refreshDevices();
  if (devices.length) {
    await goToStep(2);
    return;
  }
  currentStep = 4;
  renderApp();
}

function storedToWorkingRow(row) {
  const decided = row.decision === "YES" || row.decision === "NO";
  return {
    rowNumber: row.rowNumber,
    mobility: row.mobility,
    decision: decided ? row.decision : null,
    disposition: row.decision === "YES" ? "AutomaticYes" : decided ? "Unclassified" : "NeedsIp",
    screenReason: decided ? row.decision : "Εκκρεμεί.",
    immediate: {
      serviceAccountId: row.serviceAccountId,
      rrn: row.rrn,
      amount: row.amount,
      eticketTransactionResult: row.eticketTransactionResult,
      transactionId: row.transactionId,
      transactionDate: row.transactionDate
    }
  };
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
  const intro = ipIntro();
  ipBody.innerHTML = `
    <h2>${intro.title}</h2>
    <p class="hint">${intro.hint}</p>
    <div class="device-list" id="ip-devices">
      ${renderDeviceRows()}
    </div>
    <pre id="ip-log" class="run-log"></pre>
    <div class="actions">
      <button type="button" onclick="goToStep(1)">Πίσω στο Excel</button>
      <button type="button" class="primary" id="ip-lookup" onclick="resolveIpsFromFiles()">Φόρτωση IP από αρχεία</button>
      <button type="button" id="ssh-ping" onclick="pingDevices()" disabled>Ping</button>
      <button type="button" class="primary" onclick="goToStep(3)">Συνέχεια στα logs</button>
    </div>
  `;
  resolveIpsFromFiles();
}

function ipIntro() {
  if (isOasaFlow()) {
    return {
      title: "Συσκευές ΟΑΣΑ",
      hint: "Η IP, ο αριθμός τερματικού και ο σταθμός έρχονται από το Ips_Oasa.json ή από τη βάση. Αν λείπει η IP, γράψ’ την εδώ. Το ping και το κατέβασμα logs είναι τα ίδια με τον OSETH."
    };
  }
  if (importProfile === "DEVICE") {
    return {
      title: "Καταχωρημένη συσκευή",
      hint: "Η IP έρχεται από τη βάση ή από τα αρχεία. Αν λείπει, γράψ’ την εδώ. Μετά είναι ping και logs, όπως σε κάθε συσκευή."
    };
  }
  return {
    title: "Συσκευές τύπου 02",
    hint: "IP και όχημα από Ips_TVM360.json / Ips_Mobile.json. Αν λείπει κάτι, γράψε το — πηγαίνει στη βάση CTVM IPs ως χειροκίνητο."
  };
}

function renderDeviceRows() {
  const oasa = isOasaFlow();
  return `
    <div class="device-row ip-row${oasa ? " oasa-row" : ""}" style="color:var(--muted); font-size:11px; letter-spacing:.04em; text-transform:uppercase;">
      <span>CTVM</span><span>Τύπος</span><span>IP</span>${oasa ? "<span>Τερματικό</span><span>Σταθμός</span>" : "<span>Όχημα</span>"}<span>Ping</span>
    </div>
    ${devices.map((device) => `
      <div class="device-row ip-row${oasa ? " oasa-row" : ""}">
        <span class="mono">${escapeHtml(device.serviceAccountId)}</span>
        <span>${mobilityLabel(device.mobility)}</span>
        <input class="ip-edit" data-id="${escapeHtml(device.serviceAccountId)}" value="${escapeHtml(device.ip || "")}" placeholder="π.χ. 10.249.19.21" onchange="saveDeviceMapping(this)">
        ${oasa ? `
          <input class="ip-edit terminal-edit" data-id="${escapeHtml(device.serviceAccountId)}" value="${escapeHtml(device.terminal || "")}" placeholder="αριθμός τερματικού" onchange="saveDeviceMapping(this)">
          <input class="ip-edit station-edit" data-id="${escapeHtml(device.serviceAccountId)}" value="${escapeHtml(device.station || "")}" placeholder="όνομα σταθμού" onchange="saveDeviceMapping(this)">
        ` : `
          <input class="ip-edit bus-edit" data-id="${escapeHtml(device.serviceAccountId)}" value="${escapeHtml(device.busNo || "")}" placeholder="π.χ. 2216" onchange="saveDeviceMapping(this)">
        `}
        <span class="${device.pingReachable === true ? "verdict-ok" : device.pingReachable === false ? "verdict-bad" : ""}">${escapeHtml(device.pingLabel || "")}</span>
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
    refreshPingButton();
    setIpLog(payload.message || "Δεν διαβάστηκαν τα αρχεία IP.");
    return;
  }
  for (const hit of payload.hits || []) {
    const device = devices.find((item) => item.serviceAccountId === hit.serviceAccountId);
    if (!device) {
      continue;
    }
    if (device.ip && (device.ipSource === "Ips_Oasa" || device.ipSource === "manual")) {
      continue;
    }
    device.ip = hit.ipAddress;
    device.busNo = hit.busNo || device.busNo;
    device.terminal = hit.terminal || device.terminal;
    device.station = hit.station || device.station;
    device.ipSource = hit.source;
    device.pingReachable = null;
    device.pingLabel = "";
  }
  const list = document.querySelector("#ip-devices");
  if (list) {
    list.innerHTML = renderDeviceRows();
  }
  const missing = payload.missing || [];
  refreshPingButton();
  setIpLog([
    `JSON κατάλογος: ${payload.catalogCount} συσκευές.`,
    `Βρέθηκαν (JSON ή χειροκίνητα): ${payload.matchedCount}.`,
    missing.length ? `Χωρίς IP — γράψε την χειροκίνητα: ${missing.join(", ")}` : "Όλες οι συσκευές του Excel έχουν IP."
  ].join("\n"));
}

function refreshPingButton() {
  const button = document.querySelector("#ssh-ping");
  if (button && !button.dataset.busy) {
    button.disabled = devicesWithIp().length === 0;
  }
}

function devicesWithIp() {
  return devices.filter((device) => String(device.ip || "").trim());
}

async function pingDevices() {
  const target = devicesWithIp();
  const button = document.querySelector("#ssh-ping");
  if (!target.length) {
    setIpLog("Πρώτα χρειάζεται IP για να σταλεί SSH ping.");
    refreshPingButton();
    return;
  }
  if (button) {
    button.disabled = true;
    button.dataset.busy = "1";
  }
  setIpLog("ping και μετά η IP κάθε συσκευής…");
  const response = await fetch("/api/logs/ping", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      devices: target.map((device) => ({
        serviceAccountId: device.serviceAccountId,
        ipAddress: String(device.ip).trim()
      }))
    })
  });
  const payload = await response.json().catch(() => ({}));
  if (button) {
    delete button.dataset.busy;
  }
  if (!response.ok) {
    refreshPingButton();
    setIpLog(payload.message || "Το SSH ping απέτυχε.");
    return;
  }
  const results = Array.isArray(payload) ? payload : [];
  for (const result of results) {
    const device = devices.find((item) => item.serviceAccountId === result.serviceAccountId);
    if (!device) {
      continue;
    }
    device.pingReachable = result.reachable === true;
    device.pingLabel = (result.reachable ? "απάντησε · " : "αποτυχία · ") + (result.message || "");
    device.pingPort = result.port || null;
  }
  const list = document.querySelector("#ip-devices");
  if (list) {
    list.innerHTML = renderDeviceRows();
  }
  refreshPingButton();
  const ok = results.filter((item) => item.reachable).length;
  setIpLog([
    `ping: ${ok} από ${results.length} απάντησαν οι ίδιες.`,
    ...results.map((item) => item.message)
  ].join("\n"));
}

function setIpLog(text) {
  const log = document.querySelector("#ip-log");
  if (log) {
    log.textContent = text;
    log.scrollTop = log.scrollHeight;
  }
}

function readyToDownload(device) {
  return Boolean(device.ip) && device.pingReachable === true;
}

function renderDownload() {
  const readyDevices = devices.filter(readyToDownload);
  const skippedDevices = devices.filter((device) => !readyToDownload(device));
  const fixed = readyDevices.filter((device) => device.mobility === "Fixed");
  const mobile = readyDevices.filter((device) => device.mobility === "Mobile");
  const other = readyDevices.filter((device) => device.mobility !== "Fixed" && device.mobility !== "Mobile");

  dlBody.innerHTML = `
    <h2>Logs</h2>
    <p class="hint">Θα κατέβουν ${readyDevices.length} συσκευές με επιτυχές ping${skippedDevices.length ? `. ${skippedDevices.length} με αποτυχία ping δεν κατεβαίνουν` : ""}. Middleware: το nlog. Κινητό: κατεβαίνει το zip και γίνεται extract. Άλλο service: journalctl στο home και κατέβασμα από εκεί. Φάκελος: Desktop\\Amco files\\logs\\OSETH.</p>
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
    ${skippedDevices.length ? `<h3>Δεν κατεβαίνουν</h3><p class="hint">Αποτυχία ping. Μπορείς να αφήσεις το αρχείο χειροκίνητα.</p>${deviceMiniList(skippedDevices)}` : ""}
    <h3 class="screen-next">Συναλλαγές στο log</h3>
    <p class="hint">Κλικ σε μια συναλλαγή. Στο middleware κρατιούνται οι γραμμές ανάμεσα στο Selecting product πάνω από την ώρα και στο επόμενο. OSETH: η ώρα είναι τα ψηφία 6–9 του Transaction ID. ΟΑΣΑ: η ώρα της ημερομηνίας συναλλαγής. Άλλο service: ολόκληρο το αρχείο.</p>
    <div id="log-cases">${renderLogCases()}</div>
    <section id="log-slice" class="log-slice" hidden></section>
    <pre id="dl-log" class="run-log"></pre>
    <div class="actions">
      <button type="button" id="back-tb" onclick="goToStep(2)">Πίσω στις IPs</button>
      <button type="button" class="primary" id="start-download" onclick="startLogDownload()" ${readyDevices.length ? "" : "disabled"}>Κατέβασμα με scp</button>
      <button type="button" class="primary" id="to-process" onclick="goToStep(4)">Συνέχεια στο πόρισμα</button>
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
      <span>${mobilityLabel(device.mobility)}${device.mobility === "Mobile" ? " · zip" : ""} · ${escapeHtml(serviceLabel(device.logService))} · ${(device.logDates || []).join(", ") || "χωρίς ημερομηνία"}${device.clockTime ? " · " + escapeHtml(device.clockTime) : ""} · ${escapeHtml(pingDownloadLabel(device))}</span>
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
  const ip = (row?.querySelector(".ip-edit:not(.bus-edit):not(.terminal-edit):not(.station-edit)")?.value || device.ip || "").trim();
  const bus = (row?.querySelector(".bus-edit")?.value || device.busNo || "").trim();
  const terminal = (row?.querySelector(".terminal-edit")?.value || device.terminal || "").trim();
  const station = (row?.querySelector(".station-edit")?.value || device.station || "").trim();
  device.ip = ip || null;
  device.busNo = bus || null;
  device.terminal = terminal || null;
  device.station = station || null;
  importedRows.forEach((row) => {
    if (sameAccount(row.immediate?.serviceAccountId, device.serviceAccountId)) {
      row.stationName = station || null;
      if (terminal) {
        row.oasaTerminal = terminal;
      }
    }
  });
  device.pingReachable = null;
  device.pingLabel = "";
  refreshPingButton();
  const status = row?.querySelector("span:last-child");
  if (status) {
    status.textContent = "";
    status.className = "";
  }
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
  const network = importProfile === "DEVICE" ? entryNetwork : deskNetwork;
  if (isOasaFlow() && ip && network) {
    await deskFetch("/api/desk/ips", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        operator: "OASA",
        network: network,
        serviceAccountId: device.serviceAccountId,
        ipAddress: ip,
        terminalCode: terminal,
        stationName: station,
        mobility: device.mobility,
        source: "manual"
      })
    });
  }
  if (!response.ok) {
    const payload = await response.json().catch(() => ({}));
    setIpLog(payload.message || "Invalid IP.");
    setDlLog(payload.message || "Invalid IP.");
  } else {
    device.ipSource = "manual";
  }
}

function pingDownloadLabel(device) {
  if (device.pingReachable === true) {
    return "ping ok";
  }
  if (device.pingReachable === false) {
    return "ping αποτυχία";
  }
  return "χωρίς ping";
}

async function startLogDownload() {
  const target = devices.filter(readyToDownload);
  for (const device of devices) {
    if (target.includes(device)) {
      continue;
    }
    device.downloadStatus = device.pingReachable === false
      ? "ping αποτυχία — δεν κατεβαίνει"
      : "χωρίς επιτυχές ping — δεν κατεβαίνει";
  }
  if (!target.length) {
    renderDownload();
    setDlLog("Το κατέβασμα γίνεται μόνο για συσκευές που απάντησαν στο ping.");
    return;
  }
  const button = document.querySelector("#start-download");
  if (button) {
    button.disabled = true;
  }
  document.querySelectorAll(".dl-row").forEach((row) => {
    const id = row.querySelector(".ip-edit")?.dataset.id;
    const device = devices.find((item) => item.serviceAccountId === id);
    const cell = row.querySelector(".dl-status");
    if (device && cell) {
      cell.textContent = device.downloadStatus || "—";
    }
  });
  const fixedCount = target.filter((device) => device.mobility === "Fixed").length;
  const mobileCount = target.filter((device) => device.mobility === "Mobile").length;
  setDlLog(`Κατέβασμα ${target.length} συσκευών με επιτυχές ping: ${fixedCount} σταθερές, ${mobileCount} κινητές.`);
  const response = await fetch("/api/logs/download", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      devices: target.map((device) => ({
        serviceAccountId: device.serviceAccountId,
        mobility: device.mobility,
        ipAddress: device.ip,
        pingReachable: true,
        logDates: device.logDates || [],
        logService: device.logService || "middleware",
        clockTime: device.searchMode === "transaction" ? device.clockTime || null : null,
        grepPattern: device.searchMode === "range" ? device.grepText || null : null
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
      noteDeskLogs(device.serviceAccountId);
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
  const cases = document.querySelector("#step-download #log-cases");
  if (cases && !status.running) {
    cases.innerHTML = renderLogCases();
  }
  if (!status.running) {
    renderPipeline();
    const next = document.querySelector("#to-process");
    if (next) {
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
  renderDownload();
  setDlLog("Το " + payload.fileName + " μπήκε χειροκίνητα για " + id + ".");
  renderPipeline();
}

function sameAccount(left, right) {
  const a = String(left || "").trim().toUpperCase();
  const b = String(right || "").trim().toUpperCase();
  return a.length > 0 && a === b;
}

function renderLogCases() {
  const ready = devices.filter((device) => device.localPath);
  if (!ready.length) {
    return `<p class="hint">Οι συναλλαγές ανοίγουν αφού κατέβει ή φορτωθεί το log.</p>`;
  }

  const buttons = [];
  for (const device of ready) {
    const rows = needsIpRows().filter((row) => sameAccount(row.immediate.serviceAccountId, device.serviceAccountId));
    if (!rows.length) {
      continue;
    }
    for (const row of rows) {
      const id = row.immediate.transactionId || "";
      const clock = clockForRow(row);
      const whole = row.searchMode === "range" || (row.logService && row.logService !== "middleware");
      const label = row.searchMode === "range"
        ? `${row.rangeFrom} – ${row.rangeUntil}`
        : (whole ? serviceLabel(row.logService) : (clock || id || "χωρίς ώρα"));
      buttons.push(`
        <button type="button" class="case-open" data-tx="${escapeHtml(id)}" data-clock="${escapeHtml(clock || "")}" data-whole="${whole ? "1" : ""}" data-device="${escapeHtml(device.serviceAccountId)}" onclick="openLogSlice(this)" ${whole || clock || id ? "" : "disabled"}>
          <span class="mono">${escapeHtml(device.serviceAccountId)}</span>
          <span class="mono">${escapeHtml(label)}</span>
          <span>${escapeHtml(formatDate(row.immediate.transactionDate))}</span>
        </button>`);
    }
  }

  if (!buttons.length) {
    return `<p class="hint">Δεν υπάρχει εκκρεμής συναλλαγή για τα logs που φορτώθηκαν.</p>`;
  }
  return `<div class="case-list">${buttons.join("")}</div>`;
}

function serviceLabel(service) {
  if (!service || service === "middleware") {
    return "middleware.service";
  }
  return service;
}

function clockFromOsethId(id) {
  const digits = String(id || "").replace(/\D/g, "");
  if (digits.length < 9) {
    return null;
  }
  const hh = digits.slice(5, 7);
  const mm = digits.slice(7, 9);
  const hour = Number(hh);
  const minute = Number(mm);
  if (hour > 23 || minute > 59) {
    return null;
  }
  return `${hh}:${mm}`;
}

function clockFromDate(value) {
  const raw = String(value || "").trim();
  const compact = raw.match(/^(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})/);
  if (compact) {
    return validClock(compact[4], compact[5]);
  }
  const marked = raw.match(/(?:T|\s)(\d{2}):(\d{2})/);
  if (marked) {
    return validClock(marked[1], marked[2]);
  }
  return null;
}

function validClock(hourText, minuteText) {
  const hour = Number(hourText);
  const minute = Number(minuteText);
  if (hour > 23 || minute > 59) {
    return null;
  }
  return `${hourText}:${minuteText}`;
}

function clockForRow(row) {
  if (row.clockTime) {
    return row.clockTime;
  }
  if (row.logService && row.logService !== "middleware") {
    return null;
  }
  if (importProfile === "OASA") {
    return clockFromDate(row.immediate?.transactionDate);
  }
  if (importProfile === "DEVICE") {
    return null;
  }
  return clockFromOsethId(row.immediate?.transactionId);
}

function daysBetween(from, until) {
  const days = [];
  const start = new Date(`${from}T00:00:00`);
  const end = new Date(`${until}T00:00:00`);
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end < start) {
    return days;
  }
  for (let cursor = new Date(start); cursor <= end && days.length < 32; cursor.setDate(cursor.getDate() + 1)) {
    const month = String(cursor.getMonth() + 1).padStart(2, "0");
    const day = String(cursor.getDate()).padStart(2, "0");
    days.push(`${cursor.getFullYear()}-${month}-${day}`);
  }
  return days;
}

function flowProfile(row) {
  if (row?.operator === "OASA" || row?.operator === "OSETH") {
    return row.operator;
  }
  if (importProfile === "OASA" || importProfile === "OSETH") {
    return importProfile;
  }
  if (entryOperator === "OASA" || entryOperator === "OSETH") {
    return entryOperator;
  }
  if (deskOperator === "OASA" || deskOperator === "OSETH") {
    return deskOperator;
  }
  return "OSETH";
}

function renderFlow(flow) {
  if (!flow) {
    return "";
  }
  const missing = (flow.deviations || []).map((item) =>
    `<li>Βήμα ${escapeHtml(item.stepNumber)}: ${escapeHtml(item.expected || "")}</li>`).join("");
  return `
    <section class="flow-reading">
      <h3>Αρχέτυπο flow</h3>
      <p class="hint">${escapeHtml(flow.flowMessage || "")}</p>
      ${missing ? `<ul>${missing}</ul>` : ""}
      <h3>Ερμηνεία</h3>
      <p class="${flow.errorKind ? "slice-miss" : "hint"}">${escapeHtml(flow.errorMessage || "")}</p>
    </section>`;
}

async function openLogSlice(button) {
  const transactionId = button.dataset.tx || "";
  const serviceAccountId = button.dataset.device || "";
  const clockTime = button.dataset.clock || "";
  const wholeFile = button.dataset.whole === "1";
  const panel = document.querySelector("#step-download:not([hidden]) #log-slice, #step-process:not([hidden]) #log-slice");
  if (!panel) {
    return;
  }
  document.querySelectorAll(".case-open").forEach((item) => item.classList.remove("selected"));
  button.classList.add("selected");
  const device = devices.find((item) => sameAccount(item.serviceAccountId, serviceAccountId) && item.localPath);
  const row = needsIpRows().find((item) =>
    sameAccount(item.immediate.serviceAccountId, serviceAccountId)
    && (wholeFile || String(item.immediate.transactionId || "") === transactionId));
  panel.hidden = false;
  if (!device) {
    panel.innerHTML = `<p class="slice-miss">Δεν υπάρχει log για αυτή τη συσκευή.</p>`;
    return;
  }
  const title = wholeFile ? serviceLabel(row?.logService) : (clockTime || transactionId);
  panel.innerHTML = `<p class="hint">Άνοιγμα ${escapeHtml(title)}…</p>`;
  const response = await fetch("/api/logs/slice", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      path: device.localPath,
      transactionId,
      transactionDate: row?.immediate.transactionDate || null,
      clockTime: clockTime || null,
      profile: flowProfile(row),
      wholeFile
    })
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok || !payload.found) {
    panel.innerHTML = `
      <h3 class="mono">${escapeHtml(title)}</h3>
      <p class="slice-miss">${escapeHtml(payload.message || "Δε βρέθηκε η συναλλαγή.")}</p>`;
    return;
  }
  const lines = (payload.lines || []).map((line) => `
    <div class="slice-line ${escapeHtml(line.role || "between")}">
      <span class="slice-no">${escapeHtml(line.lineNumber)}</span>
      <span class="slice-text">${escapeHtml(line.text || "")}</span>
    </div>`).join("");
  panel.innerHTML = `
    <h3 class="mono">${escapeHtml(title)}</h3>
    <p class="hint">${escapeHtml(payload.logFileName || "")} · γραμμές ${escapeHtml(payload.lineStart)}–${escapeHtml(payload.lineEnd)}. ${escapeHtml(payload.message || "")}</p>
    ${payload.savedPath ? `<p class="hint">Η συναλλαγή αποθηκεύτηκε στο ${escapeHtml(payload.savedPath)}. Τα nlog μένουν στον ίδιο φάκελο.</p>` : ""}
    ${renderFlow(payload.flow)}
    <div class="slice-view">${lines}</div>`;
  panel.scrollIntoView({ block: "nearest" });
}

function renderProcess() {
  const yes = decisionRows("YES");
  const no = decisionRows("NO");
  const pending = needsIpRows();
  const logs = devices.filter((device) => device.localPath);
  prBody.innerHTML = `
    <div class="categories verdict-counts">
      <div class="result-card yes">
        <div class="result-code">YES</div>
        <p class="result-meta">${countLabel(yes.length, "συναλλαγή", "συναλλαγές")}</p>
      </div>
      <div class="result-card no">
        <div class="result-code">NO</div>
        <p class="result-meta">${countLabel(no.length, "συναλλαγή", "συναλλαγές")}</p>
      </div>
    </div>
    <div class="verdict-grid">
      <section class="verdict-panel">
        <h2>YES</h2>
        <p class="hint">${yes.length ? "Τύπος 03 και τύπος 02 με ποσό 0,02." : "Καμία συναλλαγή YES."}</p>
        ${yes.length ? screenTable(yes, true) : ""}
      </section>
      <section class="verdict-panel">
        <h2>NO</h2>
        <p class="hint">${no.length ? "Συναλλαγές που κλείνουν ως NO." : "Καμία συναλλαγή NO."}</p>
        ${no.length ? screenTable(no, true) : ""}
      </section>
    </div>
    ${pending.length ? `
      <h2 class="screen-next">${importProfile === "OSETH" ? "Τύπος 02 που πέρασε από IP" : "Γραμμές που πέρασαν από IP"}</h2>
      <p class="hint">${countLabel(pending.length, "συναλλαγή", "συναλλαγές")} · ${countLabel(logs.length, "log", "logs")}. Αυτές δεν είναι ακόμα YES ή NO. Η επεξεργασία ψάχνει μόνο αυτά τα Transaction ID, μόνο κάρτα.</p>
      ${renderTable(
        ["ServiceAccountID", "TransactionID", "RRN", "Amount", "Ημερομηνία", "Τύπος"],
        pending.map((row) => [
          row.immediate.serviceAccountId || "",
          row.immediate.transactionId || "",
          row.immediate.rrn || "",
          row.immediate.amount ?? "",
          formatDate(row.immediate.transactionDate),
          mobilityLabel(row.mobility)
        ])
      )}
      <h3 class="screen-next">Άνοιγμα συναλλαγής στο log</h3>
      <p class="hint">Σε μία συναλλαγή το κομμάτι συγκρίνεται με το αρχέτυπο του φορέα της. Ο OSETH και ο ΟΑΣΑ έχουν χωριστό flow και χωριστά σφάλματα.</p>
      <div id="log-cases">${renderLogCases()}</div>
      <section id="log-slice" class="log-slice" hidden></section>
      <p class="hint" style="margin-top:16px;">Logs: ${logs.map((device) => device.serviceAccountId + " → " + (device.downloadStatus || device.localPath)).join(" · ") || "κανένα"}</p>
      <pre id="pr-log" class="run-log">${lastFindings.length ? "" : "Πάτα επεξεργασία για τα logs του τύπου 02."}</pre>
      <div id="pr-results">${lastFindings.length ? renderFindings(lastFindings) : ""}</div>
      <div class="actions">
        <button type="button" onclick="goToStep(3)" ${reachedIps ? "" : "disabled"}>Πίσω στα logs</button>
        <button type="button" class="primary" id="run-process" onclick="runProcess()" ${logs.length ? "" : "disabled"}>Επεξεργασία και αποθήκευση</button>
      </div>
    ` : ""}
  `;
}

function decisionRows(decision) {
  return importedRows.filter((row) => row.decision === decision);
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
      transactions: needsIpRows().map((row) => ({
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
    const previous = new Map(
        devices.map((device) => [device.serviceAccountId, device])
    );

    const map = new Map();

    for (const row of needsIpRows()) {
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
            pingReachable: prior?.pingReachable ?? null,
            pingLabel: prior?.pingLabel || "",
            ipSource: prior?.ipSource || null,
            logDates: []
        };

        if (row.stationName) {
            current.station = row.stationName;
        }
        if (row.oasaTerminal) {
            current.terminal = row.oasaTerminal;
        }
        if (row.logService) {
            current.logService = row.logService;
        }
        if (row.clockTime) {
            current.clockTime = row.clockTime;
        }
        if (row.searchMode) {
            current.searchMode = row.searchMode;
        }
        if (row.grepText) {
            current.grepText = row.grepText;
        }
        if (row.rangeFrom && row.rangeUntil) {
            for (const day of daysBetween(row.rangeFrom, row.rangeUntil)) {
                if (!current.logDates.includes(day)) {
                    current.logDates.push(day);
                }
            }
        }
        if (row.resolvedIp) {
            current.ip = row.resolvedIp;
            current.ipSource = importProfile === "DEVICE" ? "manual" : "Ips_Oasa";
        }

        current.transactions++;

        const day = dayFromTransaction(row.immediate.transactionDate);

        if (day && !current.logDates.includes(day)) {
            current.logDates.push(day);
        }

        map.set(id, current);
    }

    devices = [...map.values()];

    for (const device of devices) {
        device.logDates.sort();
    }
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

function resultCode(row) {
  const text = String(row.immediate?.eticketTransactionResult || "").trim();
  if (/^\d+$/.test(text)) {
    return text.padStart(2, "0");
  }
  return text;
}

function rowsByDisposition(name) {
  return importedRows.filter((row) => row.disposition === name);
}

function needsIpRows() {
  return rowsByDisposition("NeedsIp");
}

function countLabel(count, singular, plural) {
  return `${count} ${count === 1 ? singular : plural}`;
}

function mobilityLabel(value) {
  if (value === "Fixed") return "Σταθερό";
  if (value === "Mobile") return "Κινητό";
  return "Άγνωστο";
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
    if (!value) return null;

    const raw = String(value).trim();

    if (/^\d{14}$/.test(raw)) {
        return `${raw.slice(0, 4)}-${raw.slice(4, 6)}-${raw.slice(6, 8)}`;
    }

    return raw.slice(0, 10);
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
