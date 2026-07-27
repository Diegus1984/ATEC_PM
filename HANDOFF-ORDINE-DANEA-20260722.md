# Handoff — Ordine fornitore Danea generato dalla RDO (strada B)

> Contesto per riprendere in un'altra chat. Progetto ATEC PM (`ATEC_PM/`), server
> ASP.NET 8 + MySQL (Dapper), client solo web `atec-pm-web/`. Porte 5150/5151, Vite 5173.
> Leggi anche: memoria `acquisti_codice_atec` + `danea_migrazione_atec` (indice MEMORY.md),
> piano `PIANO-MIGRAZIONE-DANEA-ATEC.md`.

## Cosa è stato deciso e fatto (22/07/2026 sera)

Diego ha scelto la **strada B**: l'ordine fornitore si scrive DIRETTAMENTE nel Firebird
di Danea (archivio nuovo `Atec_PM.eft`), niente import XML. Implementato e compilato
(build/tsc/eslint verdi). **MANCA SOLO LA PROVA RUNTIME.**

Flusso: Inbox Acquisti → RDO → «Vincitore» → dialog RDO chiusa → data consegna prevista
(facoltativa) + pulsante **«Genera ordine Danea»** (useConfirm) → ordine creato in Atec_PM,
righe distinta → **IO** (se matrice v7 ammette, con `date_ordered`), RDO marca
`danea_order_iddoc/num` e mostra badge «Ordine Danea n. X» (anche colonna «Ordine» in lista).

## Reverse-engineering (fonte: documento campione IDDoc=4 creato a mano da Diego in Atec_PM)

Un ordine fornitore Danea = 4 scritture in UNA transazione Firebird:

1. **TDocTestate**: `TipoDoc='E'`, `StatoOrdine='Conf'`, `Magazz='Principale'`,
   `IDListino='Forn'`, `Num`/`NumDoc` = MAX+1 per (TipoDoc E, anno, `Numeraz` vuota),
   `DescDoc` = "Ordine forn. {num} del {d/M/yy}", totali arrotondati R2 AwayFromZero
   (TotNetto/TotIva/TotIvaDetr/TotDoc/TotDocNoRit), **snapshot anagrafica** in `Anagr_*`
   e `Anagr_Dest*`, `AnnoCompetenzaRitVarie`=anno, `Rinnovo_Intervallo='Mesi_12'`,
   `IvaDovutaEntroUnAnno=1`, tutti i flag/importi residui a 0, `Tmp_*=0`,
   `NoteInterne` = "RDO #{id} — {codice ATEC} (generato da ATEC PM)".
2. **TDocRighe**: `IDArticoloScaricato` (IDArticolo), CodArticolo/CodArticoloForn, Desc,
   QtaShown=Qta, Udm, PrezzoNetto, PrezzoIvato, CodIva (dall'ARTICOLO), ImportoNetto/IvatoRiga,
   **`MovMagazz=1`** (flag "in arrivo" della riga).
3. **TDocIva**: una riga per aliquota (ImportoNetto, Iva, IvaDetr=Iva).
4. **TMovMagazz**: `QtaInArrivo`, IDArticolo, Magazz, IDAnagr fornitore, Data, PrezzoNetto,
   `IDDocRiga` → è QUESTO che alimenta la colonna «in arrivo» di Danea.

Generatori (GEN_ID +1 ciascuno): `TDocTestate__IDDoc`, `TDocRighe__IDDocRiga`,
`TDocIva__IDDocIva`, `TMovMagazz__IDMovMagazz`.

Fornitore risolto in TAnagrafica per **Partita IVA** (fallback ragione sociale esatta) —
funziona perché il bootstrap F1 ha copiato le anagrafiche con gli **IDAnagr originali**.

## GOTCHA scritture Danea (NON derogare)

- **Charset `WIN1252`** + `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`
  (già in Program.cs). MAI NONE in scrittura (mojibake + string truncation), mai ISO8859_1.
- MAI `FbCommand` senza `tx` su una connessione con transazione pendente (il driver rifiuta):
  metadati/letture preparatorie PRIMA del `BeginTransaction`.
- Numerazione MAX+1 dentro la stessa tx: collisione possibile solo se qualcuno crea un
  ordine in Danea nello stesso istante — avvisare, non è bloccato.

## File toccati

- `ATEC.PM.Server/Services/DaneaOrderService.cs` — NUOVO, tutto il cuore (commentato).
- `ATEC.PM.Server/Controllers/PurchaseRfqController.cs` — endpoint
  `POST /api/purchase-rfqs/{id}/create-danea-order` (guardie: vincitore con prezzo E
  articolo di catalogo collegato, niente doppioni) + `DaneaOrderNum` nelle SELECT.
- `ATEC.PM.Server/Services/DbService.cs` — migrazione **v50**: `purchase_rfqs.danea_order_iddoc`
  + `danea_order_num`. (⚠️ la **v49** era già occupata da un'altra sessione: default
  `bom_items.item_status` → 'VER'. `LatestSchemaVersion = 50`.)
- `ATEC.PM.Shared/DTOs/PurchaseRfq_DTOs.cs` — `DaneaOrderNum` + `PurchaseRfqCreateOrderRequest`.
- `ATEC.PM.Server/Program.cs` — `AddSingleton<DaneaOrderService>()`.
- Web: `lib/api/purchase-rfqs.ts` (`createPurchaseRfqDaneaOrder`), `lib/api/types.ts`
  (`daneaOrderNum`), `features/acquisti/AcquistiPage.tsx` (DateField + pulsante + badge
  nel `RfqDetailDialog`, colonna «Ordine» in `RfqsPanel`, colSpan 7→8).

## Limite attuale (scelta consapevole, eventuale evoluzione)

L'ordine generato ha **UNA riga**: l'articolo Danea del fornitore vincitore con
qtà = somma dei fabbisogni della RDO. Evoluzione possibile: accorpare più RDO chiuse
dello stesso fornitore in un ordine multi-riga (il servizio accetta già `List<OrderLine>`).

## DA FARE (prossima chat / Diego)

1. ~~Riavviare il server~~ **FATTO** (v50 applicata, endpoint attivo).
2. ~~Prova runtime lato ATEC PM~~ **FATTA 22/07/2026** (GUI pilotata da Claude, autorizzata):
   RDO #1 (211220726.002 / 00186255, C260505_205) → vincitore SMC Italia 16,71 € →
   ordine **n. 2 (IDDoc 5)** creato in Atec_PM. Verificate le 4 scritture Firebird
   (testata Conf 16,71+3,68=20,39, riga MovMagazz=1, IVA 22, TMovMagazz QtaInArrivo=1,
   consegna prevista 29/07) + badge/colonna «Ordine» in GUI.
3. ~~Avanzamento IO righe distinta~~ **FATTO**: bom_item 27 → IO con date_ordered 22/07.
4. ~~Controllo visivo in Danea~~ **FATTO da Diego 22/07/2026**: ordine n. 2 presente e corretto.
   Resta eventualmente il giro «Arrivo merce».
5. Se ok, valutare l'accorpamento multi-RDO per fornitore.

## Regole di collaborazione con Diego

Rispondere in italiano, risposte minime («Fatto») salvo decisioni/errori/azioni sue;
conferma su ogni azione distruttiva; MAI runtime GUI di propria iniziativa (verifica =
build/tsc/eslint); spegnere i server avviati per i test (5150/5151/5173).
