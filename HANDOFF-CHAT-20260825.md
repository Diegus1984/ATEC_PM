# HANDOFF — sessione del 25/08/2026

> Punto d'ingresso per la chat successiva. Copre: segnalazione **#119** (split DDP
> commerciale/officina, **CHIUSA e in produzione**), pulizia **Codex/composizione**
> (in produzione) e una **patch al ciclo RDO Acquisti NON ancora deployata**.

---

## 1. Stato della produzione

| | |
|---|---|
| Server | `http://192.168.2.150:5150` (servizio `AtecPmServer`) |
| Build online | **`20260825-2038`** |
| Schema | **v106**, 106 su 106, 0 rosse |
| Salute | `/api/health/ready` → `ready`, `database: ok` |
| Backup pre-migrazione | `C:\ATEC_Backups\atec_pm_prima_v105_20260825.sql` (8,04 MB, 121 tabelle, «Dump completed») |

Deploy della giornata: `1624` (#119 + v105) → `1639` (nuovo composito) → `1652` (tabelle
standard) → `1654` (catalogo senza filtro) → `2038` (v106 + correzione codici).

---

## 2. ✅ PATCH RDO PUBBLICATA il 25/08/2026 ore 23:32 (build 20260825-2332)

**Modulo Acquisti / ciclo RDO — IN PRODUZIONE.** Prima del deploy è stato fatto l'ultimo
giro di revisione adversariale sul delta (workflow a 36 agenti): 13 conferme, tutte
corrette. In più rispetto a quanto descritto sotto: blocco prezzo post-aggiudicazione
anche LATO SERVER (`SaveOffer` rifiuta CLOSED/CANCELLED), guardia estesa alle gare di
sole righe senza codice, la POST di creazione rifiuta le righe fuori dal codice della
gara (difesa dai bundle web vecchi), race dei blur prezzo/note risolta con base locale
per offerta (`offerBaseRef`), fornitori scartati dal limite mailto ora nominati,
`mark-emailed` con try/catch, regola «una gara = un articolo» estratta in
`Services/RdoGuardie.cs` con 13 test (`ATEC.PM.Tests/Calcoli/RdoGuardieTests.cs`,
suite a 283 verdi). Nessuna migrazione: schema fermo a v106. File toccati:

```
ATEC.PM.Server/Controllers/PurchaseRfqController.cs
ATEC.PM.Shared/DTOs/PurchaseRfq_DTOs.cs          (+ AtecCode su PurchaseRfqItemDto)
atec-pm-web/src/features/acquisti/CreateRfqDialog.tsx
atec-pm-web/src/features/acquisti/RfqDetailDialog.tsx
atec-pm-web/src/lib/api/purchase-rfqs.ts
atec-pm-web/src/lib/api/types/acquisti.ts
```

### Cosa corregge (3 difetti veri, trovati analizzando il multi-fornitore)

1. **Perdita di dati in aggiudicazione.** `SelectWinner` riscriveva su OGNI riga della RDO
   codice, descrizione, UM, produttore, articolo Danea **e codice ATEC** del vincitore: in una
   gara mista le righe di un pezzo diventavano un altro pezzo. Ora una **guardia rifiuta prima
   di scrivere qualsiasi cosa** se le righe della gara non hanno tutte lo stesso codice ATEC.
2. **Gare miste alla nascita.** «Richiedi RDO» dalla **testata** della card metteva tutte le
   righe in una gara sola sotto il codice della prima. Ora `CreateRfqDialog` **raggruppa per
   codice ATEC** e crea una RDO per gruppo, con try/catch per gruppo.
3. **Confronto fra offerte inutilizzabile.** Il campo prezzo partiva dal costo di riga e lo
   riscriveva: con 3 fornitori i tre blocchi mostravano lo stesso numero. Ora è **uno per
   offerta**, nell'intestazione del fornitore. Tolto anche il **ripiego lato server** che
   aggiudicava al costo di riga quando l'offerta non aveva prezzo. La mail ai fornitori porta
   ora `offer.catalogCode`, cioè il codice **di quel** fornitore.

### 🪤 Cose imparate a caro prezzo (quattro giri di revisione adversariale)

- **Saltare le righe fuori gruppo era peggio del male**: la RDO si chiudeva comunque e quelle
  righe restavano prigioniere (non annullabili, non rimettibili in gara) mentre l'ordine Danea
  ne contava la quantità. → si **rifiuta**, non si salta.
- **La guardia deve confrontare le righe TRA LORO**, non con il codice della testata: quello è
  congelato alla creazione e diverge legittimamente quando il buyer mappa l'articolo mentre
  aspetta le offerte.
- **`LoadDetail` deve leggere il codice ATEC EFFICACE**
  (`COALESCE(NULLIF(b.atec_code,''), ci.atec_code,'')`): `assign-from-bom` scrive lo snapshot
  **solo sulla riga di partenza**, quindi leggendo il solo `b.atec_code` la guardia era un
  colpo a vuoto sulla maggior parte delle righe.
- **L'aggiudicazione non deve stampare il codice della testata sulle righe**: lo snapshot vince
  sul `COALESCE` in tutte le letture e il mapping giusto sparirebbe in silenzio. Si prende dal
  **catalogo del vincitore**, con ripiego sul codice della gara se quell'articolo non è mappato.
- **RITIRATA una correzione**: avevo sbloccato il prezzo dopo l'aggiudicazione, ma lì si
  aggiorna solo `purchase_rfq_offers` mentre `bom_items.unit_cost` lo scrive solo `SelectWinner`
  (che rifiuta le RDO chiuse) → l'ordine Danea sarebbe partito col prezzo corretto lasciando
  **distinta e Bilancio con quello sbagliato**. Il prezzo torna a bloccarsi all'aggiudicazione.

### ~~Cosa manca prima di pubblicare~~ FATTO il 25/08 sera

Verifica sul delta fatta (revisione adversariale multi-agente), correzioni applicate,
deploy riuscito in 105,7 s. Verificato dopo: `/api/health/ready` 200 `database: ok`,
`version.json` = build locale `20260825-2332`, simboli `RdoGuardie`/`GaraMista`/
`RigheFuoriCodice` presenti nella DLL installata, `schema_migrations` 106/106 0 rosse.

---

## 3. #119 — CHIUSA, in produzione

Importando un gruppo Codex (`501`/`511`) in commessa i componenti si dividono: **2xx/3xx nella
DDP Commerciale, il resto in Officina**, con l'intestazione collassabile in **entrambe** le
griglie. Dettaglio completo e trappole in memoria: **`segnalazione_119_split_ddp.md`**.

Spostamento storico riuscito (v105): commessa **C260805_500**, gruppo `501140621.001` Qtà 2 →
9 righe `1xx` restano in officina, **14 righe (186 pezzi)** passate in commerciale sotto una
nuova intestazione. v106 ha poi corretto lo stato di quell'intestazione (era `DC`, che in
commerciale **non esiste**: riga bloccata senza transizioni possibili).

**Regola in un posto solo**: `Services/DdpSmistamento.cs` (18 test in
`ATEC.PM.Tests/Calcoli/SmistamentoDdpTests.cs`). «Comanda il padre» cross-tabella in
`Services/ComposizioneDdp.cs`.

---

## 4. Codex e composizione — in produzione

Vedi memoria **`codex_famiglie_e_composizione.md`**. In sintesi:

- **401 «Materia prima» ritirata**; **511 «Gruppo custom» aggiunta** (clone esatto del 501).
- **In composizione entrano solo codici Codex.** La sorgente «Catalogo» è una *lente*: mostra
  tutto il catalogo e ciò che entra in distinta è il Codex associato; le righe senza codice
  hanno il pulsante **Codifica** (stesso dialogo del Catalogo Articoli).
  🪤 **NON rimettere un filtro «solo codificati»**: provato e bocciato due volte.
- Cancellate le 2 composizioni di prova + l'articolo `501190626.001`.

---

## 5. Lavoro di DATI che aspetta qualcuno (non è software)

I componenti commerciali delle composizioni **non sono ricodificati**: nel Codex ci sono
**19 articoli `201` e 40 `301`** usati in composizione e **senza codice nuovo**. Finché è così:

- la colonna **«Cod. ATEC» resta vuota** su quelle righe — **non è un bug**, e riempirla col
  codice storico **peggiorerebbe** (vedi la trappola in `segnalazione_119_split_ddp.md`);
- quelle righe **non possono andare in RDO** (senza codice non si sa chi invitare).

La lista dei 14 della commessa C260805_500 è nel messaggio di chat; si rigenera con una query
su `bom_items` dove `parent_bom_item_id IS NOT NULL`.

---

## 6. Multi-fornitore: si fa già così

Un codice ATEC comprato da 3 fornitori **è già previsto** (indice `IX_CatalogItems_AtecCode`
NON univoco). Flusso completo e trappole in memoria: **`multifornitore_codice_atec.md`**.
🪤 In Danea **3 fornitori = 3 articoli distinti**: i fornitori alternativi della stessa scheda
(`TArticoliForn`) non entrano in ATEC PM.

---

## 7. ✅ Difetti PREESISTENTI del modulo Acquisti — CORRETTI il 26/08/2026 (build 20260826-0837)

Tutti chiusi e in produzione, tranne l'ultimo che è risolto **per disegno**. Due giri di
revisione adversariale anche su questo delta (6 conferme + 2 split, tutti corretti;
controcheck finale 8/8 OK). Suite a **288 test verdi**.

| Difetto | Esito |
|---|---|
| Aggiudicazione a prezzo **0 o negativo** | ✅ `RdoGuardie.PrezzoNonAggiudicabile` (testata): rifiuto in SelectWinner, SaveOffer E generazione ordine (vincitori storici a 0 compresi) |
| `SelectWinner` **non in transazione** | ✅ tx unica (righe+eventi+vincitore+chiusura), notifiche solo a commit; in più ricontrollo offerta DENTRO la tx e chiusura condizionata (TOCTOU con Cancel/SaveOffer concorrenti) |
| RDO **CANCELLED ancora aggiudicabile** | ✅ rifiutata (e la generazione ordine la rifiuta pure, col ricontrollo atomico nel claim) |
| **Righe murate** dopo aggiudicazione sbagliata | ✅ `Cancel` annulla anche le CLOSED **senza ordine Danea**: le righe tornano libere; pulsante e confirm dedicati nel dialog (visibilità dai campi di testata, non dai marcatori di riga) |
| Ordine Danea a **quantità congelata** | ✅ quantità ATTUALE ovunque (`b.quantity`): dettaglio, totali lista, email, ordine |
| Stato `IO` **forzato** fuori matrice | ✅ `IF(@Advance,'IO',item_status)` + evento cronistoria solo su avanzamento vero (niente doppioni su righe già IO); KPI «In Ordine Danea» allineato a `rowHasDaneaOrder` |
| `projectCode` **vuoto** nella testata del dettaglio | ✅ ProjectId/ProjectCode nella query di testata |
| Righe **senza Cod. ATEC** senza strada verso la RDO | ✅ **per disegno**: la strada è assegnare il codice (colonna «Cod. ATEC» in Inbox), come dice il toast del dialogo; `offer-plan`/`request-offers` restano per il flusso futuro |
| `CatalogMappingController` **senza `[RequireFeature]`** | ✅ serratura di classe a 6 chiavi in OR (catalogo, acquisti, assegnazione, codex, DDP commerciale); scritture con la loro chiave dedicata |

---

## 8. Come si riprende

1. Leggi le memorie: `segnalazione_119_split_ddp`, `codex_famiglie_e_composizione`,
   `multifornitore_codice_atec`, `deploy_atec_pm_lan`.
2. **Decidi cosa fare della patch RDO sul disco**: verificarla e pubblicarla, oppure ritirarla.
   Non lasciarla lì a metà — chi compila dopo se la porta dietro senza saperlo.
3. Segnalazioni: `python tools/segnalazioni.py --aperte` (si leggono sul DB di **produzione**).
4. Deploy: `deploy/aggiorna-server.ps1` **nudo**, in background. Con migrazioni: **backup del DB
   PRIMA** (`mysqldump` con `--no-tablespaces`, niente `--events`) e dopo il deploy controlla
   `SELECT COUNT(*), MIN(version), MAX(version), SUM(success=0) FROM schema_migrations`.

⏳ **Manca la verifica runtime GUI** di tutto quanto sopra: build, `tsc`, `eslint` e 270 test
verdi, dati verificati a mano sul DB di produzione, ma le schermate non sono state aperte.
