# Configurazione Sezioni Costo — Guida d'uso

> **A che pagina si riferisce:** voce di menu **Configurazione → Sezioni Costo**
> **File sorgente:** [Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml) + [.xaml.cs](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs)
> **Routing menu:** [MainWindow.xaml.cs:216](ATEC.PM.Client/Views/MainWindow.xaml.cs:216) — tag `"ConfigurazioneSezioni"` → `new CostSectionsTreePage()`

> ⚠️ **Aggiornamento 26/05/2026:** il pannello sinistro-basso "FASI TEMPLATE" è stato **rimosso**. Le fasi si creano direttamente cliccando il pulsante **"+"** arancione sulla sezione nell'albero (richiede che la sezione abbia almeno un reparto collegato). Le sezioni successive della guida mantengono il vecchio contesto per riferimento; il flusso operativo nuovo è sintetizzato nella sez. 3 "Passo 5".

---

## 1. Cos'è e a cosa serve

È la pagina che definisce lo **scheletro standard** delle commesse e dei preventivi: la struttura di **gruppi, sezioni di costo, reparti e fasi** che vengono replicate ogni volta che si crea una nuova commessa o un nuovo preventivo dai default.

In termini ATEC, qui si modella **chi può lavorare dove** (reparti → sezioni) e **cosa si fa in ciascuna sezione** (fasi template). I "centri di costo" del dominio aziendale sono i **Reparti**: hanno un codice breve, un costo orario in € e un coefficiente di ricarico (markup).

**Tutto quello che si configura qui non tocca commesse/preventivi già esistenti** — è un *template*. La struttura viene copiata al momento della creazione.

---

## 2. Perché serve configurare queste regole

Senza questa configurazione, **il sistema di costing e i preventivi non funzionano davvero**: non c'è modo di sapere quanto costa un'ora di lavoro di ciascun reparto, non c'è uno scheletro comune per i progetti, ogni commessa partirebbe da zero. In una frase: **questa pagina è il vocabolario contabile-operativo dell'azienda dentro ATEC PM**. Se non parli la stessa lingua su preventivi, commesse, costing e timesheet, niente funziona davvero.

### Cosa rende possibile la configurazione

- **Calcolo automatico dei costi.** Quando in costing aggiungi una risorsa di tipo "OFF" (officina), il sistema sa che 1 ora vale X € e applica il markup K → genera in automatico il prezzo di vendita. Senza questo, dovresti scrivere il costo orario a mano per ogni riga, ogni volta.
- **Preventivi e commesse pronti all'uso.** Creare un nuovo preventivo IMPIANTO parte da una struttura già fatta (gruppi, sezioni, fasi), non da una pagina vuota. Risparmio di ~10-15 minuti per preventivo, zero errori di omissione.
- **Confronto Budget vs Consuntivo (BVA).** Il sistema può confrontare quanto è stato preventivato e quanto è stato effettivamente speso *per ogni sezione*, **solo se** la sezione esiste a livello di template. Senza template, non c'è uno schema condiviso e il confronto è impossibile.
- **Aggregazione e reportistica.** "Quante ore l'officina ha consuntivato a maggio su tutte le commesse?" funziona solo se il reparto OFF è univoco e censito una volta sola. Senza template centralizzato ogni operatore creerebbe nomi diversi ("Officina", "OFF", "officina meccanica") e i report sarebbero inutilizzabili.
- **Distribuzione del lavoro.** L'assegnazione fase → reparto nelle commesse parte da un elenco coerente: solo i reparti collegati alla sezione sono selezionabili. Senza configurazione, il selettore sarebbe vuoto o caotico.
- **Timesheet sensato.** I tecnici rendicontano ore su fasi che esistono in template. Le ore confluiscono automaticamente nel consuntivo della sezione giusta → BVA aggiornato → margine in tempo reale.

### Cosa succede se NON è configurato bene

- Reparti mancanti → impossibile inserire alcune risorse in costing
- Costi orari errati → preventivi sotto/sovrastimati, margini fantasma
- Sezioni "default" mancanti → ogni nuovo preventivo parte vuoto, perdita di tempo enorme
- Centri di costo duplicati (es. "MAG" e "Magazzino" entrambi presenti) → confronti BVA inutilizzabili
- Fasi disancorate (senza sezione) → il timesheet permette di rendicontare ore su fasi prive di sezione → ore "perse" → consuntivo non aggregato → BVA rotto

In pratica: **due ore spese bene qui valgono decine di ore risparmiate ogni mese** su preventivi, costing e analisi marginalità.

---

## 3. Come usare la pagina — guida pratica passo-passo

### A. Setup iniziale (azienda che parte da zero)

L'ordine **conta**: i reparti vanno definiti per primi perché sono i mattoni di base. Solo dopo si costruiscono gruppi e sezioni che li referenziano.

#### Passo 1 — Censimento reparti (centri di costo)

Per ogni reparto aziendale che ha un costo del lavoro distinto, pannello sinistra-alto → bottone **`+`** → compila:

- **Codice** (3-5 lettere maiuscole, es. `UFF`, `OFF`, `MAG`, `ELE`, `MEC`). Diventerà l'etichetta visibile ovunque — pensalo come definitivo.
- **Nome esteso** (es. "Ufficio Tecnico", "Officina meccanica")
- **Costo orario €** — è il **costo aziendale lordo** (paga + contributi + struttura), non il prezzo cliente. Esempi tipici: ufficio tecnico 35-40 €, officina 28-32 €, magazzino 22-26 €.
- **K ricarico** — coefficiente moltiplicatore per ottenere il prezzo di vendita. Default `1,450` (= +45% sul costo). Si può differenziare per reparto se hai margini diversi.

⚠️ Una volta usato in una commessa, **cambiare il codice del reparto è rischioso** (riferimenti rotti). Cambiare il costo orario è sicuro ma non retroattivo: vale solo dal prossimo nuovo inserimento.

#### Passo 2 — Crea i gruppi macro

Toolbar verde **`+ Gruppo`** → nome. Sono le grandi categorie organizzative dell'azienda. Esempi tipici ATEC:

- **GESTIONE** — attività non legate a un impianto specifico (PM, contabilità, segreteria)
- **IMPIANTO** — lavorazioni standard di un nuovo impianto in costruzione
- **SITO PILOTA** — attività su prototipo
- **INSTALLAZIONE CLIENTE** — montaggio e avvio presso cliente
- **POST COLLAUDO CLIENTE** — assistenza post-vendita, garanzia
- **OPZIONI** — accessori e varianti opzionali

Per ogni gruppo modifica con ✎ e assegna un **colore di sfondo** dalla palette (24 colori) — serve a riconoscerli a colpo d'occhio nell'albero.

#### Passo 3 — Crea le sezioni operative dentro ai gruppi

Pulsante `+` sul nodo gruppo → nome + scelta **tipo**:

- **`IN_SEDE`** — il lavoro si fa nei locali aziendali
- **`DA_CLIENTE`** — il lavoro si fa fuori sede (rilevante per timesheet/trasferte/spese viaggio)

Esempio sotto **IMPIANTO**:

| Sezione | Tipo |
|---|---|
| Progettazione meccanica | IN_SEDE |
| Progettazione elettrica | IN_SEDE |
| Approvvigionamenti | IN_SEDE |
| Montaggio meccanico | IN_SEDE |
| Cablaggio elettrico | IN_SEDE |
| Collaudo interno | IN_SEDE |
| Installazione presso cliente | DA_CLIENTE |
| Collaudo cliente | DA_CLIENTE |

#### Passo 4 — Assegna i reparti alle sezioni

**Trascina** ogni badge reparto dal pannello sinistra-alto al nodo sezione nell'albero. Una sezione può avere N reparti, un reparto può stare in N sezioni.

Esempio:
- Su **Cablaggio elettrico** → trascina `ELE` e `MEC`
- Su **Montaggio meccanico** → trascina `MEC` e `OFF`
- Su **Progettazione elettrica** → trascina `UFF` (ufficio tecnico)

Il vincolo non è teorico: solo i reparti collegati a una sezione potranno essere usati come risorse in quella sezione di costing/commessa.

#### Passo 5 — Crea e collega le fasi template

Le fasi descrivono *cosa* si fa dentro una sezione. **Aggiornamento 26/05/2026:** una sola via — pulsante **"+"** arancione accanto alla matita ✎ e ✕ sulla sezione.

- Click "+" → dialog "Nuova Fase per «Cablaggio elettrico»" → digiti il nome → Salva
- **Vincolo**: la sezione deve avere almeno un reparto collegato (errore esplicito altrimenti)
- **Unicità nome**: il sistema rifiuta nomi duplicati nella stessa sezione (case-insensitive)
- La fase viene creata con `cost_section_template_id` già valorizzato (no drag&drop)

Esempi su **Cablaggio elettrico**: "Posa cavi quadro", "Connessione campo", "Verifica continuità", "Marcatura conduttori".

Per **promuovere a template** una fase creata in commessa (fase locale → globale): vedi BVA → pulsante "↑ Salva come template" sulla fase locale.

#### Passo 6 — Marca le sezioni "default"

Per ogni sezione, edit ✎ → due toggle:

- ☑ **Default Commessa** → la sezione comparirà automaticamente in ogni nuova commessa. Mettilo su quelle che servono **sempre** (Progettazione, Montaggio, Collaudo). **NON** mettere flag su sezioni opzionali.
- ☑ **Default Preventivo** → idem per nuovi preventivi. Tipicamente è un sottoinsieme più piccolo (es. solo le macro per la prima offerta cliente).

⚠️ **Errore tipico**: marcare troppe sezioni come "Default Preventivo". Se sono 30 ogni preventivo nasce con 30 sezioni di cui l'utente deve eliminare quelle che non servono → perdita di tempo. Meglio averne 5-10 e aggiungere a richiesta in commessa.

#### Passo 7 — Verifica con un caso reale

Crea un preventivo di prova (es. "PRV-TEST-IMPIANTO") e controlla:

- Le sezioni default ci sono tutte?
- Aprendo il costing, i reparti disponibili per ogni sezione sono quelli giusti?
- Il costo orario sulle risorse di default è quello atteso?

Se manca qualcosa, torna su questa pagina e sistema **prima** di mettere il sistema in mano agli utenti finali.

### B. Manutenzione ordinaria

Cosa fare e con che frequenza, una volta che il sistema è in regime:

| Quando | Cosa fare | Dove |
|---|---|---|
| Adeguamento CCNL / aumento stipendi | Aggiornare `Costo orario` su ciascun reparto | Clic sul badge reparto sx |
| Cambio politica margini | Modificare `K ricarico` sul reparto | Stesso dialog |
| Nuova lavorazione interna | Nuova sezione + reparti + fasi | `+` sul nodo gruppo |
| Nuova linea di prodotto / servizio | Nuovo gruppo + sezioni | Toolbar `+ Gruppo` |
| Reparto dismesso (es. esternalizzato) | Rimuovi il reparto da tutte le sezioni (✕ sui badge nell'albero) + disattiva il reparto | Manuale nell'albero |
| Fase obsoleta | Elimina (se libera) o scollega dalla sezione | Badge fase |

> ⚠️ **Non eliminare** un reparto o una sezione referenziata da commesse/preventivi storici — perderesti la possibilità di leggere i consuntivi vecchi. Meglio disattivare (`IsActive=false`) o lasciare in archivio. Il sistema ti avviserà comunque se l'eliminazione rompe vincoli FK.

### C. Errori comuni da evitare

- **Duplicare i reparti.** Un reparto = un costo orario unico. Se serve distinguere "officina junior" da "officina senior" perché hanno costi diversi, crea due reparti separati (`OFFJ`, `OFFS`).
- **Troppe sezioni in "Default Preventivo".** Spiegato sopra — meglio meno default e aggiunta on-demand.
- **Confondere reparto e fase.** Il reparto è **chi** (UFF, OFF, MAG), la fase è **cosa** (progettazione, montaggio, collaudo). Una sezione ha N reparti e N fasi, ma non si confondono.
- **Cambiare il codice di un reparto già in uso.** Può rompere il legame con risorse di costing già scritte. Se proprio devi, prima esporta i dati e fai una review manuale dei riferimenti.
- **Fasi orfane (senza sezione)** lasciate troppo a lungo nel pannello "libere". Vanno usate o eliminate — accumularle disorienta gli utenti.
- **Codici reparto non parlanti** (es. `R1`, `R2`). Devono essere mnemonici per chi li legge nei report: `UFF`, `OFF`, `MAG`, `ELE`, `MEC` si capiscono a colpo d'occhio.

---

## 4. Modello dati

Quattro entità correlate. Le relazioni sono N:N tra Reparti↔Sezioni e 1:N tra Sezioni→Fasi.

```
┌───────────────────────────────────────────────────────────────┐
│  GRUPPO (CostSectionGroupDto)                                 │
│  - Id, Name (es. "GESTIONE", "IMPIANTO")                      │
│  - SortOrder, IsActive, BgColor (#3B82F6, …)                  │
│  └─→ contiene N SEZIONI                                       │
│     ┌──────────────────────────────────────────────────────┐  │
│     │  SEZIONE (CostSectionTemplateDto)                    │  │
│     │  - Id, Name, GroupId, SortOrder                      │  │
│     │  - SectionType ∈ { IN_SEDE, DA_CLIENTE }             │  │
│     │  - IsDefault         (clonata su nuova commessa)     │  │
│     │  - IsDefaultQuote    (clonata su nuovo preventivo)   │  │
│     │  - IsActive                                           │  │
│     │  - DepartmentIds[]  ←  N reparti collegati           │  │
│     │  └─→ contiene N FASI ancorate                        │  │
│     │     ┌──────────────────────────────────────────┐     │  │
│     │     │  FASE TEMPLATE (PhaseTemplateDto)        │     │  │
│     │     │  - Id, Name, Category, SortOrder         │     │  │
│     │     │  - CostSectionTemplateId  ← null = libera│     │  │
│     │     │  - IsDefault                              │     │  │
│     │     └──────────────────────────────────────────┘     │  │
│     └──────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│  REPARTO (DepartmentDto) — "centro di costo"                  │
│  - Id, Code (max 10, UPPERCASE — es. "OFF", "UFF", "MAG")     │
│  - Name                                                       │
│  - HourlyCost (€/ora)                                         │
│  - DefaultMarkup (k ricarico, default 1.450)                  │
│  - SortOrder                                                  │
│  - (N reparti possono essere assegnati a N sezioni)           │
└───────────────────────────────────────────────────────────────┘
```

### Note chiave sul modello

- Una **fase template** con `CostSectionTemplateId = null` è "libera": appare nel pannello sinistra-basso e può essere trascinata su una sezione per legarla.
- I flag `IsDefault` / `IsDefaultQuote` sulla sezione decidono se quella sezione viene **clonata automaticamente** quando si crea una nuova commessa / nuovo preventivo. Senza questi flag, la sezione è disponibile ma va aggiunta a mano.
- Il **costo orario e il markup del reparto** sono i valori che il costing eredita: quando in una commessa si aggiunge una risorsa per quel reparto, parte da `HourlyCost` e `DefaultMarkup`.

---

## 5. Layout della pagina

Tre pannelli su un singolo `DockPanel`, con `GridSplitter` regolabile tra sinistra (~280 px) e destra.

```
┌─────────────────────────────────────────────────────────────────────┐
│ Sezioni Costo — Configurazione               [+ Gruppo] (verde)     │  toolbar
├─────────────────────────────┬───────────────────────────────────────┤
│ ┌─ REPARTI ──────────[+]──┐ │ STRUTTURA SEZIONI COSTO               │
│ │ trascina su una sezione │ │                                       │
│ │ [UFF] [OFF] [MAG] …     │ │ ▼ GESTIONE (gruppo, colorato)         │
│ │                          │ │   ▼ PROGETTAZIONE (sezione)           │
│ ├─ FASI TEMPLATE [+ Fase] ┤ │   │  Reparti: [UFF ✕] [OFF ✕]          │
│ │ trascina su una sezione │ │   │  Fasi:    [Lay-out] [Schemi]       │
│ │ Cerca…                   │ │   ▼ MONTAGGIO                         │
│ │ ▼ TRASVERSALE            │ │   │  …                                │
│ │   [Collaudo] [Test]      │ │ ▼ IMPIANTO                            │
│ │ ▼ MECCANICA              │ │   …                                   │
│ │   [Saldatura] …          │ │                                       │
│ └─────────────────────────┘ │                                       │
├─────────────────────────────┴───────────────────────────────────────┤
│ Status bar (txtStatus) — messaggi di conferma drag, errori, ecc.    │
└─────────────────────────────────────────────────────────────────────┘
```

### Pannello sinistro alto — Reparti

- Pulsante **`+`** in alto a destra → apre `DepartmentDialog` per creare un nuovo reparto.
- Ogni badge reparto è **draggable** (data format: `"DepartmentDrop"`) → si trascina su una sezione per associarlo.
- Clic singolo sul badge → apre `DepartmentDialog` in modalità edit del reparto.

### Pannello sinistro basso — Fasi template "libere"

- Mostra **solo le fasi non collegate** a nessuna sezione (`CostSectionTemplateId == null`).
- Raggruppate per categoria (default `TRASVERSALE`), espandibili.
- Filtrabili tramite il `TextBox` "Cerca fase…" (filtra su Nome e Categoria, case-insensitive).
- `+ Fase` (arancione, in alto) → crea una fase senza categoria/sezione (categoria default `TRASVERSALE`).
- `+` accanto a una categoria → crea una fase in quella categoria.
- Ogni badge fase è draggable (data format: `"PhaseDrop"`).

### Pannello destro — Albero gruppi/sezioni

`TreeView` con:
- Drop target sui nodi sezione e sui sotto-nodi "Reparti" / "Fasi" di ciascuna sezione.
- Highlight visivo durante il drag-over (`#E0F2FE` chiaro, `#FDE68A` ambra).
- Espansione automatica del gruppo toccato per ultimo (`_lastExpandedTreeGroup`).

### Drag adorner

Durante il drag, un piccolo badge colorato segue il cursore (vedi `DragDropAdorner`, [CostSectionsTreePage.xaml.cs:17-80](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:17)).

---

## 6. Operazioni — cosa si può fare

### 6.1 Gruppi

| Azione | Come | Endpoint |
|---|---|---|
| Creare | Bottone verde **`+ Gruppo`** in toolbar → prompt nome → POST | `POST /api/cost-sections/groups` |
| Modificare (nome, colore sfondo, ordine) | Clic sul bottone matita ✎ accanto al nodo gruppo → dialog con palette colori (24 preset) | `PATCH /api/cost-sections/groups/{id}/field` (uno per ogni campo cambiato) |
| Eliminare | Bottone 🗑 accanto al gruppo → conferma | `DELETE /api/cost-sections/groups/{id}` |

Il `SortOrder` di default per un nuovo gruppo = `max + 1`. La palette colori è in [CostSectionsTreePage.xaml.cs:1574-1584](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1574).

### 6.2 Sezioni

| Azione | Come | Endpoint |
|---|---|---|
| Creare in un gruppo | Bottone `+` accanto al nodo gruppo → prompt nome + scelta tipo (IN_SEDE / DA_CLIENTE) | `POST /api/cost-sections/templates` |
| Modificare (nome, ordine, default commessa/preventivo) | Bottone matita ✎ sulla sezione → dialog con due toggle | `PATCH /api/cost-sections/templates/{id}/field` |
| Eliminare | Bottone 🗑 → conferma | `DELETE /api/cost-sections/templates/{id}` |
| Assegnare reparti | **Drag&drop** del badge reparto sul nodo sezione | `PUT /api/cost-sections/templates/{id}/departments` |
| Rimuovere reparti | Bottone ✕ accanto al badge reparto dentro la sezione | `PUT /api/cost-sections/templates/{id}/departments` (con lista filtrata) |
| Assegnare fase | **Drag&drop** del badge fase template sul nodo sezione | `PATCH /api/phases/templates/{id}/field` (field `cost_section_id`) |

**Tipo sezione** (`IN_SEDE` / `DA_CLIENTE`): viene chiesto al momento della creazione tramite un `MessageBox.YesNo`. *Sì = DA_CLIENTE, No = IN_SEDE*. Influisce su come la sezione viene gestita in costing e timesheet (trasferte vs lavoro interno).

**Toggle Default**:
- **Default Commessa** → quando crei una nuova commessa, questa sezione c'è già.
- **Default Preventivo** → idem per nuovi preventivi.
- Le due cose sono indipendenti: una sezione può essere default solo per commesse, solo per preventivi, entrambe, o nessuna.

### 6.3 Reparti (centri di costo)

| Azione | Come | Endpoint |
|---|---|---|
| Creare | Pulsante **`+`** sopra l'elenco reparti → `DepartmentDialog` (Codice ≤10 char UPPERCASE, Nome, Costo orario €, K ricarico, Ordine) | `POST /api/departments` *(dentro DepartmentDialog)* |
| Modificare | Clic sul badge reparto a sinistra → riapre `DepartmentDialog` | `PUT /api/departments/{id}` *(dentro DepartmentDialog)* |

⚠️ **Il dialog non c'è solo qui** — è condiviso, lo apri anche da altri punti. Modificare il costo orario di un reparto **non retroattivo** sulle commesse esistenti (è una proprietà del template, non delle righe già scritte).

### 6.4 Fasi template

| Azione | Come | Endpoint |
|---|---|---|
| Crearne una libera | `+ Fase` arancione (in alto, panel sinistra-basso) | `POST /api/phases/templates` (category default = `TRASVERSALE`, `costSectionTemplateId = null`) |
| Crearne una in categoria | `+` accanto al nome categoria | come sopra ma category = quella selezionata |
| Modificare (nome, categoria) | Clic sul badge | `PATCH /api/phases/templates/{id}/field` |
| Eliminare | Bottone 🗑 sul badge | `DELETE /api/phases/templates/{id}` |
| Collegare a una sezione | Drag su sezione | `PATCH /api/phases/templates/{id}/field` (`cost_section_id`) |
| Scollegare (riportare in "libere") | Drag fase dalla sezione fuori dal contenitore sezione (o edit a `cost_section_id = null`) | `PATCH /api/phases/templates/{id}/field` |

---

## 7. Drag & Drop — meccanica esatta

L'unico modo "ufficiale" di collegare reparti/fasi alle sezioni è il **drag&drop**: niente menu, niente tasti destri. È fatto apposta per essere veloce.

### Sorgenti drag

| Sorgente | Data format | Handler |
|---|---|---|
| Badge reparto (pannello sx-alto) | `"DepartmentDrop"` | `DeptBadge_PreviewMouseMove` ([:285](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:285)) |
| Badge fase libera (pannello sx-basso) | `"PhaseDrop"` | analogo (mouse move su `BuildPhaseBadgeLeft`) |

Il drag parte solo dopo il `SystemParameters.MinimumHorizontalDragDistance` per evitare drag accidentali.

### Target drop

| Nodo target | Accetta | Effetto |
|---|---|---|
| Nodo **sezione** | `DepartmentDrop` o `PhaseDrop` | Aggiunge dipartimento o lega fase |
| Sotto-nodo **"Reparti"** della sezione | solo `DepartmentDrop` | Stesso effetto |
| Sotto-nodo **"Fasi"** della sezione | solo `PhaseDrop` | Stesso effetto |
| Qualsiasi altro nodo | niente | `e.Effects = None` |

Gli handler sono in [CostSectionsTreePage.xaml.cs:1125-1199](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1125) (`SectionNode_DragOver/Drop`, `PhaseGroupNode_*`, `DeptGroupNode_*`).

### Comportamento idempotente

Se trascini un reparto su una sezione che **lo ha già**, viene mostrato solo un messaggio in status bar (`txtStatus`) — nessuna chiamata API doppia. Vedi guard a [:1399](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1399):
```csharp
if (section.DepartmentIds.Contains(dept.Id)) {
    txtStatus.Text = $"Reparto {dept.Code} già presente in {section.Name}";
    return;
}
```

---

## 8. Endpoint API utilizzati (riferimento completo)

Caricamento iniziale ([:175-178](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:175)) — 4 GET **in parallelo**:

| Verb | Endpoint | Cosa restituisce |
|---|---|---|
| GET | `/api/departments` | `List<DepartmentDto>` — tutti i reparti |
| GET | `/api/cost-sections/groups` | `List<CostSectionGroupDto>` — gruppi |
| GET | `/api/cost-sections/templates` | `List<CostSectionTemplateDto>` — sezioni con `DepartmentIds[]` annidato |
| GET | `/api/phases/templates` | `List<PhaseTemplateDto>` — fasi (incl. libere) |

Scrittura:

| Verb | Endpoint | Quando viene chiamato |
|---|---|---|
| POST | `/api/cost-sections/groups` | Crea gruppo |
| PATCH | `/api/cost-sections/groups/{id}/field` | Rinomina / cambia colore / cambia ordine gruppo |
| DELETE | `/api/cost-sections/groups/{id}` | Elimina gruppo |
| POST | `/api/cost-sections/templates` | Crea sezione (in un gruppo) |
| PATCH | `/api/cost-sections/templates/{id}/field` | Modifica nome / ordine / `is_default_project` / `is_default_quote` |
| PUT | `/api/cost-sections/templates/{id}/departments` | Sostituisce l'intera lista reparti della sezione (`{ departmentIds: [...] }`) |
| DELETE | `/api/cost-sections/templates/{id}` | Elimina sezione |
| POST | `/api/phases/templates` | Crea fase template |
| PATCH | `/api/phases/templates/{id}/field` | Modifica nome / categoria / `cost_section_id` |
| DELETE | `/api/phases/templates/{id}` | Elimina fase |

> **Nota implementativa:** il PATCH `/field` usa il pattern `{ field: "name", value: "..." }` — è generico ed evita di avere endpoint dedicati per ogni campo singolo. Dopo ogni modifica la pagina chiama `LoadData()` che ri-fa le 4 GET.

---

## 9. Flussi tipici (casi d'uso puntuali)

### CU-1 — "Voglio aggiungere un nuovo reparto e farlo lavorare in PROGETTAZIONE"

1. Pannello sx-alto → bottone `+` → nuovo reparto
   - Es. Codice = `ELE`, Nome = `Elettrico`, Costo = `35.00`, K = `1.450`
2. Il nuovo badge **`ELE`** appare nel pannello reparti.
3. Drag del badge `ELE` sopra la sezione **PROGETTAZIONE** dell'albero a destra.
4. Status bar conferma: *"Reparto ELE aggiunto a PROGETTAZIONE"*.
5. Da ora, qualsiasi **nuova** commessa che parte dal default ha PROGETTAZIONE già con il reparto Elettrico tra quelli disponibili (e le risorse Elettrico nel costing partiranno da costo 35€/h, k 1.45).

### CU-2 — "Voglio una nuova sezione standard sui preventivi: COLLAUDO FINALE"

1. Trovare il gruppo dove infilarla (es. GESTIONE) → `+` accanto al gruppo.
2. Nome: `COLLAUDO FINALE`, Tipo: `IN_SEDE`.
3. Aprire la sezione appena creata → matita ✎ → attivare il toggle **Default Preventivo** → salvare.
4. Drag dei reparti che la possono fare (es. `UFF`, `OFF`).
5. Drag delle fasi rilevanti (es. "Test funzionale", "Verbale collaudo").

Da ora ogni nuovo preventivo nasce con COLLAUDO FINALE già pronto.

### CU-3 — "Riorganizzo: sposto la sezione MONTAGGIO dal gruppo IMPIANTO al gruppo OFFICINA"

⚠️ **Limite attuale:** la pagina **non ha drag&drop per spostare sezioni tra gruppi**. La soluzione operativa:
- Crea la sezione nel nuovo gruppo
- Sposta a mano reparti e fasi (drag)
- Elimina la vecchia (se non ha commesse collegate; altrimenti disattivala via `IsActive`)

Se serve davvero sposto-frequente, è una feature da aggiungere (PATCH `group_id` esiste lato server? — da verificare).

### CU-4 — "Devo cambiare il costo orario di OFF da 28€ a 32€"

1. Clic sul badge `OFF` nel pannello sx → `DepartmentDialog`.
2. Modifica costo orario → salva.

**Effetto:** dal **nuovo** prossimo utilizzo (nuove righe in costing/preventivi) parte dal valore aggiornato. **Le righe già esistenti restano a 28€** — il valore è snapshot al momento dell'inserimento, non legato vivo al template.

---

## 10. Cose facili da dimenticare

- **`SortOrder` conta**: l'ordine in cui appaiono gruppi, sezioni, fasi e reparti è guidato dal campo `SortOrder`. Non si possono trascinare per riordinare (devi editare il numero dal dialog).
- **Le fasi "libere"** (sx-basso) sono solo quelle con `CostSectionTemplateId == null`. Se non vedi una fase nel pannello, probabilmente è già legata a una sezione: cercala nell'albero.
- **L'eliminazione di un gruppo / sezione / reparto** può fallire se ci sono dipendenze (es. commesse esistenti che referenziano la sezione). Il backend risponde con `ApiResponse.Fail(msg)` e l'UI mostra il messaggio. Per "ritirare" un'entità senza eliminarla, c'è il flag `IsActive`.
- **`is_default_project` vs `is_default_quote`** sono due bit indipendenti. Una sezione **non** è default per entrambi i flussi finché non li attivi singolarmente.
- **Codice reparto**: max 10 caratteri, forzato UPPERCASE dal `TextBox.CharacterCasing="Upper"`. Cambiarlo dopo creazione è rischioso (referenziato in risorse di costing).
- **Colore gruppo** (`BgColor`): solo estetica nell'albero. Se vuoto, c'è un fallback `GroupColors` per nomi noti (GESTIONE blu, SITO PILOTA verde, ecc., [:96-102](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:96)).
- **`_lastExpandedGroup` e `_lastExpandedTreeGroup`**: la pagina ricorda l'ultimo expander aperto (categoria fase e nodo gruppo sezione) per ripristinarlo dopo un `LoadData()`. Buona UX: dopo un edit, l'albero non collassa tutto.

---

## 11. Permessi e visibilità

- La pagina è una voce di menu di **Configurazione**. La visibilità è gestita dal sistema feature-keys di MainWindow.
- Le operazioni sono coperte dalle policy di autenticazione/autorizzazione del server: tutti i controller usano `[Authorize]` (post-fix sicurezza del 22/05/2026).
- I dati finanziari (`HourlyCost`, `DefaultMarkup`) sono visibili a chi accede alla pagina — non c'è un mascheramento PM/ADMIN come nelle commesse. Se serve nasconderli a profili limitati, vale la pena valutarlo (oggi è "se vedi la pagina, vedi tutto").

---

## 12. Riferimenti rapidi

| Cosa | File:riga |
|---|---|
| Routing menu | [MainWindow.xaml.cs:216](ATEC.PM.Client/Views/MainWindow.xaml.cs:216) |
| Layout XAML | [CostSectionsTreePage.xaml](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml) |
| Code-behind | [CostSectionsTreePage.xaml.cs](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs) (1797 righe) |
| Caricamento dati | [:175-178](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:175) |
| Drag handlers (sorgente reparto) | [:285-301](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:285) |
| Drop su sezione | [:1146-1164](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1146) |
| AddDepartmentToSection | [:1397-1417](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1397) |
| RemoveDepartmentFromSection | [:1419-1433](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1419) |
| Edit gruppo (palette colori) | [:1586-1685](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1586) |
| Edit sezione (toggle default) | [:1518-1552](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1518) |
| Add gruppo | [:1747-1761](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1747) |
| Add sezione in gruppo | [:1687-1727](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:1687) |
| `DragDropAdorner` (badge che segue il cursore) | [:17-80](ATEC.PM.Client/Views/ConfigurazioneSezioni/CostSectionsTreePage.xaml.cs:17) |
| Dialog Reparto | [DepartmentDialog.xaml](ATEC.PM.Client/Views/ConfigurazioneSezioni/DepartmentDialog.xaml) |
| DTO server-side | `ATEC.PM.Shared/DTOs/Core_DTOs.cs` (CostSectionGroupDto, CostSectionTemplateDto, DepartmentDto, PhaseTemplateDto) |

---

*Documento creato il 25/05/2026. Aggiornare se cambia il modello dati o gli endpoint.*
