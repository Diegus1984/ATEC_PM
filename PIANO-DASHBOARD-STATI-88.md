# Segnalazione #88 — Dashboard PM e stati della commessa

> Aperta il **16/08/2026** da Paolo Zanoni (`MENU PM _ DASHBOARD`, IMPROVEMENT, MEDIUM).
> Allegati: **foto 25** (la tabella commesse com'è oggi) e **foto 26** (la fascia da eliminare),
> scaricate in `_tmp/bug88/`.
>
> Stato del piano: **TUTTO IMPLEMENTATO in locale il 16/08** (B1-B6; Q1=c, Q2=eliminate,
> Q3=generato — approvate). 150 test verdi, build server e client pulite. **Manca il deploy.**

---

## 1. Cosa chiede la segnalazione

Testuale, diviso in pezzi:

1. **La pagina d'ingresso è la Dashboard** («quando entro nel gestionale la pagina aperta a
   fianco del menu principale è appunto questa Dashboard»).
2. **Prima sezione — Commesse**: la tabella della foto 25 **senza la colonna Ore**.
3. **Colonna Stato cliccabile**, con quattro stati e questi privilegi:
   - **Bozza** — «visibile e gestibile solo a PM e amministratore»;
   - **Attiva** — visibile e gestibile secondo le regole di accesso;
   - **Stand-by** — «tutto è visibile e consultabile ma non modificabile per tutti i colleghi,
     solo PM e Amministratore hanno privilegio di operare come se la commessa fosse attiva»;
   - **Chiusa** — stessa identica regola di Stand-by.
4. **Seconda sezione — Altre attività**: tabella analoga, stessi stati cliccabili, e in più il
   privilegio (solo PM/Amministratore) di **«aggiornare una di queste attività rinominandola come
   commessa se necessario»**.
5. **Via la parte superiore della dashboard** (foto 26).

---

## 2. Cosa c'è oggi — fatti misurati, non ricordi

**La pagina.** `/` è `DashboardPage.tsx`: due schede, **Cartelle** (la vista a cartelle-commessa
del blocco 7, quella che si apre per prima) e **Panoramica**. La foto 26 è la testa della
Panoramica: 4 card (`DashboardSectionCards`) + grafico ore (`DashboardHoursChart`). La foto 25 è
la terza parte della stessa scheda: `DashboardProjectsTable`, con i tab Tutte/Attive/Bozze e le
colonne Codice · Titolo · Cliente · Stato · Ore.

**Gli stati.** `ProjectStatuses` (server) e `PROJECT_STATUS_META` (client) ne hanno **cinque**:
`DRAFT` Bozza · `ACTIVE` Attiva · `ON_HOLD` «In pausa» · `COMPLETED` Completata · `CANCELLED`
Annullata. `CANCELLED` **è il soft delete**: eliminare una commessa la porta lì. «Chiusa» oggi
vuol dire `COMPLETED` **o** `CANCELLED` (`ProjectStatuses.Closed`), e serve solo a togliere le
commesse dagli elenchi (`includeClosed`) e a mettere in sola lettura **il foglio SAL**.

**L'unico lock che esiste già** è quello del SAL: commessa chiusa → foglio in sola lettura, la
scavalca chi ha la chiave `action.sal_edit_closed`. È il modello da generalizzare.

**I permessi non hanno più livelli.** Dal 14/08 (v85–v87) la scala TECH→ADMIN è stata sostituita
da chiavi sulla persona: **«solo PM e Amministratore» non è esprimibile così com'è scritto**, va
tradotto in una chiave, seminata su chi oggi ha quel ruolo.

**Quanto lavoro è**, contato sul codice: **385 endpoint di scrittura** in tutto, di cui **91
palesemente legati a una commessa** (projectId nella rotta o nella query) — 21 costing, 18
commessa, 11 trasferta, 7 bilancio, 7 milestone, 6 cash flow, 5 feedback DDP, 4 SAL, 3 check
list, 3 righe spente, 3 ore, 1 chat, 1 dashboard, 1 lavorazioni. Gli altri (MoM, fasi, timesheet,
righe SAL) arrivano alla commessa **risalendo dall'entità**, e vanno censiti a mano: sono quelli
in cui una dimenticanza non si vede.

**Cosa c'è dentro il database di produzione oggi** (16/08): **18 commesse, tutte ACTIVE** — 16
commesse vere e 2 «altre attività». **Zero bozze, zero sospese, zero chiuse.** Le persone attive
sono 37: **1 ADMIN, 2 PM**, 6 responsabili di reparto, 28 tecnici.

> Questo numero è la cosa più importante del piano: **oggi il lock non spegnerebbe niente a
> nessuno**, perché nessuna commessa è sospesa o chiusa. Si può costruire con calma e verificarlo
> su dati veri prima che serva davvero.

---

## 3. Le decisioni da prendere prima di scrivere codice

| # | Decisione | Proposta | Perché |
|---|---|---|---|
| D1 | Mappatura dei quattro stati | Bozza=`DRAFT`, Attiva=`ACTIVE`, **Stand-by=`ON_HOLD`** (rinominato da «In pausa»), **Chiusa=`COMPLETED`** | Non si inventano stati nuovi: si riusa quello che c'è e si cambia l'etichetta |
| D2 | `CANCELLED` | **Resta fuori dalla tendina** e dalle due tabelle | È il soft delete: metterla fra le scelte vuol dire far cancellare una commessa da una tendina di stato |
| D3 | «Solo PM e Amministratore» | ✅ **DECISO 16/08: chiave** `action.project_locked_write` («Opera su commesse sospese o chiuse»), seminata su chi ha ruolo PM/ADMIN — **3 persone** | I livelli non esistono più; con una chiave il perimetro si corregge dalla pagina Permessi senza toccare il codice. Scartato il ruolo cablato: cambiare idea avrebbe richiesto un deploy |
| D4 | Le bozze | Chiave **`data.project_drafts`** («Vede le commesse in bozza»), stessa semina | Idem: «visibile solo a PM e amministratore» è un filtro di visibilità, non un livello |
| D5 | Perimetro del blocco | **Tutto quello che pende dalla commessa** (DDP, timesheet, MoM, check list, milestone, costing, documenti, SAL…), non i soli pulsanti della scheda | La segnalazione dice «tutto è visibile e consultabile ma non modificabile»: un blocco cosmetico lascerebbe passare l'API |
| D6 | Il timesheet | Le **ore su commessa sospesa/chiusa non si registrano più** (per chi non ha la chiave) | È la conseguenza diretta di D5, ma è quella che si sente di più: va detta prima, non scoperta dopo |

**Domande aperte — servono a te, non le decido io** (⚠️ bloccano B4 e B5):

- **Q1. La scheda «Cartelle» resta?** La #88 non la nomina: parla di due sezioni (Commesse, Altre
  attività) e dice di eliminare «la parte superiore» mostrando la Panoramica. Le cartelle sono il
  blocco 7 del piano V32, volute a suo tempo. Tre strade: (a) restano come scheda accanto alle due
  tabelle, (b) spariscono del tutto, (c) restano ma la dashboard si apre sulle tabelle.
  **Proposta: (c)** — non si butta niente e la pagina d'ingresso è quella chiesta.
- **Q2. Le 4 card e il grafico ore** vanno eliminati **del tutto** (via il codice) o solo tolti
  dalla pagina d'ingresso? **Proposta: eliminati**, come dice la segnalazione: codice morto che
  resta è codice che qualcuno rimetterà.
- **Q3. «Rinominare un'altra attività come commessa»**: le si assegna un **codice commessa nuovo**
  generato oggi (`C{aaaammgg}.{NNN}`) o si scrive a mano il codice? **Proposta: generato**, con
  conferma a video che mostra vecchio → nuovo codice.

---

## 4. Il lavoro, in ordine

### B1 — Il cancello unico sulle scritture *(il pezzo grosso, 60% del lavoro)*

- `ProjectWriteGuard` (server): data una commessa dice **scrivibile / in sola lettura**, con la
  regola di D1+D3. Un solo posto che sa la regola, come `ProjectStatuses` per «chiusa».
- Filtro `[RequireProjectWritable]` che risolve il `projectId` da **rotta** (`{projectId}`,
  `api/projects/{id}`) e da **query**: copre i 91 endpoint contati sopra senza toccarli uno per uno.
- Per gli altri — quelli che risalgono dall'entità (riga SAL, fase, voce di timesheet, riga MoM) —
  **chiamata esplicita** dentro l'azione, con l'elenco chiuso scritto nel piano e nel codice.
- **Test di copertura**: un test che enumera per riflessione tutte le azioni di scrittura dei
  controller «di commessa» e pretende che ognuna sia coperta (filtro o chiamata). È l'unica difesa
  contro la dimenticanza silenziosa, ed è lo stesso tipo di rete che ha già preso il difetto del
  backfill nella #83.
- Il lock del SAL (`action.sal_edit_closed`) **resta com'è**: è più stretto e più vecchio, si
  sovrappone senza contraddire.

### B2 — Le bozze si vedono solo se hai la chiave

- Filtro di visibilità su `GET /api/projects` e sui **28 punti del client** che elencano commesse
  (albero commesse, MoM, milestone, SAL, ore, lavorazioni, chat, dashboard…). Il filtro va **sul
  server**: fatto sul client, l'API resterebbe aperta.
- Gate anche sul **dettaglio** (`GET /api/projects/{id}`), altrimenti la bozza si apre a chiunque
  conosca l'id.

**B2 — esposizioni ACCETTATE (scelte, non dimenticanze):** le ore già registrate su una
commessa poi retrocessa a bozza restano visibili nel timesheet di chi le ha fatte (sono ore sue);
le RDO che contengono righe di una bozza restano nel modulo Acquisti (vivono sul codice ATEC,
non sulla commessa); gli aggregati numerici senza codice commessa (Report Controllo conteggi,
Analisi Consegne, contatori) includono le bozze nei totali; `GET /api/phases/{id}/project-id`
risponde anche per una fase di bozza (serve al dialogo di modifica ore, e rivela solo il legame
fase→id); le chat su una bozza restano visibili ai partecipanti (chi è dentro è coinvolto).

### B3 — Stato cliccabile nelle due tabelle

- `PATCH /api/projects/{id}/status` con la sola transizione di stato (l'attuale `PUT` riscrive
  tutta la commessa e chiede `action.edit_project`), realtime `ProjectsChanged` come gli altri.
- Cella con menu a tendina (le quattro voci di D1), stessa forma della cella «Tipo» della DDP.
- Rinomina dell'etichetta `ON_HOLD` da «In pausa» a **«Stand-by»** in `PROJECT_STATUS_META`.

### B4 — La Dashboard nuova *(dipende da Q1 e Q2)*

- Due sezioni: **Commesse** (codice `C`+data) e **Altre attività**, stesse colonne **senza Ore**,
  divise con `isCommessaCode` — la stessa regola già usata per il SAL e il timesheet nella #85/#86.
- Via card e grafico; la scheda Cartelle secondo Q1.

### B5 — «Rinomina come commessa» *(dipende da Q3)*

- Endpoint dedicato, protetto dalla chiave di D3, che assegna il codice nuovo e conserva quello
  vecchio nelle note (la storia non si perde).

### B6 — Migrazione, semina, deploy

- Migrazione v96: registra `action.project_locked_write` e `data.project_drafts` e le **semina**
  su chi ha ruolo PM/ADMIN — con la regola già usata dalla v85: *una chiave nuova nasce invisibile
  a tutti*, quindi va seminata o il giorno del deploy nessuno può più operare sulle sospese.
- Test, build, deploy con backup, verifica su dati veri.

---

## 5. Rischi e trappole

1. **La dimenticanza silenziosa.** Un endpoint di scrittura non coperto dal cancello lascia
   modificare una commessa chiusa senza che nessuno se ne accorga. Difesa: il test di copertura
   di B1, non la buona volontà.
2. **La chiave nuova nasce spenta per tutti** (fallback invertito, v85): senza semina, il giorno
   del deploy **nemmeno PM e ADMIN** potrebbero operare sulle commesse sospese.
   ✅ **Verificato il 16/08 su chi la riceverà**: le tre persone con ruolo PM/ADMIN — Paolo
   Zanoni, Alessandra Abatangelo, Admin ATEC — hanno **tutte username, password e utenza attiva**,
   quindi la semina (che salta chi non ha credenziali) le prende. ⚠️ **Admin ATEC ha il jolly
   `*`** e la semina lo esclude apposta: il jolly vale anche per le chiavi che non esistono
   ancora, quindi la chiave ce l'ha comunque — è giusto così, ma va saputo, altrimenti a deploy
   fatto sembra che la semina abbia mancato una persona su tre.
3. **Le bozze che spariscono** possono far sembrare persa una commessa a chi non ha la chiave.
   Oggi le bozze in produzione sono **zero**, quindi il rischio è futuro, non immediato.
4. **Il timesheet** (D6): il giorno in cui una commessa va in Stand-by, i tecnici non possono più
   imputarci ore. È voluto, ma è la cosa che genererà la telefonata.
5. **Ordine dei lavori**: B3 (stato cliccabile) **prima** di B1 sarebbe pericoloso — si darebbe la
   possibilità di sospendere una commessa mentre il blocco delle scritture non c'è ancora, cioè uno
   stato che promette una cosa e non la mantiene.

## 6. Cosa NON è compreso

- Nessun cambiamento ai **contenuti** della scheda commessa, alle notifiche o al Bilancio.
- Nessuna nuova regola di permesso oltre alle due chiavi di D3/D4.
- «Dopo questo aggiornamento valuto altre implementazioni» (parole della segnalazione): quello che
  verrà dopo non è qui dentro.
