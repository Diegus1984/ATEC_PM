# Censimento delle chiavi permesso — 13/08/2026

> Prerequisito dell'inversione del fallback (`PIANO-PERMESSI.md` §7.2): finché «chiave non
> registrata = accesso libero», una chiave scritta male **non dà errore, dà accesso**. Dopo
> l'inversione darebbe **403 a tutti**. Questo documento dice se ce ne sono. Fatto scandendo
> tutto il server e tutto il client.

## Esito: si può invertire

> **Nessuna chiave usata nel codice manca dal catalogo** — né lato server (116 occorrenze di
> `[RequireFeature]`, 34 chiavi distinte con enforcement reale) né lato client (45 chiavi fra
> `canAccessFeature`/`canWriteFeature`, menu, sezioni di commessa e mappa delle rotte).
> 45 usate dal client + 5 mai usate = 50 = il catalogo. Quadra.

Nessuna chiave è costruita dinamicamente (nessun `$"nav.{...}"`), quindi la scansione è completa.

Due literal fuori catalogo, entrambi innocui:

| Literal | Perché non è un problema |
|---|---|
| `*` | È il **jolly** della Fase A (`PermissionSeedService.Jolly`): sta in `employee_feature_access`, non deve stare in `auth_features`. |
| `action.create_project` | Non ha nessun `[RequireFeature]`, il client non la usa mai, e `POST /api/projects` non ha comunque guardie. Resta solo nel seed iniziale (gira a catalogo vuoto) e in `ATEC.PM.Shared/PermissionEngine.cs`, che è **codice morto** del client WPF ritirato. |

---

## Quello che l'inversione NON chiude (e va sistemato a parte)

**1. Sedici chiavi su cinquanta non proteggono niente lato server.**
`action.delete_project`, `data.hourly_cost`, `nav.backup`, `nav.codex`, `nav.codex_composizione`,
`nav.commesse`, `nav.danea_migration`, `nav.dashboard`, `nav.digest_email`, `nav.gamma_robot`,
`nav.project_templates`, `nav.risorse`, `nav.sal_condizioni`, `nav.utenti`, `project.dettagli`,
`project.flusso_cassa`. Quelle pagine sono protette da `[RequireLevel(n)]`, cioè dal sistema
parallelo per livello: **girare la manopola in pagina «Permessi» nasconde la voce di menu ma non
chiude l'API**. È esattamente ciò che la Fase E deve sostituire.

**2. Tre endpoint senza alcuna guardia**, trovati strada facendo:
`POST /api/projects` (creazione commessa) e `DELETE /api/projects/{id}` (soft delete a CANCELLED)
non hanno né `[RequireFeature]` né `[RequireLevel]`; le GET di `/api/resource-planner` sono aperte
a ogni autenticato. L'inversione del fallback **non** li chiude: non passano da nessuna chiave.

**3. Quattro punti dove il client decide una SCRITTURA con `canAccessFeature`** invece che con
`canWriteFeature`: `TariffOptionsPanel.tsx:47`, `FeriePage.tsx:91`, `ResourcePlannerPage.tsx:45`,
`ProjectDialog.tsx:99`. Con una concessione in sola lettura l'interfaccia resta scrivibile e a
respingere è solo l'API — lo stesso difetto già corretto in DDP, MoM e Milestone.

---

## Materiale per la Fase E — dove si decide col livello invece che con una chiave

Definizioni: `permissions.ts` (`getUserLevel` :87, `hasLevel` :108, `isAdminLevel` :116,
`isPmLevel` :121, `isResponsibleLevel` :126) e `codex-roles.ts:8` (`canRecodeCodex`).

**La chiave esiste già** — si sostituisce senza crearne di nuove:

| Punto | Oggi | Chiave candidata |
|---|---|---|
| `CommessaTree.tsx:112`, `CommessePage.tsx:336` — elimina commessa | `isPmLevel` | `action.delete_project` (registrata a livello 3: **catalogo e codice dicono cose diverse**) |
| `ProjectDetailsSection.tsx:227` — riquadri Conto Economico, e la sezione «Preventivo vs Consuntivo» (l'unica **senza** `featureKey`) | `isPmLevel` + `economicsOnly` | `data.budget` |
| `QuoteDetailPage.tsx:96` — costi del preventivo | `isPmLevel` | `data.costs` |

**La chiave NON esiste ancora** — va creata *prima* di togliere il controllo per livello,
altrimenti sotto «default nega» la funzione sparisce per tutti: cancellare messaggi altrui in
chat, hard delete righe DDP, import e allineamento fasi, sblocco righe SAL su commessa chiusa,
soglia del Bilancio, MaxCards e flag `in_dashboard` della Dashboard, import SAL, imputare ore per
un'altra persona (`TimesheetEntryDialog.tsx:256`), ricodifica Codex, composizione Codex,
composizione Gamma, cambio stato delle segnalazioni.

🔴 **Il caso peggiore, da correggere comunque:** `GammaRobotPage.tsx:21` decide con il **nome**
del ruolo (`userRole.toUpperCase() === "ADMIN"`), non con il livello, e da lì governa tutta la
scheda Composizione. È il pattern che il commento in `permissions.ts:91-95` dichiara vietato — è
la causa del vecchio incidente del ruolo DEVELOPER trattato come tecnico.

---

## Incoerenze del catalogo, da decidere

- `nav.risorse` (mai usata) e `resources.edit` (usata): due convenzioni di nome per lo stesso
  modulo, una italiana e una inglese.
- `project.dettagli` vs `action.edit_project`: stessa operazione, vince la seconda.
- `project.flusso_cassa` vs `data.revenue`: stessa pagina, vince la seconda.
- `nav.sal`, `nav.sal_condizioni`, `sal.economics`: tre chiavi per un modulo, e
  `nav.sal_condizioni` non protegge nulla.
- `nav.mom` è **una chiave per due voci di menu** (Note MoM e Verbali): non si possono separare.
- `nav.backup` e `nav.digest_email` sono a livello 3 nel catalogo **e** richiuse da `isAdminLevel`
  nella pagina: doppio cancello, se un domani si abbassa la chiave la pagina resta muta.
- `FeatureGuard` lascia passare quando `featureKey` è `undefined`: ogni sotto-rotta nuova
  dimenticata in `route-features.ts` resta aperta **in silenzio**.

## Da ricordare quando si tocca il motore

`[RequireFeature]` con più chiavi è un **OR**: basta che una conceda, sia in lettura sia in
scrittura. Le 9 action DDP di `ProjectsController` ne citano tre ciascuna e restano raggiungibili
anche togliendone due.
