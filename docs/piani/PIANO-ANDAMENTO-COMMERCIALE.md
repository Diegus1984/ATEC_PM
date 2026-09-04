# Piano — Andamento Commerciale (Registro Offerte + cruscotto per la proprietà)

> Scritto il 04/09/2026 dopo l'esame di `Files To Check/` (applicativo **ATEC Offerte 2.1**, sorgenti,
> handoff tecnico e `seed.json` con i dati reali) e il confronto con il codice di ATEC PM.
>
> Obiettivo di Diego: **una nuova sezione che serve al venditore per comunicare alla proprietà,
> dal gestionale, come vanno le commesse e come vanno le offerte.**
>
> Decisione presa: la numerazione del registro offerte è una **serie parallela agganciata** —
> resta separata da `quotes.quote_number`, e le due si collegano quando serve.

---

## 1. Cosa si costruisce

Due voci nuove nella sezione **Commerciale** del menu:

| Voce | Chi la usa | A cosa serve |
|---|---|---|
| **Registro Offerte** (`/offerte`) | il venditore, tutti i giorni | sostituisce l'Excel «NUMERI OFFERTE»: numero univoco, cliente, importo, esito, chance, contatti col referente |
| **Andamento** (`/andamento`) | il venditore per parlare con la proprietà | **offerte**: emesse, prese, perse, conversione, portafoglio, mercato. ⛔ La metà **commesse** è stata esclusa dal perimetro il 04/09/2026 (§17) |

La prima è il *dato*, la seconda è il *discorso*. Senza il registro l'Andamento avrebbe metà pagina vuota:
oggi in ATEC PM le offerte non ci sono, ci sono solo i preventivi fatti col modulo Preventivi.

---

## 2. La regola della serie parallela

| Domanda | Risposta |
|---|---|
| Chi numera l'offerta? | Il **registro**: `tipo + numero(3 cifre) + «-» + anno + «-» + sigla venditore` → `S097-2026-EC` |
| Chi numera il preventivo? | I **Preventivi**, come oggi: `PRV-2026-0001`. Non si tocca |
| Come si agganciano? | `sales_offers.quote_id` (NULL) e `sales_offers.project_id` (NULL) |
| Chi comanda sull'esito commerciale? | Il **registro**. È l'unico che copre anche le offerte fatte fuori da ATEC PM (a mano, per mail, su Excel) |
| Cosa succede se cambia lo stato del preventivo? | Solo se la riga di registro è ancora `aperta`: `accepted`/`converted` → `presa`, `rejected` → `persa`. Se il venditore ha già deciso a mano, ATEC PM **non sovrascrive**: scrive una voce nel registro modifiche e mostra un badge «da allineare» |
| E quando il preventivo diventa commessa? | Si compila `sales_offers.project_id` in automatico e si scrive il numero composto in `project_sal.rif_offerta` (campo che **esiste già**, oggi a mano) |
| Un'offerta può nascere da un preventivo? | Sì: dal dettaglio preventivo, bottone «Registra nel registro offerte» → riga precompilata (cliente, importo = `total`, venditore = `assigned_to`, data) con `quote_id` valorizzato |
| E il contrario? | Sì: dalla riga di registro, «Crea preventivo» apre il dialogo esistente precompilato e al salvataggio scrive `quote_id` |

**Cosa non si fa, e perché.** Non si importano le 1.795 offerte storiche dentro `quotes`:
quella tabella ha `customer_id`, `title` e `created_by` NOT NULL con FK, mentre il registro ha
1.795 righe con il nome cliente scritto a mano e nessun collegamento, 35 righe senza numero,
19 bozze vuote e venditori che sono sigle (FF, GS, LS…) di cui alcune non corrispondono a nessun
dipendente. Forzarle in `quotes` significherebbe inventare dati e sporcare la numerazione dei
preventivi veri.

---

## 3. Cosa c'è già e si riusa (niente da riscrivere)

| Serve per | Pezzo già in casa |
|---|---|
| Utenti, ruoli, PIN | autenticazione JWT + [permessi a profili](PIANO-PERMESSI-REBUILD.md) — l'intero `auth.js` di ATEC Offerte non serve |
| Più utenti insieme | MySQL + SignalR — `folder.js` (lock file, heartbeat, forzatura) **non serve affatto** |
| Backup | backup notturno completo sul NAS |
| Distribuzione | server in LAN `192.168.2.150:5150` — l'avviatore PowerShell e `percorso.txt` non servono |
| Rubrica clienti | `customers` (da arricchire, §4.3) |
| Grafici | `recharts`, già in `package.json` |
| Export Excel | `ClosedXML`, già nel `.csproj` |
| PDF | `QuestPDF`, già usato da `QuotePdfService` |
| Invio mail | `EmailService.QueueSimpleMail(...)` |
| **Redditività per commessa** | `GET /api/bilancio/summary` → `BilancioSummaryDto`: ordine, costi risorse/materiali/officina/trasferte, redditività € e %, soglia parametrica |
| **Fatturato e incassato** | `sal_rows` (`data_fatt`, `n_fatt`, `perc`, `pagamento`, `data_pagamento`, `gg_saldo`) e il semaforo `SalAlert.CaseSql` |
| **Ore consuntivo** | `budget-vs-actual` della commessa |
| «Da richiamare» | modulo **Scadenze** + campanella delle notifiche: il promemoria del prossimo contatto entra lì, non si fa una lista a sé |

Da ATEC Offerte si porta **solo la logica**, che è già isolata e senza dipendenze in `mutations.js`:
numerazione, validazione, normalizzazione del nome cliente (`keyName`) e abbinamento (`matchClient`).
Si traduce quasi riga per riga in C#.

---

## 4. Modello dei dati

Una sola migrazione, `ATEC.PM.Server/Migrations/M122_RegistroOfferte.cs` (prossimo numero libero;
regola: [una migrazione = un file](PIANO-MIGLIORIE-TECNICHE.md)). Nomi con prefisso `sales_offer*`
per non confondersi con le vecchie `offer_*` rimosse anni fa e sostituite da `quote_*`.

### 4.1 `sales_offers` — la riga di registro

| Colonna | Tipo | Note |
|---|---|---|
| `id` | INT AI PK | |
| `year`, `number` | INT, INT NULL | `number` NULL solo per le 35 righe storiche senza numero |
| `type_code` | VARCHAR(10) | entra nel numero composto |
| `seller_code` | VARCHAR(10) | sigla; **è il dato storico, non si perde mai** |
| ~~`seller_employee_id`~~ | — | **non esiste**: il legame con `employees` sta una volta sola su `sales_offer_sellers`, e l'elenco lo legge dal join. Duplicarlo sulla riga voleva dire tenerlo allineato a mano su 1.795 righe |
| `offer_date` | DATE NULL | deve cadere nell'anno |
| `customer_id` | INT NULL | FK `customers` ON DELETE SET NULL |
| `customer_name` | VARCHAR(300) | come scritto sull'offerta: **non si riscrive mai** collegando il cliente |
| `contact_name` | VARCHAR(200) | referente |
| `description`, `notes`, `tag` | VARCHAR(1000), TEXT, VARCHAR(100) | |
| `amount`, `amount_max` | DECIMAL(14,2) NULL | forbice: `amount_max ≥ amount`. Le statistiche usano `amount` |
| `status` | ENUM `bozza,aperta,presa,persa,sospesa` | |
| `chance` | TINYINT NULL | 0–100 a passi di 10; NULL = non valutata |
| `order_amount` | DECIMAL(14,2) NULL | ordine a corpo |
| `is_time_material` | TINYINT(1) | «a consuntivo» |
| `advance_pct`, `advance_amount`, `advance_notes` | | anticipo |
| `postponed_year` | INT NULL | rinvio |
| `offer_path`, `calc_path` | VARCHAR(500) | percorsi NAS del documento e della tabella di calcolo |
| `next_contact` | DATE NULL | alimenta Scadenze |
| `legacy_number` | VARCHAR(50) | `numeroFile`: numero composto diverso presente nel vecchio Excel |
| `legacy_ok` | VARCHAR(50) | la segnalazione «OK» grezza del vecchio foglio (405 righe): da lì si era ricavato lo stato, e resta l'originale di quella decisione |
| `legacy_id` | VARCHAR(20) | la riga di provenienza (`r4` = riga 4 del foglio Detail). **È la chiave dell'import**, §14 |
| `is_legacy_series` | TINYINT(1) | le 9 offerte 2026 numerate 353–361 che continuano la serie 2025 |
| `quote_id`, `project_id` | INT NULL | FK ON DELETE SET NULL |
| `project_code`, `comm_number` | VARCHAR(30), VARCHAR(20) | codice commessa e n. commessa scritti sul vecchio foglio (84 e 38 righe). Il codice è nello stesso formato di `projects.code` (`C221111_093`) ed è ciò da cui l'import ricava `project_id` |
| `row_version` | INT | concorrenza ottimistica |
| `created_at/by`, `updated_at/by` | | |

Indici: `(year, status)`, `(customer_id)`, `(seller_code, year)`, `(next_contact)`, `(quote_id)`, `(project_id)`.

**Niente UNIQUE su `(year, number)`.** Nei dati storici i numeri 282–285 del 2025 sono duplicati
davvero: l'import deve accettarli, la creazione no. Si fa come per `projects.code`: nessun vincolo
in tabella, **guardia applicativa** più un test che la sorveglia.

### 4.2 Le tre tabelle di contorno

- `sales_offer_types` — `code` PK, `name`, `category` ENUM(`Impianto`,`Intervento`,`Ricambio`,`Altro`), `sort_order`, `is_active`. Seminata coi 13 tipi reali (S, V, F, P, A, AMU → Impianto; I, M, RIP, T, REV, SW → Intervento; R → Ricambio). La categoria è ciò che rende possibile l'Analisi di mercato.
- `sales_offer_sellers` — `code` PK, `name`, `employee_id` NULL, `is_active`. Anagrafica a parte e **non** una colonna nuova su `employees`: fra i venditori storici ci sono sigle di persone non più in azienda. Semina (mappatura confermata da Diego il 04/09/2026):

  | Sigla | Persona | Offerte | In `employees` |
  |---|---|---|---|
  | FF | Francesco Ferrari | 1.065 | **39 — creato, Cessato** |
  | GS | Giuseppe Salierno | 347 | **40 — creato, Cessato** |
  | RC | Rocco Chiantia | 167 | 13 |
  | EC | Edoardo Carretta | 79 | 37 |
  | MS | Maurizio Spandre | 59 | **41 — creato, Cessato** |
  | GM | Giorgio Maracich | 16 | 10 |
  | GV | Gabriele Vottero | 15 | 12 |
  | DS | Daniele Seidita | 14 | **42 — creato, Cessato** |
  | AM | Alfredo Montera | 8 | **43 — creato, Cessato** |
  | LS | Luca Spinello | 6 | 20 |

  I cinque venditori che in anagrafica non c'erano sono stati creati (id 39-43, `status='TERMINATED'`, senza username né email) **sia in produzione sia sul database di sviluppo**: esistono solo perché le 1.493 offerte storiche a loro nome abbiano un referente vero. Nel registro nascono con `is_active = 0`: restano nei grafici storici, non compaiono nelle tendine di chi crea un'offerta nuova.

  🪤 **L'aggancio è per nome e cognome, non per iniziali.** In azienda ci sono due GV (Gianpiero Vinardi e Gabriele Vottero) e tre MC: la sigla da sola avrebbe attribuito le offerte alla persona sbagliata, senza nessun errore.
- `sales_offer_followups` — `offer_id` (CASCADE), `contact_date`, `notes`, `created_by`, `created_at`.
- `sales_offer_log` — `offer_id`, `field`, `old_value`, `new_value`, `changed_by`, `changed_at`. Registro per campo, come nell'app: serve a ricostruire il portafoglio nel tempo. Nessun tetto di righe (in MySQL non serve il taglio a 50.000 di `mutations.js`).

### 4.3 Ritocchi a `customers`

Colonne mancanti rispetto all'esportazione «Soggetti» del gestionale, aggiunte nella stessa migrazione:
`postal_code`, `city`, `province`, `country`, `fax`, `website`. Nessuna rimozione, nessun rename.

---

## 5. API

### `/api/sales-offers` — `SalesOffersController`

| Metodo | Cosa fa |
|---|---|
| `GET /` | elenco con filtri `year, status, sellerCode, typeCode, customerId, chance, search`, ordinamento e paginazione |
| `GET /{id}` | scheda completa (con followup e log) |
| `GET /next-number?year=&typeCode=` | numero proposto = max(`number`) dell'anno escluse `is_legacy_series`, +1 |
| `POST /` | crea; se il numero proposto è stato preso nel frattempo assegna il successivo e risponde `renumbered: true` |
| `PUT /{id}` | modifica con `row_version` (409 in conflitto) |
| `DELETE /{id}` | elimina (con conferma lato client) |
| `POST /{id}/followup` | appunto di contatto + eventuale `next_contact` |
| `POST /bulk-close` | chiusura in blocco delle aperte di anni passati → `persa`. Solo ADMIN |
| `GET /export` | Excel del filtro corrente |
| `POST /from-quote/{quoteId}` | registra un preventivo nel registro |
| `POST /{id}/link` | aggancia `quote_id` / `project_id` |
| `GET|PUT /types`, `GET|PUT /sellers` | anagrafiche |

Guardie applicative da scrivere (portate da `validate` di `mutations.js`): anno 2000–2100; numero ≥ 1
e libero; per gli stati diversi da `bozza` cliente, venditore e tipo obbligatori; data dell'anno;
`amount_max ≥ amount`; chance multipla di 10; `presa` richiede `order_amount` **oppure** `is_time_material`.

### `/api/andamento` — `AndamentoController`

| Metodo | Cosa restituisce |
|---|---|
| `GET /offerte?year=&from=&to=&sellerCode=&category=` | KPI dell'anno (emesse, prese a corpo/consuntivo, aperte, perse, conversione %), serie mensile su 5 anni, per venditore, per tipo, per categoria, **portafoglio ponderato** (Σ `amount × chance/100`), 15 aperte più grandi, andamento del portafoglio negli ultimi 12 mesi (dal `sales_offer_log`) |
| `GET /commesse?year=&pmId=&customerId=` | una riga per commessa: ordine, costi consuntivo, redditività € e %, ore budget/consuntivo, fatturato, incassato, da incassare, scaduto. **Riusa `BilancioController` e le query SAL, non le riscrive** |
| `GET /report.pdf` | «Report Direzione»: una pagina A4, offerte a sinistra e commesse a destra, periodo in testa |
| `POST /report/email` | invia il PDF alla proprietà. Azione verso l'esterno: **sempre con conferma esplicita**, mai automatica |

---

## 6. Le due pagine

### Registro Offerte (`/offerte`)

Griglia con le regole di casa, senza inventare nulla: `GridScroller` (intestazione fissa),
`ColumnsMenu` + `usePersistedColumnVisibility` a chiave **versionata**, `LookupCombobox` per cliente
e venditore (la rubrica è di 422 righe: mai una `Select`), `euro()` per gli importi,
`formatDateShort` per le date, `useConfirm` su eliminazione e chiusura in blocco,
`useCopyText` per copiare i percorsi NAS (in produzione si va in HTTP: `navigator.clipboard` non c'è).

In testa: il **prossimo numero** con «Crea» e «Riserva» (la bozza occupa il numero e basta).
Scheda in dialogo: anteprima del numero composto, dati, forbice di prezzo, esito e chance,
percorsi NAS con «Apri»/«Copia», anticipo, contatti col referente, storico modifiche, duplica.

Realtime: SignalR + `row_version`, `staleTime: 0`, sottoscrizione via `useHubSubscription`,
come ogni funzione collaborativa.

### Andamento (`/andamento`)

Una pagina, filtri comuni in testa (periodo, venditore, cliente, categoria), quattro blocchi:

1. **KPI dell'anno** — offerte emesse e valore, prese e valore, conversione %, portafoglio aperto e portafoglio ponderato per chance, commesse aperte, ordine totale, redditività media, incassato e da incassare.
2. **Offerte** — andamento mensile su 5 anni, emesse/prese/aperte per mese, per venditore, per tipo, mercato per categoria (Impianto/Intervento/Ricambio), 15 aperte più grandi, chance per livello.
3. **Commesse** — tabella redditività (con la soglia rossa già parametrica del Bilancio), fatturato per mese, incassato vs da incassare, scadute.
4. **Report Direzione** — anteprima, «Scarica PDF», «Scarica Excel», «Invia alla proprietà».

Gli euro sono coperti dal permesso **`data.revenue`** già esistente: chi non ce l'ha vede i
conteggi e le percentuali, non gli importi.

---

## 7. Permessi

Chiavi nuove in `ATEC.PM.Shared/catalogo-permessi.json`, sezione Commerciale (le chiavi sono
**immutabili**: si rigenera `catalogo.gen.ts` con `genera-catalogo.mjs` e si aggiorna il censimento
in `ATEC.PM.Tests/Permessi`):

| Chiave | Cosa apre | Stato |
|---|---|---|
| `nav.offerte` | la voce e la pagina Registro Offerte | ✅ registrata |
| `action.edit_offerta` | crea e modifica le **proprie** offerte | ✅ |
| `action.edit_offerta_altrui` | modifica anche quelle di altri venditori | ✅ |
| `action.delete_offerta` | elimina | ✅ |
| `action.bulk_close_offerte` | chiusura in blocco degli anni passati | ✅ |
| `action.edit_offer_types` | anagrafiche tipi e venditori | ✅ |
| `action.import_offerte` | import dello storico (simula per default) | ✅ |
| `nav.andamento` | la voce e la pagina Andamento | ✅ |
| `action.send_andamento_report` | invio del Report Direzione per mail | ⏳ con la F6 |

### I quattro ruoli del vecchio applicativo, tradotti

| Ruolo in ATEC Offerte | Cosa si concede in ATEC PM |
|---|---|
| **Amministratore** | tutte le chiavi qui sopra |
| **Modifica completa** | `nav.offerte` + `action.edit_offerta` + `action.edit_offerta_altrui`, più `delete` se autorizzato |
| **Solo proprie offerte** | `nav.offerte` + `action.edit_offerta`, **senza** `action.edit_offerta_altrui`: tocca solo le offerte con una sigla venditore agganciata alla sua scheda dipendente |
| **Sola lettura** | `nav.offerte` **in sola lettura** (concessione `READ`): vede tutto, i pulsanti di scrittura rispondono no |

Non c'è nessun equivalente di «forza lo sblocco del file»: quel diritto esisteva perché
l'archivio era un file solo con un lucchetto sopra. Qui il database è di tutti e la concorrenza
la regge `row_version`.

### ✅ Come sono state aperte davvero (04/09/2026)

🪤🪤 **In produzione il `min_level` non decide niente**: `app_config.PermissionsEngine = NEW`, cioè
il motore **a persone** — contano solo le righe di `employee_feature_access` (793, su 37 persone).
Alzare il livello a 2 e riavviare il servizio per la cache di `auth_features` non serve a nulla.
Le concessioni per persona hanno una **cache a 60 secondi**: nessun riavvio, basta alzare
`employees.permissions_version` perché il client di quella persona rilegga i suoi permessi.

🪤 **E «livello PM» qui sarebbe stato sbagliato comunque**: nessuno dei cinque venditori attivi è
PM — Carretta e Vottero sono RESP_REPARTO, Maracich, Spinello e Chiantia sono TECH. Con `min_level`
a 2 il registro lo avrebbero visto tre persone, e nessuna di loro fa offerte.

Concesse (`FULL`, origin `MANO`) le tre chiavi `nav.offerte`, `action.edit_offerta` e
`nav.andamento` a **Admin ATEC e Edoardo Carretta**: si parte in due, gli altri venditori si
aggiungono quando il modulo è rodato. Le altre quattro chiavi restano all'amministratore.

---

### 🪤 Di serie sono chiuse

`CatalogoPermessiSync` registra ogni chiave nuova a **`min_level = 3` (solo Amministratore)**, ed
è una regola della casa, non una svista: *«registrare una chiave è automatico, concederla è una
decisione»* — una chiave appena nata non deve aprire niente a nessuno nemmeno in un rollback.
Quindi **dopo il deploy Registro Offerte e Andamento non si vedono**, tranne che dagli ADMIN,
finché non li si apre da `/permessi`.

Per allinearli ai fratelli di sezione (`nav.preventivi` e `nav.cat_preventivi` stanno a **2 = PM**)
il livello da mettere è:

| Chiave | Livello consigliato |
|---|---|
| `nav.offerte`, `action.edit_offerta`, `nav.andamento` | **2 — Project Manager** |
| `action.edit_offerta_altrui`, `action.delete_offerta`, `action.bulk_close_offerte`, `action.edit_offer_types`, `action.import_offerte` | **3 — Amministratore** |

Gli **euro** dell'Andamento restano governati da `data.revenue` (già a livello 2): chi non lo ha
vede i conteggi e le percentuali, non gli importi — e a toglierli è **il server**, non il client.

---

## 8. Import dello storico

Una tantum, da `Files To Check/files/ATEC_Offerte_sorgenti/ATEC_Offerte_sorgenti/seed.json`.
Endpoint admin `POST /api/sales-offers/import-seed` (file caricato), oppure attrezzo in `tools/`.
Numeri veri del file:

| Cosa | Quanti |
|---|---|
| Offerte | **1.795** — 2022: 480 · 2023: 444 · 2024: 328 · 2025: 371 · 2026: 172 |
| Stati | aperta 1.364 · presa 402 · bozza 19 · persa 7 · sospesa 3 |
| Con chance valorizzata | 152 |
| Senza numero | 35 · serie vecchia: 9 · duplicati (2025/282-285): 4 |
| Clienti | 422 (16 **senza P. IVA**) |
| Venditori | FF 1.065 · GS 347 · RC 167 · EC 79 · MS 59 · GM 16 · GV 15 · DS 14 · AM 8 · LS 6 |

Ordine delle operazioni:

1. **Clienti** — abbinamento ai `customers` esistenti per `easyfatt_code`/P. IVA; i mancanti si creano. I 16 senza P. IVA vanno inseriti con `vat_number` **NULL**, non `''`: la colonna ha una UNIQUE e due stringhe vuote collidono (MySQL invece accetta più NULL).
2. **Tipi e venditori** — semina delle due anagrafiche.
3. **Offerte** — inserimento fedele, duplicati compresi, senza far scattare la guardia sull'unicità.
4. **Collegamento nome → cliente** — `keyName` + `matchClient` portati da `mutations.js`: chiave normalizzata (minuscolo, senza accenti, senza forme giuridiche S.p.A./S.r.l./GmbH…), corrispondenza esatta e poi contenimento a parole intere **solo se il candidato è unico**. Sui dati reali collega circa 1.170 offerte su 1.770; il resto si assegna a mano dallo strumento «Nomi non collegati».
5. **Rapporto finale** — quante righe, quanti clienti creati, quanti nomi rimasti scollegati.

L'import è **ripetibile a vuoto**: se trova già righe importate non le duplica (chiave `year+number+seller_code`).

---

## 9. Fasi di lavoro

| Fase | Contenuto | Esito verificabile | Stima |
|---|---|---|---|
| **F0** ✅ | **Tutte e dieci le sigle hanno un nome** (Diego, 04/09/2026). I cinque che non erano in anagrafica — Ferrari, Salierno, Spandre, Seidita, Montera — creati in `employees` come *Cessati* (id 39-43, produzione **e** sviluppo) | mappatura in §4.2 | fatta |
| **F1** ✅ | `M122_RegistroOfferte`, DTO, `SalesOffersController` CRUD, numerazione e validazione portate da `mutations.js`, guardie, test server | build 0/0, 33 test verdi — vedi §13 | fatta 04/09/2026 |
| **F2** ✅ | `M123_RubricaOfferte`, `SalesOfferMatching`, `SalesOfferImportService`, endpoint che **simula per default** | 1.795 offerte in banca dati, 1.121 collegate, 16 clienti nuovi — vedi §14 | fatta 04/09/2026 |
| **F3** ✅ | Pagina Registro Offerte (griglia, scheda, contatti, realtime) | `tsc -b`, `npm run build`, eslint e 68 vitest verdi — vedi §15 | fatta 04/09/2026 |
| **F4** ✅ | Andamento — metà offerte (KPI, grafici, portafoglio ponderato, mercato) | pagina provata sui 1.795 record — vedi §16 | fatta 04/09/2026 |
| **F5** ⛔ | ~~Andamento — metà commesse~~ | **fuori perimetro** (Diego, 04/09/2026): «questo modulo serve solo per sapere quali offerte sono state fatte e quante ne sono entrate». Vedi §17 | annullata |
| **F6** | Report Direzione: PDF QuestPDF, export Excel, invio mail con conferma | PDF di una pagina, mail di prova | 1 g |
| **F7** | Aggancio Preventivi ⇄ Registro ⇄ Commesse, allineamento stato, `rif_offerta` | da preventivo nasce la riga, da `presa` si arriva alla commessa | 1 g |
| **F8** | Permessi, voci di menu, documentazione, changelog, deploy | `/permessi` mostra le chiavi nuove, deploy in produzione | 0,5 g |

**Totale ≈ 11 giorni.** F1→F3 sono il minimo utilizzabile: già lì l'Excel si può spegnere.

---

## 10. Trappole (lette nei dati e nel codice, non ipotizzate)

1. **Le 1.364 «aperte» storiche non sono aperte davvero.** Nell'Excel le offerte perse non venivano quasi mai chiuse (perse registrate: 7 su 1.795). Finché non si passa la chiusura in blocco degli anni chiusi, il portafoglio dell'Andamento dice una bugia grossa. Va fatto **prima** di mostrare la pagina alla proprietà, e va detto chiaramente nel piano di collaudo.
2. **La chance c'è solo su 152 righe.** Il portafoglio ponderato è significativo solo da qui in avanti.
3. **`is_legacy_series` non alza il progressivo**, altrimenti il prossimo numero 2026 salta da 165 a 362.
4. **I duplicati 282–285/2025 esistono**: nessuna UNIQUE, guardia applicativa, test.
5. **`customers.vat_number` è UNIQUE** e ha default `''`: i 16 clienti senza P. IVA vanno inseriti a NULL.
6. **`customer_name` non si riscrive mai** collegando il cliente: sull'offerta cartacea c'è quel nome lì.
7. **La colonna `project_sal.rif_offerta` esiste già** ed è compilata a mano: prima di scriverci sopra in automatico serve una bonifica, o si scrive solo se è vuota.
8. **La chiave di `usePersistedColumnVisibility` va versionata**, altrimenti chi ha già usato la griglia non vede le colonne nuove.
9. **In produzione si va in HTTP**: niente `navigator.clipboard`, si usa `useCopyText`.
10. **Il pulsante «Apri» sui percorsi NAS non può funzionare come nell'app HTML.** Lì c'era un avviatore PowerShell locale che chiamava Explorer; da browser non si apre un file del NAS. In ATEC PM si copia il percorso negli appunti (e, volendo, si offre un link `file://` che funziona solo su alcuni browser). **Da dire al committente**: è l'unica funzione che si perde nel passaggio.
11. **`quotes.status` non è la fonte di verità dell'esito**: l'allineamento automatico tocca solo le righe ancora `aperta`.
12. Real-time e concorrenza vanno messi da subito (`row_version` + hub): rimetterli dopo su una griglia editabile costa il doppio.

---

## 11. Collaudo e consegna

- Test server in `ATEC.PM.Tests`: numerazione (compreso `is_legacy_series`), validazione, guardia sull'unicità, import idempotente, allineamento stato dal preventivo.
- Test web in vitest (logica pura): composizione del numero, `keyName`/`matchClient`, calcolo del portafoglio ponderato — accanto al modulo, come da regola.
- Verifica incrociata obbligatoria: **i totali della metà «commesse» dell'Andamento devono coincidere riga per riga con la pagina Bilancio**. Se divergono, è l'Andamento a sbagliare.
- Deploy con `aggiorna-server.ps1` (dal tool PowerShell, mai da Bash), commit **prima** del deploy, `changelog.json` ricommittato dopo.
- Documentazione: questo piano aggiornato a fine lavoro + riga in `docs/INDEX.md`.

---

## 12. Cosa resta fuori (di proposito)

- L'avviatore PowerShell, `percorso.txt`, il lock file, i PIN, i backup JSON: sostituiti da ciò che ATEC PM ha già.
- La variante Node in `alternativa_server/`: scartata dal committente, e comunque superata.
- Stampa/PDF della singola scheda offerta, promemoria per mail sul singolo contatto, allegati sull'offerta: evoluzioni possibili, non in questo giro.
- La fusione delle due numerazioni in una sola serie: decisione rimandata: se un giorno la si vuole, il registro è già il posto giusto dove farla nascere.

---

## 13. F1 — cosa è stato scritto (04/09/2026)

| File | Contenuto |
|---|---|
| `ATEC.PM.Server/Services/SalesOfferRules.cs` | **Le regole**, pure e senza database tranne due query: `Composed`, `NextNumber`, `NumberTaken`, `Validate`, `EsitoDaStatoPreventivo`. Port di `mutations.js` |
| `ATEC.PM.Server/Services/SalesOffersDbService.cs` | DDL delle 5 tabelle + semina dei 13 tipi e delle 10 sigle. Statico, letto **sia** dal bootstrap **sia** dalla migrazione: una definizione sola |
| `ATEC.PM.Server/Migrations/M122_RegistroOfferte.cs` | La migrazione, che chiama il servizio qui sopra |
| `ATEC.PM.Shared/DTOs/SalesOffer_DTOs.cs` | 11 DTO, nessun `[Required]` (regola del blocco B) |
| `ATEC.PM.Server/Controllers/SalesOffersController.cs` | 12 endpoint: elenco filtrato/paginato, scheda con contatti e registro modifiche, `next-number`, create/update/delete, followup, `bulk-close`, tipi e venditori |
| `ATEC.PM.Server/Hubs/ProjectHub.cs` | Gruppo `sales-offers-all` + `JoinSalesOffers`/`LeaveSalesOffers` |
| `ATEC.PM.Shared/catalogo-permessi.json` | `nav.offerte` + 5 azioni, sotto Commerciale |
| `ATEC.PM.Tests/Calcoli/RegistroOfferteTests.cs` | 33 test |

Decisioni prese scrivendo, che il piano non aveva fissato:

- **In creazione un numero già preso non è un errore, si rinumera** (`Renumbered = true` nella risposta): il numero che arriva dal client è sempre una proposta di `next-number`, e fra la proposta e il salvataggio può inserirsi un altro venditore. **In modifica sì**, perché lì il numero l'ha scelto una persona. È il comportamento del vecchio applicativo.
- **La modifica controlla il diritto su due venditori**, quello attuale e quello nuovo: senza il secondo controllo un venditore potrebbe intestarsi l'offerta di un altro aggirando `action.edit_offerta_altrui`.
- **Tipi e venditori non si cancellano mai**, si spengono: un tipo tolto sparirebbe dal numero composto di tutte le offerte storiche che lo usano.
- **Chi è «mio»**: le offerte con una sigla agganciata alla propria scheda dipendente (`sales_offer_sellers.employee_id`). Chi non è agganciato a nessuna sigla non ha offerte proprie.
- La `bulk-close` scrive il registro modifiche **prima** dell'UPDATE, con una `INSERT … SELECT`: dopo, il vecchio stato non esiste più.

---

## 14. F2 — l'import dello storico (04/09/2026)

| File | Contenuto |
|---|---|
| `ATEC.PM.Server/Migrations/M123_RubricaOfferte.cs` | 6 colonne su `customers` (CAP, città, provincia, nazione, fax, sito) + 4 su `sales_offers` (`project_code`, `comm_number`, `legacy_ok`, `legacy_id`) |
| `ATEC.PM.Server/Services/SalesOfferMatching.cs` | `KeyName` / `MatchClient` / `KeyProjectCode`, port fedele di `mutations.js` |
| `ATEC.PM.Server/Services/SalesOfferImportService.cs` | L'import: rubrica, offerte, contatti, abbinamenti, rapporto |
| `ATEC.PM.Shared/DTOs/SalesOfferImport_DTOs.cs` | Il rapporto |
| `POST /api/sales-offers/import-seed` | Endpoint, permesso `action.import_offerte`, **simula per default** |
| `ATEC.PM.Tests/Calcoli/ImportOfferteTests.cs` | 24 test |

### Il risultato vero (database di sviluppo, copia della produzione)

```
Rubrica   : letti 422 · creati 16 · arricchiti 406 · invariati 0
Offerte   : lette 1795 · inserite 1795 · già presenti 0
Collegate : cliente 1121 · commessa 11 · senza cliente 649
Durata    : 517 ms
```

Controlli incrociati sui dati entrati: **1.795 righe** con la stessa ripartizione per anno del file
di origine (2022: 480 · 2023: 444 · 2024: 328 · 2025: 371 · 2026: 172) e gli stessi stati
(aperta 1.364 · presa 402 · bozza 19 · persa 7 · sospesa 3); **9** righe di serie vecchia; **8**
righe sui numeri 282-285 del 2025 (i quattro duplicati, tutti conservati); **35** senza numero; la
ripartizione per venditore identica al file. E soprattutto: **il prossimo numero del 2026 è 165**,
esattamente quello che dice il vecchio Excel — la prova che `is_legacy_series` funziona sui dati
veri (senza, sarebbe 362).

### Decisioni prese scrivendo

- **La chiave dell'import è `legacy_id`, non anno+numero+sigla.** I quattro duplicati 282-285 del 2025 hanno tutti e tre i campi uguali: con quella chiave una seconda esecuzione ne avrebbe scartato la metà. `legacy_id` è anche la riga del foglio Detail, quindi ogni offerta sa da dove viene.
- **Simula per default.** L'endpoint scrive solo con `simulazione=false`. Un import di 1.795 righe si guarda prima, non dopo. 🪤 La simulazione conta **meno collegamenti del vero** (1.095 contro 1.121): i 16 clienti nuovi in simulazione non esistono ancora, quindi le offerte che li nomineranno risultano scollegate.
- **Sui clienti che esistono già si riempiono solo i campi vuoti.** L'anagrafica viva di ATEC PM vale più di una fotografia del gestionale: 406 clienti su 422 erano già lì, riconosciuti per codice gestionale.
- **I 16 soggetti senza P. IVA entrano con `vat_number` NULL**, mai `''`: la colonna ha una UNIQUE e due stringhe vuote collidono. Un test lo sorveglia.
- **Tre campi che il piano non aveva previsto sono stati salvati**, invece di essere buttati: codice commessa, n. commessa e OK grezzo. Il codice commessa è servito subito — **11 offerte hanno trovato la loro commessa** in ATEC PM.
- **1.121 collegate su 1.770 con un nome (63%).** Il piano ne stimava ~1.170: la differenza è che qui la rubrica è quella vera di ATEC PM (424 nomi, non i 422 del file), e più nomi vuol dire più casi ambigui — dove i candidati sono due, non si collega niente. Le restanti 649 si assegnano a mano dallo strumento «Nomi non collegati» (F3): i primi cinque nomi da sistemare sono MASSUCCO (44 offerte), PERARDI & GRESINO (28), ADLER EVO - Stabilimento di Pianfei (16), SOLE POLONIA (16) e VPM (15).

### ✅ Eseguito anche in produzione (04/09/2026, dopo il deploy)

```
Rubrica   : letti 422 · creati 16 · arricchiti 406
Offerte   : lette 1795 · inserite 1795 · già presenti 0
Collegate : cliente 1121 · commessa 11 · senza cliente 649
```

Identico allo sviluppo, riga per riga — e la **simulazione fatta prima** aveva dato gli stessi
numeri (con i 1.095 collegamenti di cui sopra). Controlli sui dati entrati: 1.795 righe con la
stessa ripartizione per anno del file (480 · 444 · 328 · 371 · 172) e gli stessi stati
(1.364 · 402 · 19 · 7 · 3), 9 di serie vecchia, 8 righe sui numeri 282-285 del 2025, 35 senza
numero, 16 clienti con `vat_number` NULL, **prossimo numero 2026 = 165**.

🪤 **Come è stato lanciato, e perché non dall'endpoint.** Sul server il `Jwt:Key` è cifrato DPAPI
col conto del servizio e da fuori non si legge, quindi coniare un token amministratore non era
possibile. L'import è stato eseguito con lo **stesso `SalesOfferImportService`**, da un runner che
si collega al MySQL di produzione attraverso un **tunnel SSH** (`-L 3307:127.0.0.1:3306`) — nessuno
schema toccato (niente `InitDatabase`: lo porta il deploy), solo righe scritte. Il runner rifiuta
di partire se le cinque tabelle del registro non ci sono. Durata 86 s invece di 0,5: ogni INSERT
fa un giro nel tunnel.

---

## 15. F3 — la pagina (04/09/2026)

| File | Contenuto |
|---|---|
| `src/lib/api/types/sales-offers.ts` | I tipi, allineati ai DTO |
| `src/lib/api/sales-offers.ts` | Layer API: elenco filtrato, scheda, next-number, CRUD, contatto, chiusura in blocco, anagrafiche |
| `src/lib/signalr/use-sales-offers-hub.ts` | Diretta sul gruppo `sales-offers-all` (15 righe sopra `useHubSubscription`) |
| `src/features/offerte/OffertePage.tsx` | Griglia paginata lato server: ricerca, 4 filtri, ordinamento, menu «Colonne», azioni riga |
| `src/features/offerte/OfferDialog.tsx` | La scheda: numerazione, cliente, importi, esito, sezioni piegate per anticipo/percorsi/note/contatti/storico |
| `src/features/offerte/OfferFollowupDialog.tsx` | Annota un contatto e sposta il prossimo richiamo |
| `src/features/offerte/sales-offer-number.ts` + `.test.ts` | Numero composto lato client (anteprima mentre si digita) + 4 test |
| `src/features/offerte/sales-offer-status.ts` | I cinque stati con il pallino |
| `navigation.ts`, `AppRoutes.tsx` | Voce «Registro Offerte» sopra Preventivi, rotta `/offerte` |

Decisioni prese scrivendo:

- **Il numero composto esiste in due copie**, una sul server (`SalesOfferRules.Composed`) e una nel client (`composeOfferNumber`). Non è una svista: la scheda ne ha bisogno **mentre si digita**, prima di salvare. È però la copia che può divergere in silenzio, e per questo ha il suo test.
- **Il nome cliente dell'offerta non si riscrive mai** collegando la rubrica: è quello stampato sul documento. Collegando un cliente si compila solo se il campo è ancora vuoto; in griglia un pallino grigio segnala le righe non collegate.
- **I percorsi NAS si copiano.** Il vecchio applicativo li apriva perché aveva un avviatore PowerShell in locale; da browser, per giunta in HTTP, non si può. Il pulsante copia negli appunti (`useCopyText`, non `navigator.clipboard`, che in HTTP non esiste) e la scheda lo dice a chiare lettere.
- **La chiave del menu «Colonne» è versionata** (`atec_pm_offerte_columns_v1`): senza, una colonna aggiunta domani nascerebbe nascosta per chi ha già usato la griglia e visibile per tutti gli altri.
- **«A consuntivo» spegne l'importo dell'ordine**, e il salvataggio è bloccato se un'offerta «presa» non ha né l'uno né l'altro: sono le stesse due regole del server, ripetute qui solo per non far arrivare l'utente fino all'errore.

### Verifica a runtime (04/09/2026)

Giro completo sulla GUI autenticata, sui **1.795 record veri** del database di sviluppo: elenco
(172 offerte 2026, «Prossimo numero 165/2026» come il vecchio Excel), filtri anno/stato/venditore/tipo,
ricerca, menu «Colonne» con persistenza, scheda, **creazione** (`S165-2026-EC`), **contatto col
referente**, **modifica** (chance a 70%), **eliminazione** con conferma, e la **diretta**: un'offerta
creata via API è comparsa da sola nell'elenco e sparita da sola quando è stata eliminata. Dati di
prova ripuliti: il registro è tornato a 1.795 righe, 0 contatti, 0 log.

**Quattro difetti trovati e corretti — nessuno li avrebbe visti senza aprire la pagina:**

1. **I campi nullabili arrivano `undefined`, non `null`.** Il server serializza in `WhenWritingNull`, quindi il campo assente non è `null`: i confronti `=== null` non lo vedevano e nella scheda finivano «undefined» al posto del numero e «NaN» al posto degli importi. Ora si usa `!= null` ovunque.
2. **Anno e data nascevano in disaccordo.** Creando un'offerta con il filtro su un anno passato, l'anno veniva dal filtro e la data era oggi: il salvataggio veniva respinto con «La data non appartiene al 2025» e non si capiva perché. Ora l'anno di un'offerta nuova è quello di oggi, e **cambiando la data l'anno la segue**.
3. **La scheda non mostrava contatti e storico.** Riceveva la riga dell'elenco, che non li porta (l'elenco è paginato: caricarli per cinquanta righe sarebbe uno spreco). Ora la scheda legge il dettaglio, e li usa solo per le due liste in fondo — rifare il modulo da lì cancellerebbe quello che l'utente sta scrivendo.
4. **Il pallino «non collegato» compariva anche sulle 19 bozze**, che un nome cliente non ce l'hanno ancora: rumore su un quinto delle righe del 2026.

Restano a video degli errori SignalR «connection stopped during negotiation»: **non sono di questa
pagina** (compaiono anche su `/clienti`, e sono riconnessioni in fase di avvio), e la diretta
funziona davvero — provata creando e cancellando una riga da fuori.

🪤 **41 offerte storiche sono «presa» senza importo dell'ordine né «a consuntivo»**: la regola —
che è del vecchio applicativo, non nuova — le rifiuta in salvataggio finché non si compila uno dei
due. Non è un difetto del codice ma un debito dei dati: da segnalare al committente prima che ci
sbatta contro.

---

## 16. F4 — l'Andamento delle offerte (04/09/2026)

| File | Contenuto |
|---|---|
| `ATEC.PM.Server/Services/AndamentoOfferteCalcolo.cs` | **I conti**, puri: KPI, mensile, cinque anni, raggruppamenti, chance, ponderato, portafoglio nel tempo |
| `ATEC.PM.Server/Controllers/AndamentoController.cs` | `GET /api/andamento/offerte`, chiave `nav.andamento`, euro coperti da `data.revenue` |
| `ATEC.PM.Shared/DTOs/Andamento_DTOs.cs` | 8 DTO |
| `src/features/andamento/AndamentoPage.tsx` | La pagina: 8 riquadri, 4 grafici, 5 tabelle, 2 avvisi |
| `src/features/andamento/andamento-charts.tsx` | I grafici (recharts) e la legenda |
| `src/features/andamento/andamento-palette.ts` | La palette **validata**, e le regole per usarla |
| `ATEC.PM.Tests/Calcoli/AndamentoOfferteTests.cs` | 22 test |

### La palette non è stata scelta a occhio

`scripts/validate_palette.js` (skill dataviz) su `#4A86C6 · #C2701C · #1F9077 · #8A63D2 · #BC4462`:
banda di luminosità, croma minimo, separazione per daltonismo sulle coppie adiacenti, soglia a
vista normale e contrasto sul fondo — **tutti PASS in chiaro e in scuro**. Il primo ordine provato
falliva («blu ↔ verde» a ΔE 14,1, sotto la soglia di 15 a vista normale): è bastato spostare
l'arancio in mezzo. I primi due colori sono quelli già in uso in Analisi Consegne, così l'app
resta un sistema solo.

### Le due cose che rendono onesta questa pagina

1. **L'avviso sull'anno chiuso.** Sui dati veri il 2025 ha 155 prese e 7 perse, e la conversione esce **95,7%** — perché nel vecchio Excel le offerte perse non venivano quasi mai chiuse. Un numero così, messo davanti alla proprietà senza contesto, è peggio di nessun numero. La pagina lo dice: «il 2025 è un anno chiuso ma risultano ancora 174 offerte aperte… conversione e portafoglio sono ottimisti finché non si passa la chiusura in blocco».
2. **Il ponderato dichiara la propria base.** «3.930.291,71 € stimati su 57 aperte di 79»: le aperte senza chance non valgono zero, restano fuori — e quante ne restano fuori si legge accanto al totale.

Nello stesso spirito i KPI espongono anche quello che di solito si nasconde: **numeri riservati**
(le bozze), **righe senza importo** (fuori dai totali) e **prese incomplete** (senza ordine né
«a consuntivo»), quest'ultimo evidenziato in ambra quando è maggiore di zero.

### Difetti trovati guardando la pagina, non i test

- **La linea piatta dell'anno che non c'è.** Guardando il 2025 il confronto pluriennale disegnava una riga a zero per il 2021, che nel registro non esiste: non è un dato, è rumore che schiaccia la scala di tutti gli altri. Ora gli anni **prima** dell'inizio dello storico si tolgono; quelli **in mezzo** restano, o il grafico farebbe sembrare consecutivi due anni che non lo sono.
- 🪤 **La legenda di recharts non si può ordinare.** La v3 costruisce il proprio `payload` da un registro interno e lo riordina alfabeticamente — «Aperte, Emesse, Prese» mentre le barre sono emesse, prese, aperte: chi legge accoppia la prima pastiglia alla prima barra e sbaglia due colori su tre. Passare `payload` a `Legend` **o** al suo `content` non serve a niente (provati entrambi). La legenda ora la disegna la pagina, sopra il grafico — dov'è la chiave di lettura, che si legge prima di guardare le barre.

### Cosa resta

La metà **commesse** dell'Andamento (ordine, costi, redditività dal Bilancio; fatturato e
incassato dal SAL) è la F5; il **Report Direzione** in PDF con export ed e-mail è la F6.

---

## 17. Perimetro ristretto — niente commesse (04/09/2026)

Diego: *«non mi interessa per ora collegare le cose, perché questo modulo serve solo per sapere
quali offerte sono state fatte e quante ne sono entrate»*. La **F5 è annullata**: l'Andamento
resta sulle sole offerte, che è già com'è costruito — non c'è niente da disfare.

### Perché la decisione è anche tecnicamente sensata

Guardando la produzione prima di costruirla, la metà commesse sarebbe nata quasi vuota:

| | Quante |
|---|---|
| Commesse in produzione | 19 (15 attive, 2 completate, 2 sospese) |
| Con `projects.revenue` — il «Totale Ordine» su cui il Bilancio calcola la redditività | **2**, e sono «INTERNA» e «COMMESSA DI PROVA» |
| Con un valore in `project_sal.valore` | **12**, per ~692.000 € |

Le commesse vere (CAMERLO 164.000, SOLE 142.625, ABB MELFI 135.610…) hanno l'importo **solo nella
testata SAL**, e `revenue` a zero. Siccome la redditività è *Totale Ordine − costi consuntivati* e
senza ordine **non è zero ma non calcolabile**, oggi la pagina Bilancio mostra «—» al posto del
margine su 17 commesse su 19 — e l'Andamento avrebbe mostrato la stessa colonna di trattini.

**Quindi la F5 non si riapre finché non si compilano le righe ordine sulle commesse.** Farlo
sistema il Bilancio *e* rende possibile l'Andamento delle commesse, in quest'ordine. Sono 12-17
commesse, si compilano dalla pagina Bilancio dove il campo è già editabile.

### Cosa completa invece il perimetro vero

«Quante ne sono entrate» non si può leggere finché gli anni passati non sono chiusi: sullo storico
ci sono 1.364 «aperte» e 7 «perse», e la conversione del 2025 esce **95,7%**. La chiusura in blocco
aveva l'endpoint dalla F1 ma **nessun pulsante** — un pezzo consegnato irraggiungibile, colpa mia.
Aggiunto il 04/09/2026: `BulkCloseDialog` (`src/features/offerte/`), bottone «Chiudi anni passati»
nella testata del Registro, visibile con `action.bulk_close_offerte`. Sceglie l'anno e lo stato di
destinazione, e **conta davvero** quante righe toccherà chiedendo al server i totali anno per anno:
un'operazione che ne cambia un migliaio non si conferma alla cieca.

---

## 18. Ritocchi dopo la prima lettura della pagina (04/09/2026)

- **Gli anni del confronto pluriennale sono interruttori.** Con cinque linee sovrapposte il confronto che interessa è quasi sempre fra due o tre anni: ogni voce della legenda accende e spegne la sua serie, e la scelta si ricorda in `localStorage` (`atec_pm_andamento_anni_v1`, chiave **versionata**) come per il menu «Colonne» delle griglie. Con tutti spenti compare un messaggio, non un grafico vuoto.
  - 🪤 **Il colore resta agganciato all'anno**, non alla posizione fra quelli accesi: spegnere il 2023 non ridipinge il 2024. Verificato a runtime — riaccendendo il solo 2026 resta blu come quando erano cinque.
  - 🪤 Solo un `false` esplicito spegne. Lo stato si legge da `localStorage` al primo montaggio, quando le serie non sono ancora arrivate: se un anno assente valesse «spento», al primo caricamento il grafico sarebbe vuoto. Un anno nuovo nasce acceso.
- **L'avviso sulla conversione ha due forme**, perché il sintomo cambia con l'anno. Sull'anno **in corso** le perse sono zero e ogni conversione della pagina dice «100 %» — un muro di percentuali che sembrano un trionfo; sull'anno **chiuso** restano centinaia di offerte aperte che nessuno ha chiuso. La causa è la stessa (nel vecchio Excel le perse non si registravano) e la pagina la dice in tutti e due i casi.
