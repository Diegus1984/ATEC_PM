# PIANO HR — Timbrature, ferie e permessi dentro ATEC PM

> **Punto d'ingresso del modulo HR.** Documento di consegna: chi riprende il lavoro
> (persona o sessione) legge questo file e sa dove siamo, cosa è già stato deciso e
> cosa manca. Ultimo aggiornamento: **27/08/2026**.

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
| Import timbrature da Ecos (client API) | ⬜ da portare, in VB.NET fuori da ATEC PM (§4) |
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
> **Resta da fare in Fase 1**: client Ecos in C# (port di `EcosApiManager.vb`), servizio di
> import che riempie `hr_timbrature` e rigenera `hr_giornate` col motore, compilazione di
> `ecos_empl_code` per i 37 dipendenti, riconciliazione assenze, pagina cartellino.
>
> **Da dove ripartire in una chat nuova**: leggere questo file, poi
> `ATEC.PM.Server/Services/Hr/` (motore già fatto) e
> `Timbrature - API/.../Api/EcosApiManager.vb` (il client da portare, §4).

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
macchina) — il gancio esiste già, `projects.is_internal`.

### Fase 4 — export al consulente e spegnimento di eTime — *solo se si sceglie lo scenario B*
Parallelo di 2-3 mesi; eTime si spegne solo quando i totali coincidono per un mese intero.
Se non coincidono mai, ci si ferma e si tiene eTime per il cartellino: **esito accettabile,
non un fallimento**.

## 7. Cosa è già stato messo nel gestionale (27/08/2026)

Struttura pronta a ricevere le pagine, **nessuna funzione attiva**:

- **`ATEC.PM.Shared/catalogo-permessi.json`** — sezione `HR` fra «Commerciale» e
  «Gestione», con `nav.hr_timbrature` e `nav.hr_richieste`. Entrambe `soloClient: true`
  con motivo, **obbligatorio** finché non esistono endpoint: il test
  `CensimentoCatalogoTests` pretende che ogni chiave sia usata dal server, oppure
  `soloClient` con motivo, oppure ritirata. **Quando arrivano gli endpoint, si toglie
  `soloClient`.**
- **`atec-pm-web/src/config/catalogo.gen.ts`** — rigenerato (`node scripts/genera-catalogo.mjs`),
  82 chiavi. Non si modifica a mano.
- **`atec-pm-web/src/config/navigation.ts`** — gruppo `hr`, due voci `status: "planned"`,
  percorsi `/hr/timbrature` e `/hr/richieste`.
- **`ModulePlaceholder.tsx`** — tolto il rimando al client WPF (dismesso dal 20/07/2026).

**Come si comportano oggi**: non avendo una voce in `LIVE_ROUTES`, le due pagine mostrano
`ModulePlaceholder` con badge «In migrazione» — nessun link morto. Le chiavi nascono
**spente**: `EnsureCatalogo` le registra a livello 3 (solo Admin) senza concessioni, quindi
finché non le concedi dalla pagina Permessi la sezione HR **non compare a nessuno**,
Admin escluso.

**Per accendere una pagina** servono tre cose: la pagina in `features/hr/`, la voce in
`LIVE_ROUTES` di `AppRoutes.tsx`, e `status: "live"` in `navigation.ts`.

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
