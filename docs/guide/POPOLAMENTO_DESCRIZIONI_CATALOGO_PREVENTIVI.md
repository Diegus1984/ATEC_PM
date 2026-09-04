# Catalogo Preventivi — Riferimento di sistema + Runbook descrizioni

Documento di riferimento sul sottosistema **Commerciale** di ATEC PM (Preventivi +
Catalogo Preventivi): come è fatto, come gestisce le immagini, e la procedura per
popolare le descrizioni dei prodotti. Tenere aggiornato quando si scopre/cambia qualcosa.

> Conteggi righe = fotografia al momento dell'analisi (giu 2026), non valori vivi.
> Citazioni file:riga possono spostarsi: verificare nel codice prima di darle per certe.

---

## 1. Architettura — sezione Commerciale

Menu **COMMERCIALE** (`MainWindow.xaml.cs`) con due voci:

| Voce menu | Tag | Pagina | Cartella | Namespace |
|---|---|---|---|---|
| **Preventivi** | `Preventivi` | `QuotesHomePage` | `Views/Commerciale/Preventivi/` | `...Views.Commerciale.Preventivi` |
| **Cat. Preventivi** | `CatalogoPreventivi` | `QuoteCatalogPage` | `Views/Commerciale/QuoteCatalog/` | `...Views.Commerciale.QuoteCatalog` |

(Nota storica: la cartella `QuoteCatalog` si chiamava `Cms` con namespace `...Views.Quotes`;
rinominata e annidata sotto `Commerciale` per coerenza menu↔cartella↔namespace.)

- **Preventivi/**: `QuotesHomePage`, `QuoteDetailPage`, dialoghi (`NewQuoteDialog`,
  `ConvertQuoteDialog`, `AddQuoteItemDialog`, `AddLocalVariantDialog`,
  `AddMaterialVariantDialog`, `MaterialRtfDialog`), `CostingTreeControl`, `Models/`.
- **QuoteCatalog/**: `QuoteCatalogPage`, `QuoteGroupDialog`, `QuoteCategoryDialog`,
  `QuoteProductDialog`, `Converters/` (namespace converter = `ATEC.PM.Client.Converters`,
  svincolato dalla cartella).
- `.csproj` è **SDK-style** (auto-glob): spostare file non richiede modifiche al progetto.

Stack tecnico: WPF/.NET 8 (client), ASP.NET Core Web API (server), **MySQL con Dapper**
(no EF Core). Pattern code-behind + MVVM parziale.

## 2. I tre cataloghi (NON sono la stessa tabella)

Stesso DB `atec_pm`. Tre sistemi distinti e indipendenti:

| | Catalogo Preventivi / CMS | Catalogo Articoli | Categorie Materiali |
|---|---|---|---|
| Radice | `quote_price_lists` (3) | `catalog_items` (~8973) | `material_categories` (8) |
| Categorie | `quote_categories` (148, FK+tree) | colonna `category` (stringa libera) | la tabella stessa |
| Controller | `QuoteCatalogController` `/api/quote-catalog/*` | `CatalogController` `/api/catalog/*` | nessuno (il `MaterialCategoriesController` è stato rimosso il 04/09/2026: nessuna pagina web lo chiamava; la tabella si legge dentro `ProjectCostingController`) |
| Service | `QuoteDbService` (→ `DbService`) | `DbService` | `DbService` |
| Scopo | offerte commerciali | anagrafica articoli/magazzino | markup costing |

> `QuoteDbService.Open()` delega a `DbService.Open()`: **un solo database**.
> Stesso articolo può stare in `quote_products` E `catalog_items` con lo stesso `code`,
> ma i due record **non sono collegati**.

## 3. Schema DB — catalogo preventivi

```
quote_price_lists (Listini)         id, name, currency, locale, is_active, sort_order
   └─ quote_groups                  id, price_list_id, name, sort_order, is_active
        └─ quote_categories         id, group_id, parent_id(self-ref), name, sort_order
             └─ quote_products      id, category_id, item_type(product|content), code,
                                     name, description_rtf(LONGTEXT,HTML), image_path,
                                     attachment_path, auto_include, sort_order, is_active
                  └─ quote_product_variants  id, product_id, code, name,
                                     cost_price DECIMAL(12,2), markup_value DECIMAL(5,3)=1.300
```
- Prezzo vendita variante = `cost_price * markup_value` (calcolato, non in tabella).
- Categorie nidificabili via `parent_id` (NULL = radice). FK `ON DELETE CASCADE`.
- `quotes` (preventivi): `quote_type` = `SERVICE` | `IMPIANTO`; `status` enum
  draft/sent/negotiation/accepted/rejected/expired/converted/superseded; revisioni via
  `revision` + `parent_quote_id`; totali denormalizzati (subtotal, total, cost_total, profit…).
- Connessione locale (script/diagnostica): `Server=localhost;Port=3306;Database=atec_pm;User=root;Password=Atec2005`.
  Server remoto prod: `192.168.2.172`.

## 4. Endpoint principali

**QuoteCatalogController** (`/api/quote-catalog`):
- `GET price-lists` · `POST/PUT/DELETE price-lists/{id}`
- `GET tree?priceListId=` — albero completo gruppi→categorie→prodotti (conteggi ricorsivi)
- `GET/POST/PUT/DELETE groups[/{id}]`
- `GET/POST/PUT/DELETE categories[/{id}]` · `PUT categories/{id}/move`
- `GET/POST/PUT/DELETE products[/{id}]` · `PUT products/{id}/move` · `POST products/{id}/duplicate`
- `POST products/upload` — upload immagini editor (vedi §6)
- `POST products/cleanup-images` — rimuove `<img>` base64 da description_rtf (quote_products + quote_items)
- `POST import` — import listini→gruppi→categorie→prodotti→varianti

**QuotesController** (`/api/quotes`) — **unico controller dei preventivi** (il vecchio
`PreventiviController` è stato eliminato e fuso qui):
- `POST` create (codice `PRV-{anno}-{0000}`, init costing se IMPIANTO)
- `POST {id}/convert` (solo IMPIANTO → crea commessa `AT{anno}{000}`, copia sezioni costo/
  materiali/fasi/pricing; body `QuoteConvertDto { PmId }`)
- `POST {id}/revision` · `POST {id}/duplicate`
- lista paginata, CRUD, items, `{id}/pdf`, `{id}/status`, `{id}/field`, `{id}/reload-auto-includes`

**QuoteCostingController** (`/api/quotes/{quoteId}/costing`) — rinominato da
`PreventiviCostingController`: sezioni costo, risorse, sezioni/righe materiali (IMPIANTO).

**CatalogController** (`/api/catalog`): CRUD su `catalog_items` + `filter-meta`.

> DTO convert: `QuoteConvertDto` in `Shared/DTOs/Quote_DTOs.cs` (era `PreventivoConvertDto`).

## 5. Editor descrizione (TinyMCE in WebView2)

- Controllo: `UserControls/HtmlEditor.xaml.cs` → WebView2 che carica
  `Assets/tinymce/editor.html`. Init: `initEditor(content, apiBaseUrl, token)`;
  API: `getContent()` / `setContent(html)`; evento `contentChanged`.
- Editor reale: **TinyMCE 5.10.9** (NON CKEditor). Le descrizioni storiche con
  `<figure class="image/table">` + base64 vengono da un **vecchio editor CKEditor**.
- TinyMCE 5: `table_use_colgroups` **disattivo** → larghezze colonna sui `<td>`, non in
  `<colgroup>`; tabelle NON in `<figure>`.
- Stesso editor usato da `QuoteProductDialog` (catalogo) e `MaterialRtfDialog` (materiali).

## 6. Gestione immagini (allegate alla descrizione)

Flusso upload (incolla/trascina/file-picker):
1. TinyMCE `automatic_uploads: true` + `images_upload_handler` (editor.html): ogni immagine
   è **caricata sul server**, non lasciata inline.
2. `POST /api/quote-catalog/products/upload` (multipart, `Authorization: Bearer`). Una POST
   **per ogni** immagine; endpoint accetta 1 file (`IFormFile`), limite **50 MB**.
3. `QuoteCatalogController.UploadProductAttachment` salva in
   `{Uploads:CmsPath}/products/` = **`C:\ATEC_PM\Uploads\cms\products\`**, nome
   `att_{yyyyMMdd_HHmmss}_{guid8}_{nome}{.ext}` con **`FileMode.CreateNew`** (token GUID →
   upload multipli/ravvicinati **non si sovrascrivono mai**); ritorna path relativo.
4. Static files: `Program.cs` `app.UseStaticFiles(RequestPath="/uploads/cms")` → cartella
   `{Uploads:CmsPath}` (da `appsettings.json`).

Storage e URL:
- Le **foto NON stanno nel DB** (eccezione: vecchie base64 da bonificare). In
  `description_rtf` c'è solo il tag `<img src>`.
- **URL salvato RELATIVO** (`/uploads/cms/products/...`). L'editor lo rende assoluto per la
  vista (`toDisplayUploads`) e lo ri-normalizza a relativo al salvataggio (`toRelativeUploads`);
  così cambiare server/porta non rompe i link. Gli URL assoluti storici diventano relativi al
  primo salvataggio dall'editor.
- PDF: `QuotePdfService.RenderHtmlBlock` → `ResolveImagePath` estrae `/uploads/cms/...` e
  legge il file da `_cmsBasePath`; funziona sia con URL assoluti sia relativi.
- `image_path`/`attachment_path` della tabella **non** sono usati da questo flusso.

Pulizia file su disco (anti-orfani):
- `UpdateProduct`: confronta vecchia/nuova descrizione e cancella le immagini **rimosse**.
- `DeleteProduct`: cancella le immagini del prodotto eliminato.
- In entrambi i casi **solo se il file non è più referenziato** in `quote_products` /
  `quote_items` / `quote_material_items`, e solo dentro `products/` (guardia anti path-traversal).
- Bonifica base64 legacy: `POST /products/cleanup-images`.

---

## 7. RUNBOOK — popolare la descrizione di un prodotto

### Regole di impaginazione (FISSE)
1. Descrizione = **tabella 1 riga × 2 colonne**, colonne **50% / 50%**.
2. **Sinistra** = testo (titolo + dati identificativi + funzione). **Destra** = vuota
   (`<p>&nbsp;</p>`) per l'immagine caricata **manualmente** dall'utente.
3. Rimuovere sempre immagini inline (`data:image…base64`, tag `<img>`) e azzerare
   `image_path`/`attachment_path`.

### Template HTML (TinyMCE — canonico)
```html
<table style="border-collapse: collapse; width: 100%;">
  <tbody>
    <tr>
      <td style="width: 50%; vertical-align: top;"><!-- DESCRIZIONE (sinistra) --></td>
      <td style="width: 50%; vertical-align: top;"><p>&nbsp;</p><!-- IMMAGINE a mano (destra) --></td>
    </tr>
  </tbody>
</table>
```
> ⚠️ NON usare markup CKEditor (`<figure class="table">`, `ck-table-resized`, `<colgroup>`):
> TinyMCE può riscriverlo e perdere il 50/50 al primo salvataggio.

Colonna sinistra consigliata (entità HTML: `&agrave;`, `&ndash;`, `&nbsp;`):
```html
<p><strong>{Titolo articolo}</strong></p>
<p>Codice commerciale: <strong>{code}</strong><br>Sigla: <strong>{sigla}</strong><br>
Costruttore: {brand}<br>Famiglia controller: {famiglia}</p>
<p>{Descrizione funzionale, 2-3 frasi.}</p>
```

### Procedura
1. Leggere il record: `SELECT id, code, name, image_path, attachment_path, description_rtf FROM quote_products WHERE id={ID};`
2. Comporre il testo. Se manca, **cercare online** con codice commerciale + sigla
   (es. `3HAC028756-001` + `DSQC 573`); incrociare con `catalog_items` (stesso `code`).
3. Aggiornare con query **parametrizzata** (mai concatenare l'HTML in SQL):
   `UPDATE quote_products SET description_rtf=@rtf, image_path='', attachment_path='' WHERE id={ID};`
4. Verificare: `width: 50%` ×2, niente `base64`, niente `<img>`.
5. In app (Cat. Preventivi) aprire la scheda e caricare l'immagine nella cella destra.

### Esempio fatto — DSQC 573 (id 254, code 3HAC028756-001)
```html
<table style="border-collapse: collapse; width: 100%;"><tbody><tr><td style="width: 50%; vertical-align: top;"><p><strong>Scheda di misura seriale ABB DSQC 573</strong></p><p>Codice commerciale: <strong>3HAC028756-001</strong><br>Sigla: <strong>DSQC 573</strong><br>Costruttore: ABB<br>Famiglia controller: IRC5</p><p>Scheda elettronica di ricambio per controller ABB IRC5, con funzione di unit&agrave; di misura seriale (SMU &ndash; Serial Measurement Board). Acquisisce i segnali di posizione dai resolver/encoder montati sugli assi del manipolatore e li trasmette al computer del controller, fornendo il riferimento di posizione degli assi necessario al controllo di moto del robot.</p></td><td style="width: 50%; vertical-align: top;"><p>&nbsp;</p></td></tr></tbody></table>
```

---

## 8. Quirk / avvertenze sui dati (verificati)

- **Markup anomalo Ricambi**: in tutta la categoria *Ricambi* (listino *Atec Service*) le
  varianti hanno `cost_price = 30,00` e `markup_value = 33,333` (≈ vendita 999,99 €). Pattern
  di import, non margine reale. Da rivedere a parte se si tocca il pricing.
- **Categorie quote_categories da bonificare**:
  - duplicati VERI gruppo 6 *Installazione & Servizi* (es. cat 52/110, 53/111, 54/112, vuote);
  - falsi duplicati gruppo 14 *Robot* (IRB 1600/2400/… sotto `Robot Nuovi` 193 vs `Robot Usati`
    195: **da NON unire**);
  - ~26 categorie vuote (0 prodotti, 0 figlie);
  - incoerenza gerarchia: cat 69/72 con `group_id=14` ma `parent_id=191` (gruppo 2).
- **Affidabilità descrizioni**: per alcuni P/N ABB non c'è datasheet pubblico chiaro; annotare
  quando la funzione è dedotta (incrocio `catalog_items` + rivenditori) e non ufficiale.

## 9. Batch (futuro)

Molte schede *Ricambi* hanno ancora immagini base64 in `description_rtf`. Sistemazione in
serie: per ogni `id` comporre il testo (online + `catalog_items`), applicare il template
50/50, azzerare le immagini. Procedere solo dopo validazione su un campione.

Possibile script una-tantum: normalizzare in blocco gli `<img src>` assoluti già nel DB a
relativi (`s#https?://host/uploads/cms/#/uploads/cms/#`) su quote_products/quote_items/quote_material_items.

## 10. Strumenti

- `scratch_db_test/` (progetto console C# + MySqlConnector): query/diagnostica/update mirati
  sul DB locale. Usare query parametrizzate per gli UPDATE di `description_rtf`.
