# ATEC PM — Struttura DB Danea Easyfatt (Firebird)

## Connessione

- **Tipo**: Firebird Embedded (fbclient.dll locale)
- **File**: `.eft` (es. `C:\ATEC_Commesse\Srl-2020-2021.eft`)
- **Credenziali**: SYSDBA / masterkey
- **Charset**: NONE (latin1)
- **Case-sensitive**: nomi tabella e colonna vanno sempre tra virgolette doppie (`"TDocTestate"`)

---

## Tabelle principali

| Tabella | Contenuto |
|---|---|
| **TDocTestate** | Header documenti (fatture, DDT, ordini, preventivi, NC) |
| **TDocRighe** | Righe dettaglio di ogni documento |
| **TDocIva** | Spaccatura IVA per documento |
| **TTipiDoc** | Anagrafica tipi documento |
| **TAnagrafica** | Clienti e fornitori |

---

## Tipi documento (TTipiDoc)

### Vendita (Acq=0)

| TipoDoc | Nome | Gruppo |
|---|---|---|
| **I** | Fattura | Fatture |
| **F** | Fattura accompagnatoria | Fatture |
| **J** | Fattura d'acconto | Fatture |
| **N** | Nota di credito | Fatture |
| **O** | Nota di debito | Fatture |
| **P** | Parcella | Fatture |
| **M** | Autofattura | Fatture |
| **D** | Doc. di trasporto (DDT) | Doc. di trasporto |
| **C** | Ordine cliente | Ordini cliente |
| **Q** | Preventivo | Preventivi |
| **G** | Rapporto d'intervento | Rapporti intervento |
| **L** | Fattura pro-forma | Fatture pro-forma |
| **B** | Vendita al banco | Vendite al banco |
| **R** | Ricevuta fiscale | Ricevute fiscali |
| **T** | Reg. corrispettivi | Corrispettivi |

### Acquisto (Acq=1)

| TipoDoc | Nome | Gruppo |
|---|---|---|
| **S** | Preventivo fornitore | Preventivi fornitore |
| **E** | Ordine fornitore | Ordini fornitore |
| **H** | Arrivo merce | Arrivi merce |
| **U** | Reg. fattura fornitore | Reg. fatture fornitore |
| **V** | Reg. NC fornitore | Reg. fatture fornitore |
| **W** | Reg. spese fuori campo IVA | Spese fuori campo |

---

## Colonne chiave — TDocTestate

| Colonna | Tipo | Descrizione |
|---|---|---|
| IDDoc | INT | PK |
| TipoDoc | VARCHAR(50) | Tipo (I, F, D, C, ecc.) |
| IDAnagr | INT | FK verso TAnagrafica |
| NumDoc | VARCHAR(50) | Numero documento (es. "14/26") |
| Data | DATE | Data documento |
| Num | INT | Numero progressivo interno |
| Numeraz | VARCHAR(50) | Numerazione |
| Anagr_Nome | VARCHAR(255) | Nome cliente (denormalizzato) |
| Anagr_Indirizzo | VARCHAR(255) | Indirizzo |
| Anagr_Cap | VARCHAR(50) | CAP |
| Anagr_Citta | VARCHAR(255) | Citta |
| Anagr_Prov | VARCHAR(50) | Provincia |
| Anagr_PartitaIva | VARCHAR(50) | P.IVA |
| Anagr_CodiceFiscale | VARCHAR(50) | CF |
| TotNetto | DECIMAL | Totale netto |
| TotIva | DECIMAL | Totale IVA |
| TotDoc | DECIMAL | Totale documento |
| TotPrezzoAcquisto | DECIMAL | Totale costo acquisto |
| TotGuadagno | DECIMAL | Totale margine |
| Pagamento | VARCHAR(255) | Condizioni pagamento |
| Pagam_Saldato | SMALLINT | 0/1 saldato |
| Pagam_TotPagamenti | DECIMAL | Totale pagato |
| Pagam_ImportoDaSaldare | DECIMAL | Residuo da saldare |
| Pagam_UltimaDataPagam | DATE | Data ultimo pagamento |
| NoteInterne | VARCHAR(255) | Note interne |
| Note | VARCHAR(255) | Note visibili |
| Extra1 | VARCHAR(255) | Campo personalizzabile 1 |
| Extra2 | VARCHAR(255) | Campo personalizzabile 2 |
| Extra3 | VARCHAR(255) | Campo personalizzabile 3 |
| Extra4 | VARCHAR(255) | Campo personalizzabile 4 |
| Agente | VARCHAR(255) | Agente commerciale |
| Agente_ImportoProvv | DECIMAL | Importo provvigione |
| StatoOrdine | VARCHAR(50) | Stato (per ordini) |
| FE_Src_Commessa | VARCHAR(100) | Riferimento commessa (fattura elettronica) |
| FE_Src_Cig | VARCHAR(15) | CIG |
| FE_Src_Cup | VARCHAR(15) | CUP |
| FE_TipoDoc | VARCHAR(4) | Tipo doc fattura elettronica (TD01, TD04, ecc.) |
| InclusoInIDDoc | INT | FK se incluso in altro documento |
| ScontiDefault | VARCHAR(50) | Sconti default |
| IDListino | VARCHAR(50) | Listino applicato |
| CausaleTrasporto | VARCHAR(255) | Causale trasporto (DDT) |
| Vettore | VARCHAR(255) | Vettore (DDT) |

---

## Colonne chiave — TDocRighe

| Colonna | Tipo | Descrizione |
|---|---|---|
| IDDocRiga | INT | PK |
| IDDoc | INT | FK verso TDocTestate |
| CodArticolo | VARCHAR(50) | Codice articolo |
| CodArticoloForn | VARCHAR(50) | Codice articolo fornitore |
| Desc | VARCHAR(2000) | Descrizione riga |
| Qta | DECIMAL | Quantita |
| QtaShown | DECIMAL | Quantita visualizzata |
| Udm | VARCHAR(50) | Unita di misura |
| Lotto | VARCHAR(50) | Lotto |
| PrezzoNetto | DECIMAL | Prezzo unitario netto |
| PrezzoIvato | DECIMAL | Prezzo unitario ivato |
| Sconti | VARCHAR(50) | Sconti applicati |
| CodIva | VARCHAR(50) | Codice aliquota IVA |
| ImportoNettoRiga | DECIMAL | Totale riga netto |
| ImportoIvatoRiga | DECIMAL | Totale riga ivato |
| PrezzoAcquisto | DECIMAL | Costo acquisto unitario |
| ImportoAcquistoRiga | DECIMAL | Totale costo riga |
| GuadagnoRiga | DECIMAL | Margine riga |
| PercProvv | DECIMAL | % provvigione |
| ImportoProvvRiga | DECIMAL | Importo provvigione riga |
| Note | VARCHAR(255) | Note riga |
| MovMagazz | SMALLINT | Movimento magazzino |

---

## Colonne chiave — TDocIva

| Colonna | Tipo | Descrizione |
|---|---|---|
| IDDocIva | INT | PK |
| IDDoc | INT | FK verso TDocTestate |
| CodIva | VARCHAR(50) | Codice aliquota |
| ImportoNetto | DECIMAL | Imponibile |
| Iva | DECIMAL | IVA |
| IvaDetr | DECIMAL | IVA detraibile |

---

## Query utili

```sql
-- Fatture emesse
SELECT * FROM "TDocTestate" WHERE "TipoDoc" = 'I' ORDER BY "Data" DESC

-- Fatture + NC vendita
SELECT * FROM "TDocTestate" WHERE "TipoDoc" IN ('I','F','J','N','O') ORDER BY "Data" DESC

-- Fatture per cliente (IDAnagr)
SELECT * FROM "TDocTestate" WHERE "TipoDoc" = 'I' AND "IDAnagr" = @idCliente

-- Fatture con riferimento commessa
SELECT * FROM "TDocTestate" WHERE "FE_Src_Commessa" LIKE '%AT2025%'

-- Righe di una fattura
SELECT * FROM "TDocRighe" WHERE "IDDoc" = @idDoc ORDER BY "IDDocRiga"

-- Fatture non saldate
SELECT * FROM "TDocTestate" WHERE "TipoDoc" IN ('I','F') AND "Pagam_Saldato" = 0

-- Fatturato per cliente (anno corrente)
SELECT "Anagr_Nome", COUNT(*) AS NumFatture, SUM("TotNetto") AS TotNetto, SUM("TotDoc") AS TotDoc
FROM "TDocTestate"
WHERE "TipoDoc" IN ('I','F') AND EXTRACT(YEAR FROM "Data") = 2025
GROUP BY "Anagr_Nome"
ORDER BY TotDoc DESC

-- DDT non ancora fatturati
SELECT * FROM "TDocTestate"
WHERE "TipoDoc" = 'D' AND ("InclusoInIDDoc" IS NULL OR "InclusoInIDDoc" = 0)
ORDER BY "Data" DESC

-- Ordini cliente aperti
SELECT * FROM "TDocTestate"
WHERE "TipoDoc" = 'C' AND ("StatoOrdine" IS NULL OR "StatoOrdine" <> 'Evaso')
ORDER BY "Data" DESC

-- Fatture fornitore
SELECT * FROM "TDocTestate" WHERE "TipoDoc" = 'U' ORDER BY "Data" DESC
```

---

## Note tecniche

- I nomi tabella/colonna sono **case-sensitive** in Firebird — usare sempre `"virgolette doppie"`
- Il file `.eft` e il file Firebird embedded — usare `ServerType = FbServerType.Embedded`
- Charset `NONE` per evitare problemi di encoding con caratteri latin1
- Il campo `Anagr_Nome` e denormalizzato nella testata (non serve JOIN con TAnagrafica per il nome)
- `IDAnagr` collega a `TAnagrafica` per dati completi del cliente
- `FE_Src_Commessa` contiene il riferimento commessa della fattura elettronica
- I campi `Extra1..Extra4` sono personalizzabili dall'utente in Danea
- `Pagam_Saldato` = 0 non saldato, 1 saldato
- `InclusoInIDDoc` indica se un DDT e stato incluso in una fattura
