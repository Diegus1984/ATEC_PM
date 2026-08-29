# ATEC PM — Gestione Segnalazioni: piano operativo

> **STATO 18/08/2026 ore 14:31 — L1-L9 IN PRODUZIONE.**
> Build `20260818-1431`, schema **v99 → v100** (M100 riuscita in 124 ms, 100/100 senza buchi),
> 180 test verdi al cancello. Verificato in produzione: servizio Running,
> `/api/health/ready` 200 `database: ok`, `version.json` e bundle allineati al locale,
> colonne nuove presenti, `/api/bug-reports` e `/counts` 200 con `isReply` e `createdById`
> valorizzati. Backup prima: `C:\ATEC_Backups\atec_pm_prima_v100_20260818.sql`.
> Revisione del 18/08 (dopo l'implementazione), difetti trovati e corretti:
> campanella «risolto» che ripartiva a ogni ritocco della risposta invece che alla sola
> transizione · `fixed_in_build` che restava alla prima risoluzione anche dopo una
> riapertura · archivio senza cancello sul server (l'API accettava anche segnalazioni
> aperte) · Error Boundary che restava bloccato sull'errore anche cambiando pagina ·
> build inventata quando manca `version.json` · copia BUG-NNN duplicata in due file.

Documento di lavoro per chi implementa (agente o sviluppatore). Le proposte sono già
state **filtrate e ordinate**: quello che sta nella sezione 3 si fa, quello che sta
nella sezione 4 **non** si fa — con la motivazione scritta, per non riproporlo tra un mese.

**Criterio di selezione.** ATEC PM ha ~10 utenti, ~100 segnalazioni in tutto e **una
sola persona** che fa triage, correzione e deploy. Le funzioni che riducono il costo del
*triage di squadra* (assegnatari, thread di commenti, deduplica) qui non pagano: il collo
di bottiglia è **la qualità della segnalazione in ingresso** e **il ritorno di
informazione al segnalatore**. Il piano ottimizza quei due, e nient'altro.

---

## 1. Stato attuale (verificato nel codice, non a memoria)

**Modulo web** — `/segnalazioni`
- Backend: `ATEC.PM.Server/Controllers/BugReportsController.cs` (rotta `api/bug-reports`),
  `ATEC.PM.Server/Services/BugReportsDbService.cs`.
- Frontend: `atec-pm-web/src/features/bug-reports/` (`BugReportsPage.tsx`,
  `BugReportDialog.tsx`, `BugAttachmentThumb.tsx`, `bug-report-utils.ts`).
- DTO: `ATEC.PM.Shared/DTOs/BugReports_DTOs.cs`.
- Tabelle: `bug_reports` (kind, title, description, area, severity, status, admin_note,
  created_by, created_at, updated_at, resolved_at, row_version) e
  `bug_report_attachments` (i file stanno **fuori** dalla cartella servita staticamente e
  si scaricano solo dall'endpoint autenticato).
- Permessi: `nav.bug_reports`, `action.manage_bug_reports`, `data.bug_reports_all`.
  **Dalla #93 ognuno vede solo le proprie**, vista completa a chi ha `data.bug_reports_all`.
- Realtime: hub `bugs-all` (`atec-pm-web/src/lib/signalr/use-bug-reports-hub.ts`).

**Registro di sviluppo** — `ATEC_PM/BUGS.md`, compilato a mano con riproduzione, causa
radice, test e build di rilascio.

**Cose già presenti che i lotti riusano — da NON reinventare:**
- `atec-pm-web/src/lib/app-version.ts` → `APP_BUILD`, l'id della build che il browser sta
  davvero eseguendo. Il contesto della segnalazione lo prende da lì.
- `atec-pm-web/src/features/commesse/chat/ChatComposer.tsx` → ha già l'incolla immagine
  dagli appunti: è il pattern da copiare.
- `ATEC.PM.Server/Middleware/ExceptionHandlingMiddleware.cs` → su ogni 500 restituisce già
  al client `Riferimento per l'assistenza: <TraceIdentifier>`, e lo stesso id finisce nel
  log del server. È il «buffer diagnostico» che serve, già fatto e senza dati sensibili.
- `ATEC.PM.Server/Services/NotificationService.cs` → `Create(...)` crea una notifica
  puntuale con destinatari espliciti (la campanella).
- `ATEC.PM.Server/Migrations/AiutiMigrazione.cs` → `AddColumnIfMissing(...)`,
  `CreaIndiceSeManca(...)`.

---

## 2. Come si legge il piano

Ogni lotto è indipendente e rilasciabile da solo; l'ordine è quello di esecuzione.
Per ognuno: **obiettivo → file → cosa fare → come si verifica**.
L1-L4 cambiano davvero la vita quotidiana; L5-L8 sono il contorno utile.

---

## 3. Lotti da realizzare

### L1 — Incolla screenshot con Ctrl+V *(il più utile, il più economico)*

**Obiettivo.** Oggi per allegare una schermata bisogna prima salvarla su disco. Con
`Win+Shift+S` l'immagine è già negli appunti: deve bastare Ctrl+V dentro il dialogo.

**File.** `atec-pm-web/src/features/bug-reports/BugReportDialog.tsx`
(pattern già scritto in `features/commesse/chat/ChatComposer.tsx`).

**Cosa fare.**
- `onPaste` sul contenuto del dialogo: leggere `event.clipboardData.items`, prendere le
  voci immagine, convertirle in `File` con nome parlante (`incolla-1.png`, `incolla-2.png`…)
  e aggiungerle a `pendingFiles`.
- Nessuna modifica al backend: l'upload avviene già dopo il salvataggio con
  `uploadBugAttachment`.
- Aggiornare il testo di aiuto: «Trascina o incolla (Ctrl+V) uno screenshot».

**Verifica.** Screenshot incollato in una segnalazione nuova → arriva come allegato; idem
su una esistente. L'incolla di testo dentro i campi non deve cambiare comportamento.

---

### L2 — Contesto tecnico catturato in automatico

**Obiettivo.** Sapere **dove** è successo e **su quale build**, senza chiederlo all'utente.

**File.**
- Migrazione: `ATEC.PM.Server/Migrations/M100_ContestoSegnalazioni.cs` (vedi §5.1).
- `ATEC.PM.Server/Services/BugReportsDbService.cs` (`InitTables`),
  `BugReportsController.cs`, `ATEC.PM.Shared/DTOs/BugReports_DTOs.cs`,
  `atec-pm-web/src/lib/api/types/bug-reports.ts`, `BugReportDialog.tsx`.

**Cosa fare.**
- Colonna `context TEXT NULL` su `bug_reports`, valorizzata **solo alla creazione**: è una
  fotografia del momento, non un campo modificabile.
- Il client compone il contesto all'apertura del dialogo: rotta corrente con query
  (`location.pathname + location.search`), `APP_BUILD`, `navigator.userAgent`, dimensione
  del viewport e **l'ultimo errore API visto** (L5).
- Nel dialogo, blocco «Contesto» in sola lettura, collassato di default (`<Collapsible>`,
  standard accordion del progetto), visibile a chi ha aperto la segnalazione e a chi la gestisce.
- **Il campo `area` resta testo libero come oggi**: non va sostituito da una tendina (§4.1).

**Verifica.** Apro una segnalazione da `/commesse/123?tab=ddp` → il contesto salvato riporta
quella rotta e la build corrente; riaprendo la segnalazione il contesto è invariato.

---

### L3 — Pulsante «Segnala un problema» in ogni pagina

**Obiettivo.** Aprire la segnalazione **da dove è successo**, senza navigare via — che è
anche la condizione perché il contesto di L2 sia veritiero.

**File.** `atec-pm-web/src/app/AppShell.tsx` (menu utente in alto a destra, accanto a
`NotificationsBell`), riusando `BugReportDialog` con `bug={null}`.

**Cosa fare.**
- Voce di menu «Segnala un problema», con il dialogo montato nell'AppShell.
- Mostrarla solo a chi ha scrittura su `nav.bug_reports` (`canWriteFeature`), coerente con
  quanto fa già la pagina.
- Dopo l'invio: toast di conferma e **nessuna navigazione forzata** — l'utente stava
  facendo altro e ci deve tornare.

**Verifica.** Da tre pagine diverse apro il dialogo, invio, resto dove ero; la segnalazione
compare in `/segnalazioni` con la rotta giusta nel contesto.

---

### L4 — Error Boundary React *(oggi non ne esiste nessuno)*

**Obiettivo.** Il crash di un componente oggi lascia la **pagina bianca**: l'utente non ha
niente da segnalare e noi non sappiamo cosa sia successo.

**File.** nuovo `atec-pm-web/src/app/ErrorBoundary.tsx`, agganciato in `AppShell.tsx`
attorno all'area delle rotte — **non** attorno a tutta l'app: la barra laterale deve
sopravvivere al crash di una pagina.

**Cosa fare.**
- Componente a classe con `componentDidCatch`; fallback leggibile: cosa è successo in una
  riga, pulsante «Ricarica la pagina» e pulsante «Segnala il problema».
- «Segnala il problema» apre il dialogo con il titolo precompilato e messaggio + stack
  (troncato a ~2000 caratteri) dentro il contesto di L2.
- Stack in console solo in sviluppo; in produzione niente stack a schermo.

**Verifica.** Componente che lancia di proposito → compare il fallback, la barra laterale
resta al suo posto, il pulsante apre il dialogo con lo stack nel contesto.

---

### L5 — Ultimo errore API nel contesto *(al posto del «network buffer»)*

**Obiettivo.** Legare la segnalazione all'errore che il server ha **già** registrato, senza
portare payload nel browser.

**File.** `atec-pm-web/src/lib/api/client.ts` (`ApiError`), nuovo
`atec-pm-web/src/lib/api/last-error.ts`, consumato dal contesto di L2.

**Cosa fare.**
- Quando una chiamata fallisce, tenere in memoria **solo**: endpoint, metodo, codice HTTP,
  ora e messaggio del server — che in produzione contiene già il
  `Riferimento per l'assistenza: <id>` prodotto dal middleware.
- Conservare **l'ultimo** errore, non una cronologia; allegarlo al contesto solo se
  avvenuto negli ultimi 5 minuti.
- **Mai** salvare corpi di richiesta o di risposta: contengono costi, ricarichi e dati
  cliente (§4.3).

**Verifica.** Provoco un 500, apro la segnalazione: nel contesto compaiono endpoint e
riferimento, e lo stesso id si ritrova nel log del server.

---

### L6 — «Copia per l'analisi» (blocco BUG-NNN)

**Obiettivo.** Portare una segnalazione dal gestionale alla sessione di correzione in un
clic. Oggi le segnalazioni si leggono sul **database di produzione via SSH**: è l'attrito
più grosso del flusso reale, e nessuna delle proposte originali lo toccava.

**File.** `BugReportDialog.tsx` (più, se comodo, una voce nel menu riga di `BugReportsPage.tsx`).

**Cosa fare.**
- Pulsante per chi gestisce (`action.manage_bug_reports`): copia negli appunti un blocco
  Markdown con id, tipo, gravità, stato, autore, data, area, **contesto**, descrizione,
  risposta corrente ed elenco allegati con l'URL dell'endpoint di download.
- Intestazione allineata a `BUGS.md` (`## BUG-NNN — <titolo>`), così si incolla direttamente
  nel registro o nel prompt di correzione.
- Nessun endpoint nuovo: i dati sono già nel DTO che il client ha in mano.

**Verifica.** Copia da una segnalazione con 2 allegati → il blocco incollato è completo e i
link scaricano i file da utente autenticato.

---

### L7 — Build di risoluzione + notifica al segnalatore

**Obiettivo.** Chiudere il cerchio: chi ha segnalato deve sapere che è risolto **e su quale
build**. Il banner «Aggiorna adesso» non ricarica da solo, quindi capita di risegnalare un
bug già corretto.

**File.** migrazione (la stessa di L2 se non è ancora stata applicata),
`BugReportsController.cs` (`UpdateStatus`), `BugReportsDbService.cs`, DTO,
`BugReportDialog.tsx`, `BugReportsPage.tsx`,
`atec-pm-web/src/features/notifications/notification-navigation.ts`.

**Cosa fare.**
- Colonna `fixed_in_build VARCHAR(40) NULL`, **valorizzata dal server** quando lo stato passa
  a `RESOLVED`, leggendo la build corrente (lo stesso `version.json` servito da `Program.cs`).
  Non è un campo da digitare a mano.
- Mostrarla nel dialogo e come colonna in griglia; se si aggiunge la colonna, **alzare**
  `visibilityStorageKey` da `bug-reports-columns-v3` a `-v4` (§5.4).
- Alla transizione a `RESOLVED`: `NotificationService.Create` puntuale con
  `type = "BUG_RESOLVED"`, `refType = "BUG_REPORT"`, `refId = id`, destinatario **il solo**
  `created_by`, messaggio «Risolto nella build X: aggiorna e verifica». Aggiungere la
  destinazione in `notification-navigation.ts`.
- **Niente notifica su `IN_PROGRESS`**: con una sola persona che corregge è rumore (§4.6).
- Non toccare i generatori periodici di `NotificationService`: questa è una creazione
  puntuale dentro il cambio di stato.

**Verifica.** Porto una segnalazione a RESOLVED: `fixed_in_build` si valorizza da sola,
la campanella arriva al solo segnalatore, il clic sulla notifica apre la segnalazione.

---

### L8 — Archivio al posto della cancellazione a mano

**Obiettivo.** Il 17/08 sono state cancellate 28 segnalazioni risolte a mano, con backup
manuale. Serve una via ordinaria che non passi dal `DELETE`.

**File.** migrazione, `BugReportsDbService.cs`, `BugReportsController.cs`, `BugReportsPage.tsx`.

**Cosa fare.**
- Colonna `archived_at DATETIME NULL`. Le viste normali e i contatori escludono le
  archiviate; nuova vista «Archivio» che mostra solo quelle.
- Azione «Archivia» per chi gestisce, ammessa solo su `RESOLVED`/`REJECTED`, con conferma
  tramite `useConfirm` (mai `window.confirm`).
- La `DELETE` esistente resta, ma smette di essere la strada normale.

**Verifica.** Archivio due segnalazioni risolte: spariscono da lista e contatori, si
ritrovano in «Archivio», e le altre sessioni si aggiornano da sole via hub.

---

### L9 — Foto allegate alla RISPOSTA *(richiesta esplicita)*

**Obiettivo.** Oggi gli allegati sono solo «della segnalazione». Chi risponde deve poter
allegare foto **alla risposta** (la schermata del prima/dopo, la correzione fatta), e il
segnalatore deve vederle come tali.

**Punto di partenza — metà è già fatta.** Chi gestisce può già caricare file su una
segnalazione altrui (`CheckCanEdit` lascia passare `PuoGestireSegnalazioni`), la tabella
`bug_report_attachments` ha già `created_by`, e dal 18/08/2026 `created_by` viaggia nel DTO
(`BugAttachmentDto.CreatedById`) e comanda chi vede il cestino. Quello che manca è
**distinguere i due tipi**: oggi finisce tutto nello stesso elenco, indistinguibile.

**File.** migrazione M100 (la stessa degli altri lotti), `BugReportsDbService.cs`
(`InitTables`), `BugReportsController.cs` (`UploadAttachment`, `DeleteAttachment`),
`ATEC.PM.Shared/DTOs/BugReports_DTOs.cs`, `atec-pm-web/src/lib/api/bug-reports.ts`,
`atec-pm-web/src/lib/api/types/bug-reports.ts`, `BugReportDialog.tsx`.

**Cosa fare.**
- Colonna `is_reply TINYINT(1) NOT NULL DEFAULT 0` su `bug_report_attachments`.
  **Flag esplicito, non dedotto dall'autore**: chi gestisce può anche essere il segnalatore,
  e in quel caso «chi ha caricato» non distingue niente.
- `POST api/bug-reports/{id}/attachments` accetta `isReply`. Il valore **si accetta solo da
  chi gestisce** (`PuoGestireSegnalazioni`): a chiunque altro si forza `0`, non si risponde
  errore.
- `BugAttachmentDto` espone `IsReply` e `CreatedByName` (utile anche sugli allegati vecchi:
  il dato in tabella c'è già).
- Nel dialogo, due zone separate:
  - «Allegati» → com'è oggi, quelli del segnalatore;
  - dentro il riquadro **Risposta**, una zona allegati propria, con lo stesso incolla Ctrl+V
    di L1. Chi gestisce la usa in scrittura; **il segnalatore la vede e scarica in sola
    lettura** — è il motivo per cui esiste la funzione.
- `DeleteAttachment`: **già sistemato il 18/08/2026**, prima e indipendentemente da questo
  lotto — un allegato lo toglie chi l'ha caricato (`bug_report_attachments.created_by`) o chi
  gestisce le segnalazioni, non più chiunque possa modificare la segnalazione
  (`BugReportsController.PuoEliminareAllegato`, con i test in
  `ATEC.PM.Tests/Permessi/VisibilitaSegnalazioniTests.cs`). La regola vale già per le foto di
  risposta caricate finora: **non va rifatta né allentata** quando arriva `is_reply`.
- Gli allegati già esistenti restano `is_reply = 0`: nessun backfill, nessuna deduzione
  retroattiva su quelli caricati finora.

**Verifica.** Da amministratore allego una foto nella risposta e passo a RESOLVED: il
segnalatore la vede nella sezione «Risposta», la scarica, e **non** può eliminarla; le sue
foto restano nell'elenco di sopra. Un utente normale che tenti l'upload con `isReply=1`
ottiene un allegato normale.

---

## 4. Proposte scartate (e perché)

### 4.1 — Tendina di macro-aree al posto del campo `area`
Una lista fissa di moduli invecchia a ogni funzione nuova e va manutenuta a mano, mentre la
**rotta catturata da L2 dà la stessa informazione, più precisa e gratis**. Se un giorno
servirà filtrare per modulo, si deriva l'area dal path — non si chiede all'utente di
classificare.

### 4.2 — Thread di commenti al posto di `admin_note`
È la voce più costosa dell'intero documento (tabella nuova, payload dell'hub, interfaccia,
permessi, notifiche per ogni commento) a fronte del guadagno più incerto: con 2-3 persone il
chiarimento avviene a voce in mezzo minuto. Da riconsiderare **solo** se il numero di
segnalatori cresce molto.

### 4.3 — Buffer client delle ultime chiamate 4xx/5xx con payload
I payload contengono costi, ricarichi e dati cliente: «sanitizzato» vuol dire giorni di
lavoro e un rischio permanente di far uscire dati dentro un allegato. L'80% del valore si
ottiene con **L5**, che porta solo il riferimento a un errore già registrato nel log.

### 4.4 — Rilevamento duplicati mentre si scrive il titolo
Va **contro la #93**: mostrerebbe titoli di segnalazioni altrui a chi non ha la vista
completa, riaprendo la visibilità appena chiusa. E con ~100 segnalazioni quasi tutte chiuse
il duplicato è un problema che non esiste.

### 4.5 — Assegnatario del ticket
C'è una sola persona che risolve: un campo con un solo valore possibile non è informazione.

### 4.6 — Notifica di «preso in carico»
Le correzioni avvengono in giornata: l'avviso intermedio è rumore che fa ignorare anche
quello che conta (la notifica di risoluzione, L7).

---

## 5. Regole di progetto da rispettare (valgono per tutti i lotti)

**5.1 Migrazioni.** Una migrazione = **un file** in `ATEC.PM.Server/Migrations/MNNN_Cosa.cs`
che implementa `IMigrazione`: si crea il file e basta, niente costanti da alzare. L'ultima
presente è `M099_VerificaScaricoOre.cs`, quindi si parte da **M100**; le colonne di questo
piano (`context`, `fixed_in_build`, `archived_at` su `bug_reports`, `is_reply` su
`bug_report_attachments`) stanno bene in un'unica migrazione, scritte con
`AddColumnIfMissing` (idempotente).
Le stesse colonne vanno aggiunte **anche** al `CREATE TABLE IF NOT EXISTS` di
`BugReportsDbService.InitTables`, altrimenti un database creato da zero nasce senza: su
database esistente l'ordine è `EnsureModuleTables` → migrazioni (quindi l'`ALTER` trova la
tabella), su database nuovo lo schema arriva dai `CREATE TABLE`. Le due strade devono
restare allineate.
Se una migrazione fallisce **il server non parte**: è voluto, non va reso `Facoltativa` per
farlo avviare lo stesso.

**5.2 Permessi.** La visibilità della #93 non si tocca: ognuno vede solo le proprie, vista
completa a chi ha `data.bug_reports_all`. Nessuna funzione nuova deve esporre il contenuto
di segnalazioni altrui.

**5.3 Realtime e concorrenza.** Ogni scrittura passa dall'hub `bugs-all`. Il `row_version` si
gestisce come già fa il dialogo — in particolare il cambio di stato **non** rifà il controllo
di concorrenza dopo un salvataggio del contenuto: è deliberato, non va «sistemato».

**5.4 Griglie.** Colonne nuove → `ColumnsMenu` con `visibilityStorageKey` **versionata**
(`bug-reports-columns-v3` → `-v4`): senza il cambio di chiave, chi ha già preferenze salvate
non vedrà mai la colonna nuova.

**5.5 Interfaccia.** Conferme con `useConfirm`; date con `formatDateShort` /
`formatDateTimeShort`; blocchi apribili con `<Collapsible>` e i token `--accordion-duration`
/ `--accordion-ease`; griglie inline piatte a riposo.

**5.6 Verifica.** Type-check con `npm run build` (oppure `tsc -b`): **`npx tsc --noEmit` esce
0 senza controllare niente**. I `.tsx` vanno scritti senza BOM. I test di `ATEC.PM.Tests`
devono restare verdi: il deploy ha il cancello e si ferma se sono rossi.
Node non è nel PATH: `$env:Path = "C:\Program Files\nodejs;" + $env:Path`.

**5.7 Deploy.** Nessun lotto va in produzione senza il giro solito (`aggiorna-server.bat`) e
senza aver verificato che compaia il banner di nuova versione.

---

## 6. Ordine di rilascio consigliato

| Rilascio | Lotti | Perché insieme |
|---|---|---|
| **1** | L1 · L2 · L3 · L9 | Sono la stessa cosa vista dall'utente: segnalare in fretta e bene da dove è successo, e ricevere una risposta con le foto. Stesso dialogo, una sola migrazione, un solo deploy. |
| **2** | L4 · L5 | Diagnostica: l'app smette di morire in bianco e la segnalazione porta con sé il riferimento all'errore. |
| **3** | L6 · L7 · L8 | Chiusura del cerchio: analisi rapida, ritorno al segnalatore, archivio al posto delle cancellazioni a mano. |
