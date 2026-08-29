# Popolamento descrizioni catalogo Gamma Ricambi

Runbook per `quote_products.description_rtf` (HTML visualizzato in Cat. Preventivi e Gamma Robot).

## Template Schede: tabella 1 riga × 2 colonne (50/50)

Ogni prodotto **Scheda** (e in generale ogni articolo ricambi singolo) deve avere una descrizione HTML con **una sola riga** e **due celle** al 50%:

- **Colonna sinistra**: titolo, metadati, testo tecnico.
- **Colonna destra**: riservata a foto/diagramma (lasciare `<p>&nbsp;</p>` se assente).

### Struttura HTML

```html
<table style="border-collapse: collapse; width: 100%;">
  <tbody>
    <tr>
      <td style="width: 50%; vertical-align: top;">
        <p><strong>Titolo descrittivo del componente</strong></p>
        <p>Codice commerciale: <strong>3HACxxxxxx-001</strong><br>
           Sigla: <strong>DSQC xxx</strong><br>
           Costruttore: ABB<br>
           Famiglia controller: IRC5</p>
        <p>Paragrafo tecnico: funzione, dove si monta, compatibilità robot/controller.</p>
      </td>
      <td style="width: 50%; vertical-align: top;">
        <p>&nbsp;</p>
      </td>
    </tr>
  </tbody>
</table>
```

### Campi colonna sinistra

| Campo | Obbligatorio | Note |
|-------|--------------|------|
| Titolo (`<strong>`) | Sì | Nome leggibile, es. "Unità di alimentazione ABB DSQC 661" |
| Codice commerciale | Sì | Codice ABB/ATEC esatto |
| Sigla | Se applicabile | DSQC, DSPC, sigla costruttore |
| Costruttore | Sì | Di solito ABB |
| Famiglia controller | Per schede robot | IRC5, S4, S4C, IRC5C, ecc. |
| Testo tecnico | Sì | 2–4 frasi: funzione, posizione nel sistema |

### Colonna destra (immagine)

- Se disponibile foto ufficiale o scatto ATEC: inserire `<img>` o `<figure>` nella cella destra.
- Prodotti legacy S2/S3/S4 possono usare `<figure class="image image-style-side">` (formato TinyMCE).
- **Non** mettere testo tecnico nella colonna destra.

## Template Robot (categoria Gamma Ricambi): tabella 2 righe × 2 colonne (50/50)

Un prodotto per ogni `gamma_robot.modello` nella categoria **Robot** (gruppo Manipolazione, listino Gamma Ricambi):

| Cella | Contenuto |
|-------|-----------|
| (0,0) | Descrizione manipolatore (titolo, modello, costruttore, testo da web ABB) |
| (0,1) | Placeholder immagine robot (`<p>&nbsp;</p>`) |
| (1,0) | Descrizione cabinet/controller (derivata da `gamma_quadro.controllore` / `generazione`) |
| (1,1) | Placeholder immagine cabinet |

Script:

- `tools/populate_robot_catalog_gamma.py` — crea categoria + 53 prodotti con baseline.
- `tools/robot_web_descriptions.py` — dizionario testi manipolatore (fonti ABB).
- `tools/update_robot_catalog_web_snippets.py --apply` — applica formato 2×2 + testi web a tutti i prodotti Robot.

### Categorizzazione

- **Robot**: manipolatori ABB (una scheda per modello).
- **Schede**: board elettroniche controller (DSQC 6xx, DSQC 10xx, SMB, RMU…).
- **Azionamenti**: MDU, ADU, bleeder, rectifier.
- **Kit Cavi**: harness, CP/CS, cavi potenza/segnale.
- **Motori / Ventole**: componenti meccanici/elettrici di supporto.

## Stessa scheda, due codici commerciali ABB

Succede spesso con le board IRC5 (es. **DSQC 668**):

| Ruolo | Codice | Prodotto catalogo |
|-------|--------|-------------------|
| **Primario** | `3HAC029157-001` | Computer asse — codice attuale manuale IRC5 3HAC047136-001 |
| **Alternativa (ALT)** | `3HAC028179-001` | Stessa DSQC 668, revisione/codice ordine precedente |

Regole:

1. **Catalogo**: restano **due prodotti distinti** (un record per codice commerciale), entrambi con descrizione 1×2.
2. **Distinta Gamma**: stesso slot (`Axis Computer` / `Schede`) con due righe:
   - `is_alternate = 0` → codice primario `3HAC029157-001`
   - `is_alternate = 1` → codice ALT `3HAC028179-001` (badge **ALT** in UI)
3. **Import nuovi quadri**: se si inserisce il primario, aggiungere anche la riga ALT (vedi `tools/fix_dsqc668_alt_rows.py`).

Non unificare i due codici in un solo prodotto catalogo: i preventivi e la distinta referenziano per `product_id` e il codice ABB deve restare esatto.

## Quando popolare

1. **Nuovo prodotto** inserito manualmente o via script di import distinta.
2. **Prodotto legacy** con descrizione breve (< 100 caratteri) o senza tabella 1×2.
3. Dopo consolidamento codici duplicati (vedi TODO.md).

## Script di riferimento

- `tools/import_irb8700_800_distinta.py` — import distinta; se crea prodotti, deve chiamare il builder descrizione.
- `tools/catalog_description.py` — helper Python per generare HTML template.

## Esempio reale (DSQC 661)

Vedi prodotto catalogo `3HAC026253-001` nel DB: tabella 50/50, metadati IRC5, colonna destra vuota.

## Checklist prima del commit

- [ ] `description_rtf` contiene `<table` con due `<td style="width: 50%"`
- [ ] Una sola riga `<tr>` nel tbody
- [ ] Codice commerciale coincide con `quote_products.code`
- [ ] Anteprima OK in QuoteProductDialog / doppio click Gamma Robot
