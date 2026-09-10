# PIANO HR — Timbrature, ferie e permessi dentro ATEC PM

> **Punto d'ingresso del modulo HR.** Documento di consegna: chi riprende il lavoro
> (persona o sessione) legge questo file e sa dove siamo, cosa è già stato deciso e
> cosa manca. Ultimo aggiornamento: **27/08/2026 (sera: Fase 1 quasi completa)**.

---

## 1. In una riga

Portare dentro ATEC PM la **timbratura delle ore**, le **richieste di ferie** e le
**richieste di permesso**, che oggi vivono in **EcosAgile** (il sistema che in azienda
chiamiamo «eTime»), riusando il motore di calcolo del cartellino **già scritto** e
aggiungendo la parte che oggi non esiste da nessuna parte: il flusso
richiesta → approvazione agganciato ai reparti e alle commesse.

## 2. Stato attuale (27/08/2026)

| Pezzo | Stato |
|---|---|
| Sezione **HR** nel menu del gestionale | ✅ creata (voci `planned`, vedi §7) |
| Chiavi permesso `nav.hr_timbrature`, `nav.hr_richieste` | ✅ a catalogo, spente (livello 3 = solo Admin) |
| Motore di calcolo del cartellino | ✅ **portato in C# e verificato**: 330/330 giornate identiche all'originale (§6 Fase 1) |
| Vista mensile, export Excel, solleciti, tre colonne | ✅ **fedeli al programma originale** (28/08, §Fase 1) |
| Import timbrature da Ecos (client API) | ✅ **portato in C#** (`EcosClient` + `HrPresenzeService` + import automatico ogni ora, era 12h), pagina Timbrature **live**; credenziali Ecos da mettere in produzione, ora dalla pagina (§6 Fase 1) |
| Flusso richiesta → approvazione ferie/permessi | ❌ non esiste da nessuna parte — **è la parte nuova** |
| Quadratura presenze ↔ ore su commessa | ❌ non esiste |
| Tabella `absences` in produzione | ⚠️ esiste, **vuota**, da rifare (§6, Fase 2) |
| Tabella `holidays` in produzione | ⚠️ esiste, **vuota** |

Numeri reali di produzione al 27/08/2026: **37 dipendenti attivi**, **12 responsabili di
reparto** (`employee_departments.is_responsible = 1`).

## 3. Le decisioni già prese

1. **Paghe, LUL e ratei ufficiali restano al consulente del lavoro.** ATEC PM li *mostra*
   come dato indicativo, non li ricalcola. Motivo: dipendono da CCNL, anzianità e
   part-time, cambiano a ogni rinnovo, e il giorno in cui ATEC PM mostrasse un residuo
   ferie diverso dal cedolino il modulo perderebbe credibilità in una settimana.
2. **Scenario A: Ecos resta il rilevatore, ATEC PM diventa il posto dove si lavora.**
   Le timbrature continuano a nascere nei terminali Ecos; ATEC PM le importa, ci porta
   dentro le richieste, e aggiunge la quadratura con le commesse. Niente hardware nuovo,
   niente responsabilità sul dato fiscale.
   *(Scenario B — ATEC PM sostituisce anche la timbratura — resta valutabile più avanti,
   a motore rodato. Il canone si toglie per ultimo, non per primo.)*
3. **Sotto-sezioni del menu HR: due, non tre.** «Ferie» e «permessi» sono lo stesso flusso
   con causale diversa: una sola pagina. Se si vogliono separate, è una riga in
   `navigation.ts` più una chiave a catalogo.

## 4. Il lavoro già fatto — progetto «Timbrature»

**Dove**: `C:\Users\diego\Desktop\Timbrature - API\` — applicazione **VB.NET / WPF**,
~6.200 righe, database locale **SQLite**.
⚠️ **Git è stato rimosso da quel progetto il 27/08/2026** su richiesta (conteneva
credenziali in chiaro nei commit, mai spinti su GitHub). I file sono intatti; la storia no.

### Cosa fa già, e che va riusato invece di riscritto

| File | Cosa contiene di prezioso |
|---|---|
| `Api/EcosApiManager.vb` | Client API EcosAgile: token, timbrature, badge, richieste assenza approvate |
| `Classes/ReportProcessor.vb` | Calcolo del cartellino: 2 entrate / 2 uscite, pausa, turni, anomalie |
| `Classes/GVL.vb` | **Le costanti CCNL** — vedi sotto |
| `Classes/DatabaseManager.vb` | Schema SQLite: `Employees`, `Timestamps`, `ProcessedReports`, `Absences`, `MailLog` |
| `CalendarPage.xaml.vb` | Calendario mensile con righe FERIE / PERMESSI / MALATTIA |
| `ReportPage.xaml.vb` | Report mensile e invio mail ai dipendenti |

**Regole di calcolo già codificate** (`GVL.vb`) — sono il vero patrimonio, sono state
tarate sul campo e non vanno reinventate:

- giornata standard 480 min, **pausa minima 30 min**, **pausa forzata 60 min**
- **scatto 30 min**, **tolleranza 10 min**, notturno **dalle 22:00**
- maggiorazioni straordinario per fascia secondo la **Circolare n. 12 del 23.12.2024**
  (CCNL metalmeccanici, colonna «Non a turni»): diurno 20%, notturno fino alle 22 25%,
  oltre le 22 35%, festivo 55%, festivo con riposo compensativo 10%, straordinario
  festivo 55%, notturno prime 2h 50% poi 60%, notturno festivo 35%, e le fasce L/M.
- causali assenza in uso: **FE** ferie · **PE** permesso/ROL · **MA** malattia · **IN** infortunio
- per dipendente: `IsForfait` + `ForfaitHours`, `IncludeOvertime`

### Come dialoga con Ecos

> **Manuale completo dell'API Ecos** (protocollo, filtri, paginazione, errori, scrittura,
> catalogo, ricette per le chiamate nuove, sonda): **[../guide/ECOS-API-MANUALE.md](../guide/ECOS-API-MANUALE.md)**
> — scritto il 08/09/2026 dalla guida ufficiale v4.1.1 e dai sample di SoftAgile (cartella `ECOS/`).

- Base URL: `https://ha.ecosagile.com/dd/api.pm?ApiName=` — **un solo dispatcher**,
  l'operazione è scelta dal parametro `ApiName`.
- Autenticazione: `TokenGet` (POST con `Userid`, `Password`, `ClientID`) → `AuthToken`.
- Risposta JSON: `ECOSAGILE_TABLE_DATA.ECOSAGILE_DATA.ECOSAGILE_DATA_ROW`, esito in
  `ECOSAGILE_ERROR_MESSAGE.CODE` (`OK` / `FAIL`). Paginazione `PageNumber`/`RowsPerPage`/`DF=1`.

## 5. 🔑 Cosa sappiamo delle API Ecos (verificato sul campo il 27/08/2026)

Prova eseguita chiamando gli endpoint **senza parametri** (quindi senza poter scrivere
nulla) e confrontando la forma degli errori. Verificato dopo la prova che **nessun dato è
stato creato**.

**La convenzione di scrittura è `...Post`, NON `...Ins`.**

| ApiName | Esito |
|---|---|
| `PeopleStampGetAll` · `PeopleBadgeGetAll` · `PeopleAbsenceRequestGetAll` | ✅ lettura, funzionanti |
| `PeopleAbsenceRequestPost` | ✅ **esiste** — bloccato: *«User … doesn't have the Service/Right to execute the API. ServiceID request»* |
| `PeopleOvertimeRequestPost` | ✅ **esiste** (richieste straordinario) — stesso blocco di diritti |
| `PeopleStampPost` | ✅ **esiste e l'utente HA già i diritti** (errore di sola validazione dati) |
| `PeoplePost` | ✅ esiste, diritti già attivi |
| `...Ins`, `...Upd`, `...Set`, `...Add`, `...Save`, `...Create`, `...Delete` | ❌ non esistono |
| `PeopleGetAll`, `PeopleAbsenceTypeGetAll`, `PeopleDepartmentGetAll` | ❌ non esistono |

**Conseguenza per il progetto**: si può scrivere in Ecos. Il dipendente chiede da ATEC PM,
la richiesta approvata viene scritta in Ecos con `PeopleAbsenceRequestPost`, Ecos resta il
padrone del dato e il consulente continua a ricevere quello che riceve oggi:
**niente doppio inserimento, niente doppia verità**.

**Manca una sola cosa, ed è una configurazione**: il diritto sul **ServiceID `request`**
per l'utente API. Lo concede l'amministratore Ecos o SoftAgile.

### Da chiedere a SoftAgile (info@ecosagile.com, 02 89054136)

0. **(nuove, dal port del client — 27/08 sera)** (a) Esiste un parametro di **ordinamento
   stabile** per `PeopleStampGetAll`? Senza, durante un import lungo una riga può scivolare
   fra due pagine e sparire. (b) Che valori assume **`StatusCode`** su una timbratura, e
   come si riconosce una timbratura **annullata**? (c) Quando una timbratura viene
   **cancellata** su Ecos resta una traccia (tombstone), o sparisce e basta?
1. Abilitare l'utente API al **ServiceID `request`** per `PeopleAbsenceRequestPost` e
   `PeopleOvertimeRequestPost`.
2. **Tracciato dei parametri** di `PeopleAbsenceRequestPost`: campi obbligatori, codici
   categoria (in uso: `F` = ferie, `P` = permesso/ROL), come si esprime la mezza giornata,
   e **se la richiesta creata nasce PENDING o ACCEPTED** → decide se l'approvazione resta
   in Ecos o passa ad ATEC PM.
3. Elenco completo degli `ApiName`, con conferma della convenzione `...Post`.

## 6. Il piano a fasi

### Fase 0 — verifiche (mezza giornata) — *parzialmente fatta*
- ✅ `absences` e `holidays` esistono in produzione, entrambe **vuote**.
- ✅ **Nessuno storico da migrare**: lo SQLite del progetto Timbrature contiene solo dati
  di prova (2-24 febbraio 2026: 932 timbrature, 379 cartellini, 48 dipendenti di cui 23
  attivi). La storia vera sta in Ecos: si riparte puliti reimportando da lì. Quei 379
  cartellini sono però diventati il **banco di prova** del port (vedi Fase 1).
- ⬜ **Decisione ferma da tre mesi** (`TODO.md:156`): le festività infrasettimanali
  consumano un giorno di ferie? Oggi il planner dice di sì perché esclude solo i weekend
  (`planner-logic.ts`), ed è sbagliato.

### Fase 1 — porto il motore dentro ATEC PM (2-3 settimane) — **INIZIATA 27/08/2026**

> **Fatto finora**: il **motore di calcolo è portato in C# e verificato**.
> `ATEC.PM.Server/Services/Hr/RegoleCartellino.cs` (soglie, arrotondamenti, maggiorazioni
> CCNL) e `MotoreCartellino.cs` (raggruppamento timbrature, assegnazione, riconoscimento
> turni, pausa dedotta, scomposizione straordinario per fascia). Classe **pura**: niente
> database, niente orologio di sistema — «oggi» si passa da fuori.
>
> **La rete di sicurezza**: `ATEC.PM.Tests/Hr/cartellini-collaudo.json` contiene **379
> giornate vere** calcolate dal motore VB in esercizio (2-24 febbraio 2026), e
> `MotoreCartellinoTests` confronta il port campo per campo. Esito: **330 su 330 giornate
> con timbrature identiche**, comprese pausa dedotta, turni riconosciuti, anomalie e le
> 78 con straordinario. Le 49 senza timbrature (forfait/assenze piene) sono escluse: non
> le produce il motore ma la riconciliazione assenze, ancora da portare.
>
> 🪤 **Trappola già pagata**: nel VB il ramo «solo entrata» **non esce**, prosegue e i
> totali vengono azzerati dal blocco finale — scrive `---` e poi lo sovrascrive con
> `0h 0m`. Tradurlo come uscita anticipata sballa quel caso. È l'unica divergenza emersa,
> e l'ha trovata il banco di prova.
>
> **Anche le tabelle ci sono** (migrazione `M107_HrPresenze`, 42 test migrazioni verdi,
> NON ancora deployata): `hr_timbrature` (grezzo **append-only**, unicità su
> `(origine, id_esterno)` così il reimport non duplica), `hr_giornate` (cartellino
> **rigenerabile**, con `calcolato_il` e `regole_versione` per sapere cosa ricalcolare se
> cambia una soglia) e `employees.ecos_empl_code`, il ponte con Ecos senza il quale le
> timbrature non sanno di chi sono. Le rettifiche non hanno tabella propria: sono righe di
> `hr_timbrature` con `origine='RETTIFICA'`, autore e motivo.
>
> **Fatto il 27/08 sera** (in locale, NON ancora deployato):
> - **`EcosClient.cs`** — port del client API (token, paginazione, timbrature, badge).
>   Differenza voluta rispetto al VB: un errore API **solleva eccezione** invece di
>   restituire dati parziali in silenzio (il cursore non deve avanzare su uno scarico rotto).
> - **`HrPresenzeService.cs`** — l'import: confronta lo scarico con le righe
>   `origine='ECOS'` esistenti (chiave `id_esterno`), inserisce le nuove, **aggiorna le
>   cambiate** (Ecos è il padrone del suo dato: l'append-only vieta le correzioni a mano,
>   non il mirror del rilevatore) e ricalcola SOLO le giornate toccate — compresa la
>   giornata VECCHIA di una timbratura spostata di giorno, che altrimenti resterebbe
>   calcolata su dati spariti. Cursore in `app_config` (`hr_sync_timbrature_da`), margine
>   10 min, idempotente. Le giornate rimaste «Giornata in corso» si chiudono d'ufficio al
>   primo import del giorno dopo. Rettifiche = righe `origine='RETTIFICA'` con autore e
>   motivo obbligatorio; si possono eliminare SOLO le rettifiche, mai il grezzo.
> - **`HrSyncBackgroundService`** — import automatico ogni ora (`Hr:ImportIntervalHours` = 1 dal 09/09/2026, era 12;
>   gate `Services:HrSync`); senza credenziali resta a riposo e lo dice una volta sola.
> - **`HrController`** (`api/hr/*`) dietro `nav.hr_timbrature` (tolto `soloClient` dal
>   catalogo): con la LETTURA si vede solo il PROPRIO cartellino; la SCRITTURA apre
>   cartellini altrui, import, mappatura (`ecos_empl_code`, unicità difesa) e rettifiche.
> - **Pagina web `/hr/timbrature` LIVE** (`features/hr/`): cartellino mensile con totali,
>   fasce CCNL in tooltip, doppio click = dettaglio giornata + rettifica, dialogo
>   «Collega Ecos» coi badge letti vivi da Ecos + «Reimporta tutto» (serve dopo aver
>   collegato una persona nuova: le sue timbrature passate erano state scartate).
> - **Test**: `EcosClientTests` (parsing/paginazione senza rete, comprese le forme
>   insidiose: riga singola come oggetto, `ECOSAGILE_DATA` stringa vuota, errori con
>   HTTP 200) e `ImportPresenzeTests` (su MySQL: idempotenza, correzione da Ecos,
>   spostamento di giorno, rettifiche, unicità mappatura). Il csproj del server ora ha
>   `InternalsVisibleTo ATEC.PM.Tests`.
>
> **Fatto il 28/08/2026 — la vista mensile torna quella dell'originale.**
> La pagina aveva una «Matrice presenze» inventata qui: una riga per dipendente, le nove
> fasce di straordinario schiacciate in una colonna «Stra.», e l'export in CSV. Il
> programma «Timbrature» (§4) ha invece una griglia precisa, che in ufficio si legge a
> colpo d'occhio da anni, e un export **Excel**. Ora sono la stessa cosa:
> - **`GetMonthlyCalendar`** (`HrAttendanceService`) è il port di `CaricaDatiMensili`:
>   una riga per VOCE — ORE ORDINARIE, le fasce della Circolare 12/2024 (solo quelle con
>   ore), PRESENZA, FERIE, PERMESSI, MALATTIA, INFORTUNIO — nome e matricola sulla sola
>   prima riga, colonna TOTALE, e i colori dell'originale (grigio su sabati/domeniche e
>   festivi, verde sul lavorato, «?» rosso sul feriale scoperto, arancio sullo
>   straordinario, blu/viola/giallo sulle causali). Testo, colore e tooltip li decide il
>   server: la pagina e il file Excel disegnano la stessa griglia, non due interpretazioni.
> - **`HrCalendarExcel`** (EPPlus, già nel progetto) rifà il foglio di `btnEsportaExcel_Click`
>   colore per colore: titolo unito, intestazioni a riga 3 con la lettera del giorno,
>   festivi rossi e feriali azzurri, ore come numeri in formato `0.0`, riquadri bloccati su
>   intestazione e colonne nome/voce, larghezze 24/16/5,5/8, riga di separazione sotto
>   INFORTUNIO. Unica aggiunta: il colore **TEAL** (assenza già approvata su Ecos), che nel
>   VB era nato dopo l'export e sul foglio spariva.
> - **`GET /api/hr/calendar`** e **`/calendar/export`**, entrambi dietro la **scrittura** su
>   `nav.hr_timbrature`: la vecchia `GET /api/hr/matrix` non controllava niente, così con la
>   sola lettura — che deve mostrare solo il proprio cartellino — si vedeva l'azienda intera.
>   Stessa guardia aggiunta a `/quadratura`.
> - Test: `CalendarioExcelTests` (il foglio, cella per cella) e `CalendarioPresenzeTests`
>   (la griglia su MySQL: verde/grigio/rosso, fasce, ferie, totali).
>
> **Portato anche il resto del programma originale (28/08, secondo giro).**
> - **Solleciti** — i due pulsanti del calendario. «Sollecita» apre il client di posta (un
>   `mailto:` per persona, con il testo dell'originale parola per parola), «Invia sollecito»
>   spedisce dal server con `EmailService`. La fonte è il **«?» del calendario**: si
>   sollecita quello che la griglia mostra, non un secondo conteggio fatto per conto suo —
>   altrimenti la mail elencherebbe giorni diversi da quelli che la persona vede a video.
>   Prima di spedire si vede sempre chi verrà scritto (conferma con l'elenco, chi è senza
>   email, chi era già stato sollecitato). Le giornate chieste finiscono in **`hr_reminders`**
>   (`M113`): **una riga per giornata**, come il `MailLog` del VB, perché la domanda vera è
>   «questo buco l'ho già chiesto?» — con una riga per email quella risposta si perderebbe.
>   Endpoint `GET/POST /api/hr/calendar/reminders` e `POST …/reminders/mark` (il `mark` serve
>   al mailto: là la mail la spedisce l'utente, il server sa solo che gliel'abbiamo messa davanti).
> - **Le tre colonne del `ReportPage`** — 🔸 grezzo · 🔷 normalizzato · ✅ finale, sei colonne
>   per stadio (E1, U1, E2, U2, pausa, ore), con intestazione a due livelli e il menu Colonne
>   per spegnere i blocchi che non servono. I due stadi **non si salvano**: si ricalcolano al
>   volo ripassando le timbrature in `TimesheetEngine`, che è puro — nessuna migrazione,
>   nessun secondo dato da tenere allineato. `TimesheetDay` ora li espone, e `Assign` tiene
>   accanto a ogni orario arrotondato quello grezzo da cui viene (banco di prova 330/330
>   ancora identico: il calcolo non è stato toccato).
>   Non riportate le colonne «Str» di grezzo e normalizzato: nel VB non vengono mai
>   valorizzate, sono sempre `0h 0m`.

> **Credenziali Ecos dalla pagina, non più dal file (28/08).** Erano leggibili solo da
> `appsettings.json` sul server: per cambiare una password bisognava entrare sulla macchina.
> Ora c'è il dialogo **«Credenziali Ecos»** nella pagina Timbrature — utente, password,
> Client ID e indirizzo API — come il «Configurazione Credenziali» del programma originale.
> Stesso meccanismo della configurazione SMTP: valori in `res_settings` (chiavi `ecos.*`),
> password cifrata con `ProtectedConfigHelper` (DPAPI ad ambito macchina, perché sul server
> il programma gira come servizio) e **write-only** — si sostituisce, non si rilegge.
> L'appsettings resta come **ripiego**: chi le ha già messe là non deve rifare niente, e la
> pagina dice sempre da dove arrivano quelle in vigore. Le credenziali si rileggono a ogni
> uso, quindi cambiarle NON richiede il riavvio del servizio. C'è anche **«Prova
> collegamento»**: una sola `TokenGet`, che non legge e non scrive niente su Ecos.
> Endpoint `GET/POST /api/hr/ecos/settings` e `POST …/settings/test`, dietro la scrittura su
> `nav.hr_timbrature`. Difeso da `CredenzialiEcosTests` (precedenza database→file, password
> write-only e cifrata a riposo, modulo a riposo se manca tutto).

> 🪤 **Difetti trovati mentre si allineava** (tutti corretti): la query di matrice e
> quadratura erano `SELECT DISTINCT … ORDER BY e.last_name` — **MySQL le rifiuta**, quindi
> quelle due pagine non avrebbero mai risposto in produzione; `M107` non era più
> rieseguibile dopo il rename di `M111` (il `RENAME TABLE` non tocca il nome dei vincoli:
> ricreare `hr_timbrature` dava «Duplicate foreign key constraint name», e il test
> `MotoreMigrazioniTests` lo ha preso); `M112` droppava `absences` ma `InitDatabase` la
> ricreava a ogni avvio, perché il bootstrap gira **prima** delle migrazioni.

> **Difese aggiunte dopo la revisione avversaria** (32 difetti confermati, corretti):
> - 🪤 **Il filtro dei doppioni sotto i 5 minuti mancava.** Nel VB stava nella CTE SQL
>   *fuori* dal motore (`ReportProcessor.vb` righe 27-48, semantica `LAG`), quindi il port
>   del solo motore non l'aveva. Senza, una doppia strisciata fa da ponte nel
>   raggruppamento a 30' e si porta via il rientro vero: mezz'ora di straordinario persa in
>   silenzio. Ora è il primo stadio di `MotoreCartellino.Assegna`, e `RegoleCartellino.Versione`
>   è passata a **2** (le giornate a versione 1 si ricalcolano da sole).
> - **`RiparaGiornate`**: ogni import rimette in pari le giornate con timbrature ma senza
>   cartellino (ricalcolo interrotto da un deploy), quelle a regole vecchie e quelle più
>   vecchie della loro ultima timbratura; toglie i cartellini orfani. Senza, un'interruzione
>   lasciava il buco **per sempre** — il giro dopo il diff trovava tutto identico.
> - **Cancellazioni su Ecos**: l'import *completo* toglie anche qui le righe che là non
>   esistono più (l'incrementale no: non ha la fotografia intera). Prima una timbratura
>   spuria cancellata su Ecos restava a sballare il cartellino, e dalla UI non si poteva
>   togliere (il grezzo non si cancella a mano).
>   🪤🪤 **08/09/2026: Ecos rimanda solo gli ultimi 60 giorni** (filtro implicito di
>   `PeopleStampGetAll`, vedi `docs/guide/ECOS-API-CATALOGO.md`): l'import completo NON è una
>   fotografia intera e avrebbe cancellato da noi la storia più vecchia. **Corretto lo stesso
>   giorno**: si cancella solo dall'**orizzonte** dello scarico (il primo `UpdateDate`
>   ricevuto) in su, `HrAttendanceService.OrizzonteEcos`; dettagli nel manuale Ecos §11.0.
>   E dallo stesso giorno l'import legge il flag **`Delete`** di Ecos (cancellazione logica,
>   la risposta alla domanda 0(c) del §5): una timbratura tolta là se ne va anche qui **al
>   primo import incrementale**, non serve più quello completo; le richieste tolte diventano
>   `CANCELLED`. Manuale Ecos §11.1.
>   **Assenze giorno per giorno (08/09/2026, M124 `hr_absence_days`)**: le richieste arrivano
>   anche già spezzate da Ecos nei singoli giorni e tratti (`PeopleAbsenceRequestRefineWorkAll`,
>   manuale Ecos §9.7); calendario, cartellino, quadratura e giustificazione usano quelle ore
>   invece di spezzare l'intervallo da soli. Chiude il buco «una richiesta a più giorni non si
>   spezza». `hr_absences` resta la tabella delle richieste.
>   **Ferie nel planner Risorse (08/09/2026, «vince Ecos»)**: dai giorni approvati nascono le
>   barre FERIE di `res_assignments` (`SyncFeriePlanner`, manuale Ecos §9.8); una barra manuale
>   che non coincide prende le date di Ecos, una senza ferie Ecos resta. E le richieste si
>   leggono senza cursore: in produzione `hr_absences` era rimasta senza richieste di Ecos.
> - **Cursore dall'orologio DI ECOS** (massimo `UpdateDate` ricevuto): il nostro non è
>   confrontabile col loro, e uno scarto apriva una finestra cieca da cui le correzioni non
>   tornavano più. Ripiego sul nostro solo se manca, con un'ora di margine (cambio d'ora).
> - **Paginazione**: `LASTPAGE` assente non vuol dire «ultima pagina» — ci si ferma solo su
>   `LASTPAGE=TRUE` o su una pagina non piena. Prima una risposta senza quel campo troncava
>   lo scarico alla prima pagina e l'import si dichiarava riuscito.
> - **Date**: solo formati espliciti. Il ripiego invariante leggeva «05/02/2026» come 2
>   maggio, mettendo la timbratura nel giorno sbagliato senza dirlo.
> - **Nessuno rettifica sé stesso** (§8: serve il secondo occhio), né elimina una rettifica
>   sul proprio cartellino; l'eliminazione lascia traccia nel log. `GET /api/hr/stato` è
>   passato dietro la scrittura (riporta l'errore grezzo di Ecos).
> - **M108**: `employees.ecos_empl_code` UNICO. Il controllo applicativo non bastava: due
>   salvataggi simultanei mettevano lo stesso badge su due persone, e da lì le ore di uno
>   finivano nel cartellino dell'altro *a caso*.
> - Web: il dialogo giornata lavora sulla data (non su uno snapshot che restava congelato →
>   rettifiche doppie), i codici non abbinati sono un **banner persistente** e non una
>   notifica verde che sparisce, errore Ecos sui badge distinto da «non configurato»,
>   conferma sul «Reimporta tutto», guardie sul doppio invio.
>
> **Limiti noti, non risolvibili qui** (da chiedere a SoftAgile, §5):
> 1. **Paginazione a offset senza snapshot**: se durante un import lungo una riga scivola
>    fra due pagine, si perde e il cursore non la ripesca. Serve un parametro di ordinamento
>    stabile lato API. Mitigazione oggi: un secondo import completo.
> 2. **Timbrature annullate**: non sappiamo che valori assume `StatusCode` (il VB lo
>    scaricava ma non lo guardava). Il campo ora viene richiesto: quando SoftAgile dirà cosa
>    significa, basta filtrarlo.
> 3. **Turni a cavallo di mezzanotte**: il giorno è quello di calendario della timbratura,
>    come nel VB — un'uscita alle 00:20 finisce nel giorno dopo. Difetto ereditato, da
>    affrontare solo se in officina nasceranno turni notturni veri.
> 4. **Nessun filtro per reparto**: chi ha la scrittura vede e rettifica i cartellini di
>    tutti. Oggi è solo l'Admin; **prima di concedere la scrittura ai 12 responsabili
>    (Fase 2) va scritto lo scoping** — il piano lo dice già in §8.
>
> **Segnalazione #132 — giustificare le ore mancanti dal calendario (28/08).**
> Doppio clic su una cella del Calendario mensile — o **tasto destro → «Giustifica ore
> mancanti…»** (menu contestuale, chiesto da Diego il 28/08): il server dice quante ore
> mancano (contratto − timbrate) e quali causali sono ammesse, si sceglie e la griglia si
> ridisegna. Le due strade aprono lo stesso dialogo e valgono sulle stesse celle.
> Port di `dgCalendar_MouseDoubleClick` + `CausaleDialog`: `GetGiustificaInfo` /
> `SaveGiustifica` in `HrAttendanceService`, `GET/POST /api/hr/calendar/giustifica` dietro
> la **scrittura** su `nav.hr_timbrature`, dialogo `GiustificaCausaleDialog` nel web.
> - 🪤 **Con timbrature vere e parziali si può solo completare la giornata (PE o IN)**:
>   ferie e malattia sono giornate intere e su mezza giornata timbrata non stanno in piedi.
>   Era la doppia lista del VB; ora è nel server, e `GiustificaOreTests` la tiene ferma.
> - 🪤 Le giustificazioni sono righe di **`hr_absences`**, la stessa tabella delle richieste
>   della Fase 2. Ne segue una regola che il VB non aveva: da qui si scrive **un giorno
>   solo**. Un'assenza che arriva da Ecos non si tocca (là è il padrone del dato) e una
>   richiesta a più giorni non si spezza — si va sulle Richieste.
> - Chi giustifica **può** essere la persona stessa: il «secondo occhio» vale per le
>   rettifiche, che riscrivono le timbrature. Qui si dichiara una causale su una giornata
>   passata, ed è quello che l'ufficio fa da anni col programma originale.
>
> **✅ Le opzioni dell'originale sono state portate (28/08, in locale).** Il censimento e le
> scelte stanno in **`PIANO-HR-PORT-ORIGINALE.md`** — 11 voci decise una per una con Diego,
> 9 importate e 2 scartate col motivo. Tutte e nove sono scritte: sollecito della singola
> giornata, risincronizzazione di un giorno e di un mese scelto, filtro «📧 Da segnalare»,
> anteprima integrale della mail, Cronologia Email (quarta scheda, migrazione **M117**),
> export Excel del cartellino, avanzamento dell'import a video, ultima lettura badge.
> **Non ancora deployate.** Il §7 di quel file elenca voce per voce dove sta ogni cosa e le
> trappole da conoscere prima di rimetterci mano.
>
> **Resta da fare in Fase 1**: compilare `ecos_empl_code` per i 37 dipendenti (dalla
> pagina, dialogo «Collega Ecos»), **riconciliazione assenze** (le 49 giornate
> forfait/assenza piena del banco di prova — dipende dal rifacimento di `absences`,
> Fase 2), e in produzione: **credenziali Ecos** dal dialogo «Credenziali Ecos» della pagina
> Timbrature (non serve più toccare l'appsettings del server) + primo import completo.
>
> **Da dove ripartire in una chat nuova**: leggere questo file, poi
> `ATEC.PM.Server/Services/Hr/` (motore + client + import) e
> `ATEC.PM.Server/Controllers/HrController.cs`.

Impostazione originale della fase:
Traduzione VB.NET → C#, SQLite → MySQL, riusando §4 senza reinventare le regole.
- `time_punches` **append-only e immutabile**, idempotente su (seriale, matricola, timestamp);
- `time_days` **rigenerabile** dal grezzo, mai modificata a mano;
- rettifiche come **record separati** con autore e motivo — il grezzo non si corregge mai;
- costanti CCNL in **una copia unica** (come oggi in `GVL.vb`);
- pagina «Timbrature» sotto HR: il dipendente vede solo il proprio cartellino.

### Fase 2 — LA PARTE NUOVA: richieste ferie e permessi (2-3 settimane)
Il dipendente chiede da ATEC PM, il responsabile di reparto approva, la campanella
notifica, l'approvato finisce nel piano ferie a Gantt che già esiste invece di essere
ricopiato a mano. Se il ServiceID `request` viene abilitato, la richiesta approvata viene
scritta anche in Ecos.

⚠️ **`absences` va rifatta, non ereditata.** Schema attuale in produzione:

```sql
absences(id, employee_id, date_from, date_to, absence_type DEFAULT 'VACATION',
         status DEFAULT 'PENDING', approved_by INT NULL, notes, created_at)
```

Lo scheletro richiesta→approvazione è giusto, ma: lavora **a giornate intere** (niente ore
né mezze giornate), non ha `updated_at`, non ha FK su `approved_by`, `absence_type` e
`status` sono VARCHAR liberi senza vincolo, mancano gli indici. Si rifà con una migrazione
dedicata in `Migrations/` (una migrazione = un file) e la vecchia si droppa.

Agganci esistenti da riusare: `employee_departments.is_responsible` (12 responsabili),
il digest al responsabile in `PlanNotificationService`, il piano ferie a Gantt
(`FeriePage.tsx`, `res_assignments.tipo='FERIE'`), le notifiche a campanella.

### Fase 3 — quadratura presenze ↔ commesse (1-2 settimane)
Indice di copertura: quanta parte della giornata pagata finisce davvero su una commessa.
È il dato che nessun software presenze generalista può dare, perché non sa cosa sia una
commessa. **Prerequisito**: contenitori per le ore indirette (riunioni, formazione, fermo
macchina). ⚠️ **`projects.is_internal` NON esiste** (verificato l'08/09/2026: la quadratura
lo leggeva e rispondeva 500 in produzione). REGOLA di Diego (08/09/2026): sono «interne» le ore
sulle commesse del cliente **«ATEC — Sistema»** (`HrAttendanceService.ClienteInternoPattern`,
riconosciuto dal nome con qualsiasi trattino). 🪤 Se quel cliente viene rinominato, la colonna
«interne» della quadratura torna a zero.

### Fase 4 — export al consulente e spegnimento di eTime — *solo se si sceglie lo scenario B*
Parallelo di 2-3 mesi; eTime si spegne solo quando i totali coincidono per un mese intero.
Se non coincidono mai, ci si ferma e si tiene eTime per il cartellino: **esito accettabile,
non un fallimento**.

## 7. Cosa è già stato messo nel gestionale (27/08/2026)

Struttura pronta a ricevere le pagine, **nessuna funzione attiva**:

- **`ATEC.PM.Shared/catalogo-permessi.json`** — sezione `HR` fra «Commerciale» e
  «Gestione». Dal 27/08 sera `nav.hr_timbrature` **non è più `soloClient`** (gli endpoint
  esistono, `HrController`); `nav.hr_richieste` resta `soloClient` con motivo finché non
  nasce la Fase 2. Le chiavi restano SPENTE (livello 3, solo Admin) finché non le concedi
  dalla pagina Permessi.
- **`atec-pm-web/src/config/catalogo.gen.ts`** — rigenerato (`node scripts/genera-catalogo.mjs`),
  82 chiavi. Non si modifica a mano.
- **`atec-pm-web/src/config/navigation.ts`** — gruppo `hr`, due voci `status: "planned"`,
  percorsi `/hr/timbrature` e `/hr/richieste`.
- **`ModulePlaceholder.tsx`** — tolto il rimando al client WPF (dismesso dal 20/07/2026).

**Come si comportano oggi**: `/hr/timbrature` è **live** (pagina vera in `features/hr/`,
voce in `LIVE_ROUTES`, `status: "live"`); `/hr/richieste` mostra ancora
`ModulePlaceholder` con badge «In migrazione». Le chiavi nascono **spente**:
`EnsureCatalogo` le registra a livello 3 (solo Admin) senza concessioni, quindi finché
non le concedi dalla pagina Permessi la sezione HR **non compare a nessuno**, Admin
escluso.

**Per accendere una pagina** servono tre cose: la pagina in `features/hr/`, la voce in
`LIVE_ROUTES` di `AppRoutes.tsx`, e `status: "live"` in `navigation.ts` (fatto per
Timbrature il 27/08).

**Fatto il 02/09/2026 — il cartellino letto da chi non usa il computer tutti i giorni.**
Richiesta di Diego: la scheda «Cartellino di una persona» deve essere leggibile da chi ha poca
dimestichezza col computer, **senza perdere l'ora timbrata**. Com'è ora (`TimbraturePage.tsx`,
`GiornataDialog.tsx`, `stato-giornata.tsx`):
- **quattro riquadri** in testa (ore ordinarie, straordinario con le fasce, ferie e assenze,
  giornate da sistemare con «di cui già segnalate»);
- **una riga per giorno**, righe da 44 px, testo 14 px: in ogni cella di orario l'ora che
  vale in grande e sotto «timbrato 07:58» quando è diversa (è il vecchio blocco 🔸 Grezzo,
  messo dentro la cella). Pausa e ore prima dell'arrotondamento stanno dietro la colonna
  «Prima dell'arrotondamento», spenta di default (menu Colonne, chiave `v4`);
- colonna **«Com'è la giornata»**: la nota del motore tradotta in parole da
  `statoGiornata()` («Manca l'uscita», «Ferie 4h», «Tutto regolare», «Uscita non timbrata,
  stimata alle 17:00», «Riposo»…) più «segnalata il gg/mm/aa». La regola di cosa è
  anomalia resta del server (`hasAnomaly`): qui si scelgono solo le parole;
- dipendente da **tendina con ricerca** in testa («Il mio cartellino» = prima voce, id 0),
  mese con frecce grandi e «Torna a oggi»; schede e pulsanti coi nomi di chi li usa
  («Aggiorna da Ecos», «Scarica Excel», «Tutti, mese per mese», «Ore sulle commesse»);
- le due azioni per riga (📧 sollecito, 🔄 risincronizza) sono diventate **pulsanti con
  il nome per esteso nel dettaglio della giornata**, che ora apre con i quattro orari in
  grande, l'ora timbrata sotto, una frase che dice cosa fare e la rettifica già su
  «Uscita» quando manca l'uscita.

**Fatto il 02/09/2026 — fascia b del CCNL, il lavoro notturno ordinario (segnalazione #145).**
Fonte: tabella «Maggiorazioni per lavoro non ordinario e straordinario» del C018 Metalmecc. PMI
Confapi data da Diego (scansione `scan0747.pdf`): «lav. notturno fino alle 22: 25%», «lav.
notturno oltre le 22: 35%». Per il «fino alle 22» fa fede l'art. 7 del CCNL: il notturno
«decorre dalle 12 ore successive all'inizio del turno del mattino» → con ingresso alle 8, dalle 20.
- **Regole** (`TimesheetRules.cs`, `Version` 3 → **4**, così `RiparaGiornate` ricalcola lo
  storico al primo import): `NightEveningStartHour = 20`; B1 = 25% (20-22), B2 = 35% (22-6).
- **Motore** (`TimesheetEngine.cs`): chiavi `B1`/`B2` in `NewBands()`; `FasceFeriale` le
  assegna alle ore notturne/serali che **restano ordinarie** (notturno − quello già in g,
  serale − quello in coda allo straordinario): niente doppio conto con g. Sabato e festivo
  non cambiano (lì è tutto straordinario o festivo, già maggiorato). Lo straordinario serale
  20-22 resta fascia a. Pause: `PauseSerali` accanto a `PauseNotturne`, così una pausa alle
  21 non è pagata come notturno. `MinutiNotturniInCoda` è diventata `MinutiInCoda(c, minuti,
  finestra)` con `NotturniFra`/`SeraliFra`.
- **Calendario/Excel**: `VociStraordinario` ha due voci nuove (`NOTT_B1`, `NOTT_B2`) e
  `EtichetteFasceTooltip` le due etichette (l'indicizzatore senza chiave rompeva il calendario).
- **Client**: `FASCE_LABELS` B1/B2; pillola «Regolare, con notturno [e straordinario]»;
  colonna Straord. mostra «notte» col tooltip quando non c'è straordinario ma ci sono ore
  notturne; riquadro «Straordinario e maggiorazioni» con «notturno ordinario Xh».
- **Test** (`TurnoNotturnoTests`): Monge 19/08 → B1 2h, B2 6h, G 2h; Sinapi 17/08 (19-7:30) →
  B1 2h, B2 5h, G 3h, A 1h30; giornata diurna → 0; straordinario serale fino alle 21 → A 4h,
  B1 0. Il banco di prova delle 379 giornate non cambia (confronta solo le fasce del VB).
- ⚠ **Chi non fa straordinario** (`CountsOvertime=false`): come per le altre fasce, la
  maggiorazione si azzera — da confermare con le paghe se il notturno va pagato lo stesso.

**Fatto il 02/09/2026 — tutto il modulo presenze è real-time (SignalR).** Ordine di Diego:
«tutte le operazioni della sezione HR devono essere SignalR». Pattern canonico del progetto
(gruppo globale su `/hubs/project`, vedi `docs/HANDOFF-WEB.md`):
- **Server**: `ProjectHub.HrGroup = "hr-all"` (+ `JoinHr`/`LeaveHr`); `HrChangeNotifier`
  (singleton, `Services/Hr/`) manda `HrChanged {action, employeeId, date}` al gruppo.
  `HrController` lo chiama dopo ogni modifica riuscita (sollecito singolo e di massa,
  causale, credenziali, mappatura, rettifica e sua eliminazione, richiesta di assenza
  creata/approvata/annullata). `HrAttendanceService` lo riceve come dipendenza opzionale
  (null nei test) e notifica **da sé**: `import-progress` a ogni fase e `import` a fine
  import — così anche l'**import automatico di ogni ora** aggiorna le pagine aperte.
- **Client**: hook `lib/signalr/use-hr-hub.ts` (JoinHr, debounce 400 ms tranne
  `import-progress` che passa subito). Montato in `TimbraturePage` (tutte e quattro le
  schede: cartellino, calendario, quadratura, cronologia — invalida tutte le chiavi `hr-*`;
  su `import-progress` solo `hr-status`, cioè la barra del dialogo «Aggiorna da Ecos») e
  in `RichiestePage` (assenze, cartellino, calendario). Alla riconnessione rilegge tutto.
- Niente self-exclusion né concurrency token: le scritture HR sono append-only (grezzo,
  rettifiche, solleciti) o a stato (richieste): niente da sovrascrivere in silenzio.

**✅ Fase 2 — ritorno verso Ecos delle ore arrotondate: COSTRUITA l'08/09/2026** (ordine di
Diego; pronta per il deploy). Com'è fatta: pulsante «Invia a Ecos» nel dialogo della giornata,
con i tre stati in parole («Da inviare: N», «Allineato con Ecos», «Inviato il gg/mm/aa hh:mm»)
e il registro degli invii sotto; `POST /api/hr/ecos/send-day`; `HrAttendanceService.InvioEcos.cs`;
migrazione M127 (`hr_punches.ecos_punched_at`/`ecos_sent_at`, tabella `hr_ecos_sends`). L'import
riconosce l'eco del nostro invio (l'ora timbrata resta) e se su Ecos cambiano l'orario vince
Ecos. Le rettifiche non partono: su Ecos non esistono. Manuale Ecos §9.3, test `InvioEcosTests`.
Il testo che segue è il progetto originale del 02/09, lasciato per storia:
colonna «Ecos» con tre stati in parole («Allineato», «Da inviare», «Inviato il gg/mm») e
un pulsante che manda a Ecos **solo le giornate in cui l'ora arrotondata è diversa da
quella timbrata**, con `PeopleStampPost` in modifica (`Edit=true` + chiave `StampID`
salvata da noi all'import: senza si crea un doppione, e un retry dopo timeout ne crea un
altro), ogni invio registrato con data e autore. Prima serve l'utente API dedicato con i
diritti di scrittura (§11). Punto di aggancio nel client: la pillola di `stato-giornata.tsx`
e il riquadro «Giornate da sistemare».

**✅ Due matricole per persona (09/09/2026, M129).** Il codice badge Ecos
(`employees.ecos_empl_code`, es. 1027) serve a noi per riconoscere timbrature e richieste; la
**matricola del libro paga** (`employees.payroll_code`, es. 001, unica) è quella che compare sotto
il nome nel calendario «Tutti, mese per mese» e nell'export Excel, come nel foglio «PRESENZE MM
MESE AA.xlsx» del consulente. Si mette dall'anagrafica dipendente (sezione Presenze, campo
«Matricola (libro paga)»); le 26 del foglio di agosto 2026 sono state precaricate dalla
migrazione, abbinate per cognome e nome. Senza matricola sotto il nome non compare niente: il
codice Ecos non è mai mostrato lì. Il foglio del consulente ha anche la riga del contratto
(«full-time» / «part-time a 20 ore a sett.»): non è stata portata, si ricaverebbe dalle ore
giornaliere (8 / 4 / 6).

**✅ «Controllo di ieri» (09/09/2026).** Richiesta di Diego: «una pagina che mi faccia vedere le
timbrature di ieri di tutti i dipendenti, così HR ci mette poco a controllarle; se è lunedì facciamo
apparire anche venerdì, sabato e domenica». **Prima pagina di Timbrature** (dal pomeriggio del 09/09:
rotta `/hr/timbrature`, prima sottovoce e prima scheda; il cartellino di una persona è passato a
`/hr/timbrature/cartellino`, la vecchia `/hr/timbrature/ieri` rimbalza; chi ha la sola lettura sul
percorso del gruppo trova il proprio cartellino, le sottovoci «di tutti» hanno `requiresWrite`;
stessa chiave `nav.hr_timbrature`): una riga per dipendente che timbra (gli stessi dell'elenco laterale del cartellino), le
stesse celle e la stessa pillola «Com'è la giornata» del cartellino, quattro riquadri (dipendenti,
regolari, da sistemare, assenti), filtro «Solo da sistemare», clic sulla riga → lo stesso dialogo
della giornata con rettifica, «Invia a Ecos», email al dipendente e rilettura da Ecos.
- **La regola dei giorni** sta in una copia sola, `Services/Hr/HrControlloGiornaliero.cs`: il blocco
  finisce nel giorno scelto (di norma ieri) e torna indietro sui riposi — sabato, domenica, i festivi
  di `TimesheetRules.IsHoliday` — fino a comprendere l'ultimo giorno lavorativo. Martedì → lunedì;
  lunedì → venerdì, sabato e domenica; il giorno dopo una festa infrasettimanale → la festa e il
  giorno lavorativo prima. Nei giorni di riposo compaiono solo le persone che hanno timbrato (o hanno
  un'anomalia): trentasette «Riposo» nasconderebbero l'unico che era in officina. Le frecce vanno al
  blocco prima/dopo (`PreviousDate`/`NextDate` decisi dal server, mai oltre oggi), un calendario salta
  a un giorno qualsiasi, «Torna a ieri» rimette il default.
- **Server**: `GET /api/hr/daily-check?date=` → `HrDailyCheckDto`
  (`HrAttendanceService.ControlloGiornaliero.cs`): cinque letture su tutto il blocco e per tutti, poi
  ogni giornata è composta da `CostruisciGiornata`, la stessa funzione del cartellino mensile
  (estratta apposta da `GetMonthlyTimesheet`): le due pagine non possono dire due cose diverse dello
  stesso giorno.
- **Client**: `ControlloGiornalieroView.tsx`; logica pura in `controllo-giornaliero.ts` (righe,
  riassunto, nome del blocco) col suo test; celle condivise in `celle-cartellino.tsx` + `ore.ts`; i
  due comandi del dettaglio in `AzioniGiornata.tsx` (li usa anche il cartellino). Chi non ha il codice
  Ecos si legge «Non collegato a Ecos» (grigio), non «Nessuna timbratura».
- **Test**: `ControlloGiornalieroTests` (la regola giorno per giorno sul calendario 2026 + la lettura
  dal database, con la giornata confrontata con quella del cartellino), `controllo-giornaliero.test.ts`.

**✅ Uscita finale mancante = anomalia, non più stima (09/09/2026, regole v5).** Ordine di Diego
dal «Controllo di ieri»: «questo non deve essere stimato: se il dipendente ha dimenticato la
timbratura, il sistema deve segnalarlo come anomalia, e dobbiamo poter inviare il sollecito». Il
motore VB, con due entrate e una uscita, inventava l'uscita alle 17:00 (`AUTO_P: Uscita mancante -
Stimata 17:00`) e contava otto ore: una giornata a posto che a posto non era. **Divergenza voluta
dall'originale.** Ora `TimesheetEngine.TurnoUscitaMancante` scrive `⚠ INCOMPLETO: Uscita mancante`,
`Uscita2 = ??:??` («Non timbrata» in rosso) e zero ore, come la giornata con la sola entrata:
pillola rossa «Manca l'uscita», sollecito (INCOMPLETO è già fra le sei parole chiave), rettifica
del dialogo già su «Uscita», email che chiede l'orario vero (`HrDayReminder.Dettaglio`, ramo nuovo).
`TimesheetRules.Version` 4 → **5**: `RepairDays` ricalcola lo storico al primo import dopo il
deploy, le vecchie «Stimata» diventano incomplete da sole (il ramo client per la nota vecchia resta
finché non sono sparite). Eccezione: **oggi** con entrata-uscita-rientro resta «Giornata in corso»
(chi è rientrato dalla pausa è al lavoro, non ha dimenticato niente; l'import di domani la chiude).
Il banco di prova VB (379 giornate) non ha casi di stima: invariato. Test `UscitaMancanteTests`.

**✅ «Allinea Ecos» dal Controllo di ieri, con la pausa dedotta (09/09/2026 pomeriggio).** Diego:
«una colonna prima di Sollecito che mi permetta di sincronizzare le ore calcolate con quelle reali:
se le ore calcolate sono corrette vorrei poterle sincronizzare con Ecos, se l'ora di pausa non
esiste la devo inserire; ovviamente con una conferma con il resoconto di cosa andremo a scrivere».
- **Colonna «Ecos»** nella griglia (prima di «Sollecito»): nuvola azzurra = c'è da scrivere (orari
  arrotondati e/o pausa dedotta), spunta grigia = allineato, nuvola spenta = bloccato (anomalia da
  sistemare prima, giornata in corso, invio incerto da verificare). Il clic apre
  `AllineaEcosDialog`: la giornata calcolata (quattro orari, ore, pausa, nota) e riga per riga cosa
  si scrive — MODIFICA «Uscita 17:12 → 17:00», NUOVA · RETTIFICA, NUOVA · PAUSA — e cosa resta fuori
  (non inviabile, da verificare). Niente parte senza «Scrivi su Ecos (N)».
- **Resoconto = server**: `GET /api/hr/ecos/send-day/plan?employeeId&date` → `HrEcosPlanDto`
  (`GetEcosPlan`, sola lettura), la stessa lettura di `SendDayToEcosAsync`: quello che si conferma è
  quello che parte. `CanSend` è falso con anomalia, senza giornata calcolata, senza credenziali o
  senza niente da scrivere.
- **Pausa dedotta → timbrature vere** (`TimbraturaDedotta`, `TimbratureDedotte`): solo con le note
  «AUTO_P: Pausa 1h detratta» (due strisciate: uscita 12:30 e rientro 13:30) e «AUTO_P: Pausa
  implicita (1 IN / 2 OUT)» (solo il rientro); un orario con l'asterisco già coperto da una timbratura
  o rettifica allo stesso minuto non si reinserisce. 🪤 «Pausa 1h forzata» resta FUORI: le quattro
  strisciate ci sono (pausa corta) e aggiungerne due farebbe una giornata «da verificare».
  L'inserimento è come per le rettifiche (`PeopleStampPost` col badge attivo, verifica della persona,
  nota «ATEC PM: pausa pranzo dedotta dal motore»); la timbratura nasce anche qui come timbratura di
  Ecos (source ECOS + StampID, motivo e autore) e la giornata si ricalcola subito, vicine comprese:
  da «Pausa 1h detratta» a «OK» con la pausa timbrata. Registro `hr_ecos_sends` con `punch_id` della
  riga nuova e `previous_time` NULL. `HrDayDto.EcosBreakToInsert` porta il flag alla riga.
- **Esito incerto** (timeout dopo la scrittura): riga di registro SENZA timbratura (`punch_id` NULL,
  messaggio «Esito incerto…»), e da lì la pausa di quella giornata **non si riprova**, nemmeno la metà
  mancante: una pausa a metà (solo l'uscita) farebbe una giornata «uscita mancante» al prossimo
  import. Il resoconto la mostra DA VERIFICARE; se Ecos l'ha creata, l'import la porta qui da sé.
- **Dopo la scrittura la giornata si rilegge da Ecos** (Diego: «una volta che ho scritto devi
  risincronizzare le righe interessate»): `SendDayToEcosAsync` chiama `ImportWindowAsync` sul giorno
  e persona (`HrEcosSendResultDto.Resynced`, messaggio «Riletta da Ecos: …»); gli orari modificati
  tornano come eco. 🪤 Le timbrature appena INSERITE Ecos non le restituisce ancora (manuale §7):
  senza protezione la rilettura le avrebbe scambiate per cancellate, tolte qui e riproposte da
  scrivere → doppioni su Ecos. Quindi `RimuoviSpariteNellaFinestra` e `RimuoviCancellateSuEcos`
  non toccano le righe con `ecos_sent_at` negli ultimi 3 giorni (`ProtezioneInviiRecenti`); dopo,
  se Ecos ancora non le ha, vince Ecos e si tolgono. Le cancellazioni vere arrivano comunque col
  flag `Delete` dell'import incrementale. Test in `InvioEcosTests` e `AllineaEcosTests`.
- **Specchio di Ecos (09/09 sera, M130).** Diego: «Ecos è la bibbia: se qualcuno ha modificato le ore,
  Ecos viene aggiornato e qui devono essere lo specchio dell'allineamento, chissene frega di come sono
  arrivate». Dopo una scrittura riuscita `hr_punches.punched_at` prende l'orario scritto (modifiche e
  rettifiche inserite), la giornata si ricalcola (grezzo compreso) e poi si rilegge da Ecos; l'orario
  originale resta SOLO nel registro `hr_ecos_sends`. Nella griglia la riga piccola «timbrato hh:mm» è
  l'orario che Ecos ha adesso (ambra se diverso dall'ora calcolata), nel dettaglio la lista si chiama
  «Timbrature su Ecos». La regola dell'08/09 «`punched_at` non cambia mai» è superata; M130 allinea le
  righe scritte prima del cambio e segna le loro giornate da ricalcolare (`rules_version = 0`).
- **Orari decisi a mano (09/09 sera).** Diego: «devo poter modificare a mano gli orari». Nel dettaglio
  della giornata il riquadro «Orari su Ecos» ha un campo ora per ogni timbratura (di Ecos o rettifica)
  e due per la pausa dedotta, proposti con l'arrotondato del motore: su Ecos — e qui, che ne è lo
  specchio — va quello che HR scrive. `POST /api/hr/ecos/send-day` porta `Times[]` (`HrEcosTimeDto`:
  `PunchId` + `Direction` + `Time` «HH:mm»; per la pausa `PunchId` null); il server usa `Target`
  (`Forzata ?? Arrotondata`) per modifiche, inserimenti e specchio, e rifiuta tutto prima di toccare
  Ecos se un orario non è «HH:mm». Logica pura del dialogo in `invio-ecos.ts` (`orariDaScrivere`,
  `scelteDaScrivere`, `orarioValido`) col suo test; test C# in `AllineaEcosTests` («gli orari decisi a
  mano vincono»). La nuvola della riga (resoconto) resta con i valori proposti dal motore.
- **La timbratura che manca si scrive nella sua riga (09/09 sera).** Diego: «quando inserisco l'ora
  mancante deve inserirsi automaticamente dove manca, senza testo di giustificazione, poi la scriviamo
  su Ecos». Con «⚠ INCOMPLETO: Uscita mancante» o «Solo entrata» il cartellino porta
  `HrDayDto.EcosMissingToInsert = "OUT"` (`HrAttendanceService.VersoMancante`, una regola sola) e nel
  riquadro «Orari su Ecos» compare la riga «Uscita — manca su Ecos — [__:__]»: HR scrive l'ora, preme
  «Scrivi su Ecos» e la timbratura nasce su Ecos (stesso inserimento via BadgeCode della pausa,
  `TimbraturaDedotta` con `Origine = "MANCANTE"`, motivo `MotivoMancante`, registro «Correct Record
  Insert (timbratura mancante)») e qui come riga ECOS; poi il ricalcolo e la rilettura come sempre.
  Nel `Times[]` viaggia con `PunchId` null e `Kind = "MISSING"` (la pausa `Kind = "BREAK"`). Il server
  la rifiuta prima di toccare Ecos se la giornata non è incompleta di quel verso, se l'ora non viene
  dopo l'ultima timbratura del giorno, o se la stessa ora è già partita senza risposta certa. Il
  resoconto della nuvola la elenca come «MANCANTE» (`Kind = "MISSING"`, fuori dalle cose che scrive).
  Il modulo «Aggiungi una timbratura» resta per gli altri casi, col motivo facoltativo (senza, il
  client manda «Inserita da HR dal dettaglio della giornata»: il server lo vuole ancora). Test C# in
  `AllineaEcosTests` («l'uscita che manca scritta a mano…», «…dopo l'ultima timbratura…»).
- Il dettaglio della giornata (`GiornataDialog`) mostra la pausa fra le cose da inviare e il pulsante
  si chiama «Scrivi su Ecos»; la conferma elenca modifiche, rettifiche, pausa e timbratura mancante.
- **I solleciti dicono la verità (09/09 sera).** Diego: «come faccio a sapere se la mail di sollecito è
  effettivamente stata inviata?». Non poteva: `QueueSimpleMail` accodava e il server rispondeva
  «Sollecito inviato» a scatola chiusa; l'esito vero finiva solo nel log del server
  (`[EmailService] Invio fallito per …`). In produzione il sollecito a Cesi del 09/09 16:00 è morto in
  coda: Aruba ha risposto «501 5.7.0 invalid LOGIN encoding», che (sonda con password finta) è la
  risposta a una password VUOTA — cioè la password SMTP salvata (blob DPAPI) non è più decifrabile dal
  server e `DecryptPassword` restituiva null in silenzio. Ora: `EmailService.SendNowAsync` manda la
  mail SUBITO e restituisce l'esito del server di posta; `POST /api/hr/day-reminder` e
  `POST /api/hr/calendar/reminders` la usano, segnano la giornata (`hr_reminders`) SOLO se la mail è
  stata accettata e in caso contrario rispondono «Sollecito NON inviato a …: motivo». Quindi: **una riga
  in «Email inviate» = il server di posta ha accettato la mail**. `EmailService.MotivoBlocco` (regola
  pura, test `EsitoInvioMailTests`) ferma prima di toccare il server: invio spento, SMTP incompleto,
  password salvata ma illeggibile («reinserirla in Configurazione email»), utente senza password; la
  usano anche «Invia prova» e il ciclo in background (che ora scrive nel log il perché). La coda resta
  per digest e RDO. Da fare a mano sul server: reinserire la password SMTP in Configurazione email e
  premere «Invia prova». La Configurazione email ha l'occhiolino sulla password (al primo clic chiede
  la password salvata a `GET /api/settings/email/password`, che la dà a chi ha la funzione «Digest
  Email» e scrive nel log chi l'ha vista) e «Cambia password» con doppia conferma
  (`CambiaPasswordSmtpDialog`, regola `password-smtp.ts` col test).
- **Cancellare le timbrature, anche quelle di Ecos (09/09 sera, segnalazione #152).** Diego: «devo poter
  cancellare le timbrature» (Buda 03/09: uscita 12:30 doppia). Nel dettaglio della giornata il cestino c'è
  su ogni rettifica e su ogni timbratura di Ecos con StampID; `DELETE /api/hr/adjustment/{id}` →
  `HrAttendanceService.DeletePunchAsync`: la rettifica sparisce e basta; la timbratura di Ecos si cancella
  PRIMA su Ecos (`EcosClient.DeleteStampAsync`, `Edit=true` + `StampID` + `Delete=1`, manuale §4.5) e poi
  qui, con riga «Delete» in `hr_ecos_sends` e ricalcolo. Se Ecos rifiuta o non risponde, qui non si tocca
  niente (riga ERROR nel registro, messaggio a video). Mai sul proprio cartellino. Test in `AllineaEcosTests`
  («Una timbratura di Ecos si cancella prima su Ecos e poi qui», «Se Ecos rifiuta…»); i vecchi test della
  rettifica in `ImportPresenzeTests` sono passati alla nuova firma.
- **Il motore non perde più le pause brevi, e l'uscita timbrata sta nella sua colonna (10/09,
  regole v6).** Guardando il Controllo di ieri del 09/09 Diego chiede cosa non torna oltre alle
  righe rosse. Due difetti veri: (a) lo stadio 2 di `Assign` (raggruppamento a 30 minuti)
  guardava solo il tempo, così il rientro di Cimmino alle 14:07, a 29 minuti dall'uscita delle
  13:38, veniva inghiottito: la giornata diventava «1 IN / 2 OUT», il motore deduceva un rientro
  che nessuno aveva timbrato e contava un'ora piena di pausa al posto della mezz'ora vera —
  mezz'ora di lavoro persa. Ora il gruppo si chiude anche al cambio di VERSO; (b) chi timbra solo
  entrata e uscita si vedeva l'uscita della sera sotto la colonna della pausa dedotta
  (`UscitaTimbrataInFondo`). Sul banco di prova delle 330 giornate VB divergono 5 giornate (Cesi
  e Cimmino), tutte per il difetto (a) e tutte a favore della persona: sono dichiarate in
  `MotoreCartellinoTests.CorrezioniVolute`, che segnala anche il caso opposto. Test in
  `PausaBreveTests`.
- **Le ore del contratto (10/09).** Diego: «le persone devono fare le ore previste dal contratto,
  se uno ha un contratto da 8 ore non può farne meno — vedi Maracich e Saffioti — quindi non deve
  esserci scritto tutto regolare». `HrDayDto.ShortMinutes` dice quanti minuti mancano alle ore
  dell'anagrafica (`employees.hr_daily_hours`), contando come fatte quelle coperte da un permesso
  o da una ferie parziale; regola in `CostruisciGiornata.MinutiMancanti`, fuori chi non timbra, i
  giorni senza timbrature, le giornate già rosse e quella in corso. A video diventa «Mancano 30m
  sul contratto» in ambra, e siccome `daSistemare` conta anche l'ambra entra da sola nel riquadro
  «Da sistemare» e nel filtro. Test: `CartellinoMensileTests` (tre casi) e `stato-giornata.test.ts`.
- **L'entrata prima delle 8 si autorizza (10/09, regole v7, M131).** Diego: «l'orario di inizio al
  mattino è alle 8; se l'orario arrotondato è prima delle 8 bisogna avere un pulsante Autorizza /
  Non autorizzare — se premo autorizza mantengo l'orario, altrimenti va approssimato alle 8,
  questo perché c'è gente che arriva, timbra alle 7:30 e si fa mezz'ora di straordinario non
  autorizzato tutti i giorni». La decisione sta in `hr_early_entries` (una riga per persona e
  giorno, che non esiste finché nessuno decide: **senza riga vale il no**); il motore alza
  l'entrata alle 8 in `EntrataNonPrimaDelleOtto`, lasciando intatti i due stadi — il cartellino
  mostra «08:00» con sotto «timbrato 07:30». Restano fuori i turni di notte, chi comincia prima
  delle 5 (è un altro turno, non un anticipo) e le giornate precedenti al **1° settembre 2026**
  (`TimesheetRules.EarlyEntryRuleFrom`): il mese in corso non è ancora chiuso, i mesi prima sì.
  `POST /api/hr/early-entry` salva e ricalcola subito; i due pulsanti stanno nel dettaglio della
  giornata, e nel Controllo di ieri la riga dice «Entrata 30m prima delle 8: da autorizzare» in
  ambra. Test: `EntrataAnticipataTests` (motore e servizio).
- **La causale si mette anche dal Controllo di ieri (10/09).** Diego: «le ore da giustificare
  vorrei poterle inserire anche nella pagina Controllo di ieri». Colonna «Causale» prima di
  «Ecos»: apre lo stesso `GiustificaCausaleDialog` del Calendario, che chiede al server quante
  ore mancano e quali causali sono ammesse. Il pulsante compare dove ha senso — regola pura
  `daGiustificare` in `controllo-giornaliero.ts` (da sistemare, non assenza, non riposo) col suo
  test.
- **I riquadri in alto filtrano (10/09).** Diego: «questi blocchi in alto devono già fare da
  filtro come già fatto in altre pagine». «Tutto regolare», «Da sistemare» e «Assenti» filtrano la
  griglia sulle righe che hanno contato, «Dipendenti» toglie il filtro; il riquadro acceso si vede
  e un secondo clic lo spegne. Le regole del filtro sono le STESSE del conteggio (`REGOLE` in
  `controllo-giornaliero.ts`, usate sia da `riassuntoControllo` sia da `filtraRighe`), così il
  numero grande e le righe non possono scollarsi — c'è un test apposta. `Riquadro` di
  `celle-cartellino.tsx` diventa un pulsante quando riceve `onFiltra`.
- **Le stesse cose anche sul cartellino di una persona (10/09).** Diego: «tutte le modifiche
  fatte qua devono esserci anche sulla pagina cartellino di una persona». I quattro riquadri del
  mese filtrano le giornate che hanno contato («Ore ordinarie» → i giorni lavorati,
  «Straordinario» → le giornate con straordinario, «Ferie e assenze» → le assenze, «Giornate da
  sistemare» → rosse e ambra), e il pulsante «Solo le giornate da segnalare» usa lo stesso stato,
  così un filtro solo è acceso per volta. «Giornate da sistemare» ora conta anche l'ambra, come
  nel Controllo di ieri (prima solo il rosso). C'è anche la colonna «Causale». Regole e totali in
  `cartellino-mese.ts` (`totaliMese`, `filtraGiornate`, `daGiustificareGiornata`), fuori dalla
  pagina e con i loro test: numero del riquadro e righe filtrate non possono scollarsi. Il resto
  delle novità di oggi era già lì, perché sta nel server (`ShortMinutes`, entrata anticipata) e
  nel `GiornataDialog`, che le due pagine si dividono.
- **Le tre pagine dicono la stessa cosa della stessa giornata (10/09).** Il calendario «Tutti,
  mese per mese» segnalava già le ore mancanti (casella rossa quando `oreMancanti >= 0.25h`) e ha
  la causale col doppio clic o col tasto destro: non gli mancava niente. Il suo conto però
  includeva lo **straordinario** e aveva una tolleranza di un quarto d'ora, mentre
  `ShortMinutes` guardava le sole ore ordinarie e segnalava da un minuto in su. Allineato il
  secondo al primo (`TimesheetRules.ShortDayToleranceMinutes`): chi è rimasto oltre l'orario non
  risulta in difetto, e la stessa giornata non può essere «a posto» in una pagina e «corta» in
  un'altra. I riquadri-filtro nel calendario non hanno senso: non ha riquadri, è la griglia a
  voci del programma originale.
- **Anche «Ecos» e «Sollecito» sul cartellino di una persona (10/09).** Nel cartellino c'era la
  sola colonna «Causale»: ora ci sono tutte e tre come nel Controllo di ieri, con «Allinea Ecos»
  (nuvola / spunta / nuvola spenta) e il 📧 sulla riga. La regola di cosa dice il pulsante è
  passata in `invio-ecos.ts` (`statoEcos`, col suo test) e la usano tutte e due le pagine: prima
  viveva dentro `ControlloGiornalieroView` e il cartellino non poteva averla.
- **Le ore già giustificate si vedono (10/09).** Diego: «se le ore sono meno delle ore previste le
  abbiamo giustificate, vorrei si vedesse in modo da non doverle rigiustificare». Su una giornata
  LAVORATA con un permesso o una ferie parziale la causale non compariva da nessuna parte: la nota
  restava quella del motore («OK») e il cartellino diceva «Tutto regolare» (caso vero: Cassano
  09/09, sette ore più un'ora di permesso). Ora `HrDayDto.JustifiedType` e `JustifiedHours`
  viaggiano insieme alla giornata, la pillola dice «Tutto regolare · 1h di permesso»
  (`coperturaGiornata`) e il pulsante della causale resta acceso con l'icona del calendario
  spuntato, per cambiarla senza rimetterla da capo (`giaGiustificata`, in `stato-giornata.tsx`
  perché la usano tutte e due le pagine). Sulle giornate di sola assenza non cambia niente: là lo
  dice già la nota.
- **L'anticipo si approva dalla riga (10/09).** Diego: «l'approvazione dell'orario prima delle 8
  vorrei apparisse anche così», con i pulsanti «✓ Approva» e «✗ Rifiuta» delle richieste. Colonna
  «Anticipo» nel Controllo di ieri e nel cartellino: finché nessuno decide ci sono i due pulsanti
  (stesso stile di `RichiestePage`), dopo resta un segno con quanto era l'anticipo e cosa si è
  deciso. Un componente solo per le due pagine, `AnticipoAzioni.tsx`, che chiama
  `POST /api/hr/early-entry` e fa rileggere la pagina. I pulsanti nel dettaglio della giornata
  restano: servono a tornare sulla decisione.
- **Decidere l'anticipo scrive l'entrata su Ecos (10/09).** Diego: «sia che accetto o che rifiuto
  l'ingresso in anticipo, fai aggiornamento dell'orario di ingresso su Ecos e sul locale», senza
  conferme — «la conferma è già data dalla approvazione o dal rifiuto». `SetEarlyEntryAsync`
  (in `HrAttendanceService.InvioEcos.cs`) salva la decisione e porta subito l'orario che vale
  sulla SOLA entrata del mattino: approvata l'arrotondato timbrato (07:30), rifiutata le 8. Una
  `PeopleStampPost` con `Edit=true` sullo StampID dell'entrata, poi lo specchio locale
  (`punched_at` = `ecos_punched_at` = orario deciso), la riga nel registro e il ricalcolo. Se Ecos
  rifiuta, qui NON si tocca niente — i due non devono scollarsi — e il motivo torna come avviso
  nel messaggio: la decisione resta comunque registrata e il conto della giornata la rispetta.
  Test in `EntrataAnticipataTests` (rifiuto, approvazione, Ecos che rifiuta). 🪤 Dopo un rifiuto
  l'entrata locale è le 8, quindi l'anticipo non si vede più e la colonna resta vuota: per
  tornare indietro si cambia l'orario a mano dal dettaglio e si riscrive su Ecos.
- **La sola uscita non è un'entrata (10/09, regole v8).** Diego: «controlla Vasile». Obreja il 02/09
  aveva una sola strisciata, l'USCITA delle 17:03, e il cartellino diceva «Entrata 17:00, uscita
  non timbrata»: l'esatto contrario di quello che era successo. Il `case 1` di `Assign` metteva la
  timbratura unica nella casella dell'entrata senza guardarne il verso — nove giornate su venti
  erano così. Ora il verso conta: con la sola uscita la nota è «⚠ INCOMPLETO: Solo uscita»,
  l'entrata resta «??:??», `VersoMancante` dice «IN» e l'entrata mancante si inserisce PRIMA della
  prima timbratura (il controllo era tarato solo sull'uscita). Cambiano anche le parole: «Manca
  l'entrata» nella pillola, il testo del sollecito e la spiegazione del dettaglio, e il modulo
  della rettifica parte già su «Entrata». Test in `UscitaMancanteTests` e `AllineaEcosTests`; il
  banco di prova delle 330 giornate VB non si sposta.
- **Ultima sincronizzazione sotto «Aggiorna da Ecos» (09/09 sera).** Data in grigio sotto il
  pulsante (`HrStatusDto.LastImport`); l'ultimo import riuscito resta scritto in `app_config`
  (`hr_last_import_at`, `ScriviUltimoImport`) così sopravvive ai riavvii del servizio.
- Test: `PausaDedottaTests` (regola pura) e `AllineaEcosTests` (resoconto, inserimento, anomalia,
  timeout) in `AllineaEcosTests.cs`; client `invio-ecos.test.ts`.

## 8. Punti delicati — da non sbagliare

**Art. 4 dello Statuto dei lavoratori.** Registrare entrata e uscita per finalità
organizzative e retributive è pacifico e non richiede accordo sindacale. **Ma** nel momento
in cui si incrocia presenza × ore-commessa per produrre **indicatori di rendimento
individuale automatici**, si scivola nel comma 1: servono accordo sindacale o
autorizzazione dell'Ispettorato, con sanzione penale a carico del legale rappresentante.
→ L'indice di copertura si espone **per reparto e in aggregato**; la vista per persona
esiste per correggere i dati e **non produce classifiche**. Questo si scrive
nell'informativa.

**Obbligatori e a basso costo**: informativa art. 13 GDPR; informazione **ex art. 4
comma 3** sulle modalità d'uso e di controllo (senza la quale i dati sono giuridicamente
inutilizzabili, anche in giudizio); aggiornamento del registro dei trattamenti; retention
definita con cancellazione automatica (5 anni è il riferimento usuale per i dati badge).

**Mai**: dati **biometrici** (impronta, volto) per il cartellino — non hanno base giuridica
e **il consenso dei dipendenti non li sana**; il Garante ha già sanzionato casi identici.

**Visibilità**: un dipendente non vede le assenze dei colleghi, e **in nessun caso la
causale sanitaria**: nel calendario di squadra si mostra «assente», mai «malattia». Il
motore permessi a chiavi copre il *chi può aprire cosa*, **non filtra i dati per reparto**:
quel filtro va scritto.

**Casi limite**
- *Dimenticanza timbratura*: anomalia segnalata, giustificativo del dipendente,
  approvazione del responsabile, la timbratura originale resta. Default **assenza con
  anomalia bloccante**: se il default fosse «presente», nessuno correggerebbe mai nulla.
- *Mezza giornata*: le assenze si gestiscono **a ore**; la mezza giornata è il caso
  particolare (ore = metà dell'orario teorico). ⚠️ **Non riusare** `TravelMath.GiorniDaOre`
  (≤4h = 0,5): è la regola dell'**indennità di trasferta**, un'altra cosa.
- *Trasferta*: chi è in cantiere non timbra. La giornata si giustifica dalle ore su fasi
  `DA_CLIENTE`, che già generano la riga di trasferta.
- *Malattia*: la registra l'**ufficio personale**, non il dipendente; in banca dati solo
  **protocollo del certificato e date**, mai la diagnosi.
- *Ore su commessa*: **non si arrotondano mai**. Si arrotondano solo le presenze,
  altrimenti i due totali non torneranno mai.

## 9. Cosa NON fare

- Non ricalcolare i **ratei** di ferie/ROL/ex festività in casa: si leggono.
- Niente **biometria**.
- Non tenere **due tabelle di ferie**: se nasce la tabella richieste, `res_assignments`
  diventa una proiezione generata, non una seconda verità.
- Non allentare la FK `project_phase_id` di `timesheet_entries` per infilarci le assenze,
  e **non inventare commesse finte** per ferie e malattia.
- Nessun **indicatore automatico di rendimento individuale** da presenza × commessa (§8).
- **Niente big bang**: eTime non si spegne per decreto, si spegne quando i totali
  coincidono per un mese intero.
- Non scrivere codice prima del **sì del consulente del lavoro** su tracciato e causali:
  definiscono tutto il resto.
- Non buttare lo **storico** di eTime: serve per il LUL e per il diritto di accesso del
  lavoratore.

## 10. Domande ancora aperte

1. **Scenario A o B** — eTime resta o va spento? *(Raddoppia o dimezza il progetto.)*
2. Le richieste approvate devono **tornare in Ecos**? *(Tecnicamente si può — §5 — manca
   solo il diritto sul ServiceID `request`.)*
3. **Chi approva**: basta il responsabile di reparto (12 persone) o servono due livelli e
   le deleghe per quando l'approvatore è in ferie?
4. Cosa riceve oggi il **consulente del lavoro**, da chi e in che tracciato?
5. Nello SQLite del progetto Timbrature **c'è storico da migrare**?

## 11. Sicurezza — in sospeso

`Timbrature/TextFile1.txt` contiene **in chiaro** password ECOS, Client ID e password SMTP
dell'account `diego.frattini@atec.srl`. Git è stato rimosso da quel progetto (quindi la
storia non le contiene più, e su GitHub non sono mai arrivate), **ma il file è ancora su
disco** e le password non sono state ruotate — scelta consapevole del 27/08/2026.
Da tenere presente: quell'utente API **può scrivere** timbrature e anagrafiche in Ecos
(§5). Se un giorno si reinizializza git in quella cartella, mettere il file in
`.gitignore` **prima** del primo commit.

---

## Riferimenti

- Ricerca e analisi che hanno prodotto questo piano: sessione del 27/08/2026.
- Piano ferie esistente: `atec-pm-web/src/features/risorse/` (Gantt, `res_assignments`).
- Motore permessi: `PIANO-PERMESSI-REBUILD.md`, catalogo unico in
  `ATEC.PM.Shared/catalogo-permessi.json`.
- Migrazioni: una migrazione = un file in `ATEC.PM.Server/Migrations/`.
