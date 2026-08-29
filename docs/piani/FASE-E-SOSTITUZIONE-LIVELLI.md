# Fase E — la scala di livello sparisce per sostituzione

> `PIANO-PERMESSI.md` §7.4 e §11. Segnalazione **#63**.
> Ogni controllo che oggi decide col **livello del ruolo** diventa una **chiave sulla persona**.
> **A parità di comportamento**: chi vede una cosa oggi la vede anche domani. Il cambio di
> politica (Tecnico senza Dashboard, ecc.) è la Fase **D**, dopo questa.

---

## 0. Le due regole da cui dipende tutto il resto

**1. Il perimetro si conserva seminando.** Una chiave nuova nasce invisibile a chiunque
(fallback invertito in Fase A). Perciò ogni chiave creata qui viene scritta in
`employee_feature_access` per **tutti i dipendenti con livello di ruolo ≥ il livello vecchio**,
`access = FULL`, `origin = CLASSE`. Chi ha il jolly `*` non ha bisogno di righe.

**2. Chiave esistente = si sostituisce e basta**, *purché* `min_level` in catalogo coincida col
livello dell'attributo che si toglie. Vale per tutte tranne una:

| Chiave | `min_level` in catalogo | Livello nel codice | Cosa si fa |
|---|---|---|---|
| `action.delete_project` | 3 | **2** | si riallinea il catalogo a 2 **e** si semina a livello ≥ 2, o i PM perdono l'eliminazione che oggi hanno |

Le altre (`nav.utenti`, `nav.permessi`, `nav.backup`, `nav.digest_email`, `nav.danea_migration`,
`nav.project_templates`, `nav.acquisti_inbox`, `resources.edit`, `data.budget`, `data.costs`)
combaciano: non si semina nulla, le righe ci sono già dalla Fase A.

### Meccanica da non sbagliare

- Più chiavi **dentro un solo** `[RequireFeature(a, b)]` = **OR**.
- **Due attributi distinti** sulla stessa action (o classe + action) = due filtri = **AND**.
  Le chiavi nuove che si aggiungono a un `[RequireFeature]` già presente vanno quindi in un
  **secondo attributo**, mai dentro il primo: dentro, aprirebbero il cancello invece di stringerlo.
- Un attributo sull'action **non si promuove mai alla classe**: quasi tutti questi controller
  hanno GET aperte di proposito (tendine, lookup, `features/my`), e chiuderle è Fase D.
- `[RequireFeature]` respinge i verbi non-GET a chi ha la concessione in `READ`; `[RequireLevel]`
  non distingueva. Nessuno oggi ha righe `READ` su queste chiavi, quindi la parità tiene.

---

## 1. Le 21 chiavi nuove

| Chiave | Liv. vecchio | Cosa governa |
|---|---|---|
| `action.recode_codex` | 1 | Ricodifica Codex: assegna/rimuove il «codice nuovo» |
| `action.assign_atec_code` | 1 | Scrive il Codice ATEC su Danea (Extra1) |
| `action.timesheet_for_others` | 1 | Imputa ore a un'altra persona del proprio reparto |
| `action.delete_ddp_row` | 2 | «Elimina definitivamente» una riga di distinta DDP |
| `action.toggle_dashboard_folder` | 2 | Spunta «In dashboard» sulle cartelle-commessa |
| `action.moderate_chat` | 2 | Vede tutte le chat, elimina messaggi e chat altrui |
| `action.sync_project_phases` | 2 | «Allinea dall'anagrafica» le fasi di una sezione |
| `action.import_sal` | 2 | Import SAL dal backup del vecchio gestionale |
| `action.timesheet_any_employee` | 2 | Imputa ore a chiunque, non solo al proprio reparto |
| `action.import_easyfatt` | 2 | Import anagrafiche da Easyfatt |
| `data.danea_explore` | 2 | Esplora l'archivio Danea (tabelle, colonne, dati grezzi) |
| `action.app_config` | 3 | Configurazione di sistema (`app_config`) |
| `action.manage_codex` | 3 | Sincronizza / modifica / elimina articoli Codex |
| `action.edit_codex_composition` | 3 | Modifica la composizione Codex |
| `action.edit_gamma_robot` | 3 | Modifica la distinta Gamma Robot |
| `action.manage_bug_reports` | 3 | Stato delle segnalazioni e gestione di quelle altrui |
| `action.edit_dashboard_settings` | 3 | Numero massimo di cartelle in Dashboard |
| `action.edit_bilancio_settings` | 3 | Soglia di redditività del Bilancio |
| `action.import_project_phases` | 3 | «Importa fasi» in una commessa |
| `action.sal_edit_closed` | 3 | Modifica il foglio SAL di una commessa **chiusa** |
| `data.timesheet_all_phases` | 3 | Alla persona si offrono **tutte** le commesse e fasi |

Due chiavi separate per il Timesheet perché il controllo di oggi non è uno solo: PM vede
chiunque, il Responsabile solo il proprio reparto. Una chiave sola avrebbe allargato o ristretto.

---

## 2. Server — i 51 `[RequireLevel]`

| File : riga | Endpoint | Liv. | Diventa |
|---|---|---|---|
| `AuthController.cs:163` | POST `/api/auth/set-credentials` | 3 | `nav.utenti` |
| `AuthLevelController.cs:187` | PUT `role-features` | 3 | `nav.permessi` |
| `AuthLevelController.cs:227` | PUT `features/{id}` | 3 | `nav.permessi` |
| `AuthLevelController.cs:250` | POST `features` | 3 | `nav.permessi` |
| `AuthLevelController.cs:277` | DELETE `features/{id}` | 3 | `nav.permessi` |
| `BackupController.cs:15` *(classe)* | tutto `/api/backup` | 3 | `nav.backup` |
| `ConfigController.cs:13` *(classe)* | tutto `/api/config` | 3 | `action.app_config` |
| `EmployeesController.cs:147/184/205` | POST / PUT / DELETE `/api/employees` | 3 | `nav.utenti` |
| `SettingsController.cs:17` *(classe)* | `/api/settings/email` | 3 | `nav.digest_email` |
| `UsersController.cs:14` *(classe)* | tutto `/api/users` | 3 | `nav.utenti` |
| `CodexController.cs:257/317/345/383/431/448` | ricodifica (6 action) | 1 | `action.recode_codex` |
| `CatalogMappingController.cs:140/153/265` | assign / assign-from-bom / unassign | 1 | `action.assign_atec_code` |
| `DaneaSyncController.cs:61/70/136/183` | `explore/*` | 2 | `data.danea_explore` |
| `DaneaMigrationController.cs:16` *(classe)* | `/api/danea-migration` | 1 | `nav.danea_migration` |
| `GammaRobotController.cs:167…367` (9 action) | scritture robot/quadri/distinta | 3 | `action.edit_gamma_robot` |
| `TemplateController.cs:14` *(classe)* | `/api/project-templates` | 2 | `nav.project_templates` |
| `ImportController.cs:15` *(classe)* | `/api/import` | 2 | `action.import_easyfatt` |
| `BugReportsController.cs:216` | PUT `{id}/status` | 3 | `action.manage_bug_reports` **+** resta `nav.bug_reports` di classe |
| `ProjectsController.cs:527` | DELETE `{id}/hard` | 2 | `action.delete_project` |
| `ProjectsController.cs:1867` | DELETE `{id}/ddp-officina/{itemId}` | 2 | `action.delete_ddp_row` **+** resta l'attributo esistente |
| `DashboardController.cs:163` | PUT `folders/{projectId}` | 2 | `action.toggle_dashboard_folder` |
| `DashboardController.cs:212` | PUT `settings` | 3 | `action.edit_dashboard_settings` |
| `BilancioController.cs:135` | PUT `settings` | 3 | `action.edit_bilancio_settings` **+** resta `nav.bilancio` di classe |
| `ResourcesController.cs:367/377/388/398` | digest preview / run-now / send | 1 | `resources.edit` |
| `ResourcesController.cs:412/421/434` | digest settings / status | 3 | `nav.digest_email` |
| `PurchaseRfqController.cs:19` *(classe)* | `/api/purchase-rfqs` | 1 | `nav.acquisti_inbox` |

**Perché Gamma Robot prende una chiave nuova e non `nav.gamma_robot`**: quella chiave è
registrata a `min_level` 2, quindi dalla Fase A i PM ce l'hanno **in FULL**. Usarla sulle
scritture regalerebbe ai PM la distinta Gamma, che oggi è riservata agli ADMIN — un cambio di
politica travestito da sostituzione.

---

## 3. Server — la scala nascosta **dentro** il codice

Non sono attributi, ma sono la stessa scala e vanno via nello stesso giro: lasciarli
significherebbe togliere `[RequireLevel]` e credere di aver finito.

| File : riga | Oggi | Diventa |
|---|---|---|
| `SalController.cs:334` e `:414` | `callerLevel < RoleLevels.Admin` (foglio SAL bloccato su commessa chiusa) | `!CanWriteUser(…, "action.sal_edit_closed")` |
| `SalController.cs:1028` e `:1043` | `CallerLevel() < RoleLevels.Pm` nel corpo | `[RequireFeature("action.import_sal")]` sulle due action, controllo nel corpo **rimosso** |
| `SalController.cs:40` | `CallerLevel()` | rimosso quando non ha più chiamanti |
| `ChatController.cs:58` | `IsPmOrAdmin()` | `CanModerateChat()` = `CanAccessUser(…, "action.moderate_chat")` — righe 74, 165, 327, 430 |
| `TimesheetController.cs:93` | `livello ≥ Pm` (ore di chiunque) | `CanAccessUser(…, "action.timesheet_any_employee")` |
| `TimesheetController.cs:96` | `livello ≥ RespReparto` + reparto condiviso | `CanAccessUser(…, "action.timesheet_for_others")`, il controllo del reparto **resta** |
| `TimesheetController.cs:478` e `:498` | stessa coppia in `GetRegistrableEmployees` | stesse due chiavi |
| `TimesheetController.cs:227` e `:289` | livello del dipendente **bersaglio** ≥ Admin | `CanAccessUser(bersaglio, …, "data.timesheet_all_phases")` |
| `BugReportsController.cs:52` | `IsAdmin` (modifica segnalazioni altrui, riga 379) | `CanAccessUser(…, "action.manage_bug_reports")` |

### 3.1 Due difetti del motore, trovati qui e corretti qui

`CanAccess(ruolo, chiave)` e `CanWrite(ruolo, chiave)` sono le API **per ruolo**: col motore
nuovo acceso **non guardano** `employee_feature_access`. Due controller le usano ancora, quindi
per quelle due decisioni i permessi per persona vengono ignorati e a decidere resta
`auth_features.min_level` — cioè il motore vecchio, sopravvissuto in due punti:

- `SalController.cs:49` — `CanSeeEconomics()` su `sal.economics`
- `DashboardController.cs:90` — `data.revenue`

Diventano `CanAccessUser(employeeId, role, …)`. Non è rifinitura: è la Fase A che non era
arrivata fin qui.

---

## 4. Server — chiavi da mettere dove oggi il freno è **solo il client**

Togliere `isAdminLevel()` da una pagina senza dare la chiave all'API non sostituisce il
controllo: **lo cancella**. Questi endpoint oggi non hanno né livello né funzione, e il divieto
sta solo nel menu del client.

| Endpoint | Chiave da mettere |
|---|---|
| `CodexController` sync / reserve / release / confirm / PUT / DELETE articolo | `action.manage_codex` |
| `CodexController` composizione (aggiungi, sposta, quantità, elimina, importa) | `action.edit_codex_composition` |
| `ProjectsController.cs:1411` DELETE `{id}/ddp/{itemId}` (hard delete riga commerciale) | `action.delete_ddp_row` *(secondo attributo)* |

---

## 5. Client — i punti che decidono col livello

`canAccessFeature` per ciò che **mostra un dato**, `canWriteFeature` per ciò che **abilita una
modifica**. Confondere i due è il difetto già corretto in DDP, MoM e Milestone.

| File : riga | Oggi | Diventa |
|---|---|---|
| `CommessaTree.tsx:112` | `isPmLevel()` — elimina commessa | `canWriteFeature("action.delete_project")` |
| `CommessePage.tsx:72` | `isPmLevel()` — `canSeeEconomics` | `canAccessFeature("data.budget")` |
| `CommessePage.tsx:336` | `isPmLevel()` — elimina commessa | `canWriteFeature("action.delete_project")` |
| `ProjectDetailsSection.tsx:227` | `isPmLevel()` — Conto Economico | `canAccessFeature("data.budget")` |
| `ProjectChat.tsx:83` e `:367` | `isPmLevel()` — elimina messaggi/chat altrui | `canWriteFeature("action.moderate_chat")` |
| `ProjectDdpCommercial.tsx:106` | `isPmLevel()` — elimina definitivamente | `canWriteFeature("action.delete_ddp_row")` |
| `ProjectDdpCommercial.tsx:107` | `canRecodeCodex()` — mappa ATEC | `canWriteFeature("action.assign_atec_code")` |
| `ProjectDdpOfficina.tsx:186` | `isPmLevel()` — elimina definitivamente | `canWriteFeature("action.delete_ddp_row")` |
| `ProjectPhaseAssignments.tsx:494` | `isAdminLevel()` — importa fasi | `canWriteFeature("action.import_project_phases")` |
| `ProjectSal.tsx:67` | `isAdminLevel()` — SAL su commessa chiusa | `canWriteFeature("action.sal_edit_closed")` |
| `SectionPhases.tsx:56` | `isAdminLevel()` — importa fase | `canWriteFeature("action.import_project_phases")` |
| `SectionPhases.tsx:67` | `isPmLevel()` — allinea dall'anagrafica | `canWriteFeature("action.sync_project_phases")` |
| `SalPage.tsx:63` | `isPmLevel()` — importa SAL | `canWriteFeature("action.import_sal")` |
| `BackupPage.tsx:40` | `isAdminLevel()` — tutta la pagina | `canAccessFeature("nav.backup")` |
| `DigestEmailPage.tsx:49` | `isAdminLevel()` — tutta la pagina | `canAccessFeature("nav.digest_email")` |
| `BugReportsPage.tsx:58` | `isAdminLevel()` — poteri di gestione | `canWriteFeature("action.manage_bug_reports")` |
| `CatalogoPage.tsx:186` | `canRecodeCodex()` — codice ATEC | `canWriteFeature("action.assign_atec_code")` |
| `GammaRobotPage.tsx:21` | `userRole === "ADMIN"` | `canWriteFeature("action.edit_gamma_robot")` |
| `codex-roles.ts:9` | `isResponsibleLevel()` | `canRecodeCodex()` → `action.recode_codex`; **nuova** `canAssignAtecCode()` → `action.assign_atec_code` |
| `CodexPage.tsx:211` | `isAdminLevel()` | `canWriteFeature("action.manage_codex")` |
| `CodexCompositionPage.tsx:406` | `isAdminLevel()` | `canWriteFeature("action.edit_codex_composition")` |
| `BilancioPage.tsx:60` | `isAdminLevel()` — soglia | `canWriteFeature("action.edit_bilancio_settings")` |
| `DashboardFolders.tsx:47` | `isPmLevel()` — spunta «In dashboard» | `canWriteFeature("action.toggle_dashboard_folder")` |
| `DashboardFolders.tsx:48` | `isAdminLevel()` — max cartelle | `canWriteFeature("action.edit_dashboard_settings")` |
| `QuoteDetailPage.tsx:96` | `isPmLevel()` — costi preventivo | `canAccessFeature("data.costs")` |
| `TimesheetEntryDialog.tsx:256` | `isResponsibleLevel()` — «Registra per» | `canWriteFeature("action.timesheet_for_others")` |

`GammaRobotPage.tsx:21` era l'ultimo punto del gestionale che decideva sul **nome** del ruolo:
è il difetto per cui il vecchio ruolo DEVELOPER, che stava sopra ADMIN, veniva trattato da
tecnico (`permissions.ts:178-189`).

### 5.1 Quattro scritture governate dalla chiave di lettura

Con la concessione in sola lettura l'interfaccia restava scrivibile e a respingere era solo
l'API. `canAccessFeature` → `canWriteFeature` in:
`ResourcePlannerPage.tsx:45`, `FeriePage.tsx:91` (`resources.edit`),
`TariffOptionsPanel.tsx:47` (`nav.config_sezioni`), `ProjectDialog.tsx:99` (`nav.anagrafica_attivita`).

### 5.2 `FeatureGuard` senza chiave

`FeatureGuard.tsx:37` lascia passare quando `featureKey` è `undefined`: una sotto-rotta
dimenticata in `route-features.ts` resta aperta **in silenzio** — l'opposto del fallback appena
invertito. Diventa un rifiuto. Oggi tutte le rotte censite hanno la chiave, quindi nessuno perde
niente.

---

## 6. Cosa è sparito — FATTO

Cancellati, zero usi rimasti in tutta la soluzione (verificato con `grep`, `dotnet build` e
`tsc -b` puliti):

- `ATEC.PM.Server/Authorization/RequireLevelAttribute.cs`
- `ATEC.PM.Server/Authorization/RoleLevels.cs`
- `permissions.ts`: `hasLevel`, `isAdminLevel`, `isPmLevel`, `isResponsibleLevel`, **e anche**
  `getUserLevel` e `ROLE_LEVEL` — non li usava più nessuno nemmeno la pagina «Permessi», e
  lasciarli sarebbe stato l'invito a ricominciare: la prima pagina scritta di fretta li riusa
  e la scala ricresce.

Restano, e non sono la scala: `FeatureAccessService.GetLevelForRole` (la usano il motore vecchio
e il seed, che devono funzionare finché l'interruttore `PermissionsEngine` può tornare indietro),
la variabile `userLevel` dentro `permissions.ts` (ramo `min_level`, stesso motivo),
`employees.user_role` come **etichetta della classe**.

---

## 6-bis. Tre decisioni prese scrivendo il codice (non erano nel piano)

**1. Prenota / rilascia / conferma del Codex hanno quattro chiavi in OR.** La specifica diceva
`action.manage_codex` e basta. Leggendo i chiamanti è saltato fuori che lo stesso pannello
«Nuovo codice Codex» si apre da quattro porte: la pagina Codex, il dialogo Codice ATEC
(Catalogo, Inbox Acquisti, DDP Commerciale) e il picker della distinta officina — e il rilascio
anche dalla pagina Ricodifica. Con la sola chiave della pagina Codex quei pulsanti avrebbero
smesso di funzionare per chi li usa ogni giorno. Le chiavi sono quindi in OR, una per porta
(`CodexController.cs:518-524`). Resta comunque una stretta: oggi quegli endpoint non hanno
**nessuna** guardia.

**2. Gamma Robot ha una chiave nuova invece di `nav.gamma_robot`.** Quella chiave è registrata a
livello 2: dalla Fase A i PM ce l'hanno già in FULL, quindi usarla sulle scritture avrebbe
regalato loro la distinta Gamma, che oggi è degli ADMIN. Sarebbe stato un cambio di politica
travestito da sostituzione.

**3. La pagina «Permessi» ora dichiara di non comandare più niente.** La Fase E le aggiunge 21
chiavi che la matrice funzioni × ruoli non è in grado di governare: dalla Fase A quella matrice
scrive in `auth_role_features` e `auth_features.min_level`, tabelle che il motore nuovo non
legge. Un amministratore poteva alzare il livello minimo di `action.delete_project`, salvare, e
non ottenere niente — la stessa malattia dei «tre motori che si contraddicono» che il piano sta
curando. `/features/my` ora dice quale motore risponde (`permissionsEngine`) e la pagina mostra
un avviso rosso finché non arriva la scheda della persona (Fase B).

---

## 7. Buchi aperti, fuori dalla Fase E

Trovati durante il censimento, **non** sono controlli per livello: chiuderli è un cambio di
politica e va deciso a parte (`PERMESSI-CENSIMENTO-CHIAVI.md` §2).

- `POST /api/projects` e `DELETE /api/projects/{id}` senza nessuna guardia
- `GET /api/employees` espone nome, email e **username** di tutti a ogni autenticato
- `DdpItemEventsController` senza alcuna chiave
- GET di `/api/resource-planner`, `/api/catalog-mapping`, `/api/auth-levels` aperte
- `POST /api/danea-sync/run` lanciabile da chiunque
- `ProjectHub` / `ResourcePlannerHub`: `JoinProject`, `ChatTyping`, presenza online senza chiave
- destinatari delle notifiche scelti con `user_role = 'ADMIN'` (`NotificationService.cs:105/375`,
  `PlanNotificationService.cs:349`, `BugReportsController.cs:392`) — un profilo fuori gerarchia
  non riceve più niente
- `POST /api/auth/change-password-login` non passa dal blocco a 5 tentativi del login
- Endpoint senza più chiamanti dopo la dismissione del client WPF: `/api/import`,
  `/api/danea-sync/explore/*`, `/api/resource-planner/digest/preview`. Qui si è preferito
  **dare loro una chiave** invece di cancellarli: è la Fase E, non una potatura.
