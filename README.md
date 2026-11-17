# AmcoLogs

Εφαρμογή γραφείου για διερεύνηση αποτυχημένων πωλήσεων CTVM. Διαβάζει Excel OSETH ή ΟΑΣΑ, ή μία συσκευή, βρίσκει την IP, κατεβάζει τα logs και κρατάει τη συγκεκριμένη συναλλαγή.

Τρέχει τοπικά ως ASP.NET Core και απλό HTML/CSS/JS στο `http://localhost:5204`. Ο κωδικός συσκευής και η σύνδεση MySQL μένουν στο μηχάνημα.

## Είσοδος

Στην Εισαγωγή διαλέγεις:

- **Daily Exceptions** — drop Excel
- **Device investigation** — μία συσκευή, με CTVM

Η IP δεν μπαίνει εδώ. Συμπληρώνεται στο επόμενο βήμα, από τα JSON ή με το χέρι.

## Daily Exceptions

Αν η κεφαλίδα έχει κωδικό τερματικού ΟΑΣΑ, το αρχείο είναι ΟΑΣΑ. Αλλιώς είναι OSETH.

OSETH, στήλες: `ServiceAccountID`, `RRN`, `Amount`, `EticketTransactionResult`, `TransactionID`, `TransactionDate`.

- τύπος 04 εκτός
- τύπος 03, και τύπος 02 με ποσό 0,02: αυτόματο YES
- άλλος τύπος 02: χρειάζεται IP και logs

ΟΑΣΑ, στήλες: κωδικός τερματικού, ημερομηνία συναλλαγής, ποσό, κωδ. κατάστασης. Το CTVM, η IP, το τερματικό και ο σταθμός βγαίνουν από το `src/AmcoLogs.Web/data/Ips_Oasa.json`:

```json
[{ "terminal": "12345", "ctvm": "CTVM36246554", "ip": "10.249.16.43", "station": "Σύνταγμα" }]
```

Ο τύπος συσκευής βγαίνει από το CTVM: ψηφία που ξεκινούν από 36 είναι σταθερό, από 4 κινητό.

## Device investigation

Φορέας OSETH ή OASA. Στον OASA διαλέγεις και TRAM ή METRO.

- **Μία συναλλαγή** στο middleware: ημερομηνία και ώρα `08:23`
- **Εύρος ημερομηνιών**: από, έως, και το κείμενο για grep
- Service: `middleware.service`, `coinman.service`, `bnr.service`, `printman.service`, `frontman.service`

## IPs

- `Ips_TVM360.json` — σταθερά OSETH
- `Ips_Mobile.json` — κινητά OSETH
- `Ips_Oasa.json` — ΟΑΣΑ, με σταθμό και τερματικό

Το JSON κερδίζει τη βάση, εκτός αν η γραμμή είναι `source = manual`. Χωρίς MySQL η χειροκίνητη IP μένει για τη συνεδρία.

## Logs

Πρώτα ping. Κατεβαίνουν μόνο όσες συσκευές απάντησαν.

Φάκελος: `Desktop\Amco files\logs\OSETH\<CTVM>`.

| Συσκευή | Τι γίνεται |
| --- | --- |
| Σταθερή | πόρτα 8022, `scp` του `nlog-all-ημέρα.log` από `/opt/amco/middleware/TVMServer` |
| Κινητή | πόρτα 1030, `sudo zip`, κατέβασμα, extract, σβήσιμο του zip τοπικά και στη συσκευή |

Το nlog δεν σβήνεται.

Μία συναλλαγή middleware κόβεται από το `Selecting product` πάνω από την ώρα μέχρι το επόμενο και γράφεται στο `nlog-slice-ημέρα-ωρα.log`. Το πλήρες nlog μένει δίπλα του.

Η ώρα:

- OSETH: τα ψηφία 6–9 του Transaction ID, π.χ. `08:23`
- ΟΑΣΑ: η ώρα μέσα στην ημερομηνία συναλλαγής
- μία συσκευή: την γράφεις `08:23`

Εύρος ή άλλο service: το grep γράφεται στο `grep-result.log`. Τα αρχεία που κατέβηκαν μένουν. Για coinman, bnr, printman και frontman η εντολή στη συσκευή είναι `journalctl -u ….service --since … --until … > αρχείο.log` και το αρχείο κατεβαίνει από το home.

## Flow

Σε μία συναλλαγή το κομμάτι συγκρίνεται με το αρχέτυπο του φορέα της. OSETH και ΟΑΣΑ είναι χωριστά:

- `src/AmcoLogs.Web/data/flow/oseth/archetype.txt`
- `src/AmcoLogs.Web/data/flow/oasa/archetype.txt`

Ως 20 γραμμές, με τη σειρά. Τα δείγματα σφάλματος μπαίνουν σε `errors/<τύπος>.txt`, ως 20 γραμμές. Το όνομα του αρχείου είναι ο τύπος. Οι γραμμές με `#` αγνοούνται.

## Βάση

Σύνδεση από Database → Connection. Το αρχείο είναι ανά μηχάνημα: `%LocalAppData%\AmcoLogs\mysql-connection.json`.

Αριστερά, με τη σειρά: General, Error reports, CTVM IPs. Μέσα σε κάθε ένα μπαίνεις σε OSETH ή OASA. Ο OASA έχει METRO και μετά TRAM. Το TRAM δεν έχει Excel.

Χωρίς MySQL η διερεύνηση μένει στη συνεδρία. Το μήνυμα είναι «Η MySQL δεν είναι συνδεδεμένη.»

## Πώς το τρέχεις

Χρειάζεται .NET 10 SDK και OpenSSH. MySQL μόνο αν θέλεις να μείνει η δουλειά.

```powershell
dotnet run --project src/AmcoLogs.Web/AmcoLogs.Web.csproj --urls http://localhost:5204
```

Ο κωδικός της συσκευής μπαίνει στο `appsettings.Local.json` (`DeviceAccess:Password`, χρήστης `amco`). Το αρχείο δεν ανεβαίνει στο git. Η πόρτα στο κατέβασμα είναι 8022 για σταθερά και 1030 για κινητά.

## Τι δεν μπαίνει στο git

- `*.log`, τα nlog που κατεβαίνουν
- `appsettings.Local.json`
- `%LocalAppData%\AmcoLogs\`

## Αρχιτεκτονική

```
src/AmcoLogs.Core            μοντέλα Excel, συσκευή, logs, γραφείο
src/AmcoLogs.Infrastructure  ClosedXML, JSON, scp, slice, flow, MySQL
src/AmcoLogs.Web             API και wwwroot
```
