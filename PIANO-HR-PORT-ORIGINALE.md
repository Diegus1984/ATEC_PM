# PIANO — Le opzioni del programma «Timbrature» che mancano ad ATEC PM

> **Punto d'ingresso di questo lavoro.** Chi lo riprende (persona o sessione) legge questo
> file e sa cosa è stato deciso, perché, e da dove partire. Nasce il **28/08/2026** da un
> censimento pagina per pagina del programma originale, con le scelte fatte da Diego una
> per una.
>
> Sopra a questo c'è **`PIANO-HR-PRESENZE.md`**, il punto d'ingresso del modulo HR: lì c'è
> come funziona il motore, l'import e cosa è già stato portato. **Leggere prima quello.**

---

## 1. Da dove nasce

Il modulo HR è il port del programma **«Timbrature»** (VB.NET/WPF, sorgenti in
`C:\Users\diego\Desktop\Timbrature - API\`). Il port ha coperto il grosso — motore di
calcolo, import da Ecos, cartellino, calendario mensile con export Excel, solleciti
mensili, giustificazione delle ore mancanti — ma **non tutto**.

Il 28/08 si è fatto il censimento completo delle pagine originali (`MainWindow`,
`ReportPage`, `CalendarPage`, `SyncEcosPage`, `MailLogPage`, `Users`, `SettingsDialog`) e
si è confrontata ogni voce con quello che gira oggi. Questo file è l'elenco di cosa manca,
con la decisione presa su ognuna.

**Una premessa che spiega metà delle differenze.** L'originale aveva un *Report Ore* con
la colonna DIPENDENTE, cioè **tutti insieme**; da noi è diviso in **Cartellino
individuale** (una persona) + **Calendario mensile** (tutti). Alcune opzioni dell'originale
quindi non mancano: sono finite nell'altra pagina. Chi legge il VB e non lo sa finisce per
riportare due volte la stessa cosa.

## 2. Le decisioni (28/08/2026, prese una per una con Diego)

| # | Voce dell'originale | Decisione |
|---|---|---|
| 1 | 📧 Sollecito sulla **singola giornata** (per riga del Report) | ✅ **importare** |
| 2 | 🔄 **Risincronizza questo giorno** da Ecos (per riga del Report) | ✅ **importare** |
| 3 | Filtro **«📧 Da segnalare»** (solo le giornate con anomalia) | ✅ **importare** |
| 4 | **Anteprima integrale della mail** prima di spedire | ✅ **importare** |
| 5 | **Sincronizzazione di un mese scelto** (anno + mese) | ✅ **importare** |
| 6 | Pagina **«Cronologia Email»** | ✅ **importare, versione completa** (serve migrazione) |
| 7 | **Export del cartellino** individuale | ✅ **importare, in Excel** (non CSV) |
| 8 | **Avanzamento dell'import** a video (barra + log) | ✅ **importare** |
| 9 | Filtro «Solo timbratura / Tutti» (esclude i forfettari) | ❌ **no** |
| 10 | 🗑 **Cancella Mese** | ❌ **no** |
| 11 | Etichetta **«ultima sincronizzazione badge»** | ✅ **importare** |

### Perché i due NO — non riproporli

- **#9 filtro forfettari.** Il nostro cartellino è di **una persona alla volta**: o è
  forfettaria o non lo è, quindi il filtro non avrebbe niente da filtrare. Nel Calendario,
  dove i dipendenti stanno insieme, i filtri utili (reparto, persona) ci sono già.
- **#10 Cancella Mese.** Nell'originale cancellava `ProcessedReports` **e** `Timestamps`
  del mese. Da noi il grezzo (`hr_punches`) è **append-only** e le giornate (`hr_days`) si
  rigenerano da sole: quel pulsante è esattamente ciò che l'append-only serve a impedire.
  Per rifare un mese c'è la **#5**, che riscarica invece di cancellare.

## 3. Cosa c'è già e va riusato (non riscrivere)

| Serve per | Dove |
|---|---|
| Invio mail | `EmailService.QueueSimpleMail(to, toName, subject, textBody, htmlBody?)` · `EmailService.Enabled` |
| Solleciti già chiesti | tabella **`hr_reminders`** (M113): una riga per `(employee_id, work_date)`, `sent_at`, `sent_by`, `channel` (SMTP/MAILTO) |
| Registrare un sollecito | `HrAttendanceService.MarkReminders(year, month, solleciti, sentBy, channel)` — `INSERT … ON DUPLICATE KEY UPDATE` |
| Testo del sollecito mensile | `HrAttendanceService.TestoSollecito(nome, giorni, richiesta)` |
| Cartellino di una persona | `HrAttendanceService.GetMonthlyTimesheet(employeeId, year, month)` → `HrMonthlyTimesheetDto` / `HrDayDto` (con `Note`, `HasAnomaly`, `Raw`, `Normalized`) |
| Import da Ecos | `HrAttendanceService.ImportAsync(full, ct)` + `ImportInProgress` / `LastImport` (il servizio è **singleton**) |
| Stato per la pagina | `GET /api/hr/status` → `HrStatusDto` |
| Lettura badge da Ecos | `GET /api/hr/mapping/badges` → `EcosClient.BadgesAsync(token, ct)` |
| Excel del calendario | `HrCalendarExcel.Genera(calendario, employeeId?)` → `(byte[], nomeFile)`, EPPlus |
| Cursore/impostazioni di modulo | `app_config` (es. `hr_sync_punches_from`) |
| Permesso | tutto dietro la **scrittura** su `nav.hr_timbrature` (`CanManageTimbrature`) |

Pagine web: `atec-pm-web/src/features/hr/` — `TimbraturePage.tsx` (contenitore + 3 schede),
`CalendarioPresenzeView.tsx`, `QuadraturaPresenzeView.tsx`, `GiornataDialog.tsx`,
`MappaturaEcosDialog.tsx`, `CredenzialiEcosDialog.tsx`, `GiustificaCausaleDialog.tsx`.

## 4. L'ordine di lavoro

Tre gruppi, indipendenti fra loro: si può fermarsi alla fine di ognuno con tutto verde.

### Gruppo A — Solleciti (voci 1, 4, 6)

**A1. Migrazione `M116`** — allarga `hr_reminders` con quello che oggi non conserviamo:
`email VARCHAR(200) NULL`, `subject VARCHAR(300) NULL`, `body TEXT NULL`.
🪤 Le righe già scritte restano senza testo: la Cronologia deve saperlo mostrare («testo non
conservato») invece di far finta che sia una mail vuota.
🪤 La chiave unica `(employee_id, work_date)` resta: il secondo sollecito sullo stesso
giorno **aggiorna** la riga. Se si vuole la storia di *tutte* le mail e non solo
dell'ultima per giornata, allora serve una tabella a parte — **ma non è quello che è stato
chiesto**: la domanda vera resta «questo buco l'ho già chiesto?».

**A2. Sollecito della singola giornata** (voce 1). Port di `btnMailDipendente_Click` +
`BuildDettaglioAnomalia` (`ReportPage.xaml.vb`).
- Quando appare il pulsante — **la regola dell'originale, da copiare esatta**: la giornata
  **non è oggi** *e* la nota contiene una di
  `INCOMPLETO` · `ERR` · `Stimata` · `nessuna timbratura` · `Permesso rettificato` ·
  `Permesso annullato`.
  🪤 **Non usare `HrDayDto.HasAnomaly`**: da noi è `Note.StartsWith("⚠")` e prende solo
  `INCOMPLETO`. Resterebbe fuori `AUTO_P: Uscita mancante - Stimata 17:00`, che nell'originale
  il sollecito ce l'ha — ed è giusto: una pausa dedotta è una regola applicata con
  sicurezza, un'uscita **indovinata** è un buco vero che la persona deve confermare.
  🪤 Gli altri `AUTO_P` (pausa dedotta/forzata) **non** danno il pulsante: nell'originale
  non sono in elenco.
- Corpo della mail: `TIMBRATURE REGISTRATE` (E1/U1/E2/U2) + `PROBLEMA RILEVATO` (una frase
  per tipo di nota, sono nel VB righe 464-511) + `RISULTATO ELABORAZIONE` (ore e straordinario),
  dentro il saluto e la firma di `btnMailDipendente_Click` (righe 283-290).
  📌 L'oggetto originale era `[eTime] Segnalazione timbrature — dd/MM/yyyy`: **togliere
  `[eTime]`**, come è già stato fatto per il sollecito mensile.
- Stato «già inviato»: da `hr_reminders`, tooltip `Sollecito già inviato il dd/MM/yyyy HH:mm`
  e pulsante spento (nell'originale 📧 lampeggiante → ✉️ opaco).
- Endpoint: `GET /api/hr/day-reminder?employeeId=&date=` (testo pronto + stato) e
  `POST /api/hr/day-reminder` (spedisce e registra). Dietro la **scrittura**.

**A3. Anteprima integrale della mail** (voce 4). Port di `ConfirmDialog.ShowEmail`:
destinatario, oggetto e **corpo per intero** prima di spedire. Un solo dialogo riusabile,
usato sia dal sollecito mensile del Calendario sia da quello della giornata.
📌 Oggi il Calendario mostra solo il riepilogo dei destinatari (`riepilogo()` in
`CalendarioPresenzeView.tsx`): quello resta per l'invio multiplo, l'anteprima si aggiunge.

**A4. Cronologia Email** (voce 6). Port di `MailLogPage`: elenco con **data invio ·
dipendente · indirizzo · giorno di riferimento · oggetto · origine (canale) · inviata da**,
e il testo rileggibile aprendo la riga (era `MailDetailDialog`).
- Dove metterla: una **quarta scheda** in `/hr/timbrature` accanto a Cartellino, Calendario
  e Quadratura — non una voce di menu nuova (non serve una chiave di permesso in più).
- Endpoint `GET /api/hr/reminders/log` con filtri mese e dipendente. Dietro la **scrittura**.

### Gruppo B — Ecos (voci 2, 5, 8, 11)

**B1. Risincronizza una giornata** (voce 2). Port di `btnResyncDay_Click`: riscarica da
Ecos le timbrature di **quel dipendente per quel giorno** e ricalcola la giornata.
- Riusare il diff dell'import (`origine='ECOS'`, chiave `id_esterno`), **non** scrivere un
  secondo percorso di scrittura del grezzo.
- 🪤 Vale la regola delle cancellazioni: su una finestra di un giorno si ha la fotografia
  completa di quel giorno, quindi si possono togliere le righe che su Ecos non ci sono più
  — come fa l'import *completo*, non l'incrementale.
- 🪤 **Non spostare il cursore** `hr_sync_punches_from`: è un ripescaggio mirato, non un
  avanzamento dell'import. Spostarlo aprirebbe una finestra cieca.

**B2. Sincronizzazione di un mese scelto** (voce 5). Stessa logica di B1 ma sull'intervallo
del mese; `POST /api/hr/import/month?year=&month=`. Anche qui **il cursore non si tocca**.

**B3. Avanzamento dell'import a video** (voce 8). Port della barra + `txtLog` di
`SyncEcosPage`. Il servizio è **singleton**: ci sta uno stato in memoria (fase corrente,
pagina scaricata, contatori, ultime righe di log) che la pagina interroga mentre l'import
gira, e `GET /api/hr/status` lo espone.
🪤 Lo stato è **in memoria**: un riavvio del servizio a metà import lo azzera, e la pagina
deve saperlo dire («import non più in corso») invece di restare girando per sempre.

**B4. Ultima lettura badge** (voce 11). Salvare in `app_config` (es.
`hr_last_badge_read`) il momento dell'ultima `BadgesAsync` riuscita, ed esporlo in
`HrStatusDto` accanto a `LastImport`. Una riga di testo nella pagina.

### Gruppo C — Cartellino (voci 3, 7)

**C1. Filtro «📧 Da segnalare»** (voce 3). Interruttore che lascia solo le giornate che
soddisfano la regola di **A2** (stessa funzione, non una seconda copia: se divergono, il
filtro mostra righe senza pulsante e viceversa).

**C2. Export Excel del cartellino** (voce 7). Il foglio della **persona aperta**, nello
stile di `HrCalendarExcel` (stessi colori e intestazioni). Non CSV: l'ufficio riceve già
Excel dal Calendario.
📌 L'originale esportava in CSV il report di *tutti*; qui è per persona, perché è così che
è fatta la pagina.

## 5. Regole di casa da non violare

- **Fedeltà al programma originale** ([[hr-fedelta-programma-originale]]): l'interfaccia non
  si reinventa. Dove l'originale ha una regola (quali causali, quando appare un pulsante),
  si copia quella, non se ne inventa una «più sensata».
- Il **grezzo non si corregge mai**: le rettifiche sono righe `origine='RETTIFICA'`.
- **Nessuno rettifica sé stesso** (vale per le rettifiche; **non** per la giustificazione
  delle ore, che l'ufficio fa da anni sul proprio cartellino).
- Ogni scrittura del `HrController` va dietro `CanManageTimbrature`, e il controller sta
  nella lista di `CancelloCommessaTests`? **No** — HR non è roba di commessa, ma se si
  aggiungono endpoint di scrittura altrove, quel test va guardato.
- **Deploy solo su ordine di Diego.** Verificare con `deploy\prova-test.ps1` (registra il
  verde) e poi `deploy\aggiorna-server.ps1` **nudo, in background**, mai dal tool Bash.

## 6. Come si verifica

- Test .NET: `ATEC.PM.Tests` — mettere sotto test almeno **la regola di A2** (quali
  giornate danno il sollecito) e **B1** (la risincronizzazione di un giorno non sposta il
  cursore e toglie le righe sparite da Ecos). Sono le due che, se sbagliano, sbagliano in
  silenzio.
- Client: `npm run build` (`tsc -b` + vite) e `npm run lint` — il conteggio dei problemi
  deve restare quello di prima (oggi 244: sono tutti preesistenti).
- Nel client web **non c'è un test runner**: la logica che vale la pena difendere va tenuta
  nel server.

## 7. Stato

**Niente di questo elenco è ancora stato scritto.** Ultimo deploy in produzione:
`20260828-1452`, schema **v115** — dentro c'è il modulo HR fino alla giustificazione delle
ore mancanti col menu contestuale, non oltre.
