# AmcoLogs

Εφαρμογή γραφείου για διερεύνηση αποτυχημένων πωλήσεων CTVM. Διαβάζει Excel, βρίσκει την IP της συσκευής, κατεβάζει το nlog της ημέρας και βγάζει πόρισμα **μόνο για τη συναλλαγή του Excel**, όχι για τις υπόλοιπες πωλήσεις του αρχείου.

Τρέχει τοπικά ως ASP.NET Core + απλό HTML/CSS/JS (`http://localhost:5204`). Τα credentials και τα logs μένουν στο μηχάνημα.

## Τι κάνει, με τη σειρά

1. **Excel** — drop `.xlsx`. Κρατάει Transaction ID, RRN, Amount, TransactionDate, EticketTransactionResult, ServiceAccountID και αν η συσκευή είναι κινητή ή σταθερή.
2. **IPs** — ψάχνει ThingsBoard. Αν δεν βρει IP, τη γράφεις χειροκίνητα στο πεδίο της συσκευής και συνεχίζεις.
3. **Logs** — WinSCP (SFTP) προς `/opt/amco/middleware/TVMserver`. Αν το κατέβασμα αποτύχει, αφήνεις εσύ το `nlog-all-YYYY-MM-DD.log` (ή `.zip`) στη συσκευή.
4. **Πόρισμα** — εντοπίζει στο log **μόνο** τις συναλλαγές του Excel (κάρτα). Μαζεύει ERROR στο παράθυρο της πώλησης και αποθηκεύει στη MySQL.

Η βάση έχει τρία ράφια, κάτω αριστερά:

| Οθόνη | Περιεχόμενο |
| --- | --- |
| Error reports | Πορίσματα: Transaction ID, παράθυρο γραμμών, ERROR |
| CTVM IPs | ServiceAccountID → IP (ThingsBoard ή χειροκίνητα) |
| General | Τα στοιχεία της συναλλαγής από το Excel |

## Βήμα 1 — Excel

Απαιτούμενες στήλες (η κεφαλίδα μπορεί να έχει μικρές παραλλαγές ονόματος):

- `ServiceAccountID`
- `RRN`
- `Amount`
- `EticketTransactionResult`
- `TransactionID`
- `TransactionDate`

Τύπος συσκευής από το ServiceAccountID:

- ξεκινάει από **36** → σταθερό CTVM
- ξεκινάει από **4** → κινητό (λεωφορείο)

Διάλεξε κατηγορία 01–04. Οι γραμμές αυτής της κατηγορίας περνάνε στα επόμενα βήματα. Αν η MySQL είναι συνδεδεμένη, το Excel γράφεται αμέσως στο General.

## Βήμα 2 — IPs

Για κάθε συσκευή της κατηγορίας:

1. Πάτα **Αναζήτηση στο ThingsBoard**. Ανοίγει Chrome, μπαίνει στα δύο dashboards (συσκευές → BUS no, οχήματα → WAN IP). Δεν περνάει από Home ούτε από λίστα dashboards.
2. Αν το lookup αποτύχει (λάθος login, offline, κ.λπ.), γράψε την IP στο πεδίο της γραμμής. Αποθηκεύεται ως `manual`.
3. **Συνέχεια στα logs** δουλεύει και χωρίς επιτυχές ThingsBoard, εφόσον έχεις IP ή σκοπεύεις να ρίξεις log χειροκίνητα στο επόμενο βήμα.

Credentials ThingsBoard μπαίνουν στο `src/AmcoLogs.Web/appsettings.Local.json` (δεν ανεβαίνει στο git):

```json
{
  "ThingsBoard": {
    "Username": "",
    "Password": ""
  }
}
```

## Βήμα 3 — Logs

Το σωστό εργαλείο εδώ είναι το **WinSCP** (`WinSCP.com`), όχι γενικό SSH client από κώδικα. Ανοίγει SFTP στην IP της συσκευής, πάει στο

`/opt/amco/middleware/TVMserver`

και παίρνει το nlog της ημέρας από το `TransactionDate` του Excel.

### Σταθερή συσκευή

```
get nlog-all-YYYY-MM-DD.log
```

### Κινητή συσκευή

1. Ping. Αν δεν απαντά, σταματάει — η συσκευή είναι offline.
2. Στο remote: `sudo zip nlog-all-YYYY-MM-DD.zip nlog-all-YYYY-MM-DD.log`
3. `get nlog-all-YYYY-MM-DD.zip`

SSH user/pass της συσκευής (ίδια με PuTTY/WinSCP) στο `appsettings.Local.json`:

```json
{
  "DeviceAccess": {
    "Username": "",
    "Password": "",
    "Port": 22,
    "RemoteLogPath": "/opt/amco/middleware/TVMserver",
    "PingTimeoutMs": 2500,
    "WinScpPath": ""
  }
}
```

`WinScpPath` αφήνεται κενό αν το WinSCP είναι στο `Program Files`. Τα αρχεία πέφτουν στο `%LocalAppData%\AmcoLogs\downloads\`.

### Χειροκίνητο drop

Αν το WinSCP δεν βρει αρχείο (offline, λάθος IP, χωρίς zip), στη γραμμή της συσκευής διάλεξε / άσε το `nlog-all-YYYY-MM-DD.log` ή το `.zip`. Αποθηκεύεται στο `%LocalAppData%\AmcoLogs\uploads\{ServiceAccountID}\` και το flow συνεχίζει κανονικά στο πόρισμα.

## Βήμα 4 — Πόρισμα

Δεν διαβάζει «όλο το log ως λίστα πωλήσεων». Για κάθε γραμμή του Excel:

1. Παίρνει το nlog της **ίδιας συσκευής** και της **ίδιας ημέρας**.
2. Ψάχνει `Selecting product` — εκεί αρχίζει μία πώληση (~200 γραμμές, όριο 250, ή μέχρι το επόμενο Selecting product).
3. Μέσα στο παράθυρο πρέπει να υπάρχει `Creating new transaction with id: {guid}` που να ταιριάζει με το **Transaction ID του Excel**.
4. Κρατάει **μόνο κάρτα** (`Setting payment method to: Card` / `Payment Method: (Card)`). Τα μετρητά αγνοούνται.
5. Μέσα σε αυτό το κομμάτι μαζεύει γραμμές `ERROR`.

Πόρισμα ανά συναλλαγή:

| Verdict | Σημαίνει |
| --- | --- |
| LocatedWithErrors | Η card πώληση βρέθηκε και έχει ERROR στο παράθυρο |
| LocatedNoErrors | Βρέθηκε, χωρίς ERROR σε αυτό το slice |
| NotFoundInLog | Το Transaction ID δεν βρέθηκε ως card πώληση στο log |
| NoLogFile | Δεν υπάρχει nlog για αυτή τη συσκευή / ημέρα |

Μετά, αν η MySQL είναι συνδεδεμένη, γράφει τα πορίσματα στα Error reports. Τα CTVM IPs και το General έχουν ήδη γραφτεί νωρίτερα (IP στο βήμα 2, Excel στο βήμα 1).

Αργότερα θα προστεθούν exceptions / logs που δείχνουν ότι η συναλλαγή δεν ολοκληρώθηκε. Προς το παρόν το πόρισμα είναι εντοπισμός + ERROR.

## Βάση δεδομένων

Σύνδεση: Database → Connection (κάτω αριστερά, όχι μόνιμα ορατή φόρμα). Δημιουργεί τη βάση αν δεν υπάρχει.

Πίνακες:

- `excel_import_batches` / `excel_import_rows` — General
- `device_ip_mappings` — CTVM IPs (`source` = `thingsboard` ή `manual`)
- `investigation_findings` — Error reports (verdict, παράθυρο γραμμών, `errors_json`)

Χωρίς MySQL το flow δουλεύει στη συνεδρία. Το πόρισμα και οι χειροκίνητες IP μένουν στη μνήμη μέχρι να συνδεθείς.

## Πώς το τρέχεις

Προϋποθέσεις: .NET 10 SDK, Chrome (για ThingsBoard), WinSCP στο laptop εργασίας, MySQL αν θέλεις αποθήκευση.

```powershell
dotnet run --project src/AmcoLogs.Web/AmcoLogs.Web.csproj --urls http://localhost:5204
```

Ή άνοιξε το `AmcoLogs.slnx` στο Visual Studio και F5 στο profile `http`.

Αντίγραψε το `src/AmcoLogs.Web/appsettings.json` σε `appsettings.Local.json` στον ίδιο φάκελο και βάλε εκεί ThingsBoard + DeviceAccess. Το Local δεν μπαίνει στο git.

## Τι δεν μπαίνει στο git

- `*.log`, `samples/`
- `appsettings.Local.json`
- downloads/uploads στο `%LocalAppData%\AmcoLogs\`
- MySQL connection (`%LocalAppData%\AmcoLogs\mysql-connection.json`)

Τα nlog έχουν σειριακούς καρτών και κλειδιά. Μην τα κάνεις commit.

## Αρχιτεκτονική

```
src/AmcoLogs.Core            μοντέλα Excel, συσκευή, logs, persistence
src/AmcoLogs.Infrastructure  ClosedXML, Playwright ThingsBoard, WinSCP, nlog locator, MySQL
src/AmcoLogs.Web             API + wwwroot (vanilla JS, όχι React)
```

- `NlogSaleLocator` — παράθυρο Selecting product + Transaction ID, μόνο κάρτα, ERROR
- `SaleInvestigation` — δένει τις γραμμές Excel με τα αρχεία nlog της σωστής ημέρας
- `RemoteLogDownloader` — ping / WinSCP scripts όπως στην εργασία

## API (σύντομα)

| Μέθοδος | Διαδρομή | Ρόλος |
| --- | --- | --- |
| POST | `/api/imports/excel` | Διάβασμα Excel, προαιρετική εγγραφή General |
| POST | `/api/thingsboard/lookup` | IP από dashboards |
| POST | `/api/devices/ip` | Χειροκίνητη IP |
| POST | `/api/logs/download` | WinSCP κατέβασμα |
| POST | `/api/logs/upload` | Χειροκίνητο nlog ανά ServiceAccountID |
| POST | `/api/logs/process` | Πόρισμα μόνο για τα Transaction ID του Excel |
| GET | `/api/logs/findings` | Error reports |
| GET | `/api/devices` | CTVM IPs |
| GET | `/api/imports/rows` | General |
