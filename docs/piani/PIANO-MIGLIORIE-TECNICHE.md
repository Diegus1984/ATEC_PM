# PIANO MIGLIORIE TECNICHE — ATEC PM

> Redatto il **14/08/2026** su analisi del codice reale (115 file server, 58 controller,
> 48 servizi, ~38k righe C# + 482 file TS/TSX). Corretto lo stesso giorno sui punti
> verificati nel codice (utente MySQL di produzione, lockout login, metrica delle
> migrazioni, fixture di test).
> Riguarda **come è fatto** il software, non cosa fa: nessuna funzionalità cambia,
> nessuna schermata si muove. Gli utenti non devono accorgersi di niente — tranne che
> le cose smettono di rompersi in silenzio.

---

## 0. Quadro misurato

| Misura | Valore | Dove |
|---|---|---|
| `DbService.cs` | ~~5.001 righe~~ → **1.646** (15/08) | le 87 migrazioni sono uscite in `Migrations/` |
| Migrazioni versionate | ~~87 blocchi, 90 `catch` "non bloccante"~~ → **una classe per versione** | `Migrations/MNNN_*.cs` + `MigrationRunner` |
| Chiamate DB sincrone | **1.848** contro 31 async | tutto il server |
| Action non-async | **576** `IActionResult` | tutti i controller |
| `SELECT` dentro `Controllers/` | **862** + 515 `db.Open()` | layer dati a metà |
| Transazioni | **63** `BeginTransaction` su **386** endpoint di scrittura | 530 statement di modifica |
| Query dentro `foreach` (N+1) | **166** | tutto il server |
| Test automatici | **0** | soluzione da 3 progetti |
| `IMemoryCache` | **0** usi | anagrafiche rilette ogni volta |
| Middleware errori globale | **0** (`UseExceptionHandler`) | 379 `catch(Exception)` sparsi |
| Health check reale | **0** (`/api/health` non guarda il DB) | usato dagli script di deploy |
| Validazione dichiarativa | **0** DataAnnotations | tutto a mano nelle action |
| Rate limiting ASP.NET | **0** | lockout login già in `AuthController` (5 tentativi / 5 min per username) |
| Indici | 95 `KEY` ≈ solo quelli delle 99 FK → **+3 mirati, −2 ridondanti** (E2, 15/08) | ore e notifiche |
| Utente MySQL | **root in sviluppo**; in produzione già `atecpm` | `appsettings.json` vs `install-server.ps1` |

---

## 1. Ordine di esecuzione

L'ordine **non** è per importanza, è per dipendenza: ogni blocco lascia il terreno
pronto per il successivo.

```
A0 (insieme versioni, da sola)  →  B2 (health/ready)
         ↓
B resto + D  →  C (test)  →  A (runner)
                                ↓
                    E (prestazioni)  →  F (layer dati)
```

| # | Blocco | Stima | Rischio | Si può fermare qui? |
|---|---|---|---|---|
| ~~**A0**~~ | ~~Insieme delle versioni al posto di `MAX` + stop on error~~ | — | — | ✅ **FATTO il 14/08/2026** (vedi in fondo al blocco A) |
| ~~**B2**~~ | ~~Health check che guarda MySQL~~ | — | — | ✅ **FATTO il 14/08/2026** |
| ~~**B**~~ | ~~Middleware errori, validazione~~ | — | — | ✅ **FATTO il 14/08/2026** (B3: restano i DTO da annotare passando di lì) |
| ~~**D**~~ | ~~Root MySQL di sviluppo, lockout IP, CORS~~ | — | — | ✅ **FATTO il 14/08/2026** — resta solo `root` in sviluppo (D1) |
| ~~**C**~~ | ~~Test su migrazioni, permessi, calcoli~~ | — | — | ✅ **FATTO il 14/08/2026** — 90 test + cancello sul deploy |
| ~~**A**~~ | ~~Runner di migrazioni (spacchettare `DbService`)~~ | — | — | ✅ **FATTO tutto** — A0, A1 e A2 (14-15/08/2026) |
| **E** | Misura, N+1, cache, indici (async solo dove serve) | 6-10 g | medio | sì, è incrementale — **E1, E4 e la prima parte di E2 fatti**; restano il resto di E2, E3 ed E5, tutti dietro la settimana di misura |
| **F** | Repository + transazioni | continuo | medio | sì, è continuo |

**Punto fermo**: **A0 non aspetta il resto di A** — è la correzione del motore, venti
minuti, aperta a ogni deploy. Il blocco **C viene prima del runner nuovo (A1/A2)**.
I test sulla catena *sono* il collaudo di quel runner. C1 va scritto sulla regola
nuova (insieme delle versioni), non su `MAX(version)`, altrimenti il test incide
il bug. Rifattorizzare le migrazioni senza quei test significa scoprire gli errori
sul database di produzione.

---

## BLOCCO B — Rete di sicurezza (1 giorno, rischio nullo)

**Obiettivo**: quando qualcosa va storto, il server lo dice invece di fingere che vada bene.

### B1 — Middleware globale degli errori
Oggi ogni action ha il suo `try/catch(Exception)` (379 in totale) e 15 punti fanno
`StatusCode(500)` a mano. Un'eccezione fuori da un try incapsulato torna al client
una pagina di errore HTML, non il contratto `ApiResponse` — e il client web la
interpreta come risposta valida.

- [x] **FATTO il 14/08/2026** — [ExceptionHandlingMiddleware.cs](ATEC.PM.Server/Middleware/ExceptionHandlingMiddleware.cs),
      registrato **per primo** nella catena di [Program.cs](ATEC.PM.Server/Program.cs) (prima
      ancora di `UseCors`, così copre tutto). Logga con `TraceIdentifier` e risponde
      `ApiResponse.Fail` in **camelCase** — con le maiuscole il client leggerebbe `success` come
      `undefined` e scambierebbe l'errore per una riuscita.
      In sviluppo il messaggio esce intero con lo stack; in produzione resta solo
      «Riferimento per l'assistenza: {id}», perché un messaggio interno può raccontare la
      struttura del database a chi non deve conoscerla.
      Non tocca la risposta se è **già partita** (`HasStarted`): riscriverci sopra darebbe un
      corpo mezzo JSON e mezzo altro.
- [x] I 379 `catch` esistenti **non** sono stati rimossi: si tolgono passando sui controller
      (blocco F). Il middleware è il fondo che c'è comunque, non un refactoring.

**Fatto**: 4 test in `Infrastruttura/ReteDiSicurezzaTests.cs` — eccezione → 500 JSON con
`success: false`; il dettaglio interno **non** esce in produzione; esce in sviluppo; una
richiesta riuscita passa intatta.
*(Il primo rosso è stato istruttivo: `JsonSerializer` scrive l'apostrofo come `'`, quindi
il test va fatto sul valore **decodificato** — che è quello che il client legge dopo il parse.)*

### B2 — Health check che guarda davvero il database
`/api/health` oggi risponde `status: "ok"` anche con MySQL spento. Gli script
`aggiorna-server.bat` e `carica-installazione.bat` si fidano di quella sonda per
dire "servizio ripartito": un aggiornamento che rompe la connessione al DB viene
dichiarato riuscito.

- [x] **FATTO il 14/08/2026** — `/api/health/ready` in [Program.cs](ATEC.PM.Server/Program.cs):
      200 `{status: "ready"}` se il database risponde, **503** `{status: "degraded"}` se no.
      `/api/health` resta com'era («il processo è vivo»), perché gli script leggono da lì
      `version` per il messaggio finale.
      **Nessun pacchetto aggiunto**: `AspNetCore.HealthChecks.MySql` non serviva, la sonda è
      `DbService.ProvaDatabaseAsync()` — una `SELECT 1`, dieci righe.
- [x] La sonda sta in `DbService` e **non** dentro l'endpoint, apposta: così si prova nei test
      in tutti e due i casi senza dover spegnere il MySQL di chi sviluppa.
- [x] Niente percorsi di rete su `/ready`, come previsto: uno share giù farebbe fallire
      l'aggiornamento con il database perfettamente sano.
- [x] Script aggiornati — `Test-ServerVivo` in [applica-aggiornamento.ps1](deploy/applica-aggiornamento.ps1)
      e l'attesa di [install-server.ps1](deploy/install-server.ps1) ora interrogano `/ready`.
      🪤 **Ripiego sul 404 da non togliere**: in caso di rollback torna in servizio la versione
      PRECEDENTE, che `/ready` non ce l'ha; senza quel ramo un ripristino riuscito verrebbe
      dichiarato fallito. Il **503** invece non ricade: è un guasto vero e deve far scattare il
      rollback.

**Fatto**: 2 test — con database raggiungibile la sonda tace, con la connessione puntata su una
porta dove non c'è nessuno la sonda lo dice. Il caso «server su, database giù» era proprio quello
in cui `/api/health` rispondeva `ok` e il deploy si dichiarava riuscito.

### B3 — Validazione dichiarativa sui DTO di scrittura
Zero DataAnnotations su 53 file di DTO: ogni action rivalida a mano, in modo diverso.

- [x] **Traduttore fatto il 14/08/2026** — [RispostaValidazione.cs](ATEC.PM.Server/Middleware/RispostaValidazione.cs),
      agganciato a `InvalidModelStateResponseFactory`. Va **prima** delle annotazioni: senza, la
      prima validazione che scatta risponde nel formato `ProblemDetails` che il client non sa
      leggere. Toglie anche il `$.` con cui il binder nomina i campi del corpo JSON.
- [x] **Primi DTO annotati**: `DepartmentSaveRequest`, `MaterialCategorySaveRequest`.

### 🧭 Il criterio (misurato, non intuito) — vale per i prossimi DTO

Una ricerca sui controller ha cambiato l'idea di partenza: ci sono già **154 `return BadRequest`**
e **90 controlli su stringhe vuote**, scritti a mano e **in italiano** («Codice obbligatorio»).

1. **Niente `[Required]` dove il controller valida già.** L'annotazione lo sostituirebbe con
   *«The Code field is required»* — in **inglese**, su un gestionale tutto italiano. Peggiorerebbe
   quello che oggi funziona.
2. **`ErrorMessage` in italiano, sempre.** È congelato da un test che fallisce se in un messaggio
   compare la parola «field».
3. **Il valore sta nei limiti, non negli obblighi**: solo 20 controlli su valori negativi in tutto
   il progetto, e **nessun limite di lunghezza**. Oggi un testo più lungo della colonna arriva a
   MySQL e torna come «Data too long for column» → per chi salva è «Errore interno del server».
4. **`[MaxLength]` e `[Range]` ricalcano la colonna vera**, non un numero inventato:
   `departments.code` è `VARCHAR(10)`, `hourly_cost` è `DECIMAL(8,2)` (quindi massimo 999.999,99).
   Le colonne si leggono nelle `CREATE TABLE` di `DbService.cs`.
5. Sono **sicure per costruzione**: non rendono obbligatorio niente di nuovo, limitano solo valori
   che oggi finirebbero comunque in errore — più tardi e in modo incomprensibile.

**Fatto**: 2 test — un reparto con codice da 30 caratteri e costo orario negativo viene fermato
**prima** del database con messaggi italiani; un reparto normale passa senza errori (le
annotazioni non stringono l'uso vero).

**Da fare quando si passa di lì** (stesso criterio, nessuna fretta): gli altri DTO `*SaveRequest`
con importi e testo libero — preventivi, SAL, trasferta, DDP.

---

## BLOCCO D — Sicurezza operativa (mezza giornata)

### D1 — Root MySQL: sviluppo e rotazione, non "creare l'utente"
In **produzione** l'utente dedicato c'è già: [`install-server.ps1`](deploy/install-server.ps1)
crea `atecpm`@`localhost` con `GRANT ALL` solo su `atec_pm`, e
[GUIDA-SERVER-LAN.md](../guide/GUIDA-SERVER-LAN.md) §9 lo conferma. I segreti di produzione
sono cifrati con DPAPI (`ProtectedConfigHelper`). `ConnectionStrings:Default` con
`User=root` è il file **di sviluppo** nel repo.

Il rischio vero è doppio: (1) il PC di sviluppo e chiunque cloni il repo parla da
`root`; (2) `GRANT ALL` in produzione è più largo del necessario; (3) la password
di `root` MySQL è ancora quella storica (TODO del 22/05/2026).

- [x] **FATTO il 14/08/2026** — verificato su ATEC-FC: la connection string cifrata usa
      `atecpm`, non `root`. Utente non ricreato.
- [x] **FATTO il 14/08/2026** — privilegi di `atecpm` stretti da `GRANT ALL` a
      `SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, CREATE VIEW,
      REFERENCES` su `atec_pm.*` (CREATE/ALTER servono: le migrazioni girano
      all'avvio; `DROP` serve a `CREATE OR REPLACE VIEW`).
- [ ] **RESTA** — in sviluppo: utente dedicato anche in `appsettings.json`
      (oggi c'è ancora `User=root;Password=Atec2005`, riga 19, e quella password **in
      locale è tuttora valida**). Se i segreti sono già cifrati, cancellare il file cifrato
      vecchio perché `ProtectedConfigHelper` altrimenti tiene la password precedente.
- [x] **FATTO il 14/08/2026** — password di `root` MySQL ruotata **sul server**; la nuova sta
      solo in `C:\ATEC_PM\Config\credenziali.txt`, in nessun file del progetto.

**Fatto quando**: ~~sviluppo e~~ produzione non usa `root` per l'app; `root` ha una
password nuova assente dal repo; `atecpm` ha il GRANT stretto. **Manca solo lo sviluppo.**

### D2 — Completare il lockout del login (per IP, non da zero)
`/api/auth/login` **non è senza limite**. [`AuthController`](ATEC.PM.Server/Controllers/AuthController.cs)
ha già lockout in memoria: 5 tentativi falliti, 5 minuti, per **username**, con
risposta 429 (righe 26-29). Mancano due cose:

1. il limite per **IP**: si può ancora spruzzare tanti username diversi dalla
   stessa macchina (VPN / LAN);
2. il contatore è un `static ConcurrentDictionary` **in memoria**: si azzera a
   ogni riavvio del servizio — cioè **a ogni deploy**, e chi attacca può
   provocarlo. Un `AddRateLimiter` non risolve nemmeno questo (è anch'esso in
   memoria): se si vuole un blocco che sopravviva al riavvio, il contatore va
   su tabella.

- [x] **FATTO il 14/08/2026** — niente `AddRateLimiter` (è anch'esso in memoria: avrebbe
      aggiunto un pacchetto senza risolvere il riavvio). Aggiunto un secondo contatore
      `_loginAttemptsByIp` in [AuthController.cs](ATEC.PM.Server/Controllers/AuthController.cs):
      **30 tentativi falliti / 5 minuti per indirizzo**, in aggiunta ai 5 per username.
      Soglia alta apposta: in ufficio si esce tutti dallo stesso NAT, e un limite stretto
      per IP chiuderebbe fuori l'azienda intera per colpa di chi sbaglia la password.
- [x] **FATTO** — ogni fallimento logga a Warning username, IP e i due contatori;
      il superamento per IP logga a parte prima del 429.
- [ ] **RESTA (non bloccante)**: il contatore è in memoria e si azzera a ogni riavvio del
      servizio, cioè **a ogni deploy**. Per un blocco che sopravviva serve una tabella.

### D3 — CORS ristretto
`AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` su tutta l'API. La SPA è servita
dallo stesso host, quindi in produzione l'unico origin reale è `http://192.168.2.150:5150`
(più `http://localhost:5173` in sviluppo).

- [x] **FATTO il 14/08/2026** — `Security:AllowedOrigins` in [Program.cs](ATEC.PM.Server/Program.cs):
      lista chiusa da configurazione, presente in `appsettings.json` (localhost 5173/5150/5151)
      e in [appsettings.server.template.json](deploy/appsettings.server.template.json).
      🪤 **Se la chiave manca si torna al comportamento di prima (tutti gli origin)** con un
      `Log.Warning`: un file di configurazione vecchio dopo un aggiornamento avrebbe altrimenti
      chiuso fuori l'intera azienda. Il warning è l'unico posto dove si legge che sta succedendo.

---

## BLOCCO C — Test automatici (3-4 giorni)

**Obiettivo**: non coprire tutto. Coprire i tre punti dove un errore **non si vede**
e costa caro. Con 87 migrazioni e i calcoli economici, oggi ogni deploy è validato
solo a mano.

- [x] **Creato `ATEC.PM.Tests`** (xUnit) e aggiunto a `ATEC.PM.sln` — 14/08/2026.
      (`ATEC.PM.Web` è un progetto stub vuoto: lasciato stare, non riusato.)
      - `Infrastruttura/DatabaseDiProva.cs` — database usa-e-getta, creato e distrutto dal test.
        **Nessuna credenziale nel repo**: la stringa di connessione arriva da `ATEC_PM_TEST_CS`
        oppure da `appsettings.Development.json` del server, così i test non aggiungono un
        secondo posto da cui una password possa uscire. `atec_pm` non viene mai toccato.
      - `Infrastruttura/FactRichiedeMySqlAttribute.cs` — senza MySQL i test si **saltano**
        invece di fallire: un rosso che significa «manca il database su questa macchina» è il
        tipo di rosso che fa smettere di guardare i test.

  ```bash
  dotnet test ATEC_PM/ATEC.PM.Tests/ATEC.PM.Tests.csproj
  ```

### ✅ C1 — Catena delle migrazioni — **FATTO il 14/08/2026**

`Migrazioni/MotoreMigrazioniTests.cs` — **9 test, tutti verdi** (47 s, girano su database
usa-e-getta):

| Test | Cosa protegge |
|---|---|
| Database vuoto → schema completo | le 87 versioni registrate, 119 tabelle |
| Buchi sotto il massimo (v12, v85) | sanati e **non** rieseguiti (16 delle 87 farebbero danni) |
| Database a v80 | v81…v87 **eseguite davvero**, non timbrate |
| Migrazione rotta | l'avvio si **interrompe** |
| …con `StopOnError=false` | prosegue, e la versione resta **pendente** |
| Registro azzerato su DB popolato | avvio fermato invece di rigiocare tutto sui dati veri |
| Versione futura mancante | **mai** timbrata dal backfill |
| Avvio ripetuto | non cambia niente |
| **Vista timesheet e fasi locali** | le ore delle fasi locali restano nel consuntivo (difetto v69) |

L'ultimo non guarda il testo della vista ma il **comportamento**: inserisce commessa, persona,
fase locale (`phase_template_id IS NULL`) e 7,5 ore, poi verifica che la riga esca dalla vista.
Efficacia dimostrata con una prova di mutazione: ricreando la vista con la `JOIN` **INNER** —
quella che girava in produzione fino alla v69 — la riga sparisce (0 ore invece di 7,5) e il test
fallisce. Un test che non sa fallire non protegge niente.

*(Nota storica: non asserire mai `MAX(version) = LatestSchemaVersion` — è la metrica del buco
che A0 ha chiuso. La verità è l'**insieme** delle righe applicate.)*

Il «database esistente» è ricreato con una **fixture sintetica** (schema completo, poi si
tolgono le righe di `schema_migrations` sopra la versione voluta), non con un dump di
produzione: nel repo non c'è, e un dump reale sarebbe anagrafica e commesse aziendali.
Se un giorno servisse quello vero: sanitizzato e **fuori dal git**.

Quando arriverà A2 con `success`/`error_text`, va aggiunta l'asserzione `success = 1`.

### ✅ C2 — Motore dei permessi — **FATTO il 14/08/2026**

> ⚠️ **Il piano indicava il file sbagliato.** [PermissionEngine.cs](ATEC.PM.Shared/PermissionEngine.cs)
> è il motore del **client WPF ritirato** il 20/07/2026: oggi non è referenziato da nessun file
> di codice (solo da documenti e dal grafo). Ha per giunta il fallback **opposto** a quello
> attuale — «funzione non registrata → accesso libero» — quindi testarlo avrebbe dato sicurezza
> su regole che non governano più niente, lasciando scoperto il motore vero.
> Il motore in esercizio è [FeatureAccessService.cs](ATEC.PM.Server/Services/FeatureAccessService.cs).
> *(`PermissionEngine` è codice morto: candidato alla rimozione, vedi TODO.)*

`Permessi/RegoleAccessoTests.cs` — **18 test, 18 millisecondi** (puri, nessun database).
Coprono `ConcedeAccesso` / `ConcedeScrittura` / `Negato`, che sono la **fonte di verità unica**:
li usa il motore a ogni richiesta *e* l'invariante «non ci si chiude fuori» di
`PermissionAdminService`. Casi: riga piena / in lettura / di diniego, nessuna riga, solo jolly,
jolly su funzione mai vista, **diniego specifico che vince sul jolly** (la regola che rende
possibili le eccezioni: chi vede tutto meno una cosa), riga che limita il jolly, riga piena che
vince sul jolly negato, maiuscole/minuscole su chiavi e valori.

`Permessi/UtenzaAttivaTests.cs` — **5 test**. «Chi non è più in forza esce subito»
(`IsUtenteAttivo`, chiamato in `OnTokenValidated` su ogni richiesta e su ogni hub). Compreso il
comportamento della cache a 30 secondi e il fatto che `DimenticaPersona` la annulla, così fra il
gesto e l'effetto non passa mezzo minuto. Un difetto qui non si vede da nessuna parte:
l'applicazione funziona benissimo, per chi non dovrebbe più entrarci.

Restano fuori (richiedono impianto maggiore, non bloccanti): lista bianca Contabilità e
`RestaUnAmministratore`, che è privato e passa da `EseguiConGaranzia`.

### ✅ C3 — Calcoli economici — **FATTO il 14/08/2026**

**29 test, 17 millisecondi** (tutti puri). Le regole sono state estratte dal codice reale e
ricontrollate una per una, non dedotte da come «dovrebbero» funzionare: un test che codifica
un'aspettativa inventata è peggio di nessun test.

`Calcoli/BilancioCommessaTests.cs` — il filo comune è che **«non calcolato» e «calcolato, fa
zero» sono due cose diverse** («—» contro «0,00 €»):
- ordine senza importi → nessun totale; ordine **da 0,00 € → totale 0**, che è un dato. La
  semplificazione ovvia (`lines.Sum(l => l.Amount ?? 0)`) farebbe comparire un **Margine di
  Sicurezza in rosso pari a tutto il costo di vendita** su commesse che l'ordine non ce l'hanno;
- riga **a forfait** (senza quantità): il costo unitario vale come importo, e il moltiplicatore
  si applica lo stesso. Riscriverla in `Quantity * UnitCost` azzererebbe tutte le forfettarie;
- quantità 0 → 0, costo unitario mancante → nessun importo, lucchetto che vince sul calcolo;
- righe **senza sezione**: pesano sul totale del foglio ma su nessuna sezione — è la differenza
  che il Riepilogo mostra come «Lavorazioni Officine non classificate»;
- sezione senza righe → nessun totale, non zero.

`Calcoli/TrasfertaTests.cs` — le due regole che costano soldi veri:
- **l'indennità resta fuori dalle spese ricaricabili**: è l'unico punto in C# dove la
  separazione è scritta. Metterla dentro non si vedrebbe finché il K resta 1,000 (oggi lo è
  sempre), e salterebbe fuori il giorno che il ricarico viene riacceso, con l'indennità gonfiata
  su tutte le commesse;
- sulla riga che arriva dal Timesheet **lo zero dei giorni è una decisione, non un vuoto**: con
  due fasi di cantiere nello stesso giorno il motore mette 1 sulla prima riga e 0 sulle altre.
  «Pulire» `TravelDays ?? …` in `TravelDays > 0 ? … : …` farebbe ricalcolare dalle date e
  **raddoppierebbe le giornate**, quindi l'indennità, in silenzio.

`Calcoli/StatoCommessaTests.cs` — **sospesa non è chiusa**: `ON_HOLD` fuori dall'elenco, o tutte
le commesse sospese diventerebbero di sola lettura con un messaggio che parla di «commessa
chiusa». Più la normalizzazione di spazi e maiuscole.

> **Trappola trovata e disinnescata**: in `FeatureAccessService.ConcedeScrittura` il commento
> diceva «decide il jolly, **che è sempre pieno**» mentre il codice, tre righe sotto, il caso
> `READ` lo gestisce eccome. Chi si fosse fidato del commento e avesse semplificato in un
> `ContainsKey` avrebbe dato la scrittura sull'intero gestionale a chi ha un jolly di sola
> lettura, senza che nessuna schermata cambiasse. Commento corretto e comportamento congelato
> in `RegoleAccessoTests`.

### Cosa resta di C (non bloccante)

L'estrazione delle regole ne ha prodotte **18 che richiedono un database** e non sono ancora
coperte. Le più utili, in ordine:
- ore di una **fase locale** nel consuntivo sotto «NON ASSEGNATO», e ore su **Extra Lavoro** che
  non contano (a meno che il PM le rimetta dentro);
- **una sola ora orfana spegne il budget digitato a mano** della commessa;
- SAL: riga **senza %IVA vale 0%, non 22%**; la modifica che non manda la %IVA la cancella;
  fattura mai emessa e già scaduta che si segnala come incasso;
- trasferta: **una riga a mano vince sul Timesheet** (e il perché è sottile: non ha la data);
  solo le ore «da cliente» fanno trasferta;
- permessi: lista bianca Contabilità e «togliere l'ultimo amministratore annulla tutto».

### ✅ Cancello sul deploy — **FATTO il 14/08/2026**

`Invoke-TestAutomatici` in [_comune.ps1](deploy/_comune.ps1), chiamata da
[aggiorna-server.ps1](deploy/aggiorna-server.ps1) **prima** di `Publish-Server`: test rossi →
l'aggiornamento si ferma e sul server non arriva niente. Sta prima della compilazione perché
`npm build` + `publish` durano minuti. Via d'uscita per le emergenze:
`aggiorna-server.bat -SenzaTest`.

Collaudato in tutti e due i versi — un cancello che non sa fermare non è un cancello:

| Prova | Esito |
|---|---|
| 61 test verdi | l'aggiornamento prosegue ✅ |
| un test reso rosso apposta | **bloccato**, con il messaggio «niente è stato toccato sul server» ✅ |
| progetto dei test assente | passa oltre senza rompere il deploy ✅ (per costruzione) |
| PC senza MySQL | i test che lo richiedono si saltano da soli, gli altri girano ✅ |

**Fatto quando**: ~~`dotnet test` gira su una macchina pulita e i tre gruppi passano;
il comando entra in `aggiorna-server.bat` prima della pubblicazione.~~ ✅ tutto fatto.

---

## BLOCCO A — Runner di migrazioni (3 giorni, rischio medio)

**Obiettivo**: rendere le migrazioni verificabili, non riscriverle.

### Il difetto da chiudere per primo
`GetSchemaVersion` legge `MAX(version)`. Ogni blocco è avvolto in un
`try/catch` che logga un warning **e prosegue** (90 occorrenze di "non bloccante").
Conseguenza concreta:

> Se la **v85 fallisce** (nessuna riga scritta in `schema_migrations`) ma la **v86
> riesce** (riga 86 scritta), `MAX(version)` diventa 86. Al riavvio, `currentVersion = 86`
> non è `< 85`: **la v85 non verrà mai più ritentata**, e l'unica traccia è un
> warning in un log ruotato ogni 30 giorni.

Non è teorico: è lo stesso meccanismo che ha fatto saltare la v66 in produzione il
04/08/2026 (documentato nel commento a `LatestSchemaVersion`).

- [x] **A0 — Correzione minima** — ✅ **fatta il 14/08/2026**, dettaglio in fondo a questo blocco.

### ✅ A0 — cosa è stato fatto (14/08/2026)

Tutto dentro [DbService.cs](ATEC.PM.Server/Services/DbService.cs). Nessuna migrazione
riscritta, nessun comportamento funzionale cambiato.

1. **`MAX(version)` → insieme delle versioni.** Nuovo `GetAppliedVersions()` che legge
   `SELECT version FROM schema_migrations` in un `HashSet<int>`; gli 87 cancelli sono
   passati da `if (currentVersion < N)` a `if (!applied.Contains(N))`. Una migrazione
   fallita resta pendente e viene ritentata al riavvio, invece di essere scavalcata
   dalla prima che riesce dopo di lei.
2. **`BackfillLegacyVersions()` — la parte che il piano non prevedeva, ed è la più
   importante.** Su un database già in esercizio una versione mancante *sotto* il
   massimo è ambigua: fallita davvero, oppure passata senza registrarsi? Il codice non
   può distinguerle, e **rieseguirla sarebbe la cosa pericolosa**: la v75, per dire, fa
   `DELETE FROM ddp_status_transitions WHERE ddp_type='OFFICINA'` e riscrive la matrice
   — rieseguita oggi cancellerebbe quello che è stato sistemato a mano da «Conf. DDP»
   dopo l'08/08. Quindi quelle versioni vengono **marcate come applicate senza essere
   eseguite**, con descrizione `backfill 14/08/2026: …`, e un `LogWarning` che le elenca.
   Quel warning è l'unico posto dove si legge che su quel database, in passato, qualcosa
   può non essere passato: **vanno verificate a mano**.
3. **Fallimento = avvio interrotto** (`OnMigrationFailed`). Con la via d'uscita
   `Migrations:StopOnError=false` in `appsettings.json` si torna al comportamento
   tollerante di prima, per rimettere in piedi il server in azienda senza aspettare la
   correzione. **Eccezione**: sul bootstrap di un database vuoto resta tollerante, perché
   lì le migrazioni girano su uno schema che le contiene già e un loro inciampo non
   significa niente.
4. **Controllo di chiusura.** Se dopo l'esecuzione manca ancora qualche versione, si
   ferma (o logga un `Error`): un blocco che lavora ma dimentica la propria riga
   tornerebbe a girare a ogni avvio per sempre, e con la vecchia regola nessuno se ne
   sarebbe accorto.
5. **`EnsureSchemaMigrationsTable`** ora garantisce le colonne `description`/`applied_at`
   con `AddColumnIfMissing`: `CREATE TABLE IF NOT EXISTS` non tocca una tabella che
   esiste già, e su un database antico ogni registrazione sarebbe fallita con
   «Unknown column» — cioè nessuna migrazione sarebbe MAI risultata applicata.

I 3 `catch` **annidati** (v35 ALTER, v81 le due FK) sono rimasti tolleranti apposta:
sono tentativi opzionali dentro una migrazione, non la migrazione.

**Perché non era teorico.** Nei log di questa macchina, `C:\ATEC_PM\Logs`:
la **v75** è fallita l'08/08 alle 14:48 («Illegal mix of collations») ed è stata
recuperata alle 14:50; la **v80** è fallita alle 23:11 («Unknown column») e recuperata
alle 23:17. Si sono salvate **solo perché** in quei minuti nessuna migrazione successiva
è passata: fosse entrata una v76 (o v81) nel frattempo, `MAX` sarebbe salito e quelle due
sarebbero rimaste perse per sempre, in silenzio.

**Collaudo** (banco di prova su database usa-e-getta, poi eliminati — il database di
sviluppo non è stato toccato):

| Scenario | Esito |
|---|---|
| Database vuoto → bootstrap completo | 87 versioni registrate, 119 tabelle ✅ |
| Buchi sotto il massimo (v12 e v85 tolte, MAX=87) | sanati e marcati `backfill`, **non** rieseguiti ✅ |
| Database a v80 come la produzione | v81…v87 **eseguite davvero**, non backfillate ✅ |
| Migrazione rotta (tabella della v81 rinominata) | avvio **interrotto**; con `StopOnError=false` prosegue e logga che la v81 è rimasta pendente ✅ |
| Riavvio a schema completo | non fa nulla ✅ |
| Registro azzerato su DB popolato (ripristino a metà) | avvio **fermato** invece di rigiocare le 87 su dati veri ✅ |
| Buco *sopra* il cutoff (v88 fallita, v89 passata) | **non** timbrata: resta pendente e verrà ritentata ✅ |

**Audit avversariale** (12 agenti su tutti gli 87 blocchi, tre lenti indipendenti: perdita dati,
continuità di servizio, errore di analisi). Verdetto unanime: **nessuno è riuscito a refutare A0**,
e il motivo è il backfill. Il dato che conta: **16 delle 87 migrazioni farebbero danni se
rieseguite** — v7, v22, v26, v39, v40, v51, v53, v54, v55, v58, v59, v65, v75, v85, v86, v87.
Qualche esempio verificato nel codice: la **v22** riporta a 22% l'IVA delle righe SAL messe a IVA
vuota (esenti, note di credito) falsando prospetto SAL e Flusso di Cassa; la **v26** fa risorgere
nel Pannello Lavorazioni le voci eliminate a mano; la **v55** duplica la cronistoria stati DDP
(quelle INSERT non hanno nessuna guardia e la tabella non ha chiave unica); la **v39/v40**
riportano la matrice degli stati al default di fabbrica cancellando quanto configurato da Conf. DDP.
Un A0 «ingenuo» — leggi l'insieme e riesegui i buchi — avrebbe potuto scatenarli tutti al primo
avvio. Con il backfill l'insieme dei blocchi che girano davvero resta **esattamente** quello di
prima (`{v : v > max}`), quindi nessuno di quei 16 è raggiungibile.

Dall'audit sono arrivate tre correzioni, tutte applicate:
- **`LegacyCutoffVersion = 87`** — il backfill vale solo per le versioni storiche. Senza, una
  migrazione *futura* fallita e scavalcata da una successiva sarebbe stata timbrata come applicata
  al riavvio dopo: il difetto di A0 rientrato dalla finestra (scenario 7 del collaudo).
- **Registro azzerato su database popolato** → avvio fermato. È raggiungibile davvero:
  `FullBackupService.RipristinaDatabase` fa TRUNCATE di tutte le tabelle, `schema_migrations`
  compresa, e ingoia i fallimenti di riga; un ripristino interrotto a metà lascia dati veri e
  registro vuoto, e le 87 migrazioni sarebbero ripartite sui dati di produzione. Il difetto è
  **preesistente ad A0** (con `MAX=0` accadeva identico), ma ora è chiuso.
- **Log leggibile del fallimento** — `LogError` prima del `throw`, e in [Program.cs](ATEC.PM.Server/Program.cs)
  `Log.Fatal` + `CloseAndFlush` al posto di `Console.WriteLine`: come servizio Windows stdout non
  va da nessuna parte, e senza questo il motivo del mancato avvio sarebbe invisibile.

**Fuori da `DbService`**, sempre dall'audit:
- [applica-aggiornamento.ps1](deploy/applica-aggiornamento.ps1) — `Start-Service` era nudo sotto
  `$ErrorActionPreference='Stop'`: un servizio che non parte uccideva lo script **prima** del
  ripristino automatico, lasciando il gestionale aggiornato e spento. Ora è in try/catch, così
  scatta il ritorno alla versione precedente. È il caso che A0 rende più probabile.
- `Migrations.StopOnError` aggiunto a [appsettings.server.template.json](deploy/appsettings.server.template.json):
  la manopola d'emergenza mancava dal file che il server legge davvero.
- [GUIDA-SERVER-LAN.md](../guide/GUIDA-SERVER-LAN.md) §8.1 — nuovo paragrafo «il servizio non parte e nel
  log c'è una migrazione FALLITA», con i comandi da dare.

**Da sapere al prossimo deploy.** All'ultimo deploy noto (09/08) la produzione era a
**v80** — da confermare sul server prima di pubblicare:

```bash
mysql -u atecpm -p -D atec_pm -e "SELECT COUNT(*) righe, MAX(version) massimo FROM schema_migrations;"
```

Se il conteggio è inferiore al massimo, ci sono buchi storici: il backfill li chiuderà e
li elencherà nel log, e vanno verificati a mano. In ogni caso al primo avvio con questa
build girano davvero le v81…v87, e adesso **se una fallisce il servizio non parte**
invece di partire monco. Backup completo prima, e `Migrations:StopOnError=false` a
portata di mano come via d'uscita.

### ✅ A1 — FATTO il 15/08/2026: le migrazioni sono classi, `DbService` non le contiene più

**Com'è adesso.** Una migrazione = un file in [Migrations/](ATEC.PM.Server/Migrations/), col nome
`MNNN_Cosa.cs`. Aggiungerne una vuol dire creare quel file: **nessuna costante da alzare, nessun
elenco da aggiornare, `DbService.cs` non si tocca**.

- [IMigrazione.cs](ATEC.PM.Server/Migrations/IMigrazione.cs) — `Versione`, `Descrizione`,
  `Applica(conn, log)`.
- [MigrationRunner.cs](ATEC.PM.Server/Migrations/MigrationRunner.cs) — le scopre dall'assembly, le
  ordina, applica le pendenti. **La riga in `schema_migrations` la scrive lui**, dopo il successo:
  prima ogni blocco scriveva la propria, e chi se ne dimenticava tornava a girare a ogni avvio per
  sempre.
- [AiutiMigrazione.cs](ATEC.PM.Server/Migrations/AiutiMigrazione.cs) — gli attrezzi condivisi fra
  le migrazioni e `DbService` (`AddColumnIfMissing`, la vista `v_timesheet_with_section`, le
  sezioni delle fasi, `SeminaChiaviPerLivello`, la conversione trasferta della v68). Erano membri
  privati di `DbService`, che una classe esterna non vede.

**Cosa è stato spostato:** 86 blocchi `if (!applied.Contains(N))` (2.630 righe) più la v87 già
fatta il 14/08 — la trasformazione l'ha fatta uno **script deterministico**, non a mano: il corpo
del `try` è diventato `Applica`, la riga di registrazione è sparita (la scrive il runner),
`_logger` è diventato `log`. I corpi sono stati poi **riconfrontati a macchina** con quelli di
prima: 86 su 86 identici carattere per carattere, descrizioni comprese.

**Numeri:** `DbService.cs` da **5.128 a 1.646 righe** (−68%). Il metodo `ApplyVersionedMigrations`
da 3.150 righe a 50: conta le pendenti, lancia il runner, controlla che dopo non resti nessun buco.

**Sparito nel frattempo**
- `LatestSchemaVersion` — la costante da alzare a mano, quella che dimenticata è costata la v66 in
  produzione (applicata in sviluppo, saltata al deploy). Adesso la versione più alta la dicono i file.
- `OnMigrationFailed` e gli 86 `try/catch` — la gestione dell'errore è una sola, dentro il runner.
- `VerificaOrdineConLegacy` — serviva a sorvegliare lo spostamento graduale (le classi dovevano
  stare sopra i blocchi rimasti). Finito lo spostamento non ha più niente da sorvegliare: tolta
  con i suoi due test, per non lasciare in giro codice morto.
- Il lato istanza di `SalDbService` e `MilestonesDbService`: le loro `InitTables`/`Seed*` sono
  statiche (usavano solo la connessione), perché una migrazione non ha un `DbService` da passare
  al costruttore.

**Guardie nuove** — un avvio che non applica niente non deve poter passare per «tutto a posto»:
- zero migrazioni trovate per riflessione → **avvio interrotto** (pubblicazione con trimming,
  assembly sbagliato: senza controllo il log direbbe «schema aggiornato»);
- serie con buchi (un file cancellato, un numero saltato) → **test rosso**, non scoperta al deploy;
- nome del file diverso dalla versione dichiarata (`M073_…` che non è la v73) → test rosso.

**I test non hanno più numeri scritti a mano**: `UltimaVersione` la chiedono al runner. Il 14/08 è
bastata una v88 scritta in un'altra sessione per far diventare rossi due test che non c'entravano.

**Da fare al prossimo deploy**: è un refactoring, lo schema non cambia. Ma è la prima volta che le
87 migrazioni girano da qui, quindi backup completo prima e `Migrations:StopOnError=false` a
portata di mano.

### A1 — il piano originale
Molte delle 87 "migrazioni semplici" non sono un `ALTER` nudo: usano
`AddColumnIfMissing` e controlli su `information_schema`. Estrarle in `.sql`
perde quell'idempotenza. E **18 migrazioni su 87** mettono DDL e conversione
dati sotto lo stesso numero di versione (v9, v10, v12, v13, v21, v23, v27,
v35, v40, v51, v55, v59, v60, v61, v62, v63, v68, v84): la v68, per esempio,
crea `project_cost_travel_rows` e nella stessa riga di versione chiama
`ConvertLegacyTravelFields`. Non sono separabili in due file senza cambiare
la semantica. *(La v87 invece è data-migration pura sui permessi: nessun DDL.)*

- [x] **FATTO 15/08/2026** — una classe C# per migrazione (`Versione`, `Descrizione`,
      `Applica(conn, log)`), anche da una riga. Niente `.sql` embedded: quasi nessuna
      migrazione è SQL puro, e i blocchi con logica vera (`ConvertLegacyTravelFields`,
      `InsertMigratedCalcRow`, `SeminaChiaviPerLivello`) devono restare C# in ogni caso.
- [x] **FATTO 14/08/2026** — `MigrationRunner`: scopre gli step, li ordina, applica i pendenti.
      **`LatestSchemaVersion` è sparita** (15/08): la versione più alta è il `Max` dell'elenco
      scoperto, quindi non c'è più una costante da ricordarsi di alzare.
- [ ] Lo schema di base (le 94 `CREATE TABLE`) diventa `Schema/baseline.sql` generato
      da `mysqldump --no-data`. `EnsureModuleTables` resta com'è: i moduli si portano
      dietro le proprie tabelle e va bene così.
- [ ] **Stamp sul fresco**: dopo il baseline + seed, scrivere in `schema_migrations`
      le versioni `1 … N` e **non** ririprodurre le 87. Altrimenti un database nuovo
      rilancia le data-migration sul vuoto (oggi il seed crea ancora `AMM` e ci pensa
      la v66 a toglierlo: prima dello stamp il seed del bootstrap deve essere già
      lo stato finale). Su un DB esistente le data-migration restano, perché i dati
      vecchi ci sono.

### ✅ A2 — FATTO il 15/08/2026: sicurezza dell'esecuzione

- [x] **Lock esclusivo** — `GET_LOCK('atec_pm_migrate:<database>', n)` in
      [DbService.InitDatabase](ATEC.PM.Server/Services/DbService.cs), rilasciato in `finally`,
      sulla **stessa connessione** che fa tutto il lavoro (il lock di MySQL è della sessione: su
      un'altra connessione non proteggerebbe niente). Se non lo ottiene, **l'avvio si interrompe**.
      Lo prende anche il **ripristino da backup**, che è l'altro processo che riscrive lo schema
      da cima a fondo — e gira a server acceso.
      - Il nome contiene il database perché `GET_LOCK` è di tutto il server MySQL: con un nome
        fisso i database di prova e quello di lavoro si bloccherebbero a vicenda.
      - 🪤 `commandTimeout` esplicito: il driver interrompe i comandi a 30 s per conto suo, e
        senza quel parametro un'attesa più lunga uscirebbe come un timeout di rete invece che
        col messaggio «un altro processo sta migrando».
      - Attesa **30 s, non 60**: lo script di aggiornamento aspettava il server ~60 s e poi
        ripristinava. Sessanta secondi di coda se lo mangiavano tutto e trasformavano un'attesa
        in un rollback. Regolabile con `Migrations:LockTimeoutSeconds`.
- [x] **Esito e durata di ogni migrazione** — `schema_migrations` ha `success TINYINT(1) NOT NULL
      DEFAULT 1`, `error_text TEXT`, `duration_ms INT`. Una migrazione fallita **lascia la sua
      riga** con l'errore dentro: prima l'unica traccia era un log che si ruota dopo 30 giorni, e
      chi apriva il registro il giorno dopo vedeva solo un buco.
      - `success = 0` vuol dire **non applicata**: `GetAppliedVersions`, `GetSchemaVersion` e il
        manifest del backup filtrano `success = 1`. Senza il filtro un fallimento verrebbe letto
        come riuscita — il difetto che A0 ha chiuso, rientrato dalla finestra.
      - `INSERT … ON DUPLICATE KEY UPDATE` e non `INSERT IGNORE`: la chiave primaria è `version`,
        quindi al ritentativo la riga del fallimento c'è già e va **sovrascritta**. Con
        `INSERT IGNORE` una migrazione riparata resterebbe segnata rotta per sempre e l'avvio si
        fermerebbe a ogni riavvio.
      - Le colonne le crea `EnsureSchemaMigrationsTable`, non una migrazione: sarebbe un cerchio.
      - Il backfill non timbra più le versioni che hanno una riga: un fallimento **registrato**
        non è l'ambiguità storica che quel meccanismo esiste per sanare.
- [x] **Fallimento = avvio interrotto**, con l'eccezione delle **pulizie**: `IMigrazione.Facoltativa`
      (default `false`). Marcate 7 su 88 — v22, v24, v31, v32, v46, v47, v65 — tutte pulizie di
      dati da cui non dipende niente. Se falliscono: warning, riga con `success = 0`, avvio che
      prosegue, ritentativo al riavvio dopo.
      - **Escluse dopo verifica**: v70 (la v73/v74 propagherebbero l'id di sezione rotto che lei
        azzera, e le ore finirebbero su una sezione inesistente **in silenzio**), v6 (le causali
        che cancella entrano nella matrice INIZIO della v40), v2 (tocca le chiavi dei permessi).
        Non è una categoria, è una verifica caso per caso.
      - Il meccanismo non basta implementarlo nel runner: anche il controllo di chiusura di
        `ApplyVersionedMigrations` deve tollerare quelle assenze, o l'avvio fallirebbe lo stesso.
- [x] **Le viste fuori dalle migrazioni** — `DbService.EnsureViews`, a **ogni avvio, in tutti gli
      ambienti**, **dopo** le migrazioni (la vista nomina tabelle che nascono da loro). Tolta la
      creazione dal bootstrap e dalle migrazioni v69/v72/v74/v80.
      - Non è solo pulizia: quelle quattro eseguivano la definizione di **oggi** dentro una
        migrazione **vecchia**. Su un database sotto la v80 — un backup di mesi fa ripristinato su
        build nuova — la v69 falliva con «Table doesn't exist» e il server non partiva.
      - E il ramo produzione non ricreava la vista **mai**: usciva prima. Sommato al backfill (che
        timbra senza eseguire) e ai pacchetti di backup (che contengono solo le TABELLE, non le
        viste), la vista in vigore poteva essere una qualsiasi. È il difetto che ha fatto contare
        male le ore del Bilancio per mesi.

**10 test nuovi** (90 in totale): il lock occupato ferma l'avvio, il lock viene rilasciato,
l'errore resta scritto, il ritentativo sovrascrive la riga, la durata viene registrata, una
facoltativa fallita non ferma l'avvio, una normale sì, l'elenco delle 7 facoltative è quello
deciso, la vista cancellata torna, la vista vecchia viene sostituita.

**Fuori dal codice del server**: `deploy/applica-aggiornamento.ps1` ora aspetta il server **3
minuti** invece di 60 secondi prima di ripristinare la versione precedente — con la vecchia
pazienza il rollback poteva scattare **mentre una migrazione era in corso** e fermare il processo
a metà scrittura dello schema. `Migrations:LockTimeoutSeconds` aggiunta ai due `appsettings`.
`GUIDA-SERVER-LAN.md` §8.1: come leggere l'errore dal database invece che dai log.

**Fatto quando**: i test C1 passano sul nuovo runner **e** sulla fixture sintetica
"DB esistente"; aggiungere una migrazione nuova richiede di aggiungere uno step
all'elenco, non di toccare `DbService.cs`.
*(A1 raggiunto il 15/08/2026: 80 test verdi e per aggiungere una migrazione basta creare un
file — nemmeno l'elenco esiste più, le scopre la riflessione. Resta A2.)*

**Rollback**: il runner nuovo legge la stessa tabella `schema_migrations` del vecchio.
Se qualcosa va storto in produzione, si ripubblica la build precedente e lo schema
resta valido. Fare comunque il backup completo (`FullBackupService`) **prima** del
primo avvio con il runner nuovo.

---

## BLOCCO E — Prestazioni (6-10 giorni, incrementale)

Non si fa "tutto insieme": si misura, si corregge il peggio, si rimisura.

### ✅ E1 — FATTO il 15/08/2026, **in produzione dalle 14:23** (resta da accendere lo slow query log)

Due misure, non una, perché guardano cose diverse e da sole mentono:

- [x] **Le richieste HTTP** — [RichiesteLenteMiddleware.cs](ATEC.PM.Server/Middleware/RichiesteLenteMiddleware.cs),
      registrato in [Program.cs](ATEC.PM.Server/Program.cs) subito **dentro** il middleware degli
      errori (più in alto misurerebbe il proprio guscio; più in basso perderebbe compressione,
      file statici e autenticazione — cioè pezzi di quello che l'utente sta aspettando).
      Una riga per ogni richiesta oltre soglia: durata, metodo, **template della rotta**
      (`api/projects/{id}/costing`, non `…/847/…`, altrimenti ogni commessa è una riga diversa e
      non si può contare niente), status, id della persona.
      Soglia in `Diagnostics:SlowRequestMs` (500), **0 la spegne senza ripubblicare**.
      - 🪤 **La query string non finisce nel log, mai**: gli hub SignalR ci passano il JWT
        (`?access_token=…`) e questi file restano sul server 30 giorni. Un token scritto lì è una
        sessione regalata a chiunque apra il file. C'è un test che lo blocca.
      - Esclusi `/hubs` (WebSocket: durano quanto la sessione, comparirebbero tutti come
        «lentissimi»), `/assets` e `/uploads` (al primo caricamento del mattino sono decine di
        file — e il mattino è proprio quando si sta indagando).
      - Livello `Information`, prefisso `[Lenta]`: una richiesta lenta non è un guasto, e
        mescolarla ai warning veri renderebbe quelli meno visibili.
- [x] **Le query MySQL** — [misura-prestazioni.ps1](deploy/misura-prestazioni.ps1), da lanciare
      dove sta il database (in sviluppo da `deploy/`, sul server dopo `scp` + `ssh`; le due righe
      di comando sono in testa allo script). Azioni: `accendi`, `spegni`, `stato`, `lente`,
      `classifica`, `svuota`, `richieste`.
      Provato in locale su MySQL vero, tutte le azioni.

**Le tre trappole trovate provandolo** (tutte silenziose: la misura avrebbe *funzionato*, dando
numeri sbagliati):
1. `SET PERSIST long_query_time` cambia il valore **globale**, ma `@@long_query_time` legge la
   copia di **sessione**, presa quando la connessione si è aperta: la verifica diceva ancora
   «10 secondi» e sembrava che l'accensione non avesse funzionato. Si rilegge `@@GLOBAL.…`.
2. `TIME_TO_SEC` **tronca ai secondi interi**: con la soglia a mezzo secondo, tutte le query fra
   0,5 s e 1 s — cioè quasi tutte quelle che si stanno cercando — uscivano come «0 secondi».
3. Il P95 calcolato con `floor((n-1) * 0.95)` su pochi campioni restituisce un valore **più basso
   del massimo osservato**: dice che va tutto bene proprio quando l'unico dato che conta è il caso
   peggiore. Percentile per rango, `ceil(p * n) - 1`.

**Perché due strumenti.** Una richiesta che fa **300 query da 5 ms** non compare in nessuno slow
query log — eppure impiega un secondo e mezzo. È esattamente la forma degli N+1 di E3 (166 punti):
si vede solo cronometrando la richiesta intera. Al contrario, una query da 3 secondi dentro un
processo notturno non passa da nessuna richiesta HTTP. Per questo `classifica` (che legge
`performance_schema`, non lo slow query log) è ordinata per **tempo totale** e non per la più
lenta: un endpoint da 4 ms chiamato 20.000 volte pesa più di uno da 9 secondi chiamato una volta,
e si corregge una volta sola.

`log_queries_not_using_indexes` resta **spento** apposta: acceso, ogni `SELECT` su una tabella
piccola finisce nel registro (le anagrafiche sono decine di righe: MySQL le legge tutte perché
conviene, non perché manchi un indice) e il registro diventa illeggibile proprio dove serve.

**8 test** in `Infrastruttura/RichiesteLenteTests.cs`. L'ultimo è un **guardiano fra i due pezzi**:
prende la regex **dallo script `.ps1`** e la applica a una riga scritta davvero dal middleware.
Senza, cambiare il testo del messaggio non romperebbe niente di visibile — lo script continuerebbe
a girare rispondendo «nessuna richiesta oltre soglia», la frase più rassicurante che possa dire, e
falsa. Provato al contrario: cambiato `ms` in `millisecondi`, il test diventa rosso.

#### Cosa resta da fare (è il lavoro vero di E1: aspettare)

- [ ] **Accendere lo slow query log sul server** — si può fare **subito, senza deploy**:
      ```
      scp -i "$env:USERPROFILE\.ssh\atec_vps" deploy\misura-prestazioni.ps1 atec@192.168.2.150:C:/ATEC_PM/Updates/
      ssh -i "$env:USERPROFILE\.ssh\atec_vps" atec@192.168.2.150
      powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione accendi
      ```
- [x] ~~Pubblicare la build~~ — **fatta il 15/08/2026 alle 14:23**: 114 test verdi, delta di 5 file,
      `/api/health/ready` risponde `ready` + `database: ok`. Da qui il log delle richieste è attivo
      (`Diagnostics` non è nel `appsettings.json` del server — resta fuori dal confronto del delta —
      quindi vale il **default del codice**: soglia 500 ms, esclusi `/hubs`, `/assets`, `/uploads`).
- [ ] **Fra una settimana di lavoro vero**: `-Azione lente`, `-Azione classifica`,
      `-Azione richieste`. Da quelle tre liste — non da un'intuizione — escono i lavori di E2
      (indici), E3 (N+1) ed E5 (async).
- [ ] **Poi spegnere** (`-Azione spegni` + `-Azione svuota`): `mysql.slow_log` cresce e non si
      svuota da sola.

### 🟨 E2 — Indici: prima parte **in produzione dal 15/08/2026 ore 15:22**, il resto aspetta i numeri

> **Deploy fatto**: 123 test verdi, delta di 5 file, `/api/health/ready` → `ready`.
> La **v91 è girata sul database vero in 488 ms** (schema v90 → v91): tre indici creati, due
> tolti, tutti e cinque i passi nel log. La stima di 1,2 s su 300.000 righe era prudente.

95 `KEY` su ~94 tabelle: gli indici sono quasi solo quelli generati dalle 99 FK.

**Cosa si è potuto fare senza la misura.** Non tutto E2 dipende da E1: dove il pattern della
query sta **scritto nel codice** e la tabella cresce per costruzione, il candidato è certo. Il
resto no, e resta fuori apposta.

#### ✅ Prima le query, poi gli indici — 3 riscritture

Un indice non serve a niente se la query gli impedisce di essere usato. **Una funzione applicata
alla colonna rende inutilizzabile qualunque indice su quella colonna**, e il difetto è invisibile:
il risultato è giusto, la pagina funziona, e sotto si legge tutta la tabella.

- [x] [DashboardController.cs](ATEC.PM.Server/Controllers/DashboardController.cs) — le ore del mese
      (`YEAR(work_date)=… AND MONTH(work_date)=…`) e della settimana
      (`YEARWEEK(work_date,1)=…`) diventano intervalli di date. Sono **due scansioni complete
      della tabella delle ore a ogni apertura della home, per ogni persona**.
      🪤 `WEEKDAY()` e non `WEEK()`/`DAYOFWEEK()`: `YEARWEEK(...,1)` è la settimana ISO, che
      comincia di **lunedì**. Con una settimana che parte di domenica, il lunedì mattina il
      totale della home comparirebbe azzerato — e nessuno lo chiamerebbe un difetto del codice,
      direbbero che «le ore non si vedono».
- [x] [NotificationService.cs](ATEC.PM.Server/Services/NotificationService.cs) — gli **8**
      `DATE(n.created_at) = …` del dedup («l'ho già segnalata oggi?») diventano intervalli.
      Quella sottoquery gira **una volta per ogni riga trovata** da ogni controllo di scadenza.
- [x] `ProjectsController` **guardato e lasciato com'è**: lì `YEAR(te.work_date)` sta nella
      `SELECT`/`GROUP BY`, non nel filtro — non impedisce nessun indice.

#### ✅ Poi gli indici — [M091](ATEC.PM.Server/Migrations/M091_IndiciOreENotifiche.cs)

| Tabella | Indice | Perché |
|---|---|---|
| `timesheet_entries` | **`ix_te_employee_date`** (`employee_id, work_date`) | i 3 endpoint del Timesheet filtrano esattamente così; c'era il solo `employee_id`, quindi MySQL leggeva **tutte** le ore della persona (~1.500 l'anno, per sempre) e scartava le date |
| `timesheet_entries` | **`ix_te_work_date`** (`work_date`) | gli aggregati globali (home, anomalie, ultimi 90 giorni) non filtrano per persona: il composito non li serve, la sua prima colonna non c'è nella query |
| `notifications` | **`ix_notif_dedup`** (`notification_type, reference_type, reference_id, created_at`) | il dedup di sopra; c'era solo il tipo, che non seleziona niente visto che è lo stesso per tutte le righe di quel controllo |

**Tolti** `idx_te_employee` e `idx_type`: sono il **prefisso** dei nuovi, non fanno guadagnare
niente in lettura e si pagano a ogni scrittura.
🪤 Il `DROP` viene **dopo** il `CREATE`: `employee_id` ha una chiave esterna e MySQL rifiuta di
lasciarla senza un indice utilizzabile. Invertendo l'ordine la migrazione fallirebbe in
produzione — e da A2 un fallimento **ferma l'avvio del servizio**. Congelato da un test provato
al contrario (con il drop davanti, diventa rosso).

**9 test** in `Prestazioni/IndiciEQueryTests.cs`: l'equivalenza delle somme prima/dopo su ore
sparse dentro e fuori dai periodi (su una tabella vuota il confronto passerebbe anche con una
riscrittura sbagliata), il confine del lunedì, gli indici presenti e i vecchi spariti, la v91
su un database **come la produzione** (che parte con gli indici vecchi, non con quelli del
bootstrap) ed eseguita due volte di fila, il guardiano sui file caldi e il conteggio delle
finestre di dedup.

#### 🔍 La revisione avversariale (15 rilievi) — e le due cose che ha trovato nei TEST

Dodici agenti su quattro lenti indipendenti. Il risultato utile non è stato sul codice ma sulla
**rete di sicurezza**: due revisori hanno rotto il codice di proposito e hanno dimostrato che la
suite restava verde. Un test che non sa fallire non protegge niente, e questi non sapevano.

1. **`WEEKDAY` → `DAYOFWEEK` nel controller: 6 test su 6 verdi.** Le query «nuove» erano
   **ricopiate** dentro il test, quindi il test provava sé stesso. Ora le due somme della home
   sono costanti pubbliche (`DashboardController.SqlOreMese` / `SqlOreSettimana`) e **il test
   esegue quelle**. Le ore di prova sono seminate su 14 giorni consecutivi con importi tutti
   diversi: così qualunque spostamento della finestra, anche di un giorno, cambia la somma —
   in qualsiasi giorno della settimana il test venga eseguito.
2. **Finestra di dedup allargata da 1 a 30 giorni: 6 test su 6 verdi.** Quella modifica non
   rompe nessuna regola di prestazioni — la query resta veloce — ma **spegne il promemoria
   giornaliero**: le scadenze ancora aperte smettono di riavvisare. Ora un test conta le **8**
   finestre e pretende che durino un giorno. I modelli del guardiano sono diventati espressioni
   regolari: con il confronto letterale bastava uno spazio (`YEAR( work_date`) per aggirarlo.

Entrambe le mutazioni sono state rifatte dopo la correzione: adesso diventano rosse.

**Bonifica completata** (era rimasta a metà nello stesso file): in `NotificationService` c'erano
ancora **15 filtri `DATEDIFF(colonna, CURDATE())`** — i cinque controlli di scadenza più le dieci
pulizie. `sal_rows` ha **due indici costruiti apposta su `data_fatt`** e quel `DATEDIFF` li
rendeva entrambi irraggiungibili. Tutti portati a confronto sulla colonna nuda (le colonne sono
`DATE`, quindi la conversione è esatta); il `DATEDIFF` resta solo nella `SELECT`, dove non filtra
niente. Stessa cosa per i tre `DATE(reserved_at) < CURDATE()` di `CodexGeneratorService`, uno dei
quali dentro una `DELETE`.

**Il rilievo che ho respinto**: marcare la v91 `Facoltativa` perché «un indice non deve poter
fermare l'azienda». Le sette facoltative del progetto sono tutte pulizie di **dati**; qui si tratta
di **schema**, e i tre indici nuovi nascono da questa migrazione — lasciarla saltare vorrebbe dire
permettere alla produzione un assetto diverso da quello che i test certificano, con la suite verde.
È la trappola della v69. Sull'altro piatto: la costruzione dei tre indici, misurata su 300.000
righe, dura **1,2 secondi**. La scelta ora è scritta nel commento della classe invece di essere
implicita.

**Bootstrap allineato**: le `CREATE TABLE` di `DbService` creavano ancora i due indici vecchi, che
la v91 sostituiva un attimo dopo. Ora un database nuovo nasce già con l'assetto giusto.

**Trovato e non corretto** (difetto **preesistente**, non introdotto da E2): il dedup delle
anomalie ore cerca la notifica nel **giorno lavorato** e non in quello corrente, quindi chi
registra le ore il giorno dopo si prende fino a 8 notifiche identiche. Registrato come
**BUG-014** in [BUGS.md](../BUGS.md): correggerlo richiede prima di portare il giorno dentro il
riferimento della notifica, o due giorni anomali della stessa persona si annullerebbero.

#### Guardato e scartato (per non rifare l'analisi)

- **`projects.code`**: le liste commesse sono ordinate per codice, ma `ProjectSorting` ordina con
  `REGEXP` + `SUBSTRING` sul codice — **nessun indice può servire quell'`ORDER BY`**. Resterebbero
  due `WHERE code = ?` del generatore di codice, su una tabella da qualche migliaio di righe:
  guadagno nullo, costo in scrittura reale.
- **La ricerca commesse `LIKE '%term%'`** su `code`, `title`, `company_name` (era il secondo punto
  di questo blocco): confermato che nessun indice può servirla, ma su una tabella di quelle
  dimensioni non è un problema misurabile. `FULLTEXT` cambierebbe anche il **comportamento** per
  chi cerca (parole intere, niente pezzi di parola): è una decisione di prodotto, non tecnica, e
  non si prende per un guadagno che nessuno ha visto.

**Da sapere al prossimo deploy.** La v91 è l'unica migrazione nuova e fa solo DDL sugli indici:
su `timesheet_entries` e `notifications` di queste dimensioni sono secondi, e in MySQL 8 la
creazione è online (le scritture non si fermano). Resta il fatto che è uno schema che cambia:
backup completo prima, come sempre.

#### Cosa resta (dopo la settimana di misura)

- [ ] Indici sui campi di filtro/ordinamento **emersi da E1** — DDP, SAL, Codex, Bilancio sono i
      candidati attesi, ma si decidono con `-Azione lente` e `-Azione classifica` alla mano.
      Un indice inutile non si vede: rallenta le scritture e occupa spazio, in silenzio.

### 🟨 E3 — N+1: **censimento fatto il 15/08/2026**, correzioni dopo la misura

Elenco completo, voce per voce, in **[CENSIMENTO-N1-E3.md](../archivio/CENSIMENTO-N1-E3.md)**.

Un rilevatore deterministico ha trovato **240 candidati in 45 file** (più dei 166 stimati: prende
anche i cicli scritti su una riga sola senza graffe, la forma più insidiosa). Otto classificazioni
indipendenti sul codice vero, poi verifica avversariale sui candidati in cima.

| Classe | Quanti |
|---|---:|
| VERO_LETTURA (si accorpa con `WHERE id IN`) | 35 |
| VERO_SCRITTURA (accorpabile, con più cautela) | 127 |
| LEGITTIMO (deve chiamare una volta per giro, o gira una volta al mese) | 51 |
| FALSO_POSITIVO (query fuori dal ciclo, o codice già corretto) | 27 |

**Il numero che cambia il piano di lavoro: 78 candidati su 240 non sono difetti.** E dei 162 veri,
per impatto sono **2 alti**, 50 medi, 110 bassi. «Correggere 166 punti» non è mai stato il lavoro
di E3: il lavoro è correggerne una manciata, e sapere quali.

**I due alti, dopo la verifica avversariale** (che ha risalito i chiamanti fino al client web):
- ✅ **`TravelFromTimesheet.cs:104`** — confermato, e **peggio di come era stato descritto**: la
  trasferta viene rigenerata su **tutte le giornate di cantiere storiche** della commessa a **ogni
  salvataggio di una singola riga di ore** (`TimesheetController` la chiama su ogni POST e ogni
  DELETE). Il costo cresce senza limite con lo storico e si paga a ogni imputazione, per 35 persone.
  Il difetto strutturale non è il round-trip: è che si ricostruisce tutto per una modifica puntuale.
- ⬇️ **`CostingDataService.cs:197`** — confermato ma **declassato a medio**: le decine di UPDATE
  sono reali, ma il percorso non è caldo (salvataggio del pannello Distribuzione nei Preventivi) e
  la chiamata è fire-and-forget lato client.

> ### ⚠️ Il difetto più serio non è di prestazioni — **BUG-015**
> Sulle stesse righe di `CostingDataService.cs:201` la UPDATE filtra per `id` e basta, **senza
> vincolo di appartenenza al preventivo** (stesso buco nell'endpoint singolo
> `QuoteCostingController.cs:370`). Un utente autenticato può sovrascrivere contingenza, margine e
> ombreggiatura delle righe materiale di **qualunque altro preventivo** passando id arbitrari — e
> sono percentuali che concorrono al prezzo d'offerta. **Verificato a mano**: la riga sopra, nello
> stesso metodo, il filtro ce l'ha. Registrato in [BUGS.md](../BUGS.md), da chiudere insieme
> all'accorpamento di quelle righe.

- [ ] Correggere i punti che la misura di E1 conferma, partendo da `TravelFromTimesheet`.
- [ ] Chiudere BUG-015 nello stesso passaggio.

### ✅ E4 — FATTO il 15/08/2026: cache delle anagrafiche

- [x] [AnagraficheCache.cs](ATEC.PM.Server/Services/AnagraficheCache.cs) — cache di processo
      **senza scadenza**: una voce vale finché qualcuno non scrive su quella tabella. Niente
      `IMemoryCache`: è costruito attorno alla scadenza, e qui la scadenza non la vogliamo — un
      TTL sarebbe più comodo da scrivere e sbagliato da usare (regola della freschezza).
      Contatore di versione per riga: una lettura partita **prima** di una modifica non può più
      salvare il proprio risultato dopo l'invalidazione, che è il modo silenzioso in cui una
      cache resta sbagliata per sempre.
- [x] **In cache solo ciò di cui si conoscono TUTTE le scritture**, e sono due:
      `ddp_aggregations`(+`ddp_aggregation_states`) e `ddp_status_transitions`. Un endpoint
      ciascuna, verificato riga per riga.
      - Le aggregazioni le rilegge **ogni utente ogni minuto** (la campanella delle scadenze si
        aggiorna da sola), e `control-summary` le caricava **due volte per richiesta**.
      - La matrice delle transizioni veniva letta **una volta per riga**: aggiudicare una RDO da
        200 righe erano 200 query identiche. È anche un N+1 di E3 chiuso senza toccare le query.
- [x] **Il guardiano** — [CacheAnagraficheTests.cs](ATEC.PM.Tests/Infrastruttura/CacheAnagraficheTests.cs)
      legge i sorgenti, cerca chi scrive sulle tabelle in cache e pretende che quel file
      invalidi. Provato al contrario: tolta l'invalidazione, il test diventa rosso — e i test
      sono il cancello del deploy. **L'invalidazione non è affidata alla memoria di chi scrive
      codice**, ed è la ragione per cui la cache si può fidare: nello stesso progetto, delle
      quattro operazioni di `DepartmentsController` tre mandano la notifica e la quarta no.
- [x] **Invalidare DOPO il commit, mai dentro la transazione**: InnoDB è in REPEATABLE READ, e
      un'altra richiesta rileggerebbe i valori *di prima* ripopolando la cache con quelli.
- [x] Il **ripristino da backup** svuota tutto (`InvalidaTutto`): riscrive l'intero database
      sotto i piedi dell'applicazione accesa.
- [x] `FeatureAccessService`: le regole diventano **uno scatto immutabile scambiato in blocco**.
      Prima erano quattro campi che `Reload` azzerava uno per uno mentre `EnsureLoaded` ne
      controllava due: un `Reload` che cadeva nel mezzo faceva un **500 su una richiesta
      protetta**, e capitava proprio quando un ADMIN cambia i permessi mentre l'azienda lavora.
      La sua cache **non** è stata spostata dentro `AnagraficheCache`: le voci per-persona sono a
      scadenza breve e quella scadenza copre punti di scrittura non ancora censiti: toglierla
      prima di averli tappati trasformerebbe una finestra di un minuto in una infinita, sui
      permessi. L'invariante «chi scrive, ricarica» è comunque sorvegliata dal test.

**La revisione avversariale (23 rilievi, 7 confermati) ha trovato il difetto peggiore nel codice
appena scritto**: il ripristino da backup svuotava la memoria **solo se arrivava in fondo**, e non
toccava affatto le regole dei permessi. Un ripristino interrotto a metà (commit fallito, zip
corrotto) lasciava il gestionale acceso a decidere i permessi con le regole del database
sostituito — senza scadenza, senza messaggio, fino al riavvio del servizio. Peggio: chi ripristina
un pacchetto **proprio per annullare un cambio di permessi sbagliato** si ritrovava il database
giusto e i permessi sbagliati ancora in vigore. Ora lo svuotamento è in un `finally` e comprende
`FeatureAccessService.Reload()`, con due test che lo bloccano (provati al contrario: tolta la
ricarica, diventano rossi).

Sempre dalla revisione: `CanAccess` dichiarava una fotografia sola delle regole e poi ne prendeva
quattro (i metodi pubblici che richiamava ripescavano ognuno la più recente), e
`IsMotoreNuovoAttivo` teneva il lock dei permessi mentre interrogava il database — su **ogni**
richiesta autenticata.

**Le tabelle guardate e SCARTATE** (per non rifare l'analisi): `employees` — 13 punti di scrittura
su 6 file, tre letture indipendenti ne hanno contati 12, 13 e 15, ed è di per sé la prova che il
censimento non è completabile. `sal_conditions`, `sal_sap_causali`, `sal_payment_states`,
`ddp_treatments`, `ddp_destinations` — non cambiano «una volta al mese» come dice il piano: le
voci nuove le crea chi compila una DDP o un SAL dalla commessa, quindi la cache si butterebbe di
continuo. `departments`, `material_categories`, `activity_catalog`, `cost_section_templates`,
`phase_templates` — superficie di scrittura pulita ma guadagno trascurabile (letture-elenco poche
per pagina; le decine di JOIN restano a carico di MySQL, che è il posto giusto). Un candidato
vero rimasto fuori: l'aggregazione **A2** del Gestore DDP, letta due volte per richiesta con SQL
scritto a mano invece che da `DdpAggregationSet` — si prende quando si tocca quel file.

**Due difetti trovati per strada e chiusi:**
- **L'interruttore del motore permessi non funzionava.** `PermissionsEngine` sta in `app_config`,
  la pagina Configurazione lo scriveva, ma `FeatureAccessService` lo legge una volta e lo tiene
  per tutta la vita del processo: girarlo **non aveva alcun effetto fino al riavvio del
  servizio**, e nessuno lo diceva. Ora il salvataggio ricarica.
- **Spostare qualcuno di reparto non cambiava subito i suoi permessi**: l'appartenenza alla
  Contabilità è in cache per un minuto e `SaveDepartments` non la invalidava.

### E5 — Async (solo dove E1 lo giustifica)
1.848 chiamate sincrone, 576 action non-async: in teoria ogni richiesta tiene un
thread del pool per tutta la query. In LAN a poche decine di persone non è detto
che sia la causa del "va lento la mattina". Si misura in E1, poi si converte
**solo** gli endpoint lenti trovati lì — non 576 action a tappeto (diff enorme,
conflitti, poco visibile agli utenti). N+1 e indici, dopo la settimana di slow
query, hanno ritorno più alto.
- [ ] Dove si converte: `QueryAsync`/`ExecuteAsync` + `async Task<IActionResult>`,
      per modulo.
- [ ] Regola: non lasciare mai `.Result`/`.Wait()` (oggi sono solo 2, non peggiorare).

---

## BLOCCO F — Layer dati e transazioni (continuo)

**Obiettivo**: non un "grande refactoring". Una regola da applicare **ogni volta che
si tocca un controller per altri motivi**.

### F1 — Transazioni sulle scritture multi-tabella
63 `BeginTransaction` contro 386 endpoint di scrittura. I casi peggiori misurati:

| Controller | Endpoint di scrittura | Transazioni |
|---|---|---|
| `SalController` | 26 | **1** |
| `ProjectCostingController` | 21 | 2 |
| `ProjectsController` | 19 | 2 |
| `CodexController` | 19 | 2 |

- [ ] Censire gli endpoint che scrivono su **più tabelle** e avvolgerli in transazione
      (creazione commessa + copia fasi + snapshot; SAL + righe + pagamenti; DDP +
      cronistoria stati).
- [ ] Criterio: se un `catch` a metà lascerebbe righe orfane, serve la transazione.

> ### ✅ Audit «comando dentro una transazione senza dichiararla» — 14/08/2026
>
> Fatto su tutti e 28 i file che usano `BeginTransaction` (6 agenti + verifica avversariale,
> più un controllo indipendente a campione). **Nessun difetto attivo**: dove c'è una
> transazione, i comandi la dichiarano.
>
> **Il caso che si vedeva nei log** (`[Permessi] Registro non scritto: The transaction
> associated with this command is not the connection's active transaction`, 14/08 ore 09:22)
> era reale ed è **già stato corretto quello stesso giorno**: il percorso era
> `EseguiConGaranzia` → `ScriviRiga` → `Registra`/`Propaga` sulla connessione con la
> transazione aperta. Fra le 09:22 e le 09:43 `Propaga` è stato spostato dopo il commit su
> una connessione nuova e a `Registra` è stato aggiunto il parametro `tx`. Da lì i warning
> sono cessati. Effetto mentre durava: **i permessi venivano scritti ma il registro restava
> vuoto**, e la pagina Permessi non lo diceva.
>
> Chiusi in via preventiva tre residui **latenti** (nessuno attivo oggi, tutti silenziosi
> se qualcuno domani chiamasse quel codice dentro una transazione):
> - `PermissionChangeService.Propaga` non aveva il parametro `tx` → aggiunto;
> - `DdpItemEvents.Registra` non lo accettava **e aveva il `catch` completamente vuoto** →
>   aggiunto `tx` e il log. È la sorgente del «Consegnato il»: una storia persa non lasciava
>   traccia da nessuna parte. I quattro chiamanti sono stati verificati uno per uno e nessuno
>   è dentro una transazione;
> - i fallimenti di registro ora sono `LogError` (non `Warning`) quando l'eccezione è
>   `InvalidOperationException`: quello non è un guaio del database ma un difetto di
>   programmazione, ed è esattamente ciò che il 14/08 è passato inosservato in mezzo agli
>   altri warning.
>
> Chi rifà questo lavoro in futuro: il difetto **non si vede dall'interfaccia** (l'utente
> vede il salvataggio riuscito) e non si vede nei test che non guardano il log. Il modo
> rapido di accorgersene è cercare `active transaction` nei log del server.

### F2 — Portare l'SQL fuori dai controller
862 `SELECT` e 515 `db.Open()` dentro `Controllers/`. Il pattern giusto **esiste già**
(`QuoteDbService`, `SalDbService`, `MilestonesDbService`, `ResourcesDbService`): è
applicato a metà, e i controller rimasti senza sono i più grossi
(`ProjectsController` 125 KB, `QuotesController` 81 KB, `SalController` 66 KB).

- [ ] Regola operativa: **quando tocchi un endpoint, la sua query si sposta** nel
      `*DbService` del modulo. Niente sessioni dedicate di refactoring.
- [ ] Le 38 query costruite per interpolazione di stringa vanno parametrizzate durante
      lo spostamento (il `$"..."` sopravvive solo dove interpola nomi di tabella/colonna
      già passati da una whitelist, come fa `UpdateField`).
- [ ] Un controller che scende sotto le 400 righe non ha più bisogno di essere spezzato.

### F3 — Notifiche SignalR
36 `_ = ...SendAsync(...)` fire-and-forget: se la chiamata fallisce, l'eccezione
sparisce e la UI di qualcuno resta vecchia.
- [ ] Incapsulare in un helper che logga il fallimento (non serve garantire la
      consegna: il client rilegge comunque, con `staleTime: 0`).

---

## 3. Cosa questo piano NON fa

Delimitare lo scope serve a non trasformarlo in un cantiere infinito.

- **Non** introduce EF Core. Dapper resta: il costo di migrare 1.848 query non è
  giustificato da nessun beneficio reale qui.
- **Non** riscrive il client web. I file grossi (`MilestoneGantt.tsx` 53 KB,
  `CatalogoPreventiviPage.tsx` 45 KB) sono grandi ma funzionanti e collaudati.
- **Non** tocca le funzionalità aperte (import/export del blocco 2, export Excel dei
  permessi): sono lavoro di prodotto, stanno negli altri piani.
- **Non** cambia il modello di deploy né aggiunge Docker.
- **Non** riscrive le 87 migrazioni esistenti: le **rende verificabili** (A0 subito;
  spostamento in classi/file solo dopo C1).

---

## 4. Due cose che restano fuori dal codice

- **Backup fuori dal server**: il backup completo funziona ed è collaudato, ma la
  copia vive sulla stessa macchina che dovrebbe proteggere. Un guasto disco o un
  ransomware li prende entrambi. Serve una copia su NAS o disco esterno — è
  configurazione, non sviluppo, ma è il rischio singolo più grosso del sistema.
- **HTTP in chiaro sulla LAN**: i JWT viaggiano leggibili sulla rete aziendale e via
  VPN. Un certificato interno (anche autofirmato, distribuito una volta sulle
  macchine) chiuderebbe la questione.
