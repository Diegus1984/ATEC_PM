# Rebuild gestione permessi — piano

**Stato:** in implementazione — **passi 1-6 FATTI il 20/08/2026** (catalogo unico + censimento; EnsureCatalogo + M102; split M103; micro prezzi M104 + filtro unico; scheda persona = matrioska; **pagina Master + copia-clone**, 205/205 test). Resta il passo 7 (pulizia: gergo vecchio, layer 9 aree lato server, motore OLD quando NEW è definitivo) su richiesta esplicita. **Nota: tutto ancora da deployare in produzione.**  
**Simulazione UI:** canvas Cursor `permessi-simulazione-v2`.  
**Regola agent:** `.cursor/rules/permessi-catalogo-sensitive.mdc` (creata 15/08/2026)  
**Documento precedente (motore classi):** `PIANO-PERMESSI.md` — resta storico; questo file è la direzione nuova.

---

## 1. Scopo del rebuild

### Il problema
La gestione attuale (classi TECH / RESP / PM + «Applica classe» / «come la classe» / origin CLASSE·MANO) **non funziona bene per chi amministra**.

- Non puoi concedere permessi **anche solo temporanei** a una persona senza litigare con un **pacchetto di classe** che non vuoi (o non puoi) modificare per tutti.
- L’admin pensa «do a Buda / Tomasi questa cosa»; il sistema parla di pacchetti, origini e riallineamenti.
- Risultato: eccezioni confuse, errori in UI, sensazione di non avere il controllo.

### L’obiettivo
**Controllo diretto e completo** su cosa ogni utente può vedere e fare:

- scheda **persona** = verità a runtime (menu + micro);
- **template master** TECH / RESP / PM / ADMIN = profilo base editabile (stessa matrioska), non pacchetto vivo;
- eccezioni e concessioni temporanee = ritocco sulla persona (marcate, non sovrascritte dal prossimo «Applica template»);
- due persone uguali = **copia scheda** persona→persona, oppure **Applica template** con anteprima;
- TECH / RESP / PM restano anche **scala gerarchica** (ore, Cosi, ambito): quello non si confonde col menu.

In una frase: *i permessi di navigazione viaggiano con l’utente; i master sono scorciatoie (template), non il timone silenzioso.*

### Stato di partenza (verificato sul codice il 15/08/2026)

Il motore in produzione (v86/v87) ha **già** la semantica che questo piano chiede: grant sulla
persona, applicazione esplicita con anteprima, eccezioni `origin = MANO` rispettate (144 righe
rispettate nell’applicazione reale del 14/08), copia da persona. Il rebuild è quindi
**catalogo + UI + linguaggio**, non un motore nuovo. Le cose davvero nuove sono quattro:

1. **Editor dei master.** Oggi i pacchetti (`auth_class_features`) si modificano **solo via
   migrazione** — la #77 è diventata la M089, codice da deployare; `PermissionAdminService` li
   legge soltanto. La pagina Master trasforma quel lavoro in un gesto dell’admin.
2. **Catalogo unico matrioska** allineato al menu reale, al posto della matrice a 9 aree.
3. **Separazione vera menu PM / albero commessa.** Oggi esiste solo a metà: 5 sezioni dell’albero
   riusano la chiave `nav.*` del menu (vedi §3.2) — per questo la M089, togliendo `nav.mom` ai
   tecnici, l’ha spenta anche dentro la commessa.
4. **Micro «vede prezzi» per voce** dichiarato a catalogo (oggi: 3-4 chiavi globali, vedi §4).

---

## 2. Principi

| Principio | Significato |
|-----------|-------------|
| Utente al centro | Grant di menu/micro sulla persona = verità runtime |
| Template ≠ classe viva | Master TECH/RESP/PM si editano a parte; **non** aggiornano nessuno finché non applichi |
| Eccezioni protette | Riga «a mano» sulla persona: Applica template **non la tocca** |
| Matrioska | Sezione spenta → tutto il sottoalbero negato |
| Micro per figlio | Sola lettura / vede prezzi **per voce**, non un flag unico sul padre |
| PM ≠ Commessa | Raccoglitore menu PM e albero sotto Commessa hanno **flag separati** |
| Gerarchia ≠ menu automatico | L’etichetta TECH/PM non apre il menu da sola: apre il *template di partenza* |
| Catalogo dichiara il sensibile | I prezzi non si “scoprono” cercando € a video |
| Server enforce | Voce spenta → 403 anche via URL |
| Spento ≠ assente | Togliere per eccezione scrive una riga di **diniego (`NO`) a mano**, non cancella: un’assenza non si può proteggere (§3.7) |
| Copia = sostituzione | Copia scheda A→B sostituisce tutta la scheda menu/micro di B, **origin compresi** (§3.6) |

---

## 3. Modello funzionale

### 3.1 Cosa vede (scheda permessi)

Allineata al **menu laterale reale**, generata dal catalogo voci:

1. **Sezione** (Principale, PM, Officina, …) — flag padre a tre stati: *spenta / parziale / tutta*
2. **Voci** del menu — solo se il padre è rilevante; ogni voce: visibile + sola lettura + (se dichiarato) vede prezzi
3. Sotto la voce **Commesse** (annidate, non in un pannello a parte): **sezioni dell’albero scheda**

```
Commesse [✓]  ·  Sola lettura (elenco)
  └─ Dashboard Commessa [✓]  · Sola lettura · Vede prezzi
  └─ Verbali MoM [✓]         · Sola lettura
  └─ DDP Commerciali [✓]     · Sola lettura · Vede prezzi
  └─ …
```

### 3.2 Menu PM (raccoglitore) vs sotto Commessa

Il blocco **PM** nel laterale è un **raccoglitore** di viste globali (MoM, SAL, Check list, …).

Le stesse funzioni **dentro una commessa** hanno **permessi separati**.

**Esempio:** tecnico con MoM nell’albero commessa, **senza** voci MoM nel menu PM.

**Oggi è impossibile per 5 sezioni.** MoM, Check list, Milestones, SAL e Lavorazioni riusano
nell’albero la stessa chiave `nav.*` del menu (`commessa-sections.ts`); Dashboard, Flusso, Chat,
DDP ×2 e Documenti hanno già chiavi `project.*` proprie. La separazione richiede lo **sdoppiamento
delle 5 chiavi** (es. `project.mom`, `project.checklist`, …) con una **migrazione di travaso** che
decida i grant — prima domanda: chi oggi ha `nav.mom` riceve anche la chiave d’albero? (La #77 ha
tolto il menu ai tecnici; se l’albero debba restare in lettura — l’intento della v87 — è la prima
decisione del travaso.) Risolto nel §12.4: il travaso è una **fotografia** del motore attuale —
nessuno cambia il giorno del cutover — e la domanda #77 diventa un gesto admin sulla nuova scheda.

### 3.3 Micro standard

Su ogni voce accesa, dove applicabile:

- **Sola lettura** — vede ma non modifica
- **Vede prezzi** — solo se la voce è dichiarata sensibile nel catalogo (costi/€/margini)

Esempio: DDP full + prezzi sì; MoM sola lettura; Dashboard senza prezzi.

Se la voce è spenta, il micro non conta.

### 3.4 Chi è (anagrafica)

| Attributo | Uso |
|-----------|-----|
| TECH / RESP / PM / ADMIN | (1) **scala gerarchica** ore/Cosi/ambiti; (2) **quale template master** è il default di partenza |
| Reparto | Contesto organizzativo |

L’etichetta **non** scrive da sola i grant: indica gerarchia + template di riferimento. I grant li ha la scheda persona (dopo applica / copia / ritocco).

Indicazione ore (da esplicitare in implementazione): es. TECH = sé; RESP = reparto; PM/Admin = ambito più largo — **fuori** dalla matrioska menu.

**Vincolo di implementazione:** l’ambito non si decide col **nome** del ruolo sparso nel codice —
la Fase E ha appena tolto 78 punti così (`GammaRobotPage` era il caso peggiore). Se serve una
scala, si esprime con **chiavi di ambito** (es. `ore.scope.self / reparto / tutte`) lette dal
motore, così resta ritoccabile per persona come tutto il resto.

### 3.5 Template master (nuova «classe» — fatta bene)

Pagina (o sezione) **Master permessi**: una scheda matrioska per ogni profilo — TECH, RESP_REPARTO, PM, ADMIN — **identica** come UX alla scheda persona.

| | Template master | Scheda persona |
|--|-----------------|----------------|
| Cosa è | Profilo base editabile | Verità a runtime |
| Modifica | Non cambia nessuno al salvataggio | Cambia subito cosa vede quell’utente |
| Applicazione | Solo con **Applica template** (esplicito + anteprima) | — |
| Eccezioni | — | Righe marcate «a mano» / lucchetto |

**Applica template** (su una o più persone della stessa gerarchia, o selezionate):

1. Anteprima: elenco voci che cambierebbero  
2. **Salta** le righe persona già eccezione a mano  
3. Conferma → aggiorna solo le righe non protette  
4. Conta in esito: aggiornate / rispettate come eccezione  

**Cosa non è:** niente aggiornamento silenzioso di tutti i TECH quando salvi il master; niente «come la classe» come stato di default confuso; niente obbligo di riallineare per fare un’eccezione temporanea.

Flusso tipico:

1. Si definisce il master **PM** (menu + albero + micro).  
2. Nuovo PM / «parti da template PM» → Applica (o seed alla creazione utente).  
3. Zanoni e Abatangelo allineati; Tomasi = master TECH + eccezioni (es. MoM in commessa, DDP write).  
4. Si evolve il master PM → si applica **solo** a chi vuoi (con anteprima), non a chi ha eccezioni bloccate.

### 3.6 Copia scheda (persona → persona)

- Pulsante **Copia da…** (es. Zanoni → Abatangelo)
- **Sostituisce tutta** la scheda menu/micro del destinatario (anche le eccezioni: è un clone voluto)
- **Gli origin si copiano dal sorgente** (riga da template → `CLASSE`, eccezione → `MANO`).
  L’implementazione attuale marca tutto `MANO`: se restasse così, sul clonato ogni futuro
  «Applica template» salterebbe **tutte** le righe e l’evoluzione dei master (§3.5) sarebbe inerte
- Anteprima obbligatoria prima della conferma
- Gerarchia TECH/RESP/PM **non** viene sovrascritta (resta in anagrafica)
- Diverso da Applica template: la copia non «rispetta» eccezioni; azzera e clona

### 3.7 Dinieghi — spegnere non è cancellare

Il motore ha già le **righe di diniego** (`access = 'NO'`, v87), nate perché *un’assenza non si
può marcare a mano*: senza, la prima applicazione di massa riaccendeva il Timesheet agli Acquisti.
La matrioska ci deve poggiare sopra:

- **Sulla persona**, spegnere una voce che il template concede = scrivere `NO` col badge
  «a mano», **non** cancellare la riga: solo così l’eccezione sopravvive al prossimo
  «Applica template».
- **Nel master**, voce spenta = assenza dal pacchetto (Applica toglie le righe `CLASSE`
  corrispondenti): il `NO` serve alle eccezioni per persona, non ai template.
- **Il toggle di sezione è zucchero UI**: materializza una riga per ogni voce figlia; il server
  ragiona per voce, non per sezione.
- La lista **esclusiva** della Contabilità è fatta di dinieghi espliciti (v87): la migrazione al
  nuovo catalogo li **preserva tali e quali**, non li ricava dall’assenza.
- `/features/my` continua a **filtrare i dinieghi** dai grants: per il client «chiave presente =
  concessa».

### 3.8 Funzioni fuori menu

Il catalogo ha ~71 chiavi e molte **non sono voci di menu né sezioni di commessa**
(`action.delete_project`, `resources.edit`, `data.budget`, …). La matrioska mostra il menu: se il
layer «9 aree / funzioni avanzate» sparisce senza un posto nuovo, quelle chiavi diventano
**ingovernabili da UI**.

Regola: **ogni chiave del catalogo ha una casa**, cioè o è

1. **agganciata a una voce** come micro/azione (es. `action.delete_project` sotto Commesse), oppure
2. nel blocco **«Funzioni avanzate»** della scheda (chiuso di default, come oggi).

La **mappa completa chiave → casa** non si scrive a mano: si **genera** — il censimento del §12
emette gli stub delle chiavi senza casa e il tipo TypeScript generato blocca le chiavi fuori
catalogo. (Era il rischio 2 del §7.)

---

## 4. Catalogo e dati sensibili

### Regola
I dati sensibili **non** si rilevano a runtime cercando `€` in pagina.

### Chi / dove / quando
- **Chi:** sviluppatore  
- **Quando:** stesso PR della schermata o API che espone costi/prezzi/margini  
- **Dove:** catalogo voci (menu + sezioni commessa), campo esplicito es. `sensitive: ["prices"]`

### Effetto
La scheda admin mostra «Vede prezzi» solo se dichiarato. A runtime API/UI non espongono importi a chi non ha il micro.

**Il costo vero è server, non UI.** Oggi i prezzi sono governati da 3-4 chiavi globali
(`data.costs`, `data.revenue`, `sal.economics`). Risolto nel §12.3: le proprietà coi valori si
marcano `[DatoSensibile]` sui DTO e **un solo filtro di risposta** le azzera per chi non ha il
micro — niente filtraggio per-endpoint; la **mappa voce → endpoint** la genera il censimento.

### Come non dimenticarlo
1. Regola Cursor `.cursor/rules/permessi-catalogo-sensitive.mdc`
2. Commento in testa ai file catalogo
3. Test/CI di censimento ancorato alla **mappa voce → endpoint** del punto sopra (il solo grep su `euro(` fa rumore, non censimento)
4. Checklist PR in code review

Senza dichiarazione, l’admin **non può** gestire i prezzi su quella voce.

---

## 5. Come deve essere la pagina (da simulazione v2)

Riferimento vivo: canvas Cursor **`permessi-simulazione-v2`**.  
Questa sezione descrive la pagina prodotto da realizzare (scheda persona in **Permessi**), non il mock.

### 5.1 Percorso
- Elenco persone (`/permessi`) → scheda persona  
- Voce dedicata **Master / Template** (TECH, RESP, PM, ADMIN) — stessa matrioska, senza anteprima «utente live»  
- Niente gergo vecchio: «come la classe», «Riallinea alla classe», «Mostra solo quelle diverse» come esperienza quotidiana confusa  

### 5.2 Layout scheda persona (due colonne)

```
┌──────────────────────────────────────────────────────────────────────────┐
│  [Persona ▾]  [Applica template ▾]  [Copia scheda da…]                    │
│                         Chi è: TECH · Officina Mec.                      │
├────────────────────────────┬─────────────────────────────────────────────┤
│  COSA VEDREBBE A VIDEO     │  SCHEDA ADMIN — Cosa vede                   │
│  (sola lettura, live)      │  (editabile)                                │
│                            │                                             │
│  Menu laterale             │  Sezioni menu (toggle padre + Voci)         │
│   Principale               │   ☑ Principale     [parziale]  [Voci]       │
│    Dashboard               │     …                                       │
│    Commesse                │   ☐ PM (raccoglitore) [spenta] [Voci]       │
│    …                       │   …                                         │
│  ────────────              │                                             │
│  Albero sotto una          │  Sotto Commesse (annidato, vedi sotto)      │
│  commessa                  │  Righe eccezione: badge «a mano»            │
│    Dashboard Commessa      │                                             │
│      [sola lettura|full]  │  ───                                        │
│      [prezzi]             │  Chi è — link anagrafica                     │
└────────────────────────────┴─────────────────────────────────────────────┘
```

- **Sinistra — anteprima:** aggiornata a ogni cambio; pill `sola lettura` / `full` / `prezzi`.
- **Destra — editor:** modifica scheda persona; riga diversa dal template → badge **a mano** (protetta da Applica template).

### 5.3 Intestazione scheda persona
- Selettore / breadcrumb persona  
- **Applica template** (del suo TECH/RESP/PM, o scelta) → anteprima → conferma (rispetta «a mano»)  
- **Copia scheda da…** → anteprima → conferma (sostituisce tutto il menu/micro)  
- Badge **Chi è:** gerarchia + reparto — modifica in Utenti  

### 5.4 Pagina Master / Template
- Lista profili: TECH · RESP · PM · ADMIN (e futuri se servono)  
- Click → **stessa matrioska** della scheda persona (menu + sotto Commesse + micro), **senza** colonna «cosa vedrebbe a video» di una persona reale  
- Salva master = aggiorna solo il template  
- Dalla lista master o dalla lista persone: **Applica a…** (selezione persone + anteprima)  

### 5.5 Editor menu laterale (destra)
Per ogni **gruppo** del catalogo (Principale, PM (raccoglitore), Officina, Acquisti, …):

| Controllo | Comportamento |
|-----------|----------------|
| Toggle sezione | Accende/spegne **tutte** le voci figlie |
| Pill stato | `spenta` / `parziale n/m` / `tutta` |
| Pulsante Voci | Espande l’elenco voci |

Per ogni **voce** (se sezione aperta e non bloccata):

| Controllo | Comportamento |
|-----------|----------------|
| Checkbox voce | Visibile nel menu laterale |
| Sola lettura | Solo se voce accesa — micro **di quella voce** |
| Vede prezzi | Solo se il catalogo dichiara `sensitive` prices su quella voce |
| Badge a mano | Solo in scheda persona, se diversa dal template di riferimento |

Sezione **PM** etichettata come **raccoglitore** (non decide da sola l’albero sotto Commessa).

### 5.6 Albero sotto Commesse (annidato)
**Non** un secondo blocco «2. Sotto Commessa» a fondo pagina.

Quando in Principale è aperta la lista voci e **Commesse** è spuntata:

```
☑ Commesse
    ☐ Sola lettura          ← riguarda l’elenco commesse
    │
    │  Sezioni nell’albero della commessa
    │  (indipendenti dal menu PM)
    ├─ ☑ Dashboard Commessa
    │      ☐ Sola lettura   ☐ Vede prezzi
    ├─ ☐ Flusso di Cassa
    ├─ ☑ Verbali (MoM)          pill: «non in menu PM» | «anche in menu PM»
    │      ☐ Sola lettura
    ├─ ☑ DDP Commerciali
    │      ☐ Sola lettura   ☐ Vede prezzi
    └─ …
```

- Ogni **figlio** ha i propri micro (sola lettura / prezzi).  
- Pill «non / anche in menu PM»: solo confronto (non sincronizza).  
- Commesse spenta → niente albero in editor né in anteprima.

### 5.7 Cosa non deve comparire
- «Come la classe» / «Riallinea» / «Mostra solo diverse» come esperienza quotidiana confusa  
- Aggiornamento silenzioso di tutti i TECH al salvataggio del master  
- Tabella piatta di 80 feature senza legame al menu  
- Un solo switch MoM per menu PM e albero commessa insieme  

### 5.8 Comportamenti da rispettare
1. Tomasi: menu PM spento; albero può avere Verbali MoM (eccezione o master TECH).  
2. Accendere MoM nel raccoglitore PM → laterale; albero non cambia da solo.  
3. DDP full + MoM sola lettura = micro per figlio.  
4. Dashboard Commessa con «Vede prezzi».  
5. Copia Zanoni→Abatangelo: clone completo; Applica template PM su un TECH con eccezioni: le «a mano» restano.  
6. Modifica master PM + salva: **nessun** utente cambia finché non Applichi.

### 5.9 Elenco persone
- Utenti con utenza attiva  
- Sintesi (sezioni accese, n. eccezioni a mano) — non «diverse dalla classe» come concetto centrale  
- Azioni: Applica template ai selezionati (anteprima); Copia da persona  

---

## 6. Cosa togliere (pulizia, dopo cutover)

> ✅ **Fatto il 20/08/2026** (passo 7 del §12.6), tranne il «secondo passo» qui sotto.

### Cancellare / spegnere (guscio classe *vecchio*)
- ✅ UX «come la classe» / «Riallinea alla classe» / «Mostra solo quelle diverse» — sparite dal
  client col passo 5 e ora anche dal contratto: niente `StatoClasse` né `DiverseDallaClasse`
- ✅ Semantica Applica che **non** rispetta eccezioni o che confonde CLASSE/MANO all’utente
- ✅ Layer admin delle «9 aree» — sostituito dalla matrioska: via DTO, array e ramo `AreaId`
- ✅ Qualsiasi aggiornamento automatico dei grant persona al solo cambio etichetta ruolo —
  verificato: `PUT /api/users/role` cambia `employees.user_role` e **basta**, i grant non si
  muovono (cambia solo quale template propone «Applica template»)
- ✅ *(non era in elenco ma è la stessa malattia)* «Nuova funzione» / «Elimina» nel Catalogo
  funzioni: dal passo 2 `auth_features` è la proiezione di `catalogo-permessi.json`, e un
  secondo posto da cui registrare chiavi è di nuovo «due elenchi che divergono»

### Evolvere (non buttare l’idea)
- ✅ Pacchetti per profilo → **template master** (matrioska), applicati solo con gesto esplicito + anteprima + rispetto eccezioni *(passo 6: pagina `/permessi/master`)*
- ✅ `origin` / equivalente → solo per marcare eccezione «a mano» (protezione), non per narrare «come la classe» in UI  

### Secondo passo (motore VECCHIO, se NEW è definitivo) — ⏸️ **NON ancora fatto, di proposito**
- Ramo OLD in `FeatureAccessService` (livelli + `auth_role_features` runtime)  
- Interruttore `PermissionsEngine` quando non serve più il rollback  
- La pagina Catalogo funzioni (matrice funzioni × ruoli, `min_level`, `behavior`) esce **con
  loro**: finché il rollback esiste è il pannello del motore vecchio, e spegnerla adesso
  lascerebbe il rollback senza volante. Oggi avvisa in rosso che non comanda più niente.  

### Non cancellare
- Grant sulla persona (verità runtime)
- Righe di diniego `access='NO'` (v87): reggono le eccezioni «in meno» e le liste esclusive (Contabilità)  
- `FeatureAccessService` / `RequireFeature`  
- Catalogo voci / `auth_features` (o successore)  
- Copia da persona + Imposta sulla persona  
- `user_role` TECH/RESP/PM — gerarchia **e** scelta del template  
- Invariante ultimo amministratore  
- Log / `permissions_version`  

Le migrazioni storiche non si riscrivono; si spegne il codice runtime obsoleto.

---

## 7. Rischi noti (applicare al software attuale)

1. Doppio motore durante la transizione  
2. Mappa chiavi feature → voci matrioska ambigua → si genera col censimento (§12; regola in §3.8)  
3. Micro non uniforme oggi  
4. API senza enforce allineato al menu  
5. Migrazione eccezioni MANO esistenti e dei dinieghi `NO` (preservarli tali e quali, §3.7)  
6. Copia scheda e Applica template confusi tra loro (messaggi UI chiari)  
7. Residui dove `user_role` apre ancora il menu da solo  
8. Due liste (sidebar + `COMMESSA_SECTIONS`) da unificare in un catalogo, con **5 chiavi condivise da sdoppiare** (§3.2)  
9. Admin / jolly `*` nei template  
10. Comunicazione: «template» ≠ vecchia «classe»  

Priorità: cutover pulito, eccezioni, enforce API, micro prezzi, UX Applica vs Copia.

---

## 8. Migrazione (intenti)

- Soft: schede persona dalle grant attuali  
- Master iniziali: da pacchetti classe attuali (TECH/RESP/PM/ADMIN) convertiti in matrioska template  
- Eccezioni MANO → badge «a mano» sulla persona; dinieghi `NO` **preservati tali e quali** (§3.7)  
- Sdoppiamento delle 5 chiavi condivise menu/albero con **travaso esplicito** dei grant (§3.2)
- «Copia da»: adeguare l’implementazione attuale (oggi marca tutto `MANO`) alla regola origin del §3.6
- Cutover corto o feature flag; evitare due verità a lungo  

---

## 9. Criteri di successo

- Admin concede/revoca a una persona in pochi click, anche in modo temporaneo (eccezione a mano)  
- Master PM/TECH editabili senza muovere nessuno finché non si Applichi  
- Applica template rispetta le eccezioni; Copia da persona clona tutto  
- Tecnico con MoM in commessa ma senza raccoglitore PM  
- Prezzi nascosti dove dichiarato, enforce API  
- Spegnere una voce a una persona **sopravvive** all’Applica template (diniego a mano, §3.7)
- Un master si modifica **dall’app**, senza scrivere una migrazione (oggi: M086–M089)
- Niente gergo «come la classe» nel flusso quotidiano  

---

## 10. Riferimenti

| Cosa | Dove |
|------|------|
| Piano classi attuale (storico) | `PIANO-PERMESSI.md` |
| Profili / note correlate | `PIANO-PERMESSI-PROFILI.md` |
| Regola agent catalogo sensitive | `.cursor/rules/permessi-catalogo-sensitive.mdc` |
| Mock UI scheda persona | canvas `permessi-simulazione-v2` |
| Menu oggi | `atec-pm-web/src/config/navigation.ts` |
| Sezioni commessa oggi | `atec-pm-web/src/features/commesse/commessa-sections.ts` |

---

## 11. Fuori scope di questo documento (ancora aperti)

- Elenco fine regole ore/Cosi per TECH vs RESP vs PM (impostazione chiusa: chiavi di ambito, §12.5 — resta l'elenco dei valori)  
- Micro “speciali” oltre sola lettura / prezzi (il modello li accoglie: micro = chiavi figlie, §12.1 — resta da decidere quali)  
- UI mock dedicata alla sola pagina Master (estensione del canvas)  
- ~~Piano di sprint / ordine di implementazione~~ → chiuso: §12.6  

---

## 12. Soluzione alle parti aperte — catalogo polimorfico autocensito

Deciso il 15/08/2026. Le «mappe da scrivere» non si scrivono: o valgono **per costruzione**
(una chiave può nascere solo dentro una casa) o si **generano dal codice**. Il modello è
polimorfico: ogni cosa governabile è una voce dello stesso contratto, e i consumatori — sidebar,
albero commessa, scheda admin, enforcement — sono proiezioni dello stesso albero.

### 12.1 Un solo contratto, N kind

`ATEC.PM.Shared/catalogo-permessi.json` — unico file sorgente, versionato, **embedded resource**
di Shared (il server lo legge a runtime, il web alla build):

```json
{ "kind": "voce", "chiave": "nav.commesse", "label": "Commesse",
  "figli": [
    { "kind": "sezione-commessa", "chiave": "project.mom", "label": "Verbali (MoM)" },
    { "kind": "sezione-commessa", "chiave": "project.ddp_commerciale", "label": "DDP Commerciali",
      "micros": ["prices"] },
    { "kind": "azione", "chiave": "action.delete_project", "label": "Elimina commessa" }
  ] }
```

Contratto unico `VoceCatalogo`: `kind · chiave · label · micros · figli` (+ i campi di servizio
`soloClient`+`motivo`, `eredita`, `alias`, `ritirata` — §12.8). Kind previsti: `sezione` (gruppo menu), `voce` (voce menu), `sezione-commessa` (albero),
`azione` (funzione fuori menu: la casa è la voce che la ospita — chiude il §3.8), `ambito`
(scala ore/Cosi — chiude il §3.4). Un kind nuovo non rompe i consumatori: chi non lo conosce usa
il renderer di default (riga con checkbox nella scheda). `micros: ["prices"]` **è** la
dichiarazione `sensitive` del §4: una cosa sola, non due.

**I micro sono chiavi figlie, non colonne nuove**: «vede prezzi» sulle DDP = chiave
`project.ddp_commerciale.prices` generata dal catalogo. Così eccezioni a mano, dinieghi `NO`,
anteprima, Applica template, copia e log valgono anche per i micro **senza una riga di codice
nuova** (`employee_feature_access` resta com'è). La «sola lettura» resta `access = READ` sulla
chiave della voce.

### 12.2 Autocompletamento client (per costruzione)

Uno script (`atec-pm-web/scripts/genera-catalogo.mjs`, agganciato a `npm run dev`/`build`)
genera `catalogo.gen.ts`: l'albero tipizzato + il tipo unione
`type ChiaveCatalogo = "nav.commesse" | "project.mom" | …`.

- `navigation.ts` e `commessa-sections.ts` smettono di essere cataloghi: tengono la decorazione
  (icone, path, status) e tipizzano `featureKey: ChiaveCatalogo` → **l'editor suggerisce le
  chiavi e `tsc` blocca quelle fuori catalogo**. Una chiave inventata non compila.
- La scheda matrioska (persona e master) **rende l'albero del catalogo** con uno switch sul
  `kind`: una voce aggiunta al JSON compare da sola nella scheda admin, senza toccare la pagina.

### 12.3 Autocensimento server (generato, col cancello dei test)

Un test in `ATEC.PM.Tests` (già cancello del deploy) riflette sugli attributi:

- ogni chiave usata in `[RequireFeature]` (leggibile via reflection da `Arguments[0]`) deve
  esistere nel catalogo — se manca, il test **fallisce stampando lo stub JSON pronto da
  incollare** (kind dedotto dall'uso): la mappa chiave → casa si compila da sola al primo giro;
- ogni chiave di catalogo o è usata da almeno un endpoint o è marcata `soloClient` — le «16
  chiavi che non proteggono nulla» del censimento Fase E diventano un fatto visibile e vietato;
- il test **scrive la mappa chiave → endpoint** come artefatto
  (`PERMESSI-MAPPA-ENDPOINT.gen.md`): la mappa del §4 è un output, non un documento da mantenere.

**Prezzi.** Le proprietà coi valori si marcano sui DTO in Shared (`[DatoSensibile("prices")]`).
**Un solo result filter globale** in `Program.cs`: per gli endpoint la cui voce dichiara il micro
`prices`, se l'utente non ha `<chiave>.prices` (normale `CanAccessUser`: è una chiave come le
altre) **azzera le proprietà marcate** prima della serializzazione (riflessione cacheata per
tipo, envelope `ApiResponse` compreso) — niente filtraggio per-endpoint. Il censimento chiude il
cerchio nei due versi: endpoint che restituisce membri `[DatoSensibile]` con voce senza micro
`prices` → rosso; voce col micro senza nessun membro marcato → rosso (dichiarazione morta). Gli
endpoint non-DTO (file, export) escono dal filtro e il censimento li elenca come casi da gestire
a mano.

*Nota d'implementazione (passo 4):* i tipi di ritorno degli endpoint non si risolvono con la
riflessione statica (`IActionResult` è opaco), quindi la coerenza endpoint↔DTO è garantita **a
runtime dal filtro** (che risolve le voci dagli attributi dell'endpoint) e presidiata dal
censimento su tre fronti: proprietà marcate obbligatoriamente nullable, filtro registrato in
Program.cs, dichiarazioni a catalogo e membri marcati che esistono insieme. L'abbinamento fine
voce→DTO resta una regola di review (`.cursor/rules/permessi-catalogo-sensitive.mdc`).

**In scrittura il filtro ha un gemello obbligatorio (anti-sovrascrittura).** Chi non ha il micro
riceve i campi prezzo a `null`: se poi SALVA, quel `null` non deve arrivare al DB — o il
salvataggio di un utente senza prezzi **cancellerebbe i prezzi veri**. Regola: sugli endpoint di
scrittura i membri `[DatoSensibile]` in ingresso si **ignorano** quando lo scrivente non ha il
micro (si conserva il valore a DB); censimento rosso su ogni PUT/POST con DTO sensibile senza
questo comportamento. È la falla più pericolosa dell'intero disegno: dati distrutti da un gesto
legittimo.

**Regola nullable.** Le proprietà `[DatoSensibile]` devono essere **nullable** (`decimal?`):
azzerare un `decimal` produrrebbe uno **0,00 € finto** — peggio che nascondere, è un dato falso a
video. Censimento: `[DatoSensibile]` su tipo non-nullable → rosso. Il client rende `null` come
«—» e non somma mai campi nulli.

**SignalR non passa dal filtro.** Gli eventi realtime restano «id + azione, i dati si rileggono
via API» — è già lo stile del codice (`new { action, bugId }`). Censimento: payload di hub con
membri `[DatoSensibile]` → rosso.

### 12.4 Travaso delle 5 chiavi = fotografia, non decisione

Come il seed della Fase A («non traduce: interroga il motore»): la migrazione dello split scrive
`project.X := stato effettivo di nav.X` per ogni persona (stesso `access`, stesso `origin`) e nei
pacchetti di classe. Il giorno del cutover **nessuno vede niente di diverso, per costruzione**
(diff a zero verificabile come in Fase A). La domanda della #77 — ridare l'albero MoM ai
tecnici — smette di essere una decisione di migrazione: diventa un normale gesto admin sulla
nuova scheda (o un Applica template) il giorno dopo.

### 12.5 Ambiti ore/Cosi = kind del catalogo

`ore.scope.self / ore.scope.reparto / ore.scope.tutte` come voci `kind: "ambito"` sotto
Timesheet: stessa matrioska, stesso censimento, stesse eccezioni per persona. Nessuna scala
speciale e nessuna decisione sul nome del ruolo (il vincolo del §3.4). Resta aperto solo
**l'elenco fine** dei valori (quali ambiti per quali funzioni).

### 12.6 Ordine di implementazione (dedotto dalle dipendenze)

1. ✅ **FATTO 20/08/2026** — `ATEC.PM.Shared/catalogo-permessi.json` (73 chiavi, embedded) +
   `PermessiCatalogo.cs` (lettore/validatore) + `genera-catalogo.mjs` → `catalogo.gen.ts`
   (tipo unione, agganciato a predev/prebuild, `featureKey` tipizzato nei due file client) +
   `CensimentoCatalogoTests` (4 garanzie) + artefatto `PERMESSI-MAPPA-ENDPOINT.gen.md`.
   Zero cambi runtime; 193/193 test verdi, build web verde. 9 chiavi marcate soloClient,
   1 ritirata (`data.hourly_cost`), 6 condivise menu/albero marcate per il passo 3.
2. ✅ **FATTO 20/08/2026** — `CatalogoPermessiSync.Allinea` chiamato da `InitDatabase` accanto a
   `EnsureViews` (dentro il lock, avvio fermo se il catalogo è rotto, manopola StopOnError);
   migrazione **M102** (`auth_features.retired_at`). Registra chiavi nuove a `min_level 3`
   (rollback al motore vecchio non apre niente), **materializza i micro** come chiavi figlie
   (§12.8.3), **migra gli alias** una volta sola coi grant al seguito (§12.8.2, log escluso),
   marca ritirate e ripesca, segnala le orfane senza toccarle (§12.8.10). `/features/my` esclude
   le ritirate (catalogo, jolly, righe esplicite). 4 test su DB vero (idempotenza compresa);
   il test di idempotenza ha già scovato una chiave fantasma (`action.create_project`, seminata
   solo dal bootstrap, mai usata → registrata come ritirata). 197/197 verdi.
   Le migrazioni non registrano più chiavi: restano per i **grant** (chi riceve cosa), che sono
   decisioni.
3. ✅ **FATTO 20/08/2026** — migrazione **M103**: `project.{mom,checklist,milestones,sal,
   work_requests}` fotografate da `nav.*` (righe persona con accesso E origine, dinieghi
   compresi, pacchetti di classe, liste motore vecchio, `min_level`). I 5 controller in **OR**
   (`project.X`, `nav.X`) — §12.8.4; albero client su `project.*`, menu su `nav.*`;
   `FeatureGuard`/rotte accettano più chiavi in OR (`mom/:id`). Chat resta volutamente
   condivisa (funzione unica, per tutti dalla v88). **Matrice runtime: 324 controlli su 36
   utenti veri, 0 divergenze** (`project.X ⟺ nav.X` per tutti + endpoint OR coerenti).
   198/198 test (fotografia provata anche su dati pregressi, diniego NO compreso).
   Da qui il caso #77 (albero senza menu) è un gesto admin, non una migrazione.
4. ✅ **FATTO 20/08/2026 (pilota DDP)** — migrazione **M104**: semina fotografica di
   `<voce>.prices` per le 6 voci della famiglia distinte (DDP Commerciali/Officina, Gestore,
   Inbox Acquisti, Lavorazioni, Inbox Officina), eredità = la voce stessa: chi vede la voce
   tiene i suoi numeri, dinieghi compresi. `[DatoSensibile]` sui DTO delle righe
   (UnitCost/TotalCost/TotalValue/HourlyRate…, tutti nullable), **`PrezziSensibiliFilter`**
   unico in Program.cs: in lettura azzera (il JSON OMETTE i campi, `euro()` rende «—»), in
   scrittura **respinge con 403** i membri sensibili valorizzati da chi non ha il micro;
   i percorsi di scrittura trattano null come «non toccare» (COALESCE/flag). Censimento:
   nullable obbligatorio, filtro registrato, dichiarazioni⟺membri marcati. **Matrice runtime:
   219 controlli su 36 utenti + utente con diniego di prova — micro assente, prezzi spariti
   dal JSON, scrittura respinta.** 202/202 test. Limite noto: nelle viste aggregate del
   Gestore i totali calcolati a video valgono 0 per chi non vede i prezzi (le celle mostrano
   «—»); l'estensione ad altre voci sensibili = stessa ricetta, una migrazione di semina per PR.
5. ✅ **FATTO 20/08/2026** — la scheda persona È la matrioska (`MatrioskaPermessi.tsx` +
   `SchedaPersonaPage` riscritta): a sinistra «cosa vedrebbe a video» (menu + albero commessa,
   ricalcolati a ogni modifica), a destra l'editor che **rende l'albero del catalogo** con lo
   switch sul kind (renderer di default per i kind futuri, §12.2). Sezioni con toggle padre e
   pill spenta/parziale n/m/tutta; voci con checkbox + micro «sola lettura» e «vede prezzi»
   (che scrive su `<chiave>.prices`); sezioni-commessa ANNIDATE sotto Commesse; azioni sotto
   la voce che le ospita; badge «a mano» e «torna al template» per riga. Spento = riga `NO` a
   mano (§3.7: passa dall'endpoint Imposta esistente). Vecchio layer «9 aree» e «Funzioni
   avanzate» eliminati; gergo nuovo («Applica template», «Copia scheda da…», «Torna al
   template»); l'elenco mostra «Eccezioni a mano» (campo `AMano` nuovo) al posto di «diverso
   dalla classe». API server invariate (Imposta/riallinea/applica/copia di Fase B). tsc,
   eslint, build e 202/202 test verdi.
6. ✅ **FATTO 20/08/2026** — pagina **Master / Template** (`/permessi/master`): un tab per
   profilo, la STESSA matrioska della scheda persona (senza anteprima utente), pacchetto
   editabile dall'app («spenta» nel master = la voce ESCE dal pacchetto, §3.7; il jolly del
   template non si tocca; salvare NON muove nessuno). **«Applica a…»** con selezione persone,
   template ESPLICITO (`ApplicaClasseRequest.Classe`, validato) e anteprima obbligatoria.
   **Copia = clone con gli origin del sorgente** (§3.6/§12.8.5): le righe da template restano
   CLASSE (i futuri Applica funzionano sul clone), le righe in più SPARISCONO, e anche la
   copia passa dall'anteprima. Endpoint nuovi: GET classi, PUT pacchetto; 3 test su DB
   (template senza effetti collaterali, override rispettoso delle eccezioni, clone con
   origin). 205/205 test, tsc/eslint/build verdi. Prima di questa pagina un ritocco al
   pacchetto era una migrazione (la #77 fu la M089).
7. ✅ **FATTO 20/08/2026** — pulizia §6. **Via il layer «9 aree»**: `AreaPermessoDto`,
   `SchedaPermessiDto.Aree`, l'array `Aree` + `AreaDiChiave` + `StatoArea` nel servizio e il
   ramo `AreaId` di `Imposta` (una chiave alla volta: la matrioska accende una sezione
   mandando le sue chiavi). **Via il «diverso dalla classe»**: `DiverseDallaClasse` (scheda ed
   elenco) e `FunzionePermessoDto.StatoClasse` — l'elenco mostra «a mano», e in più `Elenco`
   non scandisce più l'intero catalogo per ogni persona. Gergo nuovo anche nelle stringhe a
   video e nei nomi: `EsitoApplicaClasseDto.Combo` → `Voci`, `PUT /api/permessi/combo` →
   `/voce`, «Applica classe» → «Applica template», «Riallineato alla classe» → «Tornato al
   template». **Catalogo funzioni**: tolti «Nuova funzione» ed «Elimina» (client + endpoint
   `POST`/`DELETE /api/auth-levels/features` + `CreateAuthFeatureRequest`) — dal passo 2
   `auth_features` è la proiezione del JSON, quindi creare una chiave lì la lasciava orfana e
   cancellarne una portava via le righe di `auth_role_features` per poi vederla tornare al
   riavvio; la pagina resta come specchio del catalogo e pannello del motore vecchio, con
   l'avviso che dice dove si registrano davvero le funzioni (`min_level`/`behavior` restano
   editabili: sono le manopole del rollback).
   🪤 **Il censimento ha trovato un buco vero appena tolte le 9 aree**: `nav.commesse` e
   `nav.risorse` risultavano «usate» solo perché comparivano come stringhe in quell'array —
   sul server nessun `[RequireFeature]` le nomina. Marcate `soloClient` col motivo (§12.8.6):
   `GET /api/projects` è l'elenco commesse di tutto il gestionale (lo leggono MoM, Timesheet,
   Check list, Chat, Trasferta) e metterci il gate chiuderebbe mezze pagine; la lettura del
   planner risorse è di tutti per scelta e la scrittura ha già `resources.edit`. 205/205 test,
   tsc/eslint/build verdi.
   **Non toccato di proposito**: il ramo OLD di `FeatureAccessService` e l'interruttore
   `PermissionsEngine` (§6 «secondo passo») — è la strada del rollback e resta finché NEW non
   è definitivo in produzione.

Ogni passo è deployabile da solo; fino al 5 **non cambia niente per gli utenti**.

### 12.7 Perché risponde ai requisiti

- **Si autocompleta**: l'editor suggerisce le chiavi (tipo unione generato) e quelle inventate
  non compilano; il test emette gli stub delle voci mancanti; le due mappe (§3.8, §4) sono
  artefatti generati; `EnsureCatalogo` allinea il DB da solo; la scheda admin mostra da sola le
  voci nuove.
- **È polimorfico**: un contratto, N kind, renderer con default; i micro sono chiavi come le
  altre (tutta la macchina eccezioni/dinieghi/log li tratta gratis); il filtro prezzi lavora su
  qualsiasi DTO marcato senza conoscere gli endpoint; il censimento legge gli attributi, non i
  nomi dei controller.

### 12.8 Revisione adversariale (15/08) — falle trovate e contromisure

Le prime tre (sovrascrittura in scrittura, nullable, SignalR) sono già regole del §12.3.
Le altre:

1. **Nascita dei micro `.prices` = blackout prezzi.** Sotto «default nega», al primo avvio col
   filtro attivo nessuno (tranne il jolly) avrebbe i micro → prezzi spariti per tutti insieme
   (la lezione della v85). Contromisura: **semina fotografica** — ogni voce sensibile dichiara
   nel catalogo da quale chiave globale eredita (`"eredita": "data.costs"`) e ogni persona
   riceve `<voce>.prices` = stato effettivo di quella chiave oggi. Diff a zero prima di
   accendere il filtro.
2. **Rinominare una chiave = distruggere i grant travestito da refuso.** `chiave` è un
   identificatore immutabile. Per rinominare: campo `alias` nel catalogo → `EnsureCatalogo`
   migra le righe una volta sola; una chiave che sparisce senza `alias` né `ritirata: true` →
   censimento rosso.
3. **Micro invisibili al jolly e alla scheda.** L'espansione del jolly e la scheda admin leggono
   `auth_features`: se i micro non vi fossero registrati, l'admin non li vedrebbe e il jolly non
   li concederebbe. `EnsureCatalogo` **materializza anche le chiavi micro** come righe di
   `auth_features`.
4. **Lo split ripete il bug della Sintesi (Fase 1).** Un endpoint che serve sia l'albero sia una
   pagina globale, se riceve solo la chiave nuova, **svuota la pagina globale senza errore a
   video**. Contromisura: chiavi assegnate endpoint per endpoint usando la mappa generata, **OR**
   dove i consumatori sono due; prova finale = matrice runtime utente × endpoint prima/dopo
   (stile `fase-e-runtime.js`), non solo il diff sulle righe.
5. **Ambiti ore senza semina = timesheet morto.** `ore.scope.*` nasce con semina fotografica dal
   ruolo attuale (TECH→self, RESP→reparto, PM/ADMIN→tutte); se una persona ha più ambiti,
   **vince il più largo**.
6. **`soloClient` come scappatoia.** Marcare `soloClient` zittisce il censimento: la marca
   richiede un `motivo` obbligatorio e il censimento stampa l'elenco dei soloClient
   nell'artefatto, così restano sotto gli occhi a ogni run.
7. **Catalogo malformato scoperto tardi.** Chiave duplicata, `kind` sconosciuto, micro ignoto,
   `eredita` verso una chiave inesistente: validazione dello stesso schema in DUE punti —
   `genera-catalogo.mjs` (fallisce la build web) ed `EnsureCatalogo` (ferma l'avvio del server,
   come una migrazione fallita, sotto lo stesso lock MySQL: una sola istanza allinea).
8. **`catalogo.gen.ts` non committato = clone che non compila.** Il generato si committa; il
   generatore in `predev`/`prebuild` rigenera e un controllo di freschezza fallisce la build se
   il JSON è stato editato senza rigenerare (o il `.gen` a mano).
9. **Flag speciali del client sfuggono al modello.** `economicsOnly` in `commessa-sections.ts`
   decide ancora per categoria fuori dal motore: al cutover diventa un micro/voce di catalogo
   come gli altri; il generatore vieta flag di visibilità che non siano chiavi.
10. **Righe orfane dopo un ritiro.** Una chiave `ritirata` lascia righe in
    `employee_feature_access` e nel log: si tollerano (sono storia), escono da `/features/my`
    perché fuori catalogo, e una pulizia `Facoltativa` le archivia — il log non si cancella mai.

---

*Documento vivo: aggiornare qui le decisioni di prodotto sul rebuild permessi, non spargere accordi solo in chat.*