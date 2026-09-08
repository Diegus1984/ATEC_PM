# EcosAgile API — manuale operativo di ATEC PM

> **Punto d'ingresso per qualunque lavoro che tocchi Ecos («eTime»).** Scritto il 08/09/2026
> studiando la cartella `ECOS/` data da Diego (guida ufficiale v4.1.1, «Source Samples»,
> progetto C# `EcosApiGetData`, collezione Postman) e confrontandola con il nostro client
> `ATEC.PM.Server/Services/Hr/EcosClient.cs`. Deve bastare **da solo**: non serve riaprire i
> PDF per implementare una chiamata nuova. Le cose scritte qui sono di tre tipi, e la
> differenza conta:
>
> - **📘 dalla guida** — documentato da SoftAgile, vale in generale;
> - **✅ verificato sul nostro tenant** — provato davvero (27/08, 29/08/2026), con l'esito;
> - **❓ non verificato** — da provare con la sonda (§12) prima di scriverci sopra del codice.
>
> Piano e stato del modulo HR: [../piani/PIANO-HR-PRESENZE.md](../piani/PIANO-HR-PRESENZE.md).

---

## 0. In una pagina

```
POST https://ha.ecosagile.com/dd/api.pm?ApiName=<Nome>&AuthToken=<token>&DF=1[&PageNumber=N&RowsPerPage=N]
Content-Type: application/x-www-form-urlencoded
body (GET*):   Campo=<operatore><valore>      es. UpdateDate=>='2026-08-01 00:00:00'   EmplID==18
body (Post*):  Campo=valore                    es. StampID=9504&StampDateTime=2026-09-01 08:00:00
```

- **Un solo endpoint**, l'operazione è `ApiName`. Sempre **POST**, sempre **form-url-encoded**
  (JSON nel body = filtri ignorati, `TokenGet` → `-19`).
- **Token**: `TokenGet` con `Userid`, `Password`, `ClientID` (maiuscole esatte) → `AuthToken`;
  scade dopo **~60 s di inattività** (ogni chiamata riuscita lo rinnova).
- **Filtri nel BODY, mai in query string** (là vengono ignorati e torna tutta la tabella).
  Stringhe e date fra apici singoli, numeri senza apici. Più filtri = AND.
- **Risposta**: `ECOSAGILE_TABLE_DATA.ECOSAGILE_DATA.ECOSAGILE_DATA_ROW` (array, **oggetto
  singolo** se una riga sola, stringa vuota/assente se zero) + `ECOSAGILE_ERROR_MESSAGE`
  con `CODE` (`OK`/`FAIL`), `ERROR_CODE`, `MESSAGE`, `RECORDCOUNT`, `LASTPAGE`, `MAXRECORD`.
  **Gli errori arrivano con HTTP 200**: si guarda `CODE`, non lo status.
- **Paginazione**: `PageNumber` da 1, `RowsPerPage` ≤ `MAXRECORD` (tipicamente 1000);
  ci si ferma su `LASTPAGE=true` o pagina vuota; tetto duro alle pagine.
- **Date** con `DF=1` → `yyyy-MM-dd HH:mm:ss`; **decimali con la virgola** (`"32,12"`);
  booleani ora `"TRUE"` ora `true`.
- **Scrittura** = `<Entità>Post` (mai `Ins`/`Upd`/`Save`): insert di default, **`Edit=true`**
  in query string + chiave nel body = update parziale; `ReturnAllPostedRecord=1` per avere
  il record intero. **Gli insert non sono idempotenti**: chiave restituita da salvare,
  mai ritentare alla cieca.
- **Cancellazione logica**: i record cancellati restano visibili con `Delete=1`.
- **Credenziali nostre**: `res_settings` chiavi `ecos.*` (password cifrata), ripiego
  `appsettings` sezione `Ecos`; si cambiano dalla pagina Timbrature → «Credenziali Ecos».

---

## 1. Le fonti e dove stanno

Cartella **`C:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ECOS\`** (fuori dal repository):

| File | Cos'è | Note |
|---|---|---|
| `EcosAgile_API_User_Guide_v4.1.1_EN.pdf` | **Guida ufficiale v4.1** (26/05/2026, 59 pagine, inglese) | Identica byte per byte a quella sul Desktop (md5 `6d634427…`). È **l'unica documentazione esistente**: online non c'è niente |
| `EcosAgile - API User guide -  Source Samples.pdf` | Vecchi esempi (app Ionic/Angular, 9 pagine) | Mostra parametri **non documentati** nella v4.1 (§3.8) e la cancellazione via `Delete=1` |
| `EcosAgile - EcosApiGetData/` (+ `.zip`) | Progetto C# di riferimento di SoftAgile (§17 della guida) | .NET Framework 4.7.2, `WebClient`, Newtonsoft 12, MySql.Data 8.0.30; sync incrementale API → tabella SQL Server/MySQL |
| `api.ecosagile.com_postman.json/` (+ `.zip`) | Collezione Postman | Tre richieste: `TokenGet`, `PeopleStampGetESS`, `CVPost` |

🪤 Il `App.config` del progetto C# contiene **due blocchi** di `<add key>`: in .NET l'ultima
chiave vince, quindi la configurazione effettiva è quella del server **interno di SoftAgile**
(`10.27.1.92/EcosProjDev`, ClientID 5). Non è roba nostra: il progetto va letto per il
**metodo**, non per i valori.

---

## 2. Il nostro tenant — fatti verificati

| Cosa | Valore | Fonte |
|---|---|---|
| Host / istanza | `https://ha.ecosagile.com/dd/api.pm` → istanza **`dd`** | ✅ in uso; la guida elenca `ha.ecosagile.com` fra gli host legittimi (§2.2): la URL di login definisce host e istanza |
| ClientID | **10305** | ✅ `res_settings` di produzione |
| Utente API | **`api.it`** — utente dedicato, salvato dalla pagina il 08/09/2026 («Prova collegamento» ok) | ✅ `res_settings` di produzione. Prima c'era `maria.carretta`, account personale che la guida vieta (§21.1); diritti di `api.it` misurati lo stesso giorno (§2.1) |
| Dove stanno le credenziali | `res_settings` chiavi `ecos.baseurl`, `ecos.userid`, `ecos.clientid`, `ecos.password` (cifrata con `ProtectedConfigHelper`/DPAPI a scope macchina, write-only); ripiego `appsettings.json` sezione `Ecos` (`BaseUrl`, `UserId`, `Password`, `ClientId`) | `EcosClient.ResolveCredenziali()` |
| Come si cambiano | Pagina `/hr/timbrature` → dialogo **«Credenziali Ecos»** (+ «Prova collegamento» = una `TokenGet`); endpoint `GET/POST /api/hr/ecos/settings`, `POST /api/hr/ecos/settings/test` | `HrController.cs:260-297` |
| Rilettura | a ogni uso: cambiare la password **non richiede riavvio** | |
| Import automatico | `HrSyncBackgroundService` ogni `Hr:ImportIntervalHours` (12), gate `Services:HrSync` | |
| Cursore incrementale | `app_config.hr_sync_punches_from` = massimo `UpdateDate` **secondo l'orologio di Ecos** (ripiego sul nostro con 1 h di margine) | `HrAttendanceService.cs:16` |
| Password in chiaro sparse | `TextFile1.txt` del vecchio programma «Timbrature» sul Desktop (password Ecos + SMTP). Non ruotate, scelta consapevole di Diego | [[modulo_hr_presenze]] §11 |

### 2.1 Cosa il nostro utente può e non può chiamare (✅ provato)

> Misurato con **`api.it`** il 08/09/2026 (calibrazione dal server con `tools/sonda_ecos_server.py`,
> §12). Dove l'esito differisce dal vecchio utente `maria.carretta` (27-29/08) è scritto.
> L'**import vero** con `api.it` è riuscito lo stesso giorno (log del server: sincronizzazione
> del mese alle 08:33, import automatici alle 08:48 e 09:11 — ogni riavvio del servizio ne fa
> partire uno).

| ApiName | Esito con `api.it` | Dettagli |
|---|---|---|
| `PeopleStampGetAll` | ✅ lettura | timbrature; filtri usati da noi: `UpdateDate>=`, `YearMonth='aaaamm'`. 🪤 **Ecos rimanda solo gli ultimi 60 giorni** (§5) |
| `PeopleBadgeGetAll` | ✅ lettura | anagrafica badge; 🪤 `UpdateDate` dei badge è vecchio (2019): chiedere dal 1900, non dal 2020 |
| `PeopleAbsenceRequestGetAll` | ✅ lettura | **34 campi**, ne leggiamo 14 (§6.2) |
| `PeopleOvertimeRequestGetAll` | ✅ lettura | **0 record nel 2026**: in ATEC gli straordinari non passano da lì |
| `PeopleStampPost` | ✅ **scrivibile** | a vuoto risponde `-11` di validazione («Column: `StampDateTimeTZOffSet`, Not an integer»): esiste e siamo autorizzati, niente creato |
| `Timesheet2GetAll`, `TimesheetGetAll` | ❌ `-2` | ServiceID **TimesheetAnalysis**, RightID 1 (uguale col vecchio utente) |
| `PeopleExpressLightGetAll` | ❌ `-2` | **esiste**; ServiceID **PeopleExpressLight**, RightID 1 |
| `PeopleAbsenceRequestPost` | ❌ `-2` | ServiceID **PeopleAbsenceRequestMSS**, RightID 4 (col vecchio utente il messaggio citava `request`) |
| `PeopleOvertimeRequestPost` | ❌ `-2` | ServiceID **PeopleOvertimeApproveMSS**, RightID 4 |
| `PeoplePost` | ❌ `-2` | ServiceID **JobData**, RightID 2 — col vecchio utente era permessa; a noi non serve |
| `PeopleGetAll`, `PeopleAbsenceTypeGetAll`, `PeopleDepartmentGetAll`, `Timesheet2GetESS` | ❌ `-99 Wrong API name` | non esistono (27/08) |
| suffissi `…Ins`, `…Upd`, `…Set`, `…Add`, `…Save`, `…Create`, `…Delete` | ❌ non esistono | la scrittura è **solo** `…Post` |

🪤 Il `-11` di `PeopleStampPost` dice «RecordID: 11882»: **non è una timbratura creata**. I nostri
`StampID` stanno fra 10.272.222 e 10.344.175, `StampID==11882` non esiste per noi e dopo la prova
non è comparsa nessuna timbratura nuova: è l'id interno della definizione di colonna.
🪤 `PeopleStampPost&Edit=true` **a corpo vuoto** non risponde `-17` ma una **risposta vuota**
(nessun JSON): la modalità modifica si calibra solo con una chiave vera nel body.

Mai provate (❓): `PeopleExpressGetAll` / `PeopleExpressMidGetAll`, `AnagActivityProjectGet`,
`AnagJobCodeGetAll`, `PeopleExpenseGetFullESS`, `PeopleTimesheetGetESS`, `PeopleStampGetESS`,
`PeopleStampPostESS`, `CVPost`.

**Metodo di calibrazione** (riusabile, non scrive nulla): chiamare l'API **senza parametri**
e leggere la forma dell'errore — `-99 Wrong API name` = non esiste; `-2` sui diritti = esiste
ma negata; errore di validazione (`-12`, `-16`…) = esiste e siamo autorizzati. Dopo la prova
del 27/08 è stato verificato che non era stato creato nulla. La sonda lo fa con `--calibra`.

---

## 3. Il protocollo (📘 guida v4.1)

### 3.1 URL e parametri di query string

```
https://<host>/<istanza>/api.pm?ApiName=<Nome>&AuthToken=<token>[&flag…]
```

| Parametro | Vale per | Significato |
|---|---|---|
| `ApiName` | tutte | nome dell'operazione, **case-sensitive** |
| `AuthToken` | tutte tranne `TokenGet` | il token; 🪤 va **URL-encoded** (un `&` dentro lo tronca e l'errore arriva travestito da «privilegi insufficienti») |
| `DF=1` | GET | date in `yyyy-MM-dd HH:mm:ss` (senza: `dd/MM/yyyy HH:mm:ss`). **Sempre** in produzione |
| `PageNumber` | GET | pagina, da 1 |
| `RowsPerPage` | GET | righe per pagina, tetto server `MAXRECORD` (tipicamente 1000) |
| `ResultFields=A,B,C` | GET (release ≥ 6.10) | limita le colonne; 🪤 un nome sbagliato = `-13` e zero righe |
| `ReadLanguage=IT` | GET | descrizioni tradotte dove esistono (`WriteLanguage` è un'altra cosa) |
| `Edit=true` | Post* | aggiorna invece di inserire (§4.2) |
| `ReturnAllPostedRecord=1` | Post* | torna il record intero, con i valori calcolati dal server |
| `AppCode=<etichetta>` | tutte | etichetta nel log di audit di Ecos (es. `AppCode=ATEC_PM`) — **noi oggi non lo mandiamo** |
| `UniqueDeviceID` | `TokenGet` | identificativo dispositivo per l'audit |

**Tutto il resto va nel body**: filtri per le GET, valori per le Post.

### 3.2 `TokenGet`

Body: `Userid=…&Password=…&ClientID=…` (nomi esatti; `userid`, `USERID`, `clientid` → `-19`).
Risposta: `…ECOSAGILE_DATA_ROW.AuthToken` (oggetto singolo), `MESSAGE: "Token Created"`.
Errori: `-19` credenziali mancanti/encoding sbagliato · `-98` credenziali errate (**non
ritentare in automatico: blocca l'utente**) · `-97` utente bloccato · `-18` ClientID/tenant.

Vita del token: **~60 s di inattività**, rinnovato da ogni chiamata riuscita. Pattern
consigliati: script breve = un token per tutto il giro; batch lungo = rinnovare su
`ERROR_CODE -1` (o `-99`) e ritentare **una volta**; back-end web = token nuovo per ogni
richiesta di integrazione salvo attività continua. 🪤 **Non fare lavoro lento (DB) fra due
chiamate con lo stesso token**: vedi §11.

Richiede **TLS 1.2** (ok su .NET 8).

**Cosa torna davvero `TokenGet`** (✅ visto nell'API Test Panel del tenant, 08/09/2026, con
l'account di Diego): oltre ad `AuthToken` la riga contiene `EmplID` (la persona dell'utente:
è quello che le `*GetESS` usano per restringere), `UserID`, `Timeout` (30), `UserLevel` (2 =
Professional), `UserStatusCode` (`A` = attivo), `AppLogPost`. Il pannello mostra anche due
parametri facoltativi di `TokenGet` non citati nella guida: `OpenIdToken` (SSO) e
`PresenceDeviceCode` (terminale di timbratura).

**API Test Panel** (`IT Operation > Api > API Test Panel`, ✅ raggiungibile con l'account di
Diego): tendina con **tutte le API del tenant** (comincia da `AccountCRMDataGetAll`), scheda
«Api Group», campi `N. Top Results` (conferma il `TopResult` dei vecchi sample, §3.8),
`Page Number`, `Rows Per Page`, `ReadLanguage`, `ReturnAllPostedRecord`, `Fields` (=
`ResultFields`). I filtri si scrivono come coppie `"Campo":"<op><valore>"` separate da
virgola — è la sintassi del pannello, non dell'API — e devono stare nella **ALLOWED LIST** della
configurazione dell'API: è da lì che nasce il `-13`. È il posto dove leggere il tracciato di
un'API (campi ammessi e obbligatori) senza chiederlo a SoftAgile.

### 3.3 Filtri (body delle GET)

Sintassi: `Campo=<operatore><valore>`, un parametro per campo, combinati in **AND**.

| Operatore | Esempio di valore |
|---|---|
| `=` | `=18` · `='Doe'` (quindi il body è `EmplID==18`: il primo `=` è del form, il secondo è l'operatore) |
| `<>` | `<>'820'` |
| `>` `>=` `<` `<=` | `>='2025-01-01 00:00:00'` |
| `LIKE` | `LIKE '%John%'` (`%` jolly) |
| `IN(...)` | `IN('A','B')` · `IN(18,19,20)` — l'unico modo di fare OR sullo stesso campo |
| `AND` sullo stesso campo | `TSDate=>='2025-01-01' AND TSDate<='2025-01-31'` (un solo parametro) |

Regole: **stringhe e date fra apici singoli, numeri senza** (`EmplID==18` giusto,
`EmplID=='18'` sbagliato; `Surname==Doe` senza apici = zero righe, la causa più comune di
«non torna niente»). Apice dentro una stringa si **raddoppia** (`LIKE '%O''Reilly%'`).
Nomi dei campi **case-sensitive** (`emplid` → `-13`). Frammenti SQL tipo `ORDER BY` → `-20`.
Preferire gli **ID** ai codici nei filtri (indicizzati: `ActivityID==6011` batte
`ActivityCode=='628'`).

### 3.4 La busta di risposta

```json
{ "ECOSAGILE_TABLE_DATA": {
    "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": [ … ] },
    "ECOSAGILE_ERROR_MESSAGE": {
      "CODE": "OK", "ERROR_CODE": "0", "MESSAGE": "Correct Record Read",
      "USERMESSAGE": "", "ERROR_DATE": "11:09:41.853", "ERROR_CLIENTID": "10305",
      "RECORDCOUNT": "1000", "LASTPAGE": "false", "REACHEDMAXRECORD": "true", "MAXRECORD": "1000" } } }
```

- `ECOSAGILE_DATA_ROW`: **array** con più righe, **oggetto** con una riga sola;
  `ECOSAGILE_DATA` è **stringa vuota o assente** con zero righe. Normalizzare sempre.
- **L'ordine dei campi non è garantito**: accedere per nome, mai per posizione.
- `CODE` è `OK` o `FAIL`; tutti i valori sono **stringhe** (anche numeri e flag).
- Su certi errori Ecos risponde **HTML** invece che JSON (il nostro client lo dice a chiare
  lettere: «risposta non JSON»).

### 3.5 Tipi di dato

| Tipo | Come arriva | Come lo trattiamo |
|---|---|---|
| Date | `yyyy-MM-dd HH:mm:ss` con `DF=1` | `EcosClient.ProvaData`: **solo formati espliciti** (🪤 `DateTime.TryParse` leggeva `05/02/2026` come 2 maggio) |
| Numeri decimali | **virgola** (`"38,1120575"`), anche in scrittura | `Replace(',', '.')` + `InvariantCulture` (vedi `Duration` delle assenze) |
| Booleani | `"TRUE"`/`"FALSE"` **oppure** `true`/`false` JSON | `Testo()` li rende sempre `"TRUE"`/`"FALSE"` |
| Numeri | a volte quotati a volte no | `Testo()` prende il testo grezzo |
| File/binari | in lettura il campo con suffisso **`FS`** porta il Base64 (`PictureFS`) | 🪤 in **scrittura** è l'opposto: `CVAttachment` = Base64, `CVAttachmentFS` = **nome file** (§4.4) |
| Formati data accettati in input | `yyyy-MM-dd`, `yyyy-MM-dd HH:mm`, `yyyy-MM-dd HH:mm:ss` (preferito) | |

### 3.6 Paginazione

- `PageNumber`/`RowsPerPage` in query string; il server tronca a `MAXRECORD` (leggerlo dalla
  risposta, non cablarlo). 📘 consiglia 1000; **noi usiamo 500** (`EcosClient.RowsPerPage`).
- **Regole di stop** (📘): `LASTPAGE=true`, oppure `RECORDCOUNT` 0 o < `RowsPerPage`, oppure
  nessun `ECOSAGILE_DATA_ROW`. Più un **tetto duro** (📘 10000; noi `MaxPages=2000`, oltre →
  eccezione «risposta API anomala»).
- 🪤 **`LASTPAGE` assente NON vuol dire ultima pagina**: il nostro client si ferma su
  `LASTPAGE=TRUE`, su pagina vuota, o su pagina non piena *solo se* `LASTPAGE` manca. Col
  default a «vero» una risposta senza quel campo troncava lo scarico alla prima pagina e
  l'import si dichiarava riuscito.
- 🪤 **Filtro e `RowsPerPage` identici su tutte le pagine**, altrimenti il server rimescola e
  si perdono/duplicano righe.
- 🪤 Paginazione **a offset senza snapshot**: una riga che cambia posizione durante uno
  scarico lungo può scivolare fra due pagine. Mitigazione: un import completo periodico
  (già domanda aperta a SoftAgile: esiste un ordinamento stabile?).

### 3.7 Naming: `GetAll` / `GetESS` / `GetMSS`

| Suffisso | Chi | Cosa torna |
|---|---|---|
| `*GetAll` | integrazioni back-office (**noi**) | tutti i record che l'utente API è autorizzato a leggere |
| `*GetESS` | app self-service del dipendente | **solo l'`EmplID` della sessione**, anche senza filtro |
| `*GetMSS` | app del responsabile | solo il perimetro organizzativo del manager |

### 3.8 Parametri «di contrabbando» (dai vecchi Source Samples, ❓ non nella v4.1)

`CacheBusting=<ms>` su `TokenGet`; `TopResult=100` (limite righe senza paginazione);
`ChannelActive=0`; `ShowTraceSQL=true` (trace SQL nella risposta, solo per debug). La vecchia
app chiamava `PeopleTimesheetGetESS` in **HTTP GET** col token in query string e riceveva il
profilo dell'utente della sessione (`EmplID`, `NameFirst`, `NameLast`, `ShortName`,
`CompanyID`, `EMail`, `StampPicture`, `MinHourControl`, `CountryDefaultLanguageID`,
`LocationID`). Non contarci: la v4.1 dice «sempre POST».

---

## 4. Scrittura (`<Entità>Post`)

### 4.1 Insert (default)

Body = valori dei campi. Risposta = **la chiave** del nuovo record
(`…ECOSAGILE_DATA_ROW.StampID`), `MESSAGE: "Correct Record Insert"`.
Esempio dalla guida per `PeopleStampPost`:

```
StampDateTime=2025-05-25 09:53:59&BadgeCode=123&VersusCode=IN&PresenceDeviceID=10&GPSLat=38,1120575&GPSLong=15,6483509&StatusCode=A
```

(quindi una timbratura ha almeno: `StampID` chiave, `StampDateTime`, `BadgeCode`/`EmplID`,
`VersusCode` `IN`/`OUT`, `PresenceDeviceID`, `GPSLat`/`GPSLong`, `StatusCode`, `TypeCode`
es. `AUTOMATIC`, `InsertDate`, `UpdateDate`, `YearMonth`, `StampLocationName`).

### 4.2 Update (`Edit=true`)

`Edit=true` **in query string** + **chiave del record nel body** + i soli campi da cambiare.
È un update **parziale**: gli altri campi restano. 🪤 Chiave omessa → `-17`.

```
POST …/api.pm?ApiName=PeopleStampPost&Edit=true&AuthToken=<t>
body: StampID=9504&StampDateTime=2026-09-01 08:00:00
```

### 4.3 Idempotenza — la regola che decide il design

📘 «**Non ritentare alla cieca una Post**»: un timeout dopo che il server ha già scritto,
seguito da un retry, crea un **doppione**. Quindi ogni scrittura verso Ecos da ATEC PM ha:
1. la chiave restituita **salvata da noi** subito (colonna dedicata, es. `StampID` in
   `hr_punches.external_id` c'è già per l'import);
2. un registro degli invii (data, autore, esito, chiave) prima di ritentare;
3. `ReturnAllPostedRecord=1` quando servono i valori calcolati dal server.

### 4.4 Allegati

Campo `<Nome>` = contenuto **Base64** (senza prefisso data-URI: la vecchia app faceva
`substr(23)`), campo gemello `<Nome>FS` = **nome del file**. Errori: `-14` contenuto
corrotto/troppo grande/formato non ammesso, `-15` manca il campo `*FS`. Esempio `CVPost`:
`CVAttachment=<base64>&CVAttachmentFS=MJ_CV.pdf&NameFirst=Michael&NameLast=Jordan`.

### 4.5 Cancellare

Non esiste una `…Delete`. La cancellazione è un **update** con la chiave, `Delete=1` e
`UpdateDate=<adesso>` (Source Samples §5: «add always UpdateDate to the deletes»). ❓ da
provare sul nostro tenant prima di usarla.

### 4.6 I nostri diritti di scrittura oggi

Con `api.it` (08/09/2026): `PeopleStampPost` ✅ scrivibile; `PeopleAbsenceRequestPost` ❌
(ServiceID `PeopleAbsenceRequestMSS`, RightID 4), `PeopleOvertimeRequestPost` ❌
(`PeopleOvertimeApproveMSS`, RightID 4), `PeoplePost` ❌ (`JobData`, RightID 2, non ci serve).
La validazione a vuoto di `PeopleStampPost` cita **`StampDateTimeTZOffSet`** («Not an integer»):
un campo intero di fuso orario che la guida non documenta, da scoprire con la prima scrittura vera.
Il tracciato dei parametri di `PeopleAbsenceRequestPost` (obbligatori, codici categoria,
mezza giornata, stato di nascita PENDING/ACCEPTED) è **da chiedere a SoftAgile** — non si
scrive codice prima di averlo (PIANO-HR-PRESENZE §5 e §9).

---

## 5. Sincronizzazione incrementale — regole e come lo facciamo noi

📘 L'algoritmo canonico (§16): cursore per API; filtro `UpdateDate=>'<cursore>'` (📘 usa `>`
stretto); pagine fino a `LASTPAGE`; massimo `UpdateDate` visto; **cursore avanzato solo dopo
il commit a valle**; su errore non avanzare. Mai scarichi interi senza filtro: fanno scattare
il limitatore e il **blocco dell'account**.

| Filtro | Cosa misura | Quando |
|---|---|---|
| `UpdateDate` | ultima modifica **del record** | timbrature, richieste, spese, anagrafiche generiche |
| `SyncUpdateDate` | ultima modifica della persona **o di qualunque dato collegato** | famiglia `PeopleExpress*GetAll`: prende anche le modifiche su tabelle collegate che `UpdateDate` non vede |
| `EffDate` | data di validità di un evento di impiego | **solo** filtro di business, mai cursore (un job del 2008 può comparire in un sync del 2025) |

**Cancellazioni logiche**: i record cancellati **restano visibili con `Delete=1`** — chi
specchia i dati deve propagare il flag (📘 §7.8, §16.5). Vedi §11.1: noi non lo leggiamo.

**Il nostro import** (`HrAttendanceService.Import.cs`):
- **incrementale** ogni 12 h: `UpdateDate>=<cursore>` (con `>=`, non `>`: la riga di confine
  si rilegge ed è innocua perché l'upsert è per `StampID`); cursore = max `UpdateDate` di
  Ecos, scritto **dopo** la scrittura su DB;
- **completo** («Reimporta tutto»): dal 2020, e in più **rimuove** le righe `source='ECOS'`
  che nel dump non ci sono più (l'incrementale non ha la fotografia intera). Serve dopo aver
  collegato un dipendente nuovo (le sue timbrature passate erano state scartate);
- **finestra** (risincronizza giorno/mese): filtro `YearMonth='aaaamm'` con `UpdateDate` dal
  1900, inserisce/aggiorna su tutto il mese, cancella **solo dentro la finestra chiesta**,
  **non tocca il cursore**; se torna zero righe e la finestra è di tutti, non cancella nulla;
- le assenze (`PeopleAbsenceRequestGetAll`) non hanno filtro di periodo: si scaricano e si
  tengono quelle che toccano la finestra; upsert per `ecos_absence_id`;
- **rettifiche nostre** (`source='ADJUSTMENT'`) non si cancellano mai dall'import.

🪤🪤 **Ecos rimanda solo gli ultimi 60 giorni.** `PeopleStampGetAll` ha un filtro implicito lato
server, `UpdateDate>=oggi-60` (catalogo, Criteria), che si somma in AND al nostro: chiedere «dal
2020» o «dal 1900» non cambia niente. Conseguenze, scoperte il 08/09/2026 (TODO §11):
- l'**import completo NON è una fotografia intera**: le righe `ECOS` più vecchie di 60 giorni
  non tornano e `RimuoviCancellateSuEcos` **le cancellerebbe da noi**;
- la risincronizzazione di un giorno o di un mese **oltre i 60 giorni** riceve zero righe; per
  «tutti» la rete regge, per la **singola persona** cancellerebbe le sue timbrature del giorno;
- la storia oltre i 60 giorni **esiste solo da noi**: l'import va tenuto acceso e non si
  riparte mai da zero. ✅ **Corretto l'08/09/2026** (§11.0): si cancella solo dall'orizzonte
  dello scarico in su, «Reimporta tutto» rilegge gli ultimi 60 giorni e lascia il resto.
Anche `PeopleAbsenceRequestGetAll` ha una finestra: `DateBegin>=oggi-90 OR DateEnd>=oggi-60`.

Cosa loggare per ogni chiamata (📘 §16.7): `ApiName`, pagina, nomi dei campi filtrati (non i
valori con dati personali), `CODE`/`ERROR_CODE`/`MESSAGE`, `RECORDCOUNT`/`LASTPAGE`, status
HTTP. **Mai `Userid`, `Password`, `AuthToken`.**

---

## 6. Catalogo API

> **Il catalogo vero del nostro tenant** (84 API dell'API Library, con **ServiceID**, filtri
> impliciti e ordinamento, letto il 08/09/2026) sta in **[ECOS-API-CATALOGO.md](ECOS-API-CATALOGO.md)**.
> Lì ci sono anche le API che il 27/08 avevamo cercato coi nomi sbagliati (`AnagTSCategoryGetAll`
> per le causali, `AnagDepartmentGetAll` per i reparti, `PeopleEmploymentGetALL` per le persone
> in forza) e le trappole dei filtri impliciti (§5).

### 6.1 Le più comuni (📘 §19) — i nomi vanno confermati in `IT Operation > Api > Gestione Api`

| Scopo | ApiName | Filtri chiave | Sul nostro tenant |
|---|---|---|---|
| Anagrafica dipendenti | `PeopleExpressGetAll` / `…LightGetAll` / `…MidGetAll` | `SyncUpdateDate` | ❓ mai provata; è il lookup canonico `EmplID`↔`EmplCode` |
| Timbrature | `PeopleStampGetAll` / `PeopleStampGetESS` | `UpdateDate`, `StampID`, `StampDateTime`, `EmplID`, `YearMonth` | ✅ in uso |
| Badge | `PeopleBadgeGetAll` | `UpdateDate` | ✅ in uso |
| Richieste di assenza | `PeopleAbsenceRequestGetAll` | `UpdateDate` | ✅ in uso |
| Richieste di straordinario | `PeopleOvertimeRequestGetAll` | `UpdateDate` | ✅ abilitata, vuota |
| Giustificativi approvati **consolidati** (assenze + straordinari) | `Timesheet2GetAll` | `UpdateDate`, stato approvazione | ❌ `-2` (ServiceID `TimesheetAnalysis`) |
| Progetti/attività | `AnagActivityProjectGet` | `UpdateDate`, ID progressivo | ❓ |
| Mansioni | `AnagJobCodeGetAll` | `UpdateDate`, `JobCodeID` | ❓ |
| Note spese complete | `PeopleExpenseGetFullESS` | `UpdateDate`, `ExpenseID` | ❓ (è ESS: solo l'utente della sessione) |
| Inserire una timbratura | `PeopleStampPost` / `PeopleStampPostESS` | — | ✅ diritto presente |
| Inserire/aggiornare una persona | `PeoplePost` | — | ✅ diritto presente |
| Inserire un CV | `CVPost` | — | ❓ |

### 6.2 Campi conosciuti

**`PeopleStampGetAll`** (leggiamo): `StampID`, `StampDateTime`, `EmplID`, `EmplCode`,
`NameComplete`, `VersusCode`, `StampLocationName`, `YearMonth`, `UpdateDate`, `StatusCode`.
Esistono anche (📘/esempio SoftAgile): `BadgeCode`, `PresenceDeviceID`, `GPSLat`, `GPSLong`,
`TypeCode`, `InsertDate`, e (dalla validazione di `PeopleStampPost`) **`StampDateTimeTZOffSet`**
intero. ❓ **`Delete`** (§11.1). ❓ valori di `StatusCode` (l'esempio della
guida inserisce `A`; una timbratura annullata come si riconosce? domanda aperta a SoftAgile).

**`PeopleBadgeGetAll`** (leggiamo): `EmplID`, `EmplCode`, `NameComplete`, `BadgeCode`,
`InForce` (`TRUE`/`FALSE`), `StatusCode`.

**`PeopleAbsenceRequestGetAll`** — 34 campi (✅ sonda 29/08). Leggiamo: `AbsenceRequestID`,
`EmplID`, `EmplCode`, `NameComplete`, `CategoryCode`, `CategoryDescShort`, `StatusCode`,
`DateBegin`, `DateEnd`, `FullDay`, `HourBegin`, `HourEnd`, `Duration`, `UpdateDate`.
Altri visti e utili: `CategoryID`, `StatusDescShort`, `DataApprove`, `ApproveNameComplete`
(chi e quando ha approvato), `ResidualEndYear`, `ResidualToDate` (**residui ferie**), `Note`,
`Delete`, `YearMonth`. Valori osservati: `CategoryCode` `F` ferie · `P` permesso/ROL ·
`M`/`MA` malattia · `I`/`IN` infortunio (mappa in `SyncAbsences`); `StatusCode` `ACCEPTED` /
`REJECTED` / `CANCELLED` / altro = in attesa.

Per scoprire i campi di un'API nuova: `python tools/sonda_ecos.py <ApiName> --righe 1`.

### 6.3 `EmplID` contro `EmplCode`

📘 §11: `EmplID` = ID interno numerico, **stabile**, chiave dei join; `EmplCode` = codice
visibile a video, **può cambiare**. 🪤 Nelle pagine web di Ecos la colonna etichettata
«EmplID» mostra in realtà l'`EmplCode`. Noi colleghiamo le persone per **`EmplCode`**
(`employees.ecos_empl_code`, unico): funziona, ma se un giorno un codice viene riassegnato la
storia si sposta di persona. `EmplID` arriva già in ogni risposta: è il candidato per una
seconda colonna di aggancio.

---

## 7. Errori

### 7.1 Catalogo `ERROR_CODE` (📘 §20.3) e cosa fare

| Codice | Significato | Azione |
|---|---|---|
| `0` | ok | — |
| `-1` | token scaduto/invalido | nuovo `TokenGet`, ritentare **una volta** |
| `-2` | l'utente API non ha il **profilo di autorizzazione** per quell'API | il `MESSAGE` dice il ServiceID: chiederlo a SoftAgile |
| `-10` | valore troppo grande/piccolo | validare in locale |
| `-11` | tipo sbagliato (testo in numerico, data malformata) | date `yyyy-MM-dd HH:mm:ss`, decimali con virgola |
| `-12` | campo obbligatorio mancante in una Post | leggere la definizione in Gestione Api |
| `-13` | campo/filtro inesistente o **maiuscole sbagliate** | controllare il nome esatto |
| `-14` / `-15` | allegato corrotto / manca il campo `*FS` | §4.4 |
| `-16` | parametro obbligatorio mancante (query string o body) | il `MESSAGE` dice quale |
| `-17` | **chiave mancante** in una `Edit=true` | mettere `StampID`/`EmplID`… nel body |
| `-18` | ClientID/tenant | verificare il ClientID |
| `-19` | `TokenGet` senza credenziali o body non form-url-encoded | §3.2 |
| `-20` | frammento SQL in un filtro (`ORDER BY`…) | toglierlo, usare la paginazione |
| `-97` | utente **bloccato** (troppi tentativi o blocco admin) | sbloccare da admin Ecos, ruotare la password |
| `-98` | credenziali errate | **non ritentare in automatico** |
| `-99` | errore generico **oppure «Wrong API name»** | log completo (senza segreti), un retry; se persiste, SoftAgile |

Codici nuovi possono comparire: un negativo sconosciuto si tratta come generico e si logga
il `MESSAGE` letterale.

### 7.2 Sintomi (📘 §20.4)

| Sintomo | Causa probabile | Rimedio |
|---|---|---|
| `CODE=OK` ma zero righe | valore senza apici, data nel formato sbagliato, filtro in query string | apici, `DF=1`, filtro nel body, allargare il filtro per capire |
| Torna tutta la tabella | filtro messo in query string | spostarlo nel body |
| HTTP 404 | istanza sbagliata o `ApiName` scritto male | §2.2 della guida: la URL di login |
| Timeout | payload pesante / niente filtri | `UpdateDate`, `RowsPerPage` più basso, `ResultFields`, finestra più stretta |
| Errore di parse JSON | risposta troncata o pagina HTML | guardare lo status HTTP e il body grezzo |
| Account bloccato temporaneamente | chiamate senza filtri o troppo frequenti | filtrare, rallentare, poi supporto |
| Insert `OK` ma senza chiave | manca `ReturnAllPostedRecord=1` o l'API non torna chiavi | aggiungerlo; leggere la definizione |

### 7.3 Come si comporta il nostro client

`CODE ≠ OK` → **`EcosApiException`** con `MESSAGE` e `CODE` (mai dati parziali in silenzio: il
cursore non deve avanzare su una pagina rotta). Risposta non JSON → «risposta non JSON
(primi 160 caratteri)». Rete giù → «Ecos non raggiungibile»; la **cancellazione** (spegnimento
del servizio) è distinta e non finisce a log come guasto di rete. **Nessun rinnovo automatico
del token** su `-1`: l'import fallisce e riparte al giro dopo (cursore fermo).

Dove guardare lato Ecos: `IT Operation > Audit > Application Log` (Module = API) o
`API Audit`; sessioni create dai token in `Audit > Elenco sessioni`.

---

## 8. Il nostro client — mappa di `EcosClient.cs`

| Pezzo | Cosa fa |
|---|---|
| `ResolveCredenziali()` / `SalvaCredenziali()` / `LeggiCredenziali()` / `Configured` | credenziali DB→appsettings, password write-only cifrata; `Configured=false` = modulo a riposo |
| `TokenAsync()` | `TokenGet`; `EstraiToken` statico per i test |
| `GetPunchesAsync(token, updateDa)` | `PeopleStampGetAll` con `UpdateDate>=` (null = dal 2020) |
| `GetPunchesMonthAsync(token, anno, mese)` | stesso, con `YearMonth='aaaamm'` e `UpdateDate` dal 1900 (fotografia del mese) |
| `BadgesAsync(token)` | `PeopleBadgeGetAll` dal 1900, deduplicato per `EmplCode` (attivo prima) |
| `GetAbsenceRequestsAsync(token, updateDa)` | `PeopleAbsenceRequestGetAll` |
| `FetchTutteLePagineAsync(api, token, campi, updateDa, filtro?, log?)` | **il motore**: URL con `PageNumber`/`RowsPerPage=500`/`DF=1`/token escapato; body `UpdateDate>=…` + un filtro extra opzionale; stop su `LASTPAGE`/vuota/non piena-senza-LASTPAGE; tetto 2000 pagine |
| `EstraiPagina(json, campi, api)` | busta → righe + `bool? ultima` (tre stati); `CODE≠OK` → eccezione |
| `Testo(el, campo)` | qualunque tipo JSON → testo (`TRUE`/`FALSE`, numeri grezzi) |
| `ProvaData(s, out dt)` | solo formati espliciti |
| `PostAsync(url, form)` | legge **sempre** il body, anche su HTTP ≠ 200 |

Test senza rete: `ATEC.PM.Tests/Hr/EcosClientTests.cs` (token, HTML, array/oggetto singolo,
`ECOSAGILE_DATA` vuoto, `LASTPAGE` assente, tipi JSON, errore → eccezione, formati data,
righe senza orario scartate, badge, assenze, credenziali mancanti) e `CredenzialiEcosTests.cs`
(precedenza DB→file, password write-only cifrata, modulo a riposo). Il csproj del server ha
`InternalsVisibleTo ATEC.PM.Tests`.

Costanti da conoscere: `RowsPerPage=500`, `MaxPages=2000`, `DallInizio=1900-01-01`, ripiego
`2020-01-01`, `BaseUrlPredefinito=https://ha.ecosagile.com/dd/api.pm?ApiName=`.

---

## 9. Ricette per le implementazioni future

### 9.1 Leggere un'API nuova (GET)

1. **Nome e campi esatti** in `IT Operation > Api > Gestione Api` (serve il profilo
   `1642-APIRead`, che il nostro utente forse non ha) **oppure** con la sonda:
   `python tools/sonda_ecos.py NomeApi --righe 1` (mostra `CODE`, i nomi dei campi e una
   riga d'esempio senza nominativi). Se torna `-2`, il `MESSAGE` dice quale ServiceID chiedere.
2. In `EcosClient.cs`: un `record` per la riga, un array `static readonly string[]` con i
   campi, un metodo `XxxAsync(token, updateDa)` che chiama `FetchTutteLePagineAsync`.
   Anagrafica (le righe vecchie contano) → `DallInizio`; dati che scorrono → cursore.
   Filtri aggiuntivi nella forma `("Campo", "='valore'")`.
3. Parsing: date con `ProvaData`, decimali con virgola→punto, booleani `== "TRUE"`; una riga
   senza chiave o senza data si **scarta e si logga**, non si inventa.
4. Test in `EcosClientTests` con una risposta JSON finta (copiare la forma dalla sonda,
   **senza dati veri**): array, oggetto singolo, vuoto.
5. Lato import: upsert per chiave esterna, cursore per API in `app_config`, avanzato **dopo**
   il commit; log delle pagine tramite il parametro `log`.
6. Se Ecos espone `Delete`, filtrarlo o propagarlo (§11.1).

### 9.2 Scrivere in Ecos (Post)

1. Verificare il diritto con `--calibra` (vuoto → validazione = ok; `-2` = chiedere il
   ServiceID). Tracciato dei campi obbligatori da Gestione Api o da SoftAgile.
2. Inserimento: body coi valori, **salvare subito la chiave restituita**; `Edit=true` +
   chiave per gli aggiornamenti (parziali). Decimali con la virgola, date ISO.
3. Registro degli invii (data, autore, chiave, esito) → un retry controlla il registro prima
   di rispedire. Nessun retry automatico su timeout.
4. `AppCode=ATEC_PM` in query string, così nell'audit di Ecos si vede chi ha scritto.
5. Verifica dopo la scrittura con una GET filtrata sulla chiave (`StampID==<id>`).

### 9.3 Ritorno verso Ecos delle ore arrotondate (chiesto da Diego il 02/09)

`PeopleStampPost` + `Edit=true` + `StampID` (già in `hr_punches.external_id`) +
`StampDateTime` arrotondato; solo per le giornate in cui l'ora arrotondata differisce da
quella timbrata; ogni invio registrato con data e autore; stati «Allineato / Da inviare /
Inviato il». ⚠️ Da fare **solo con l'utente API dedicato** (§9.4) e dopo il sì sul fatto che
sovrascrivere l'ora timbrata in Ecos sia accettabile: la regola di Diego è che l'ora
originale non deve mai sparire — se Ecos non tiene la storia, l'originale resta solo da noi.

### 9.4 Utente API dedicato — cosa chiedere a SoftAgile (info@ecosagile.com, 02 89054136)

- ✅ **fatto il 08/09/2026**: utente di servizio **`api.it`**. Da confermare che abbia livello
  **2 - Professional**, profilo **`1642-APIRead`** (libreria API + Test Panel) e **solo** i
  profili delle API elencate;
- i ServiceID che la calibrazione del 08/09 dà negati a `api.it`: **`PeopleAbsenceRequestMSS`**
  (RightID 4) per `PeopleAbsenceRequestPost`, **`PeopleOvertimeApproveMSS`** (RightID 4) per
  `PeopleOvertimeRequestPost`, **`TimesheetAnalysis`** (RightID 1) per `Timesheet2GetAll`,
  **`PeopleExpressLight`** (RightID 1) per `PeopleExpressLightGetAll`;
- il tracciato di `PeopleAbsenceRequestPost` e il significato di `StatusCode` sulle
  timbrature; se esiste un ordinamento stabile per la paginazione;
- password da ruotare **ogni 3 mesi**, mai nei sorgenti (📘 §21.2).

### 9.5 `Timesheet2GetAll` quando arriverà il diritto

È il dataset che la guida raccomanda per i feed paghe: assenze **e** straordinari approvati,
consolidati, filtro `UpdateDate` + stato. Sostituirebbe la lettura separata di
`PeopleAbsenceRequestGetAll` + `PeopleOvertimeRequestGetAll`. Prima cosa: sonda con
`--righe 1` per i campi veri, poi §9.1.

### 9.6 Anagrafica persone (`PeopleExpressGetAll`)

Cursore su **`SyncUpdateDate`**, non `UpdateDate`. Versione `Light` se bastano
`EmplID`/`EmplCode`/nomi. Serve per il collegamento stabile per `EmplID` (§6.3).

---

## 10. Trappole — elenco consolidato

1. Filtri in query string = ignorati, torna tutto (e si rischia il blocco dell'account).
2. Stringhe/date senza apici = zero righe senza errore.
3. Nomi di parametri e campi **case-sensitive** (`Userid`, `Password`, `ClientID`; `-13`).
4. Body JSON invece di form-url-encoded = `-19` / filtri ignorati.
5. `ECOSAGILE_DATA_ROW` oggetto singolo con una riga; `ECOSAGILE_DATA` stringa vuota con zero.
6. Errori con **HTTP 200**; a volte HTML al posto del JSON.
7. `LASTPAGE` assente ≠ ultima pagina; filtro e `RowsPerPage` fissi per tutto il giro.
8. Token che muore dopo 60 s di inattività: niente lavoro lento fra due chiamate.
9. Decimali con la **virgola**, in lettura e in scrittura.
10. Date: `DF=1` sempre; parse solo con formati espliciti.
11. Insert **non idempotenti**: salvare la chiave, mai ritentare alla cieca.
12. `Edit=true` senza chiave = `-17`; è un update **parziale**.
13. Allegati: nome/contenuto invertiti fra lettura (`*FS` = Base64) e scrittura (`*FS` = nome file).
14. `Delete=1` = cancellato logicamente ma **ancora restituito**.
15. `EmplID` (stabile) ≠ `EmplCode` (visibile, riassegnabile); la pagina Ecos li confonde.
16. `SyncUpdateDate` per la famiglia `PeopleExpress*`; `EffDate` non è un cursore.
17. Badge: `UpdateDate` fermo al 2019 → anagrafiche dal 1900, mai dal cursore.
18. Token con `&` dentro: sempre `Uri.EscapeDataString`.
19. `-98` ritentato in automatico blocca l'utente (`-97`).
20. Il `App.config` del sample SoftAgile ha due blocchi: vince l'ultimo (valori interni loro).
21. Paginazione a offset: una riga può scivolare fra due pagine durante uno scarico lungo.
22. Account personale come utente API: vietato dalla guida; lo è stato fino al 08/09/2026, ora c'è `api.it` (§9.4).
23. Il **ClientID è del tenant, non della persona**: 10305 vale per ogni utente ATEC; la persona in Ecos è `EmplID`/`EmplCode`.

---

## 11. Trovato durante lo studio (08/09/2026) — da sistemare, non ancora fatto

### 11.0 ✅ Import completo e finestra dei 60 giorni (scoperto e corretto il 08/09/2026)

`PeopleStampGetAll` torna solo `UpdateDate>=oggi-60` (§5). Fino all'08/09 `ImportAsync(full:true)`
confrontava **tutta** la storia con quello scarico e avrebbe cancellato ogni timbratura più
vecchia (`hr_punches` parte dal 25/06); la risincronizzazione di un giorno vecchio per una
persona le svuotava la giornata. **Corretto** (`HrAttendanceService.OrizzonteEcos`):
- l'**orizzonte** di uno scarico è il minimo `UpdateDate` ricevuto: da lì in su lo scarico è
  una fotografia fedele (una timbratura esistente ha `UpdateDate ≥ StampDateTime`, quindi
  sarebbe arrivata), sotto non dice niente;
- l'import completo e la risincronizzazione cancellano **solo** le righe `ECOS` con
  `punched_at ≥ orizzonte + 10 minuti` (margine per i terminali con l'orologio avanti);
  scarico vuoto = nessun orizzonte = nessuna cancellazione, persona o no;
- il messaggio di esito dice «le timbrature prima del gg/mm/aaaa sono rimaste com'erano», e
  i dialoghi del client chiedono «Rileggere da Ecos gli ultimi 60 giorni?».
Test: `Import_completo_non_tocca_la_storia_che_Ecos_non_restituisce_piu`,
`Un_giorno_piu_vecchio_di_quello_che_Ecos_restituisce_non_si_svuota`,
`Nella_finestra_si_cancella_solo_dall_orizzonte_in_su`, `L_orizzonte_e_il_primo_UpdateDate…`.
🪤 Nei test finti l'`UpdateDate` va messo **vicino alla timbratura** (un minuto dopo), come nella
realtà: con una data «a caso» giorni dopo nessuna riga risulta cancellabile.
Alternativa da valutare: `PeopleStampPeriodDayGetAll` (catalogo) dà una riga per giorno/persona
con gli orari `Stamp1`…`Stamp6`, filtrata per **`StampDate`** (ultimi 90 giorni): è la fotografia
per giorno che serve alla risincronizzazione, anche se senza `StampID`.
Inoltre `PeopleStampGetAll` accetta il filtro **`Delete`**: si può chiedere `Delete==1` per
sapere cosa è stato cancellato negli ultimi 60 giorni invece di dedurlo per differenza.
✅ **`StampPresenceMonthCardGetAll` risponde ad `api.it` e restituisce la storia** (provato il
08/09 con `YearMonthS` di giugno 2026, gennaio 2026 e settembre 2025): cartellino giornaliero di
Ecos con gli orari in `StampCode1…8` (HTML da ripulire, senza `StampID`). È la via per i mesi
oltre i 60 giorni; per gli ultimi 60 resta `PeopleStampGetAll` con lo `StampID`. E per le assenze ✅ **`PeopleAbsenceRequestRefineWorkAll` risponde ad `api.it`** (08/09): una
riga per giorno di assenza con ore, causale, stato, `EmplID` ed `EmplCode`, filtrabile per
`TSDate`: toglie il problema della finestra dei 90 giorni e del taglio delle richieste multi-giorno.

### 11.1 🔴 Non leggiamo il flag `Delete`

La guida (§7.8, §16.5) dice che i record cancellati **restano visibili con `Delete=1`**. Il
nostro import non chiede il campo né sulle timbrature né sulle assenze: se Ecos cancella
logicamente, una timbratura tolta là **continua ad arrivare e a contare** nel cartellino, e
nemmeno l'import completo la toglie (confronta gli ID, e l'ID c'è ancora). Sulle assenze il
campo `Delete` **esiste** (sonda 29/08); sulle timbrature è da verificare. Da fare:
1. `python tools/sonda_ecos.py PeopleStampGetAll --righe 1` → c'è `Delete`?
2. Aggiungere `Delete` a `PunchFields` e `AbsenceFields`; riga con `Delete=1` → si tratta
   come cancellata (rimossa se `source='ECOS'`, assenza a stato `CANCELLED`).
3. Risponde anche alla domanda 0(c) del PIANO-HR-PRESENZE §5 (tombstone o sparizione).

### 11.2 🟠 Token scaduto nella risincronizzazione di un mese con assenze

In `ImportWindowAsync(conAssenze: true)` la chiamata `GetAbsenceRequestsAsync` usa il token
**dopo** `ImportPunches` (scrittura DB + ricalcolo giornate). Se quel lavoro dura più di
60 s il token è morto: la chiamata fallisce con `-1`, viene catturata e loggata come
«Assenze non scaricate» — nessun danno, ma le assenze del mese non si riallineano. Rimedio:
nuovo `TokenAsync()` prima della chiamata (o rinnovo automatico su `-1` in `PostAsync`).

### 11.3 🟡 Migliorie a costo zero

- `AppCode=ATEC_PM` in query string su tutte le chiamate (audit lato Ecos).
- `RowsPerPage` 500 → 1000 (metà delle chiamate); leggere `MAXRECORD` dalla risposta.
- Loggare `RECORDCOUNT`/`LASTPAGE`/`REACHEDMAXRECORD` per pagina (checklist 📘 §16.7).
- Rinnovo automatico del token su `-1` con un solo retry.
- `ResultFields` per le API larghe (34 campi delle assenze, ne servono 14) — richiede
  release Ecos ≥ 6.10, da verificare con la sonda.

### 11.4 Domande ancora aperte (da SoftAgile)

Ordinamento stabile per la paginazione · valori di `StatusCode` sulle timbrature ·
tracciato di `PeopleAbsenceRequestPost` e significato di `StampDateTimeTZOffSet` · i quattro
ServiceID negati a `api.it` (§9.4) · come si calibra `Edit=true` senza toccare un record.

---

## 12. Attrezzi: `tools/sonda_ecos_server.py` e `tools/sonda_ecos.py` (sola lettura)

Manuale d'uso in [../tools/TOOLS.md](../tools/TOOLS.md).

**`sonda_ecos_server.py` — quella da usare.** Gira **sul server di produzione** con le credenziali
che il server ha già: carica via scp uno script PowerShell che decifra (DPAPI) la connection
string di `appsettings.Secrets.json`, legge `res_settings`, decifra la password Ecos, prende il
token e fa le chiamate chieste; poi lo script viene cancellato. Password e token non escono mai
dal server e non stanno sulla riga di comando. Stesse regole della sonda locale: `--calibra` a
corpo vuoto (unico modo ammesso per una `Post*`), letture con filtro e poche righe, nominativi
mai stampati. È così che è stata misurata `api.it` il 08/09/2026.

```powershell
python tools/sonda_ecos_server.py PeopleStampGetAll --righe 1 --valori
python tools/sonda_ecos_server.py Timesheet2GetAll PeopleAbsenceRequestPost --calibra
```

**`sonda_ecos.py` — la variante locale**, per quando si vuole provare un utente diverso da quello
salvato: credenziali da variabili d'ambiente (`ECOS_USERID`, `ECOS_PASSWORD`, `ECOS_CLIENTID`,
opzionale `ECOS_BASEURL`), mai scritte su disco né a log; le mette **Diego nel suo terminale**,
non passano da una chat. Chiama solo `Get*` (le `Post*` solo in `--calibra`); nasconde i
nominativi salvo `--mostra-nomi`.

```powershell
$env:ECOS_USERID="…"; $env:ECOS_CLIENTID="10305"; $env:ECOS_PASSWORD="…"
python tools/sonda_ecos.py PeopleStampGetAll --righe 1                      # campi + riga d'esempio
python tools/sonda_ecos.py PeopleAbsenceRequestGetAll --filtro "UpdateDate=>='2026-08-01 00:00:00'" --righe 3
python tools/sonda_ecos.py Timesheet2GetAll PeopleExpressLightGetAll --calibra   # esiste? ho il diritto?
python tools/sonda_ecos.py PeopleStampGetAll --filtro "YearMonth=='202608'" --conta   # quante righe in tutto
```

⚠️ Le chiamate sull'API di produzione di Ecos si fanno **su richiesta di Diego**, non di
iniziativa: filtri stretti, poche righe, e il limitatore di Ecos blocca l'account a chi
esagera.

---

## Riferimenti

- Guida ufficiale: `ECOS/EcosAgile_API_User_Guide_v4.1.1_EN.pdf` (§ citati sopra).
- Client: `ATEC.PM.Server/Services/Hr/EcosClient.cs`; import:
  `HrAttendanceService.Import.cs`; endpoint: `Controllers/HrController.cs`.
- Piano HR: [../piani/PIANO-HR-PRESENZE.md](../piani/PIANO-HR-PRESENZE.md) (§4-§5, §11);
  port dell'originale: [../piani/PIANO-HR-PORT-ORIGINALE.md](../piani/PIANO-HR-PORT-ORIGINALE.md).
- Vecchio client VB (`Api/EcosApiManager.vb`) nel progetto «Timbrature - API» sul Desktop.
