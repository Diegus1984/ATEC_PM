# Permessi — piano

> 13/08/2026 (sera) — **rev. 2**, corretta dopo la lettura del codice (§0).
> Sostituisce `PIANO-PERMESSI-PROFILI.md`.
> Segnalazione **#63** (Paolo Zanoni, OPEN). Allegato: `CONFIGURAZIONE GESTIONALE.xlsx`.
> Prima di toccare codice: `SELECT status FROM bug_reports WHERE id = 63` in produzione.

---

## 0. Fotografia del codice (verificata il 13/08)

Numeri veri, non a memoria: da qui dipendono l’ordine delle fasi e le taglie.

| Cosa | Dove | Com’è oggi |
|---|---|---|
| Fallback server | `ATEC.PM.Server/Services/FeatureAccessService.cs:141` | feature non registrata = **aperta a tutti** |
| Fallback client | `atec-pm-web/src/lib/auth/permissions.ts:51` | permessi non caricati = **menu intero** |
| Lista Contabilità cablata | `FeatureAccessService.cs:168` | terzo motore, per un ufficio |
| Bypass ADMIN implicito | `FeatureAccessService.cs:217` | l’admin sfugge alla lista di reparto |
| Controlli a livello — server | 20 controller | **51 `[RequireLevel]`** |
| Controlli a livello — client | 20 file | **24 chiamate** `isPmLevel` / `isAdminLevel` / `isResponsibleLevel` / `hasLevel` |
| Catalogo chiavi | `auth_features` | ~50 (43 nel seed + migrazioni) |
| Permessi al client | `session.ts:113`, `AuthBootstrap.tsx:23` | caricati **solo al login** |
| Durata token | `AuthController.cs:144` | **8 ore** |
| `status='ACTIVE'` | `AuthController.cs:96` | verificato **solo al login** |
| Schema | locale **v84** · produzione **v80** | v81-v84 mai deployate |
| **v84** | `DbService.cs:4442` | crea i profili GRANTS `TECNICO` / `RESP_TECNICO` / `ACQUISTI` — **cioè quello che questo piano toglie** |

Tre conseguenze, sviluppate più sotto: l’inversione del fallback va fatta **in due posti** (§7),
E è la fase più grossa e deve venire **prima** di D (§11), la v84 va neutralizzata **prima** del
primo deploy (§10.0).

---

## 1. Obiettivo

Un solo modo per decidere chi vede cosa. Si gestisce in pochi secondi
(assunzione, dimissione, caso speciale, cambio di regola). Non un Excel,
non una scala di ruoli, non tre motori insieme.

La maschera di Paolo resta: **export** dal gestionale, non anagrafica.

---

## 2. Regola unica

Il motore legge **una sola tabella**:

```
employee_feature_access (employee_id, feature_key, access, origin)
access = READ | FULL
origin = CLASSE | MANO      ← chi ha scritto la riga (serve al §4.4)
riga assente = non vede
```

Nient’altro concede permessi. Non il livello. Non la classe. Non il reparto.
Non un `if` sul cognome.

### 2.1 L’unica eccezione, e sta nella stessa tabella

La riga jolly `feature_key = '*'` vale tutto. Ce l’ha **solo la classe Admin**.

Serve perché §7 inverte il fallback: una funzione nuova nasce invisibile a
chiunque — compreso chi deve concederla. Senza jolly, ogni deploy che aggiunge
una pagina diventa una migrazione dati su 19 persone, e il primo che se ne
accorge è l’utente.

Il jolly non è un livello mascherato: è una riga come le altre, si vede sulla
pagina della persona, si può togliere. Con un limite solo — l’ultimo (§5).

---

## 3. Cosa si toglie

| Oggi (accrocchio) | Perché esce |
|---|---|
| Scala TECH &lt; RESP &lt; PM &lt; ADMIN | Un numero apre metà gestionale. Ha costretto GRANTS e la lista chiusa Contabilità. |
| Matrice `/permessi` funzioni × ruoli | Si modifica un ruolo e non si sa chi cambia. |
| Profili GRANTS + eccezioni per persona (**è la v84, solo in locale**) | Due sistemi. I cognomi non sopravvivono a chi se ne va. |
| `ContabilitaFeatures` hardcoded nel servizio | Terzo motore, solo per un ufficio. |

**Non si toglie** (non è un permesso):

- `projects.pm_id` — il PM di quella commessa (notifiche, referente)
- `employee_departments.is_responsible` — responsabile di reparto
- `status = INACTIVE` — chi è via non compare in lista e non entra

`user_role` resta solo come **etichetta della classe** (filtro, ultimo pacchetto
applicato). Da sola non apre nessuna pagina — ma **solo a fase E conclusa**: fino
a lì porta ancora il `level_value` che 51 controlli server e 24 client leggono
(§11).

---

## 4. Due assi, non si mescolano

| Asse | Cos’è | Esempi | Concede permessi? |
|---|---|---|---|
| **Reparto** | Dove lavora (anagrafica, già c’è) | UTE, MEC, PLC, INS, **Acquisti**, **Contabilità** | No |
| **Classe** | Che autorità ha nel gestionale | **Tecnico, Responsabile, PM, Admin** | No: riempie le combo, poi si stacca |

Vinardi è **Responsabile** in **Acquisti**. Non è «un Acquisti».
Un tecnico UTE è **Tecnico** in **UTE**. Il reparto non è una classe.

### 4.1 Le quattro classi

Pacchetto di righe da scrivere sulla persona. Un clic, poi si staccano.
Il dato vero resta sulle combo.

| Classe | In sintesi |
|---|---|
| **Tecnico** | Commesse, ore, Chat, Documenti. Risorse / MoM / Milestone / DDP in lettura. Niente Dashboard, niente Lavorazioni. |
| **Responsabile** | Come Tecnico + DDP in scrittura. |
| **PM** | Il set attuale da livello 2. |
| **Admin** | Tutto (riga jolly `*`), incluso «gestisce i permessi». |

In creazione: si sceglie il **reparto** (anagrafica) e la **classe**.
Suggerimento, non vincolo: `is_responsible` → Responsabile, altrimenti Tecnico.
PM e Admin si scelgono a mano.

### 4.2 Quello che sembrava «preset Acquisti / Contabilità»

Sono combo **di quelle persone**, non una quinta e sesta classe.

- In Acquisti il Timesheet è spento → sulla persona, una combo. Il prossimo
  assunto in ACQ: classe Responsabile (o Tecnico) + **Copia da** un collega
  dello stesso ufficio, oppure si spegne il Timesheet. Non si chiama «classe
  Acquisti».
- In Contabilità si vede SAL / costi / Clienti, non le commesse → stesse
  combo sulla persona. Due-tre colleghi: **Copia da** basta. Il motore non
  ha più l’array `ContabilitaFeatures`.

Il filtro in Permessi è «reparto = Acquisti» o «classe = Responsabile».
Due filtri distinti.

### 4.3 Quattro gesti

| Evento | Gesto | Tempo |
|---|---|---|
| Arriva un collega | Reparto + classe (Applica) | ~10 s |
| Se ne va | **Disattiva**. Fuori lista, fuori menu, **fuori subito** (§8) | ~5 s |
| Uno è diverso | Apri lui, cambi **una combo** | ~5 s |
| «Tutti i tecnici vedano le Lavorazioni» | Filtra classe Tecnico → **Applica ai selezionati** → conferma l’anteprima | ~30 s |
| Stesso pacchetto di un collega (stesso ufficio) | **Copia da** | ~5 s |

Niente righe Excel. Niente eccezioni nominate. Se due persone hanno combo
diverse, è perché qualcuno le ha cambiate — si vede sulla pagina, non in
un elenco nascosto di cognomi.

### 4.4 Regola dell’«Applica classe» (era il buco della rev. 1)

**Applica classe riscrive solo le righe `origin = CLASSE`.** Le combo cambiate
a mano (`origin = MANO`) restano dove sono.

Senza questa regola il gesto da 30 s qui sopra ri-accende il Timesheet
all’ufficio Acquisti e ridà le commesse alla Contabilità. In silenzio. Sarebbe
l’Excel di Paolo un piano più sotto: il piano si regge sulle eccezioni per
persona (§4.2) e il gesto di massa gliele cancella.

Ne discendono due cose:

- **Anteprima obbligatoria** prima di ogni applicazione multipla: «3 persone,
  7 combo cambiate», con l’elenco. Si conferma quello, non «Applica».
- Una combo toccata a mano diventa `MANO` e ci resta finché non si preme
  **Riallinea alla classe** su quella riga: gesto esplicito, per persona.

**Copia da** copia tutto e marca tutto `MANO`: sto dicendo «voglio esattamente
le sue», non «voglio la sua classe».

---

## 5. Interfaccia

Due posti, stesso dato.

**Pagina della persona** (come chiede l’intestazione dell’Excel):

1. Le **9 aree di Paolo**, ciascuna una combo (non abilitato / lettura / scrittura).
2. **Funzioni avanzate** chiuse di default (Backup, Utenti, Inbox, Codex, Danea, …).
   Il catalogo del gestionale è più largo dell’Excel: non si scaricano 40 combo
   su un tecnico. Ma ci devono essere tutte: «non vedo la pagina X» si risponde
   qui, non leggendo il DB.
3. In testa: **«4 combo diverse dalla classe»** con filtro «mostra solo quelle».
   È la differenza fra «configurato» e «andato alla deriva».
4. Pulsanti: Applica classe · Copia da · Riallinea alla classe · (solo chi ha `nav.permessi`)
5. In fondo: **ultime 20 modifiche** ai suoi permessi (§9).

**Pagina Permessi** — lista persone **attive**:

- una riga = una persona
- filtro per **classe** e per **reparto** (due cose diverse)
- colonna **«Diverso dalla classe»**
- selezione multipla + applica classe **con anteprima** (§4.4)
- **Esporta maschera** → Excel di Paolo, sempre allineato

### 5.1 Non ci si chiude fuori

Invariante lato server, controllata su **ogni** percorso di scrittura — combo
singola, applica classe, copia da, applica ai selezionati, **e disattivazione in
anagrafica**:

> deve restare almeno **un dipendente ACTIVE** che può scrivere `nav.permessi`
> (per riga propria o per jolly).

Rifiuto con messaggio leggibile, non 500. La rev. 1 copriva un solo gesto su cinque.

### 5.2 Persona senza righe

Chi viene creato da Utenti e non passa da Permessi ha zero righe: non vede
niente. Deve trovare **«Nessun permesso assegnato — chiedi a un amministratore»**,
non un menu vuoto né un rimbalzo di `HomeRoute`.

---

## 6. Le 9 aree (Excel → chiavi già esistenti)

| Colonna Paolo | Chiave | Combo |
|---|---|---|
| Commesse | `nav.commesse` | no / vede elenco |
| TimeSheet | `nav.timesheet` | no / carica ore |
| Risorse | `nav.risorse` + `resources.edit` | no / lettura / scrittura |
| Chat | `project.chat` | no / uso |
| Verbali MoM | `nav.mom` | no / lettura / scrittura |
| Milestones | `nav.milestones` | no / lettura / scrittura |
| DDP Comm. + Officine | `project.ddp_commerciale` + `project.ddp_officina` | no / lettura / scrittura (una combo, due chiavi) |
| Lavorazioni | `nav.work_requests` | no / lettura / scrittura |
| Documenti | `project.documenti` | no / accesso e file |

Dashboard (`nav.dashboard`) **non è** tra le 9: Tecnico e Responsabile non ce l’hanno.
Atterraggio = prima voce visibile (Commesse). `HomeRoute` al render, non
`AppRoutes()` — già visto il 04/08: i permessi non sono ancora caricati.

---

## 7. Motore

`FeatureAccessService.CanAccessUser` / `CanWriteUser` (e lo specchio in
`permissions.ts`) diventano:

```
riga jolly '*' sulla persona?      → sì
grants della persona sulla chiave? → sì / no
chiave sconosciuta                 → no
```

Fine. Niente `min_level`, niente `access_mode`, niente array Contabilità,
niente bypass ADMIN implicito (`FeatureAccessService.cs:217`): l’admin passa
perché ha il jolly, che si vede.

### 7.1 L’inversione va fatta in due posti

Oggi il fallback permissivo è **doppio**: `FeatureAccessService.cs:141` e
`permissions.ts:51` (`features.size === 0 → true`, idem `hasLevel` con
`levels.length === 0`). Invertito solo il server, un `/features/my` andato male
mostra il menu intero e l’utente sbatte contro pagine che esplodono.

Con l’API dei permessi irraggiungibile il menu deve essere **vuoto**, con un
messaggio. È scomodo ed è giusto.

### 7.2 Prima di invertire: censimento delle chiavi

Ogni chiave citata in `[RequireFeature]` e in `canAccessFeature()` deve esistere
in `auth_features`. Oggi una chiave scritta male non dà errore: dà **accesso
libero**, quindi nessuno se ne accorge. Dopo l’inversione dà 403 a tutti.
Una query (o un test) una volta sola, in Fase A.

### 7.3 Regole di sicurezza che restano

Già fatte in Fase 1, da non disfare:

- `[RequireFeature]` sull’API, non solo nascondere il menu
- più chiavi = OR (Sintesi DDP / Inbox)
- `AccessOnly` per `mark-read` e simili
- gate per-action su DDP e file, mai di classe su `ProjectsController`
- Inbox Acquisti/Officina usano `nav.acquisti_inbox` / `nav.officina_inbox`,
  non le chiavi di sezione commessa

### 7.4 La scala sparisce per sostituzione

`isPmLevel()` / `[RequireLevel(n)]` spariscono **per sostituzione**, non
per cancellazione alla cieca: ogni controllo diventa una chiave sulla
persona (`action.edit_project` c’è già; elimina commessa, costi preventivo,
backup, utenti, config, Codex, Danea, Gamma, Template, Import … diventano
chiavi se mancano).

Sono **51 punti sul server e 24 sul client** (§0). Non è una rifinitura: è la
fase più grossa del piano, e va prima di D (§11).

Feature nuova: si registra nel catalogo, si mette nella **classe** che la
deve avere, si re-applica a chi ha quella classe. Se non la registri,
**nessuno** la vede tranne il jolly (oggi il contrario: non registrata =
aperta a tutti — si inverte).

---

## 8. Chi è già collegato

Oggi i permessi arrivano al client **solo al login** (`session.ts:113`,
`AuthBootstrap.tsx:23`), il token dura **8 ore** (`AuthController.cs:144`) e
`status='ACTIVE'` è controllato **solo al login** (`AuthController.cs:96`).
Quindi, così com’è:

- cambio permessi → l’API stringe subito, il menu no: resta com’era fino a F5;
- **Disattiva** non butta fuori nessuno: chi ha il token lavora fino a 8 ore.

Il gesto «Se ne va, ~5 s» (§4.3) e la riga «utente INACTIVE → 401» del collaudo
sono veri solo con due aggiunte, **in Fase A**:

1. `status` verificato a ogni richiesta autenticata (o a ogni rinnovo del token)
   → 401 e fuori;
2. contatore di versione dei permessi della persona: se cambia, il client
   ricarica `/features/my` da solo. SignalR è già in casa — un `permessi-<id>`
   basta.

Costa poco e va messo subito: senza, ogni collaudo restituisce risultati che
sembrano difetti del motore e non lo sono.

---

## 9. Storico

Chi ha tolto cosa a chi, e quando. Con le righe per persona e i timbri di massa
è la prima domanda dopo il primo incidente, e senza registro non ha risposta.

`employee_feature_access_log (id, employee_id, feature_key, access_before,
access_after, origin, changed_by, changed_at)`, scritto dagli stessi percorsi
dell’invariante §5.1. Stessa idea di `ddp_item_events`. Si legge dalla scheda
persona (§5, punto 5).

---

## 10. Migrazione (senza big bang)

0. **Si neutralizza la v84.** In produzione i tre profili non esistono (schema
   fermo a v80): il rischio è che ce li porti il primo deploy. In locale ci sono
   e vanno tolti. La migrazione di Fase A parte cancellando `TECNICO` /
   `RESP_TECNICO` / `ACQUISTI` da `auth_levels` e le loro righe da
   `auth_role_features`, riportando a `TECH` / `RESP_REPARTO` chiunque vi fosse
   assegnato. Il blocco `if (currentVersion < 84)` resta dov’è: è già registrato
   in locale, non si riscrive la storia.
1. Si crea `employee_feature_access` (+ il log del §9).
2. **Seed**: per ogni dipendente attivo si materializzano i permessi che ha
   *oggi* (livello + grant di ruolo + lista Contabilità), `origin = CLASSE`.
   Nessuno perde nulla in quel momento.
3. **Si verifica il seed col diff, non con la fiducia.** Uno script che per ogni
   dipendente attivo calcola l’insieme delle chiavi viste col motore vecchio e
   col nuovo, e stampa la differenza. Deve essere **vuota**. Il seed non può
   materializzare ciò che non è in catalogo (§7.2): il diff è l’unico modo di
   accorgersene prima degli utenti.
4. Si accende il motore nuovo. Il vecchio non concede più.
5. Si sistemano le classi (Tecnico senza Dashboard, MoM/Milestone in
   lettura) e si re-applicano. Timesheet spento e lista Contabilità si
   sistemano **sulle persone** di quei reparti (`origin = MANO`), non
   inventando classi. **Questo** è il taglio della #63, non lo seed.
6. Collaudo con token per classe (Tecnico, Responsabile, PM, Admin) **e**
   con una persona Acquisti e una Contabilità (combo diverse, stessa classe
   o copia da collega).
7. Deploy. Poi si pulisce `auth_role_features` / `min_level` come fonte.

Fase 1 (chiavi chat/DDP/documenti + buco `server_path`) è **già fatta in
locale, dentro la v82, non in produzione**. Va in produzione **prima** o insieme
allo seed: senza quelle chiavi i pack non possono governare le 9 aree.

---

## 11. Fasi di lavoro

| # | Cosa | Taglia | Esito | Stato |
|---|---|---|---|---|
| **0** | Deploy di ciò che è in locale e non in produzione (v81→v84 **meno** i profili, §10.0) | S | Le 9 aree esistono come chiavi | **fatta, in produzione** |
| **A** | Tabella + jolly + seed + diff + censimento chiavi + motore legge solo la persona + propagazione (§8) + log (§9) | **L** | Un motore. Nessuno perde nulla. Un cambio si vede subito | **fatta, in produzione** |
| **B** | Quattro classi + combo sulla pagina persona + `origin` + copia da | M | Assunzione e caso speciale in secondi | **fatta 13/08/2026 (v86), collaudata a runtime, non deployata** |
| **E** | Sostituire `isPmLevel` / `RequireLevel` con chiavi — 51 punti server + 24 client | **L** | Niente scala nascosta nei pulsanti | **fatta 13/08/2026 (v85), non deployata, manca il collaudo a runtime** — dettaglio in `FASE-E-SOSTITUZIONE-LIVELLI.md` |
| **D** | Classi definitive + Dashboard via da Tecnico/Responsabile | S | #63 chiusa nei fatti | **IN PRODUZIONE dal 14/08/2026** (v87, build `20260814-1008`) — taglio applicato: 221 combo su 32 persone, 144 righe a mano rispettate |
| **C** | Lista Permessi (attivi) + filtro classe/reparto + bulk **con anteprima** + export Excel | M | Cambio di regola e maschera Paolo | **quasi tutta fatta con la B/D**: manca solo l'export della maschera di Paolo |

Ordine previsto: **0 → A → B → E → D → C**. È stato seguito **0 → A → E → B**: la B è stata fatta
subito dopo la E, e con lei è finito il periodo in cui i permessi si potevano cambiare solo dal
database.

**Dove vivono le classi** (il piano non lo diceva): in tabella, `auth_class_features`
(migrazione v86), **non** nel codice e **non** in `auth_role_features` — quest'ultima è la lista
bianca del motore vecchio, che deve restare intatta finché l'interruttore può tornare indietro.
Averle in tabella è ciò che rende la **Fase D una modifica di dati e non un deploy**.

**Cosa resta alla C**: solo l'**export della maschera di Paolo**. Filtri, ricerca, selezione
multipla e «Applica classe ai selezionati» con anteprima sono arrivati con la B e la D.

### Il taglio della Fase D, in chiaro

| Chi | Perde | Guadagna |
|---|---|---|
| **Tecnico** (29 persone) | Dashboard · DDP passano da scrittura a **lettura** · Risorse a lettura | Verbali MoM e Milestones **in lettura** (prima non li vedeva) |
| **Responsabile** (4) | Dashboard | Verbali MoM e Milestones in lettura. **Tiene** DDP in scrittura e gli attrezzi del mestiere (Catalogo, Codex, Inbox, Clienti, Fornitori, planner, ricodifica, Codice ATEC) |
| **PM**, **Admin** | niente | niente |
| **Contabilità**, **Acquisti** | niente | niente — le loro eccezioni sono `MANO` e il timbro di massa non le tocca |

**Da chiarire con Paolo, l'unico punto rimasto aperto**: nell'Excel **Tomasi** è l'unico
non-responsabile con le DDP in scrittura. Dopo la Fase D le ha in lettura come gli altri tecnici.
Se era voluto, gli si mette la combo su «lettura e scrittura» dalla sua scheda (diventa `MANO` e
nessuna applicazione di classe gliela toglie); se era una svista, non si fa niente. **Non si è
cablato il suo cognome da nessuna parte** (§12).

**Perché E prima di D** (cambiato rispetto alla rev. 1). Finché E non è fatta la
«classe» non è un’etichetta: porta il `level_value`, e 51 controlli server + 24
client leggono ancora quello. Rimettere un responsabile sulla classe Tecnico gli
spegne cose che **nessuna riga di permesso può ridargli** — ricodifica Codex,
flusso RDO, ore per conto di un altro. Lo dice già il commento della v84
(`DbService.cs:4455`), che infatti aveva dovuto far nascere i profili a livello 1.

Se E va spezzata per ragioni di tempo, la parte **obbligatoria prima di D** è
l’inventario delle azioni oggi coperte **solo** dal livello ≥ 1 e usate da chi
cambia classe. Il resto (roba da Admin, dove la classe non cambia) può seguire.

C si può usare in forma minima già in B (pagina persona).

---

## 12. Cosa non si fa

- Non si creano classi col nome di un reparto (niente «Acquisti», niente
  «Contabilità» tra Tecnico / Responsabile / PM / Admin).
- Non si creano ruoli `TECNICO` / `ACQUISTI` in `auth_levels` (è la v84: si toglie).
- Non si fa una tabella «eccezioni» per cognome.
- Non si lascia `min_level` come fallback «se manca la riga».
- Non si applica una classe a più persone senza anteprima.
- Non si tiene il fallback permissivo sul client «tanto poi chiude l’API»: il
  menu intero su un’API caduta è esattamente il difetto che si sta togliendo.
- Non si chiede a Paolo «Tomasi sì o no». Se un tecnico deve scrivere le DDP,
  gli si mette la combo. Se era un responsabile non marcato, si marca in
  anagrafica e si applica il preset Responsabile.

---

## 13. Collaudo minimo (fase A+D)

Per ciascuna classe, un utente vero (o token). In più: un collega Acquisti
(Timesheet spento) e uno Contabilità (solo SAL/Clienti) — combo sulla
persona, non una classe extra.

- menu = solo le voci del pack
- sezioni commessa = come l’Excel
- scrittura negata dove la combo è lettura (403, non solo bottone nascosto)
- utente `INACTIVE` **con token ancora valido** → 401 alla prima richiesta, non
  «al prossimo login»; fuori dalla lista Permessi
- cambio di una combo mentre l’utente è collegato → il menu si aggiorna **senza F5**
- **Applica ai selezionati** su una selezione che contiene il collega Acquisti →
  il Timesheet resta spento (riga `MANO`), e l’anteprima lo diceva prima
- API dei permessi irraggiungibile → menu vuoto e messaggio, non gestionale intero
- feature nuova non registrata → non la vede nessuno, **tranne** chi ha il jolly
- togliere `nav.permessi` all’ultimo Admin **o disattivarlo in anagrafica** →
  rifiutato in entrambi i percorsi
- persona senza righe → schermata «Nessun permesso assegnato», non menu vuoto
- export Excel: 19 attivi × 9 aree, valori come li scrive Paolo

---

## Appendice — stato locale al 13/08

Locale **v84**, produzione **v80**: v81-v84 non sono mai partite.
Non rifare, non disfare — tranne dove detto.

- **v82 — Fase 1 (da tenere).** Chiavi `project.chat`, `project.ddp_commerciale`,
  `project.ddp_officina`, `project.documenti` a livello 0 + pulizia
  `auth_role_features` orfane. `RequireFeature` OR + `AccessOnly`. PUT commessa:
  `action.edit_project` + percorso sotto `BasePath`. Cartella Chat non
  raggiungibile da Documenti. Inbox ≠ sezioni DDP di commessa.
- **v83 —** `project_chat_messages.reply_to_message_id` (niente a che vedere coi permessi).
- **v84 — i tre profili GRANTS: da togliere** in Fase A (§10.0).

Dettaglio della Fase 1 in `PIANO-PERMESSI-PROFILI.md` (archivio).
