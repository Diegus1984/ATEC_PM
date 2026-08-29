# PIANO — Fasi dettaglio multi-sezione (libreria unica di fasi)

> Deciso il **07/08/2026** con Diego. Chiude la parte «fare ordine per logica e sintassi»
> della segnalazione **#42**, insieme a [ANAGRAFICHE-FASI-SEZIONI.md](../guide/ANAGRAFICHE-FASI-SEZIONI.md)
> (fotografia dell'anagrafica) e a [PIANO-SEGNALAZIONI-BILANCIO.md](PIANO-SEGNALAZIONI-BILANCIO.md).

## Stato

| Fase | Stato |
|---|---|
| **F0** migrazione v73 | ✅ fatta 07/08/2026 — `dotnet build` 0 errori. **Mai eseguita su un database vero**: gira al primo avvio del server |
| **F1** server | ✅ fatta 07/08/2026 — endpoint dei legami, unicità globale del nome, guardia sezione sui legami |
| **F2** Configurazione sezioni | ✅ fatta 07/08/2026 — il drag **aggiunge**; `npm run build` + eslint puliti |
| **F3** nascita fasi (commessa, preventivo, picker) | ✅ fatta 07/08/2026 — la fase nasce una volta per sezione; build + eslint puliti |
| **F4** pulizia | ✅ fatta 07/08/2026 (migrazione **v74**) — con un'eccezione motivata, vedi §9 |
| **F5** runtime | ✅ fatta 07/08/2026 sul DB di sviluppo — vedi §8, ripetuta dopo la F4 |
| **Deploy** | ✅ **in produzione dal 07/08/2026 ore 20:20** — vedi §10 |

**Scelte fatte in F3, da confermare a voce:**
- le fasi di default nascono **solo dalle sezioni attive** (`cst.is_active = 1`): sono le stesse
  che `ProjectCostingController` porta in commessa, e una fase su una sezione spenta resterebbe
  appesa a una sezione che nella commessa non esiste;
- le fasi **«trasversali»** (predefinite e senza nessuna sezione) **restano** com'erano, sia sulla
  commessa nuova sia nella conversione del preventivo: toglierle è una decisione di anagrafica —
  vedi §7;
- `BulkCreate` **non duplica** la stessa fase nella stessa sezione, ma la accetta in sezioni diverse.

## 1. Cosa vogliamo

Una **sola libreria di fasi**. «Call Cliente» esiste **una volta** in anagrafica e viene
agganciata a più sezioni di costo — Program Manager, Progettazione, quello che serve. Oggi
per ottenere lo stesso risultato bisogna creare tre fasi diverse con lo stesso nome, ed è
esattamente da lì che vengono i doppioni («Programmazione PLC» / «Programmazione Plc»,
«Simulazione RobotStudio» ×2, «Caricamento Sw & Debug» ×2).

**Decisione presa:** se una fase è agganciata a 3 sezioni, sulla commessa **nasce 3 volte**,
una riga per sezione. Le ore restano separate per sezione, che è l'unico modo perché il
Bilancio distingua le ore di «Call Cliente» fatte in PM da quelle fatte in Progettazione.

Effetto collaterale voluto: le fasi in anagrafica **diminuiscono**.

## 2. Perché oggi non funziona

`phase_templates.cost_section_template_id` è **una colonna sola**
([DbService.cs:487](ATEC.PM.Server/Services/DbService.cs)): una fase sta sotto una sezione e
basta. Il drop dal dock della Configurazione sezioni fa `PATCH cost_section_template_id`
([CostSectionsTreePanel.tsx:215](atec-pm-web/src/features/admin/config-sections/CostSectionsTreePanel.tsx)),
quindi trascinare «Call Cliente» su una seconda sezione **la toglie dalla prima**, in
silenzio. La pagina nuova racconta già il modello che vogliamo; il database no.

## 3. Modello dati

### 3.1 Tabella ponte

```sql
CREATE TABLE IF NOT EXISTS phase_template_sections (
    id INT AUTO_INCREMENT PRIMARY KEY,
    phase_template_id INT NOT NULL,
    cost_section_template_id INT NOT NULL,
    sort_order INT NOT NULL DEFAULT 0,
    is_default TINYINT(1) NOT NULL DEFAULT 0,
    UNIQUE KEY uk_pts (phase_template_id, cost_section_template_id),
    FOREIGN KEY (phase_template_id) REFERENCES phase_templates(id) ON DELETE CASCADE,
    FOREIGN KEY (cost_section_template_id) REFERENCES cost_section_templates(id) ON DELETE CASCADE,
    INDEX idx_pts_section (cost_section_template_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
```

`sort_order` sta **sul legame**: l'ordine di «Call Cliente» dentro PM non è quello dentro
Progettazione. `is_default` idem: una fase può nascere da sola sotto PM e non sotto
Progettazione.

`ON DELETE CASCADE` su entrambi i lati: cancellare una fase o una sezione porta via i suoi
legami, non lascia orfani. Questo **rende superflua** la guardia lato sezione — vedi §4.

### 3.2 Colonne che cambiano significato

| Colonna | Prima | Dopo |
|---|---|---|
| `phase_templates.cost_section_template_id` | la sezione della fase | **non più letta**; resta popolata un ciclo per sicurezza, si toglie in F4 |
| `phase_templates.is_default` | nasce su ogni commessa nuova | **non più letta**; il default è del legame |
| `phase_templates.category` | copia del nome della sezione | **muore**: con N sezioni non esiste un valore giusto |
| `phase_templates.department_id` | copiata in `project_phases` | il reparto giusto dipende dalla **sezione**, non dalla fase → smettere di usarla (oggi è NULL quasi ovunque: nessuna UI la scrive) |
| `project_phases.cost_section_template_id` | snapshot, spesso NULL | **obbligatoria in scrittura**: è lei che dice dove vanno le ore |

La struttura di `project_phases` **non cambia**: il modello di commessa era già pronto, gli
id delle righe non si toccano, le ore registrate non rischiano nulla.

### 3.3 Migrazione — v73 (73 è il primo numero libero: l'ultima è la 72)

Pattern idempotente non bloccante come la v68 (`DbService.cs:3645-3681`), **e il DDL va
replicato in `InitDatabase`** — è la trappola già costata la v69 (i `CREATE OR REPLACE` di
`InitDatabase` girano solo in sviluppo, in produzione la vista sbagliata è rimasta in vigore).

1. **v73a** — `CREATE TABLE phase_template_sections` (sopra).
2. **v73b** — riempi i legami da quello che c'è:
   ```sql
   INSERT IGNORE INTO phase_template_sections (phase_template_id, cost_section_template_id, sort_order, is_default)
   SELECT id, cost_section_template_id, sort_order, is_default
   FROM phase_templates WHERE cost_section_template_id IS NOT NULL
   ```
3. **v73c — il passo che rende tutto sicuro.** Congela lo snapshot sulle fasi di commessa
   che oggi vivono di fallback:
   ```sql
   UPDATE project_phases pp
   JOIN phase_templates pt ON pt.id = pp.phase_template_id
   SET pp.cost_section_template_id = pt.cost_section_template_id
   WHERE pp.cost_section_template_id IS NULL AND pt.cost_section_template_id IS NOT NULL
   ```
   **Va fatto prima di qualunque altra cosa.** Da domani una fase avrà 3 sezioni e la colonna
   singola ne conterrà una a caso: ogni lettura `COALESCE(pp.…, pt.…)` non ancora congelata
   darebbe **la sezione sbagliata**, senza errore. Loggare quante righe restano NULL.

Righe che restano NULL dopo la v73c = fasi davvero senza sezione. Nel piano segnalazioni
erano **426 su 494**; il numero va ricontato dopo la bonifica v70 (che ha già azzerato i
riferimenti rotti). Vedi §7.

## 4. Server — cosa cambia, file per file

| File | Cosa | Come |
|---|---|---|
| [PhasesController.cs:31](ATEC.PM.Server/Controllers/PhasesController.cs) `GetTemplates` | ritorna una sezione | ritorna la **lista** dei legami per fase (id, nome sezione, gruppo, sort, default) |
| [PhasesController.cs:520](ATEC.PM.Server/Controllers/PhasesController.cs) `CreateTemplate` | unicità nome **per sezione** | unicità nome **globale** — è la regola che impedisce di ricreare i doppioni. Niente sezione nel body |
| [PhasesController.cs:552](ATEC.PM.Server/Controllers/PhasesController.cs) `UpdateTemplateField` | `allowed` include `cost_section_template_id`, `sort_order`, `is_default` | resta solo `name`. Gli altri tre si spostano sugli endpoint dei legami |
| — nuovo | | `POST /api/phases/templates/{id}/sections` (aggiungi legame), `DELETE .../sections/{sectionId}`, `PATCH .../sections/{sectionId}` (sort_order, is_default). Tutti `[RequireFeature("nav.config_sezioni")]` come le altre scritture di anagrafica |
| [PhasesController.cs:562](ATEC.PM.Server/Controllers/PhasesController.cs) `DeleteTemplate` | degrada le fasi di commessa a locali | invariato; i legami se ne vanno in CASCADE |
| [PhasesController.cs:254](ATEC.PM.Server/Controllers/PhasesController.cs) `BulkCreate` | riceve `templateIds`, scrive senza sezione | riceve **coppie** `(templateId, sectionId)`; scrive `cost_section_template_id` + snapshot `name` |
| [PhasesController.cs:103](ATEC.PM.Server/Controllers/PhasesController.cs) `CreateLocal` | rifiuta due fasi con lo stesso nome **nella commessa** | unicità su **(nome, sezione)**: altrimenti «Call Cliente» già presente in PM impedisce di crearla in Progettazione |
| [PhasesController.cs:146](ATEC.PM.Server/Controllers/PhasesController.cs) `PromoteToTemplate` | crea il template con la sezione | crea il template **+ il legame**. Nessun client la chiama oggi, ma resta esposta |
| [ProjectsController.cs:247](ATEC.PM.Server/Controllers/ProjectsController.cs) fasi di default | `phase_templates WHERE is_default=1`, insert **senza sezione** | `phase_template_sections WHERE is_default=1`: **una riga per legame**, con la sua sezione scritta |
| [QuotesController.cs:839-877](ATEC.PM.Server/Controllers/QuotesController.cs) conversione preventivo→commessa | fasi delle sezioni presenti **+** le «trasversali» (`is_default=1 AND cost_section_template_id IS NULL`) | fasi dai legami delle sezioni presenti. **Le trasversali spariscono** — vedi §6 |
| [CostSectionsController.cs:219](ATEC.PM.Server/Controllers/CostSectionsController.cs) guardia DELETE sezione | conta `phase_templates.cost_section_template_id` | conta i **legami**; il resto della guardia (commesse, preventivi) resta com'è |

### Letture da ripulire (F4, dopo il backfill)

Sei punti leggono `COALESCE(pp.cost_section_template_id, pt.cost_section_template_id)`.
Dopo la v73c il fallback non serve più e **mente**, perché `pt` avrà una sola delle N sezioni:

- [DbService.cs:1368](ATEC.PM.Server/Services/DbService.cs) — `v_timesheet_with_section`, la vista da cui passa il costo consuntivo del Bilancio (⚠️ costante unica, non duplicarla)
- [TimesheetController.cs:184](ATEC.PM.Server/Controllers/TimesheetController.cs) — tendina fasi del timesheet
- [PhasesController.cs:63,67](ATEC.PM.Server/Controllers/PhasesController.cs) — lista fasi della commessa
- [BudgetVsActualController.cs:123](ATEC.PM.Server/Controllers/BudgetVsActualController.cs) — preventivato per sezione
- [EmployeesController.cs:87](ATEC.PM.Server/Controllers/EmployeesController.cs) — tecnici eleggibili per fase (via reparti della sezione)
- [ProjectsController.cs:1887](ATEC.PM.Server/Controllers/ProjectsController.cs) — dashboard commessa

## 5. Client — cosa cambia

**Tipi.** `PhaseTemplateDto`: via `costSectionTemplateId`/`costSectionName`/`category`, dentro
`sections: { sectionId, sectionName, groupName, sortOrder, isDefault }[]`.

**Configurazione sezioni** ([CostSectionsTreePanel.tsx](atec-pm-web/src/features/admin/config-sections/CostSectionsTreePanel.tsx)):

- il drop su una sezione **aggiunge** un legame (POST), non sposta;
- il dock mostra su quante sezioni sta ogni fase, e marca **«nessuna sezione»** sulla riga —
  che nel modello nuovo significa: *questa fase non entrerà mai in nessuna commessa*;
- «Scollega dalla sezione» in albero diventa «togli da questa sezione» (DELETE del legame);
- «Default» si spunta **in albero**, sulla riga della fase dentro la sezione — non nel dialogo
  della fase, che ormai è comune a tutte le sezioni;
- `EditPhaseDialog`: resta il solo nome, via il campo Categoria;
- l'ordinamento in `SectionNode` passa al `sort_order` **del legame** — questo chiude anche il
  difetto per cui oggi il riordino con il drag non si vede quando le categorie sono miste;
- i quattro handler di drag&drop prendono `try/catch` + `notifyError`: adesso il drop può
  fallire per davvero (legame già esistente) e oggi tacerebbe.

**Commessa** ([ProjectPhaseAssignments.tsx](atec-pm-web/src/features/commesse/ProjectPhaseAssignments.tsx)):

- `existingTemplateIds` (:500) esclude dal picker i template già presenti → deve ragionare a
  **coppie**, altrimenti una volta messa «Call Cliente» in PM non la si può più aggiungere a
  Progettazione;
- il picker mostra la fase **una volta per sezione ammessa**, e manda le coppie a `bulk`;
- la lista è già raggruppata per sezione (:505) e la creazione di fase locale ha già la sezione
  preselezionata (:288): «Call Cliente» ×3 si legge bene senza altro lavoro.

**Timesheet**: la tendina è già raggruppata gruppo → sezione → fase
([TimesheetEntryDialog.tsx:125](atec-pm-web/src/features/timesheet/TimesheetEntryDialog.tsx)),
quindi tre «Call Cliente» sotto intestazioni diverse si distinguono. **Non serve altro lì**,
ma è il posto dove si vede se il backfill della v73c è andato bene.

## 6. Ordine di lavoro

| Fase | Contenuto | Visibile all'utente | Stima |
|---|---|---|---|
| **F0** | Migrazione v73 a+b+c, DDL anche in `InitDatabase` | no | 1 mezza giornata |
| **F1** | Server: endpoint legami, `GetTemplates`, unicità globale, guardia sezione | no | 2 |
| **F2** | Configurazione sezioni: drop aggiunge, dock, default in albero, ordinamento, errori a video | sì | 2 |
| **F3** | Nascita fasi: `ProjectsController`, `BulkCreate`+picker, `CreateLocal`, conversione preventivo | sì | 2 |
| **F4** | Pulizia: via i sei `COALESCE`, via `category`, via la colonna singola e `is_default` da `phase_templates` | no | 1 |
| **F5** | Runtime sull'app vera: crea commessa, aggiungi la stessa fase a 2 sezioni, imputa ore, controlla il Bilancio | — | 1 |

**F0 e F1 non cambiano niente a video**: si possono mandare in produzione da soli. Da F3 in poi
cambia come nascono le commesse, quindi va fatto in un colpo con F2.

## 7. Rischi, trappole, domande aperte

**Le fasi di commessa senza sezione.** Dopo la v73c restano le righe il cui template non aveva
sezione. Proposta: lasciarle come sono — le ore ci sono e il totale resta giusto, semplicemente
non entrano nella ripartizione per sezione del Bilancio. **Serve una regola tua** solo se le
vuoi assegnare in blocco (per nome della fase). ⏳ *Da decidere.*

**Le fasi «trasversali» alla conversione preventivo→commessa.** Oggi
[QuotesController.cs:854](ATEC.PM.Server/Controllers/QuotesController.cs) aggiunge alla commessa
le fasi con `is_default=1` e **nessuna** sezione. Nel modello nuovo una fase senza legami non
entra da nessuna parte: quel blocco sparisce. ⏳ *Da confermare con Paolo* — se qualcuna di
quelle serve, va agganciata a una sezione.

**Nomi identici ×3.** Dove le fasi si vedono fuori dal contesto della sezione (export/stampa
Gantt, milestone, report) tre «Call Cliente» sono indistinguibili. Da controllare in F5.

**Ore già registrate.** Non si toccano: gli id di `project_phases` restano, il timesheet punta
lì. La v73c scrive solo una colonna che oggi è NULL.

**Migrazione in produzione.** Le migrazioni girano all'avvio del servizio `AtecPmServer`; la
v73c va loggata con i conteggi (righe congelate / righe rimaste NULL) perché è l'unico modo per
accorgersi se un numero del Bilancio si muove.

## 8. Collaudo runtime (F5) — 07/08/2026, database di **sviluppo**

Server avviato in Development, giro completo via API + pagina aperta nel browser. Dati di prova
rimossi a fine collaudo (commessa cancellata in hard-delete, anagrafica riportata com'era),
server e Vite spenti.

**Migrazione v73**, al primo avvio:

> 39 legami fase→sezione creati dall'anagrafica esistente, 26 fasi di commessa hanno ora la
> sezione scritta sulla riga. Restano 3 fasi di commessa senza sezione.

**Il giro completo**, con «Call Cliente» agganciata a Program Manager + Progettazione UTM:

| Prova | Esito |
|---|---|
| Aggancio a una seconda sezione | ok — la fase resta anche nella prima |
| Aggancio ripetuto sulla stessa sezione | rifiutato: «già nella sezione …» |
| Commessa nuova con fasi di default | «Call Cliente» nasce **2 volte**, una per sezione |
| Fasi «trasversali» (default, senza sezione) | entrate come prima (3 righe) — comportamento invariato |
| Ore: 5h su una riga, 3h sull'altra | tendina timesheet: due voci sotto sezioni diverse |
| **Bilancio** | 5h su «Program Manager», 3h su «Progettazione UTM» — **ripartizione corretta** |
| Import a coppie (`bulk`) | 1ª volta «1 fasi aggiunte», 2ª «0 fasi aggiunte (1 erano già in quella sezione)» |
| Fase locale con nome già usato in ALTRA sezione | accettata |
| Fase locale con nome già usato nella STESSA sezione | rifiutata |
| Pagina Config. Sezioni | dock: «Call Cliente — 3 sezioni»; albero: 3 righe, ognuna col badge `+2` |

**Non coperto:** il drag&drop vero col mouse (guidato via API), e la **produzione** — lì la v73
non è ancora passata, e i numeri di partenza sono diversi (in produzione le fasi di commessa
senza sezione erano molte di più).

## 9. F4 — la pulizia, e le due colonne che restano

**Fatto.** Le sei letture non usano più `COALESCE(pp.cost_section_template_id,
pt.cost_section_template_id)`: la sezione è **solo** quella scritta sulla riga della fase di
commessa. Toccati `v_timesheet_with_section` (migrazione **v74**, perché il `CREATE OR REPLACE`
di `InitDatabase` in produzione non gira), `TimesheetController`, `PhasesController`,
`BudgetVsActualController`, `EmployeesController`, `ProjectsController`.

`category` è uscita dall'anagrafica delle fasi: via dal DTO, dalla `SELECT`, dall'`INSERT`,
dalla whitelist della PATCH (ora accetta **solo** `name`) e dalla copia della conversione
preventivo. La colonna resta a DB, vuota, e `project_phases.category` resta com'è (è lo
snapshot che marca 'LOCALE').

**Trovato strada facendo:** `POST /api/phases` (creazione di una singola fase di commessa,
nessun client la chiama ma è esposta) inseriva la riga **senza sezione**. Finché c'era il
ripiego non si notava; togliendolo avrebbe prodotto ore fuori dalla ripartizione. Ora scrive la
prima sezione della fase di anagrafica.

**Non fatto, e non per dimenticanza:** le due colonne `phase_templates.cost_section_template_id`
e `is_default` **restano**.

- `is_default` è l'unica cosa che tiene in vita le fasi **«trasversali»** (predefinite e senza
  sezione, che la creazione commessa e la conversione preventivo portano ancora dentro).
  Cancellarla vuol dire farle sparire — che è proprio la decisione ancora aperta al §7.
- `cost_section_template_id` è la rete per una SPA rimasta in cache che chiama `bulk` con i soli
  id, e viene riallineata da sola a ogni scrittura (`SyncLegacyPhaseColumns`). Costa poco e non
  può divergere.

Quando Paolo decide sulle trasversali, quelle due colonne (e il ramo di compatibilità della
PATCH) si tolgono in mezz'ora.

## 10. In produzione — 07/08/2026 ore 20:20

Backup prima: `C:\ATEC_Backups\atec_pm_prima_v73_20260807.sql` (7,8 MB, «Dump completed»).
Delta di 6,1 MB su 160,4 (9 file), con stop del servizio perché tocca DLL. `AGGIORNAMENTO OK`,
schema **74**, health 200, build `20260807-2019` e bundle `index-CY1AR-BK.js` allineati al locale.

> [Migration v73] 32 legami fase→sezione creati dall'anagrafica esistente, **356 fasi di commessa
> hanno ora la sezione scritta sulla riga**. Restano **121 fasi di commessa senza sezione**.
>
> [Migration v74] 2 imputazioni ore restano senza sezione, di cui **0 prima ne prendevano una dal
> template** — nessuna ora cambia sezione nel Bilancio.

**Il numero che conta è lo zero della v74:** togliere il ripiego non ha spostato un solo numero.

**Da sapere, però:** in produzione ci sono **3 imputazioni ore in tutto** — il timesheet non è
ancora entrato nell'uso. Quindi lo zero è vero ma poco stressato: il collaudo serio di quella
riga arriverà quando i tecnici cominceranno a imputare ore.

**Le 121 fasi di commessa senza sezione** vengono dalle 8 fasi di anagrafica che non hanno
nessuna sezione. Finché restano così, le ore imputate lì non entrano nella ripartizione per
sezione del Bilancio: è la stessa lista di §7 su cui serve la decisione (assegnare o cancellare).

**Runtime dopo la F4** (07/08/2026, DB di sviluppo, dati di prova rimossi):

- v74 al primo avvio: *«0 imputazioni ore restano senza sezione, di cui 0 prima ne prendevano
  una dal template»* — in sviluppo la vista nuova non sposta nessun numero;
- ripetuto il giro completo: «Call Cliente» su 2 sezioni → 2 righe in commessa → 5h + 3h →
  **Bilancio: 5h su Program Manager, 3h su Progettazione UTM**;
- fasi commessa, anagrafica fasi, tendina timesheet, budget-vs-actual, dashboard commessa,
  tecnici eleggibili e `/api/bilancio/summary`: tutti rispondono, nessun errore SQL.
