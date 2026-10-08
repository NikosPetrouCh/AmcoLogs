# AmcoLogs

Εφαρμογή γραφείου για διερεύνηση αποτυχημένων πωλήσεων CTVM. Διαβάζει Excel, βρίσκει την IP της συσκευής, κατεβάζει το nlog της ημέρας και βγάζει πόρισμα **μόνο για τη συναλλαγή του Excel**, όχι για τις υπόλοιπες πωλήσεις του αρχείου.

Τρέχει τοπικά ως ASP.NET Core + απλό HTML/CSS/JS (`http://localhost:5204`). Τα credentials και τα logs μένουν στο μηχάνημα.

## Τι κάνει, με τη σειρά

1. **Excel** — drop `.xlsx`. Κρατάει Transaction ID, RRN, Amount, TransactionDate, EticketTransactionResult, ServiceAccountID και αν η συσκευή είναι κινητή ή σταθερή.
2. **IPs** — διαβάζει `Ips_TVM360.json` (σταθερά) και `Ips_Mobile.json` (κινητά). Αν λείπει IP, τη γράφεις χειροκίνητα.
3. **Logs** — `scp -P 1030 amco@IP:...` στο `Desktop\Nikos`. Αν αποτύχει, αφήνεις εσύ το nlog στη συσκευή.
4. **Πόρισμα** — εντοπίζει στο log **μόνο** τις συναλλαγές του Excel (κάρτα). Μαζεύει ERROR στο παράθυρο της πώλησης και αποθηκεύει στη MySQL.

Η βάση έχει τρία ράφια, κάτω αριστερά:

| Οθόνη | Περιεχόμενο |
| --- | --- |
| Error reports | Πορίσματα: Transaction ID, παράθυρο γραμμών, ERROR |
| CTVM IPs | Αντιστοιχίες CTVM → IP → όχημα από JSON + χειροκίνητες εγγραφές |
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

Τα αρχεία είναι στο `src/AmcoLogs.Web/data/`:

- `Ips_TVM360.json` — σταθερά: `{ "CTVM36246554": "10.249.16.43", ... }`
- `Ips_Mobile.json` — κινητά: `{ "vehicle", "ctvm", "ip" }`

Το ServiceAccountID του Excel αντιστοιχίζεται στα ψηφία του `CTVM…`. Αν λείπει IP ή όχημα, γράψε το στο πεδίο (βήμα 2) ή στο **CTVM IPs** αριστερά. Η χειροκίνητη γραμμή αποθηκεύεται στη MySQL (`source = manual`) και την επόμενη φορά ενώνεται με τα JSON. Τα JSON **δεν** αλλάζουν· η βάση κρατά την ένωση.

Τα δύο αρχεία μπαίνουν στο git (`src/AmcoLogs.Web/data/`). Με push ανεβαίνουν στο GitHub.

## Βήμα 3 — Logs

Η εντολή είναι ακριβώς αυτή η μορφή:

```
scp -P 1030 amco@10.249.19.21:/opt/amco/middleware/TVMserver/nlog-all-YYYY-MM-DD.log "C:\Users\User\Desktop\Nikos\CTVMxxxx\"
```

- Πόρτα **1030**, χρήστης **amco**, φάκελος προορισμού **Desktop\Nikos\ΣΥΣΚΕΥΗ**
- Ημερομηνία από το `TransactionDate` του Excel
- Ίδια εντολή για σταθερά και κινητά — χωρίς ping και χωρίς zip

Αν το `scp` αποτύχει, διάλεξε / άσε το nlog στη γραμμή της συσκευής.

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
- `device_ip_mappings` — CTVM IPs (`source` = `Ips_TVM360` / `Ips_Mobile` ή `manual`)
- `investigation_findings` — Error reports (verdict, παράθυρο γραμμών, `errors_json`)

Χωρίς MySQL το flow δουλεύει στη συνεδρία. Το πόρισμα και οι χειροκίνητες IP μένουν στη μνήμη μέχρι να συνδεθείς.

## Πώς το τρέχεις

Προϋποθέσεις: .NET 10 SDK, OpenSSH `scp`, MySQL αν θέλεις αποθήκευση.

```powershell
dotnet run --project src/AmcoLogs.Web/AmcoLogs.Web.csproj --urls http://localhost:5204
```

Ή άνοιξε το `AmcoLogs.slnx` στο Visual Studio και F5 στο profile `http`.

Το `appsettings.Local.json` έχει DeviceAccess (χρήστης `amco`, πόρτα 1030). Δεν μπαίνει στο git.

## Τι δεν μπαίνει στο git

- αρχεία `*.log` (τα nlog των συσκευών), `samples/`
- `appsettings.Local.json`
- downloads/uploads στο `%LocalAppData%\AmcoLogs\`
- MySQL connection (`%LocalAppData%\AmcoLogs\mysql-connection.json`)

Οι φάκελοι πηγαίου κώδικα `src/AmcoLogs.Core/Logs` και `src/AmcoLogs.Infrastructure/Logs` **μπαίνουν** στο git. Χωρίς αυτούς το build σπάει (`AmcoLogs.Core.Logs`, `InvestigationFinding`).

Τα nlog έχουν σειριακούς καρτών και κλειδιά. Μην τα κάνεις commit.

## Αρχιτεκτονική

```
src/AmcoLogs.Core            μοντέλα Excel, συσκευή, logs, persistence
src/AmcoLogs.Infrastructure  ClosedXML, IP JSON catalog, scp, nlog locator, MySQL
src/AmcoLogs.Web             API + wwwroot (vanilla JS, όχι React)
```

- `FileIpCatalog` — Ips_TVM360.json + Ips_Mobile.json
- `NlogSaleLocator` — παράθυρο Selecting product + Transaction ID, μόνο κάρτα, ERROR
- `SaleInvestigation` — δένει τις γραμμές Excel με τα αρχεία nlog της σωστής ημέρας
- `RemoteLogDownloader` — `scp -P 1030 amco@IP:...`

## API (σύντομα)

| Μέθοδος | Διαδρομή | Ρόλος |
| --- | --- | --- |
| POST | `/api/imports/excel` | Διάβασμα Excel, προαιρετική εγγραφή General |
| POST | `/api/devices/resolve` | IP από τα δύο JSON |
| POST | `/api/devices/ip` | Χειροκίνητη IP |
| POST | `/api/logs/download` | scp -P 1030 |
| POST | `/api/logs/upload` | Χειροκίνητο nlog ανά ServiceAccountID |
| POST | `/api/logs/process` | Πόρισμα μόνο για τα Transaction ID του Excel |
| GET | `/api/logs/findings` | Error reports |
| GET | `/api/devices` | CTVM IPs |
| GET | `/api/imports/rows` | General |
