# HANDOFF — Importi in € nella griglia del Cash Flow commessa

> ## ✅ CHIUSO il 03/08/2026 — non c'è più niente da fare qui
>
> Scelta dell'utente: **opzione 1 (misto)**. Implementato in
> `atec-pm-web/src/features/commesse/ProjectCashFlow.tsx`:
> - `euro()` sulla colonna «Totale» di tutte le righe di importo e sulle righe calcolate
>   (PAGAMENTO, ENTRATE/USCITE MESE, DIFFERENZA); celle mensili compatte a interi (`n0`)
>   con didascalia «Importi in €» sopra la griglia e intestazione «Totale (€)».
> - `EditRow` ha `kind: "money" | "percent"` (default `percent`): «Aggiustam. Manu» e
>   «BANCA» sono `money` e usano `MoneyInput` controllato (stato `string[]` per riga,
>   niente `key` col valore); le due righe `%` sono rimaste identiche.
> - `CategoryAmountRow`: totale editabile → `MoneyInput`, categoria collegata → `euro()`.
> - `parseNum` eliminato in favore di `parseDecimal` di `@/lib/format`.
> - `MoneyInput` ha una prop opzionale `format` (default `euro`) per la formattazione a
>   riposo: serviva per le celle compatte, non cambia nessuno degli altri usi.
> - Tooltip del grafico in `euro()`; asse Y lasciato compatto.
>
> `tsc -b`, `eslint src` (0 errori) e `npm run build` verdi. **Manca la verifica a
> runtime sulla GUI** e il deploy sul server.
>
> Sotto resta il piano originale, per storia.

> Creato il **31/07/2026**. È l'unico pezzo rimasto aperto della richiesta
> «gli importi monetari devono essere tutti con € e due decimali, in tutto ATEC PM».
> Tutto il resto è già fatto, in produzione e verificato: vedi «Cosa è già stato fatto».

## Obiettivo

Portare anche la **griglia mensile del Cash Flow commessa** allo standard degli importi
(`euro()` / `MoneyInput`). Oggi quella tabella è l'ultima che mostra i soldi come interi
senza simbolo (`4.000` invece di `4.000,00 €`) e usa `<input>` grezzi non controllati.

**File unico da toccare:** `atec-pm-web/src/features/commesse/ProjectCashFlow.tsx`
(sezione «Commesse → tab Flusso di cassa»).

## Decisione da prendere PRIMA di scrivere codice

La griglia ha **13 colonne mensili**. Scrivere `1.234,00 €` in ognuna la rende larghissima
e costringe a scorrere in orizzontale per leggere una riga. Le opzioni:

1. **Misto (consigliato)** — `euro()` completo nella colonna «Totale» e nelle righe
   calcolate; celle mensili con importo compatto (interi, come oggi) e simbolo € solo
   nell'intestazione della tabella. Resta leggibile.
2. **Tutto `euro()`** — coerenza assoluta con la regola, ma tabella molto più larga.
3. **Tutto `euro()` + zoom/compattazione** — come 2, riducendo font e padding delle celle.

Chiedere all'utente quale preferisce: è una scelta di leggibilità, non tecnica.

## Com'è fatto il file adesso (mappa)

| Componente | Righe | Cosa contiene | Tipo di dato |
|---|---|---|---|
| `CalcRow` | ~441-465 | PAGAMENTO, ENTRATE MESE, USCITE MESE, DIFFERENZA | **importi**, sola lettura, resi con `n0()` |
| `EditRow` | ~467-503 | riga con celle mensili **editabili** | **dipende dall'uso** (vedi sotto) |
| `CategoryAmountRow` | ~505-575 | nome categoria + totale editabile + importi mensili | totale = **importo editabile**, celle = importi in sola lettura |

`EditRow` è usato quattro volte e **non tutte sono soldi**:

| Riga | Uso | Tipo |
|---|---|---|
| ~318 | `%` incasso (`INCOME_PCT`) | **percentuale** — NON convertire |
| ~331 | `Aggiustam. Manu` (`ADJUSTMENT`) | **importo €** |
| ~386 | `%` per categoria (`CAT_PCT`) | **percentuale** — NON convertire |
| ~408 | `BANCA` (`BANK`) | **importo €** |

Helper locali del file:
- `n0(v)` → intero all'italiana senza simbolo (`Math.round(v).toLocaleString("it-IT")`).
- `parseNum(s)` → parsing che toglie i punti e converte la virgola. Equivalente a
  `parseDecimal` di `@/lib/format`, che nel frattempo è stato reso altrettanto tollerante:
  valutare se eliminare `parseNum` e usare quello condiviso.

## Cosa fare

1. **`EditRow` riceve un `kind: "money" | "percent"`** (default `percent`, così le due righe
   di percentuale non cambiano). Quando è `money` la cella usa `MoneyInput`
   (`@/components/shared/money-input`), altrimenti resta l'input attuale.
2. **Da uncontrolled a controlled.** Le celle oggi usano
   `defaultValue={n0(v)}` + `key={`${i}-${v}`}` per farsi rimontare quando il valore cambia:
   `MoneyInput` è controllato, quindi serve uno stato testuale per cella (o per riga, un
   `string[]` allineato a `values`, come è stato fatto per «Incasso totale» con `payText`).
   Il `key` col valore va tolto, altrimenti il campo si rimonta mentre scrivi.
3. **`CalcRow` e le celle di sola lettura** → `euro()` al posto di `n0()`, secondo l'opzione
   scelta al punto «Decisione».
4. **`CategoryAmountRow`**: il totale categoria editabile (~559) diventa `MoneyInput`;
   il valore mostrato quando la categoria è collegata (~557) usa `euro()`.
5. Non toccare le percentuali né il campo del **nome categoria**.

Il ricalcolo (`calc`, `useMemo` a ~114) lavora su numeri e non va toccato: cambia solo
come i numeri vengono mostrati e raccolti.

## Trappole

- **`MoneyInput` normalizza al blur**: rimette nello stato la stringa canonica del numero,
  così chi salva con `parseNum`/`parseDecimal` non trova mai «1.234,50» ambiguo.
- **Percentuali**: non devono avere il €. Se per errore ci passano dentro, il totale «%»
  in cima diventa illeggibile.
- **Salvataggio per cella**: `saveCell(dataType, refId, monthIndex, value)` parte a ogni
  commit; con lo stato controllato assicurarsi di non chiamarlo a ogni tasto ma solo al
  blur (`onCommit`), come fa già `MoneyInput`.
- **Categoria collegata** (`category.isLinked`): il totale non è editabile, mostra solo il
  valore. Mantenere il comportamento.

## Verifica (IMPORTANTE)

```bash
cd atec-pm-web && npx tsc -b && npx eslint src && npm run build
```

**`npx tsc --noEmit` NON controlla niente** in questo progetto: `tsconfig.json` è una
solution config con `references` e senza file propri, quindi esce 0 anche con errori veri.
Usare sempre `tsc -b` (o `npm run build`).

> Nota shell: Node non è nel PATH → `$env:Path = "C:\Program Files\nodejs;" + $env:Path`.

## Deploy

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File deploy/aggiorna-server.ps1
```

Compila, carica su `192.168.2.150`, riavvia e verifica; se il nuovo server non risponde
torna da solo alla versione precedente. `index.html` non è più cacheato, quindi all'utente
basta un F5.

## Cosa è già stato fatto (NON rifare)

- **`euro()`** (`@/lib/format`) è l'unico formattatore: `4.000,00 €`. Convertite le 44
  formattazioni a mano del Commerciale (`fmt2(x)€`).
- **`MoneyInput`** (`@/components/shared/money-input`): formattato a riposo, grezzo in
  modifica, normalizza al blur, allinea a destra. Già in uso in: Importo Ordine SAL,
  Ricavo/Budget commessa, Costo acquisto/Prezzo listino catalogo, Order price e Trasferta
  consuntivo (Prev vs Consuntivo), Costo unit. DDP, Costo unitario Officina, Costo orario
  Config Sezioni, variante locale preventivi, €/h e costo materiale del costing, costo
  variante Catalogo Preventivi, **«Incasso totale» del Cash Flow** (già controllato, con
  stato `payText`).
- `parseDecimal` regge ora `1.234,50` e ignora spazi/€.
- Regola scritta in `atec-pm-web/BLOCKS-RULES.md` → «Regola — importi sempre con € e due
  decimali».

## Regole di progetto da rispettare

- `BLOCKS-RULES.md`: fedeltà ai blocchi shadcn, **conferma obbligatoria** su ogni azione
  distruttiva (`useConfirm`, mai `window.confirm`), riga che si adatta al testo, menu
  «Colonne» sulle griglie.
- Ambiente multi-utente: se si tocca il salvataggio, mantenere il realtime già presente.
