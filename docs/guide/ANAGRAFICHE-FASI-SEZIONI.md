# Anagrafiche aggiornate — sezioni di costo e fasi

> Stato del **07/08/2026**, letto dal database di produzione.
> Preparato per la segnalazione **#42** dopo le decisioni di Paolo.

## In due righe

- **23 sezioni di costo attive** in 5 gruppi, più 1 disattivata.
- **53 fasi** in anagrafica: **41 assegnate** a una sezione, **12 senza**.
- **Nessuna delle fasi senza sezione è di quelle che nascono automaticamente** su ogni commessa nuova: erano 25, ora sono 0. Le 12 rimaste sono fasi opzionali, quasi tutte mai usate.

---

## Sezioni di costo, con le fasi che ci finiscono dentro


### GESTIONE

**Program Manager** — `SEDE` · reparti: PM
- Call Cliente
- Gestione Commessa
- Project Management
- Riunioni Avanzamento

**Documentazione Interna** — `SEDE` · reparti: PLC QLT ROB UTE
- Creazione Manuali Operativi

**Robot Studio - Cella Simulazioni** — `SEDE` · reparti: ROB
- Simulazione RobotStudio
- Simulazione RobotStudio

**Progettazione Ufficio Tecnico Meccanico** — `SEDE` · reparti: UTM
- Distinta base
- Messa in tavola
- Progettazione 3D
- Progettazione Meccanica
- Studio layout

**Progettazione Ufficio Tecnico Elettrico** — `SEDE` · reparti: UTE
- Progettazione Elettrica
- Progettazione Schemi Elettrici

**Sviluppo SW Back Office** — `SEDE` · reparti: PLC ROB
- Programmazione HMI
- Programmazione PLC
- Programmazione Plc
- Programmazione Robot
- Programmazione Safety

**Riunioni Commessa** — `SEDE` · reparti: ACQ INS MEC PLC PM QLT ROB UTE UTM
- Riunione

**Riunioni Commessa - Cliente** — `CLIENTE` · reparti: ACQ INS MEC PLC PM QLT ROB UTE UTM
- *(nessuna fase assegnata)*

**Ufficio Acquisti** — `SEDE` · reparti: ACQ
- Emissione ordini
- Richiesta offerte fornitori
- Solleciti e tracking consegne


### SITO PILOTA

**Allestimento Meccanico / Elettrico** — `SEDE` · reparti: INS MEC UTE UTM
- Cablaggio quadro elettrico
- Collaudo Hardware
- Collaudo finale IN ATEC
- Lavorazione carpenteria
- Lavorazione officina meccanica
- Montaggio elettrico IN ATEC
- Montaggio meccanico IN ATEC
- Preinstallazione elettrica IN ATEC
- Preinstallazione meccanica IN ATEC

**Commissioning PLC / HMI** — `SEDE` · reparti: PLC
- Caricamento Sw & Debug
- Commissioning PLC IN ATEC

**Commissioning Robot** — `SEDE` · reparti: ROB
- Caricamento Sw & Debug
- Commissioning Robot IN ATEC

**Coordinamento Attività / Capo Cantiere** — `SEDE` · reparti: QLT UTE UTM
- *(nessuna fase assegnata)*


### INSTALLAZIONE CLIENTE

**Coordinamento Attività / Capo Cantiere** — `CLIENTE` · reparti: QLT UTE UTM
- *(nessuna fase assegnata)*

**Installazione Meccanica / Elettrica** — `CLIENTE` · reparti: INS MEC
- Installazione elettrica in CANTIERE
- Installazione meccanica in CANTIERE

**Commissioning PLC / HMI** — `CLIENTE` · reparti: PLC
- Commissioning PLC in CANTIERE

**Commissioning Robot** — `CLIENTE` · reparti: ROB
- Commissioning Robot in CANTIERE

**Collaudo finale** — `CLIENTE`
- Collaudo finale in CANTIERE


### POST COLLAUDO CLIENTE

**Formazione** — `CLIENTE` · reparti: PLC ROB
- *(nessuna fase assegnata)*

**Assistenza Produzione** — `CLIENTE` · reparti: PLC ROB
- *(nessuna fase assegnata)*

**Intervento Post Collaudo** — `CLIENTE` · reparti: ACQ INS MEC ROB UTE UTM
- *(nessuna fase assegnata)*


### OPZIONI

**Assistenza Remoto Cliente** — `SEDE` · reparti: PLC ROB
- *(nessuna fase assegnata)*

**Ore Viaggio** — `CLIENTE`  ⚠️ **DISATTIVATA** · reparti: INS MEC PLC PM QLT ROB UTE UTM
- *(nessuna fase assegnata)*

**Inoperoso** — `CLIENTE` · reparti: INS MEC PLC ROB UTE UTM
- *(nessuna fase assegnata)*

---

## Fasi senza sezione di costo

Nessuna di queste nasce da sola su una commessa nuova: si aggiungono solo a mano, quando
servono. Finché restano senza sezione, le ore imputate sopra non entrano nella ripartizione
per sezione del Bilancio — il totale ore resta comunque giusto.

| fase | usata in | da fare |
|---|---|---|
| Allestimento Robot | mai usata | **probabilmente da cancellare** |
| Analisi FEM | mai usata | **probabilmente da cancellare** |
| Documentazione AS-BUILT | mai usata | **probabilmente da cancellare** |
| Documentazione tecnica | 1 commesse | assegnare o cancellare |
| Formazione cliente | mai usata | **probabilmente da cancellare** |
| Garanzia / Assistenza post-vendita | mai usata | **probabilmente da cancellare** |
| Gestione Avanzamento Commessa Speciale | mai usata | **probabilmente da cancellare** |
| Gestione Commessa | mai usata | **probabilmente da cancellare** |
| Gestione Interna | mai usata | **probabilmente da cancellare** |
| Gestione resi / non conformità | mai usata | **probabilmente da cancellare** |
| Stampa 3D | mai usata | **probabilmente da cancellare** |
| Trasporto / Logistica | mai usata | **probabilmente da cancellare** |

---

## Cosa è cambiato con le tue decisioni

| | |
|---|---|
| **Programmazione Robot** | **Eliminata.** Era in 17 commesse ma completamente vuota — zero ore, zero assegnazioni, zero ore preventivate. Cancellate anche le 17 copie: lasciarle avrebbe prodotto 17 fasi orfane senza sezione, cioè il disordine da togliere. |
| **Collaudo finale in CANTIERE** | Creata la sezione nuova **«Collaudo finale»** (`CLIENTE`, gruppo INSTALLAZIONE CLIENTE) e assegnata. |
| **Ore Viaggio** | **Disattivata, non cancellata.** Sparisce dalle commesse e dalle offerte nuove, ma le **4 commesse che già la usano restano intatte**. Cancellarla le avrebbe lasciate appese a una sezione inesistente. |
| **Ufficio Acquisti** | Creata: non esisteva una sezione per gli acquisti. Ci sono andate Richiesta offerte fornitori, Emissione ordini, Solleciti e tracking consegne. |

Controllato dopo: **nessun riferimento rotto** fra fasi, sezioni e commesse.

---

## ⚠️ La vera ragione del disordine: in anagrafica ci sono DUE elenchi di fasi

Guardando l'elenco qui sopra saltano all'occhio dei doppioni — «Simulazione RobotStudio» due
volte, «Programmazione PLC» e «Programmazione Plc», due «Caricamento Sw & Debug». Non sono
sviste sparse: sono il sintomo di una cosa più grossa.

Le 53 fasi si dividono in due blocchi netti:

| | quante | nascono da sole su una commessa nuova | usate davvero | già con una sezione |
|---|---|---|---|---|
| **A — elenco storico** | 39 | **28** | 29 | 30 |
| **B — aggiunto dopo** | 14 | **0** | 2 | 11 |

Il blocco B — *Call Cliente, Riunioni Avanzamento, Riunione, Caricamento Sw & Debug,
Progettazione Schemi Elettrici, Creazione Manuali Operativi, Programmazione Plc,
Programmazione Robot…* — sembra **un secondo elenco più pulito, iniziato e mai finito**:
quasi tutte quelle fasi hanno già la loro sezione di costo (11 su 14, contro le 30 su 39 del
blocco storico, e prima di oggi erano molte meno), ma **nessuna è marcata come predefinita**.

Il risultato è che il blocco B **non compare mai** sulle commesse nuove: continuano a nascere
con le fasi del blocco storico. I due elenchi convivono, si sovrappongono nei nomi, e chi apre
la tendina delle fasi vede il miscuglio dei due. **È esattamente il «fare ordine per logica e
sintassi» della segnalazione.**

### I doppioni veri

| nome | storico | aggiunto dopo |
|---|---|---|
| Simulazione RobotStudio | id 23 — predefinita, in 17 commesse | id 50 — mai usata |
| Programmazione PLC / «Programmazione Plc» | id 15 — predefinita, in 17 commesse | id 52 — mai usata |
| Gestione Commessa | id 46 — senza sezione, mai usata | id 48 — su Program Manager, 1 commessa |
| Caricamento Sw & Debug | — | id 55 e **id 56**, due volte (una per PLC, una per Robot) |

Sui primi due la scelta è semplice: **si tiene quella usata e si cancella il gemello mai usato**.
Il terzo va deciso. Il quarto forse è voluto (stessa attività su due sezioni diverse), ma in una
tendina piatta due voci identiche non si distinguono.

### La domanda che resta su «Programmazione Robot»

Ne esistevano **due**. È stata eliminata quella che avevi indicato — la predefinita, presente in
17 commesse e senza sezione. **Ne resta una** (id 53, blocco B, mai usata, già su «Sviluppo SW
Back Office»). Va tolta anche quella, o quella va bene ed era proprio il doppione a fare
confusione?

### La proposta

1. Cancellare i gemelli mai usati del blocco B che duplicano una fase storica.
2. Decidere se il blocco B deve **sostituire** il blocco storico — nel qual caso va marcato come
   predefinito e le fasi storiche corrispondenti vanno ritirate — oppure se va assorbito.
3. Sulle 12 fasi senza sezione: 11 non sono mai state usate da nessuno. Quasi certamente si
   cancellano invece di assegnarle.

Nessuna di queste è una decisione tecnica: sono scelte di come volete chiamare il lavoro.
