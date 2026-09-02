# Guida al server aziendale — ATEC PM (rete interna)

> Scritta il 29/07/2026. Descrive come si installa, si aggiorna e si gestisce ATEC PM
> sulla macchina in azienda. Pensata per essere capita anche senza esperienza di server.

> ⚠️ **La lezione del VPS Aruba (giugno 2026):** un server sparito nella notte si porta
> via tutto quello che vive solo lì. I backup che stanno sulla stessa macchina **non
> bastano** — vedi §7.

---

## 1. Il quadro generale

| Pezzo | Dove gira |
|---|---|
| **Server API + client web** (ASP.NET Core 8, un solo programma) | Sulla macchina **ATEC-FC**, servizio Windows sempre acceso |
| **Database** (MySQL 8.4, schema `atec_pm`) | Sulla stessa macchina |
| **Client** | Browser dei colleghi, nessuna installazione |

**Indirizzo per gli utenti:** `http://192.168.2.150:5150` — funziona anche col nome
della macchina: `http://ATEC-FC:5150` (il server ascolta sia in IPv4 sia in IPv6:
con il solo IPv4 chi risolve il nome in IPv6 restava appeso).

La macchina **non è esposta su internet**: chi lavora da fuori entra prima in VPN.

| Dato | Valore |
|---|---|
| Server | **ATEC-FC**, IP **192.168.2.150**, Windows 11 24H2 |
| Utente amministratore | `atec` |
| Servizio Windows | `AtecPmServer` — "ATEC PM Server", gira come utente `ATEC-FC\atec` |
| Programma | `C:\ATEC_PM\Server` |
| Configurazione di produzione | `C:\ATEC_PM\Server\appsettings.json` (+ copia in `C:\ATEC_PM\Config`) |
| Log | `C:\ATEC_PM\Logs\server-AAAAMMGG.log` (30 giorni) |
| Backup automatici | `C:\ATEC_Backups` (ogni notte alle 02:00) |
| Documenti commessa | percorso configurato nel gestionale (`BasePath`, default `C:\ATEC_Commesse`) |
| Allegati CMS / preventivi | `C:\ATEC_PM\Uploads\cms` |

Sulla stessa rete vivono anche **Danea/Firebird** (192.168.2.115) e **SERVER-CODEX**:
da qui le sincronizzazioni funzionano senza VPN né inoltri di porta.

---

## 2. Come si entra nel server

Dal PC di sviluppo, in PowerShell:

```powershell
ssh -i "$env:USERPROFILE\.ssh\atec_vps" atec@192.168.2.150
```

Non chiede password: usa la **chiave** `atec_vps` (la stessa del VPS di ATEC Risorse).
Il file senza estensione è privato — non va dato a nessuno.

In alternativa si entra in Desktop Remoto come utente `atec`.

---

## 3. Prima installazione (una volta sola)

**Passo 1 — dal PC di sviluppo:** doppio click su **`carica-installazione.bat`**
(cartella `ATEC_PM`). Compila client web e server, prepara il pacchetto e lo carica
in `C:\ATEC_PM\Updates` sul server. Se la compilazione fallisce si ferma lì.

**Passo 2 — sul server** (SSH o Desktop Remoto, utente amministratore):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\install-server.ps1
```

Chiede due password: **root di MySQL** e quella dell'utente **`ATEC-FC\atec`**, con cui
gira il servizio (serve per leggere documenti commessa e allegati Danea sulle cartelle
di rete). Poi da solo: crea le cartelle, crea il database `atec_pm` con un **utente
MySQL dedicato** (niente root) e una password casuale, scrive la configurazione di
produzione con una **chiave JWT nuova**, blinda i permessi, dà all'account il diritto
"Accedi come servizio", registra il servizio Windows, apre la porta 5150 sul firewall,
avvia e verifica che risponda.

Le sincronizzazioni Danea e Codex restano **spente**: si accendono dopo, a gestionale
funzionante (§5).

Opzioni utili:

| Opzione | Quando serve |
|---|---|
| `-AccountServizio LocalSystem` | per far girare il servizio come account di sistema. Attenzione: così **non** legge le cartelle di rete (`\\Server-maga\...`) — esce in rete come *computer* e prende "accesso negato" |
| `-Porta 8080` | per usare una porta diversa da 5150 |
| `-AttivaDaneaSync` / `-AttivaCodexSync` | accende subito le sincronizzazioni (meglio dopo, vedi §5) |
| `-SaltaDb -DbPassword ...` | database e utente MySQL già creati a mano |

Alla fine stampa il riepilogo e crea **`C:\ATEC_PM\Config\credenziali.txt`** con
password del database e chiave JWT: **portane una copia fuori dal server** (le stesse
informazioni sono in `C:\ATEC_PM\Config\appsettings.originale.json`).

**Passo 3 — subito dopo:** entra nel gestionale da `http://192.168.2.150:5150` — la
prima volta con utente **`admin`** e password **`admin`** — **cambia subito quella
password** e controlla in Impostazioni il percorso dei documenti commessa.

---

## 4. Aggiornare il programma

> **Doppio click su `aggiorna-server.bat`** (cartella `ATEC_PM`).

Compila da pulito, confronta con il server e spedisce solo i file cambiati. Se tocca
anche il codice C# ferma il servizio, sostituisce il programma **senza toccare la
configurazione di produzione**, riavvia e verifica (una decina di secondi di
interruzione). Se tocca **solo** il client web, vedi il paragrafo sotto: nessuna
interruzione.

**Quanto dura** (misurato il 19/08/2026 su questa LAN, aggiornamento con codice C#
modificato):

| fase | secondi | com'era il 18/08 |
|------|--------:|-----------------:|
| test automatici | **0,9** | ~240 |
| npm build (client) | **2,4** | 30–52 |
| applica sul server (stop, scambio, riavvio, verifica) | 10,0 | 10,0 |
| confronto e pacchetto | 6,1 | 6,1 |
| upload | 5,9 | 5,9 |
| dotnet publish (server) | 4,5 | 5,0 |
| **totale** | **~30 s** | ~5 minuti |

Le prime due righe sono quelle che sono cambiate, e per lo stesso motivo: **non si rifà
quello che è già stato fatto**. I test si registrano verdi con `prova-test.bat` (qui
sotto) e `npm build` si salta se il client non è cambiato. Se non c'è proprio niente da
spedire, lo script se ne accorge e chiude in **12,5 secondi** dicendo *«già aggiornato»*.

> **Dal 14/08/2026 partono prima i test automatici.** Se sono rossi l'aggiornamento si
> ferma lì e **in azienda non cambia niente**: nessun file spedito, servizio intatto.
> Girano *prima* della compilazione apposta — `npm build` + `publish` durano minuti, e
> scoprire il guasto dopo averli aspettati non serve a nessuno.
>
> **Dal 16/08/2026 si saltano quando non c'è niente da riprovare.** I test sono passati da
> 72 a 189 e la suite dura **3 minuti e 43** (era oltre 6: vedi il riquadro sui test qui
> sotto). Aspettarli per
> ripubblicare una scritta cambiata nel client era tempo buttato: i test sono in C# e il
> client non lo guardano nemmeno. Ora lo script calcola l'impronta dei sorgenti **C#**
> (server, DTO condivisi, test) e la confronta con quella dell'ultima esecuzione verde: se è
> identica, li salta e lo dice. Se erano rossi l'impronta viene cancellata, quindi non si
> eredita mai un verde vecchio. Per rifarli comunque: `aggiorna-server.bat -ConTest`.
>
> **Dal 19/08/2026 i test si possono togliere di mezzo PRIMA: `prova-test.bat`.** I test non
> sono mai girati in azienda — girano qui sul PC, su database MySQL locali usa-e-getta, e il
> server non li vede nemmeno. Il problema non era *dove* giravano ma *quando*: in mezzo al
> deploy, con qualcuno che aspetta davanti allo schermo. `prova-test.bat` esegue la stessa
> suite quando fa comodo (anche mentre si continua a lavorare) e, **se è verde, registra
> l'impronta**: da quel momento `aggiorna-server.bat` la salta perché non c'è più niente da
> riprovare, e il deploy dura una trentina di secondi invece di minuti. Se dopo i test si
> cambia anche una riga di C#, l'impronta non coincide più e il deploy li rifà da solo — è
> voluto: è il caso in cui l'esito potrebbe essere diverso. Lo script se ne accorge anche
> quando il C# cambia **mentre** i test girano, e in quel caso il verde non lo registra.
>
> - `prova-test.bat` — suite completa, registra il verde
> - `prova-test.bat -Comunque` — la rifà anche se l'impronta è già verde
> - `prova-test.bat -Filtro NomeTest` — solo una parte (non registra: una parte non dice
>   niente sul resto)

> **Dal 19/08/2026 anche `npm build` si salta se il client non è cambiato.** Erano 30-52
> secondi spesi a ricostruire un bundle **identico** ogni volta che si toccava solo il C# —
> il 48% del deploy. Lo script confronta percorso, dimensione e data dei sorgenti di
> `atec-pm-web` con quelli dell'ultima compilazione riuscita; se coincidono riusa quella. Il
> **primo** aggiornamento dopo questa novità compila comunque (non c'è ancora niente con cui
> confrontarsi). Per ricompilare a forza: `aggiorna-server.bat -ConClient`.
>
> ⚠️ Quando la build si salta, `version.json` e la versione mostrata nel client restano
> quelle di prima: identificano la versione del **client web**, non del deploy. È voluto —
> agli utenti non compare la barra blu «aggiorna adesso» perché non c'è niente di nuovo da
> ricaricare nel browser.

> **Perché la suite è scesa da 6 minuti a 3m43** (19/08/2026). Ogni test che usa MySQL si
> creava un database vero e ci costruiva dentro 119 tabelle: 5 secondi a testa, 62 volte.
> Adesso le classi che non stanno provando le migrazioni **condividono un database solo** e
> ogni test riparte pulito in 45 millisecondi (`ATEC.PM.Tests/Infrastruttura/SchemaCondiviso.cs`);
> chi prova il motore delle migrazioni con migrazioni finte usa un database col **solo
> registro**, senza le altre 119 tabelle che non guardava nemmeno (quei 14 test: da 242
> secondi a 0,7). Restano lenti apposta i quattro casi in cui partire da un database vergine
> *è* il test: motore migrazioni, migrazione su dati pregressi, indici, ripristino da backup.

> **Alla fine lo script stampa dove se n'è andato il tempo**, fase per fase e in ordine di
> durata. Serve a non ottimizzare a naso: la prima riga di quella classifica è il prossimo
> lavoro da fare. Le voci aperte stanno in [TODO.md](../TODO.md) §3.
>
> I test che hanno bisogno di MySQL si saltano da soli se sul PC non c'è: l'aggiornamento
> prosegue con i soli test puri, non si blocca per quello.
>
> In **emergenza** — bisogna pubblicare subito e si sa già perché i test sono rossi — si
> salta la rete con `aggiorna-server.bat -SenzaTest`. È l'eccezione, non l'abitudine: chi
> lo usa sta pubblicando codice che il progetto stesso dichiara rotto.

> Dal 04/08/2026 il pacchetto **non viene più compresso** (`atec-pm-server.tar`).
> Misurato su questa LAN: comprimerlo costava 14,8 secondi e non faceva risparmiare
> trasferimento, perché la rete regge 80 MB/s sui file grandi (2 secondi in entrambi i
> casi). In estrazione non cambia niente: `tar -xf` riconosce da sé se il pacchetto è
> compresso, quindi anche un vecchio `.tgz` resta installabile.
>
> Dal 07/08/2026 si spediscono **solo i file cambiati** (`atec-pm-delta.tar`, in genere
> pochi MB invece di 160). Prima di caricare, lo script chiede al server l'elenco dei
> file installati con la loro impronta e lo confronta con quello appena compilato: i
> ~150 MB del runtime .NET, che non cambiano mai, non partono nemmeno. La versione
> nuova viene composta **sul server**, copiando quella attuale con `robocopy` e
> applicandoci sopra i file nuovi; le protezioni non cambiano (stop del servizio,
> versione precedente messa da parte, ripristino automatico se la verifica fallisce).
>
> Il pacchetto completo torna da solo quando serve: prima installazione, oppure quando
> è cambiato più del 60% (per esempio a un aggiornamento del runtime .NET). Se il
> server rifiuta il differenziale con *«la versione installata non corrisponde»*
> qualcuno ha toccato a mano `C:\ATEC_PM\Server`: in quel caso si forza il completo con
> `aggiorna-server.bat -Completo` (oppure lanciando `deploy\aggiorna-server.ps1
> -Completo`). Il server non viene toccato finché il controllo non passa.

Se il nuovo server non risponde entro un minuto, lo script **rimette da solo la
versione precedente** e ti mostra le ultime righe di log: il gestionale torna a
funzionare da solo. La versione sostituita resta in `C:\ATEC_PM\Server.precedente`
fino all'aggiornamento successivo.

### Aggiornamenti di solo client: nessuna interruzione

Se l'aggiornamento tocca **solo la parte web** (`wwwroot`) — ed è il caso di 3 deploy
su 4, quelli che non cambiano una riga di C# — il servizio **non viene fermato**: i
file nuovi si affiancano a quelli vecchi e poi si scambia `index.html`. Chi sta
lavorando non se ne accorge, le connessioni restano su, e **si vede la barra
«Aggiorna adesso» entro un minuto** (come sempre: non ricarica da sola).

Perché è sicuro: gli asset hanno l'hash nel nome, quindi i file nuovi non danno
fastidio a nessuno; prima di scambiare `index.html` lo script **controlla che tutti
gli asset che cita siano già sul disco**; e gli asset della build precedente **non
vengono cancellati subito**, ma al deploy dopo — così una scheda rimasta aperta sulla
versione vecchia continua a funzionare invece di trovare un chunk sparito.

Appena c'è di mezzo un file fuori da `wwwroot` (un `.dll`, l'eseguibile, la
configurazione) si torna da soli alla procedura con stop, backup e rollback. Per
forzare comunque lo stop: `aggiorna-server.bat -ConServizioFermo`.

### Chi è collegato se ne accorge da solo

Ogni build conia un id (data e ora, es. `20260806-1031`) che finisce in `version.json`
accanto agli asset ed è scritto **sotto «ATEC PM» nel menù a sinistra**: per sapere su
quale versione sta girando una postazione basta guardare lì (o farselo leggere per
telefono).

Le schede già aperte ricontrollano quel file ogni minuto e ogni volta che tornano in
primo piano. Appena vedono un id diverso dal proprio, in cima compare una barra blu
**«È disponibile una nuova versione di ATEC PM»** con il pulsante **Aggiorna adesso**.

La barra **non si chiude e non ricarica da sola**: il momento lo sceglie l'utente, così
a nessuno sparisce da sotto le mani una griglia o un foglio a metà compilazione. Il
rovescio della medaglia è che chi la ignora resta sulla versione vecchia finché non
clicca. Il pulsante fa il lavoro che prima richiedeva **Ctrl+F5**.

---

## 5. Configurazione di produzione

Vive **solo sul server**, in `C:\ATEC_PM\Server\appsettings.json`, e **non viene mai
sovrascritta** dagli aggiornamenti. Dopo il primo avvio, connessione al database e
chiave JWT non si leggono più in chiaro (compare `ENCRYPTED`): sono cifrate in
`appsettings.Secrets.json`, decifrabili solo su questa macchina.

⚠️ **Non copiare mai l'`appsettings.json` del progetto sopra quello del server**: si
perderebbero chiave JWT (tutti fuori: login rifiutati) e credenziali del database.

**Accendere le sincronizzazioni** (dopo che tutto il resto gira):

1. `notepad C:\ATEC_PM\Server\appsettings.json` → sezione `Services`, metti a `true`
   `DaneaSync` e/o `CodexSync`
2. `Restart-Service AtecPmServer`
3. controlla i log: `Get-Content C:\ATEC_PM\Logs\server-*.log -Tail 40`

Nella stessa sezione: `Notifications` (campanella e promemoria) e `Backup` (dump
notturno) sono già accesi; `PlanDigest` (email di riepilogo) resta spento finché non
si configura l'SMTP dal gestionale.

---

### 5.1 Sincronizzazione con ATEC Risorse (VPS) — dal 02/09/2026

Il planner Risorse di ATEC PM e il programma **ATEC Risorse** sul VPS (`https://178-32-137-221.sslip.io`)
si tengono allineati da soli, nei due versi (piano e regole in `docs/piani/PIANO-SYNC-RISORSE.md`).
Il motore (`RisorseSyncService`) gira **dentro il servizio ATEC PM** e parla col VPS in HTTPS.

| Cosa | Dove |
|---|---|
| Indirizzo del VPS, utente di servizio, interruttore | `C:\ATEC_PM\Serverppsettings.json`, sezione `RisorseSync` (`Enabled`, `BaseUrl`, `Username`) + `Services:RisorseSync` |
| Password dell'utente di servizio `sync.pm` | `appsettings.Secrets.json`, chiave `RisorseSync:Password` (cifrata DPAPI, ambito macchina). **Non sta in chiaro da nessuna parte.** Sul VPS la stessa password vive in `/opt/atec-risorse/appsettings.json`, sezione `Sync` |
| Stato, ultimo giro, registro, «Sincronizza adesso», «Prova collegamento» | nel gestionale: **Gestione avanzata → Digest Email → scheda «Sincronizzazione ATEC Risorse (VPS)»** (solo Admin) |
| Registro dei giri | tabella `res_sync_log` (solo i giri con scritture o errori; i giri a vuoto non scrivono); mappa id PM ↔ VPS in `res_sync_map` |
| Log del servizio | righe con prefisso `[RisorseSync]` in `C:\ATEC_PM\Logs\server-AAAAMMGG.log` |

**Spegnere/accendere**: dalla scheda (interruttore «Attiva», vale subito) oppure `Enabled: false`
nella sezione `RisorseSync` e `Restart-Service AtecPmServer`. Spento, i due programmi continuano a
funzionare da soli e si riallineano alla riaccensione (confronto completo, niente da perdere).

**Se il VPS non risponde**: il motore riprova ogni 60 s e lo scrive nel registro; le modifiche fatte
in PM restano in attesa e partono al primo giro buono. Un VPS che risponde con **zero allocazioni**
o con più di metà delle righe sparite **ferma il motore** (protezione contro le cancellazioni di
massa): si guarda cos'è successo e si riparte con «Sincronizza adesso».

**Copie fatte al go-live (02/09/2026)**: `appsettings.json.prima-sync-20260902` e
`appsettings.Secrets.json.prima-sync-20260902` accanto agli originali; sul VPS
`/var/lib/atec-risorse/backup/risorse-pre-sync-20260902.db`.

## 6. Comandi utili (sul server)

```powershell
Get-Service AtecPmServer                                  # è acceso?
Restart-Service AtecPmServer                              # riavvia
Get-Content C:\ATEC_PM\Logs\server-*.log -Tail 50         # ultimi log
Invoke-RestMethod http://localhost:5150/api/health        # risponde? (status/versione)
Get-ChildItem C:\ATEC_Backups | Sort LastWriteTime -Desc | Select -First 5
```

Dal PC di sviluppo, verifica veloce:

```bash
curl.exe -s -o NUL -w "%{http_code}" http://192.168.2.150:5150/api/health
```

Atteso `200`.

### 6.1 «Va lento»: come si misura invece di indovinare

Due liste, e nessuna delle due si costruisce a memoria.

**Le richieste lente** (oltre 500 ms) le scrive il server nei suoi log, con la rotta e la
durata. Per leggerle raggruppate, **sul server**:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione richieste
```

**Le query lente** vanno prima accese — e vanno lasciate accese **una settimana di lavoro
vero**, altrimenti si misura il silenzio:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione accendi
# ...una settimana dopo...
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione lente
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione classifica
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione spegni
```

Lo script si carica dal PC di sviluppo con
`scp -i "$env:USERPROFILE\.ssh\atec_vps" ATEC_PM\deploy\misura-prestazioni.ps1 atec@192.168.2.150:C:/ATEC_PM/Updates/`.

⚠️ **Ricordarsi di spegnere**: il registro delle query lente sta in una tabella (`mysql.slow_log`)
che cresce e non si svuota da sola — `-Azione svuota` dopo aver letto.
La misura delle richieste HTTP invece può restare accesa: scrive solo sopra soglia. Si spegne da
`appsettings.json`, `Diagnostics:SlowRequestMs: 0`.

---

## 7. Backup e ripristino — LA PARTE CHE SALVA LA VITA

Nel gestionale, sezione **Backup DB**, ci sono due livelli.

### 7.1 Backup del database (dump .sql leggero)

Il pulsante «Backup ora» scrive un dump SQL in `C:\ATEC_Backups`; nella stessa
cartella finiscono i dump fatti prima delle migrazioni e quelli di sicurezza dei
ripristini. Pesa pochi MB e contiene **solo i dati**: le tabelle le ricrea il
programma all'avvio. Da qui si ripristina con «Ripristina», che prima di sostituire
i dati fa una copia di sicurezza.

> ⏰ **Dal 26/08/2026 il notturno delle 02:00 crea il PACCHETTO COMPLETO** (§7.2), non
> più il solo dump: il dump era ridondante (il pacchetto lo contiene) e lasciava fuori
> proprio i file. Se il pacchetto fallisce (NAS spento), quella notte si RIPIEGA sul
> dump .sql locale — una notte senza NAS non è una notte senza backup. Per tornare al
> solo dump: `Backup:AutoCompleto = false` in `appsettings.json`.

### 7.2 Backup completo: database + file (da portare via)

Il dump da solo non basta: documenti di commessa, foto, video e allegati stanno su
disco, non nel database. Il pulsante **«Crea pacchetto completo»** (e il notturno
delle 02:00) produce un unico `.zip` nella destinazione configurata — di serie la
share `\\Server-maga\d\ATEC_Backups\Pacchetti`, si cambia dalla card «Destinazione
dei pacchetti» in fondo alla pagina Backup — con dentro:

- `database.sql` — tutti i dati
- `documenti/` — la cartella delle commesse (`BasePath`)
- `allegati/` — `C:\ATEC_PM\Uploads\cms`
- `manifest.json` — data, macchina, versione dello schema, conteggi

La creazione gira in background con una barra di avanzamento (con molti GB ci vogliono
minuti; foto e video non vengono ricompressi, sarebbe tempo perso).

**Pulizia automatica:** i backup più vecchi di **60 giorni** (`Backup:GiorniConservazione`)
si eliminano da soli — pacchetti e dump — ma le copie **più recenti restano sempre**
(`Backup:PackageKeep` pacchetti, di serie 3; 5 per i dump), qualunque età abbiano: se
il notturno resta fermo dei mesi, l'anzianità da sola non cancella gli ultimi backup.

**Ripristino:** dal menu del pacchetto — «Ripristina tutto», «solo database» o «solo
cartelle». Il database viene sostituito (con copia di sicurezza automatica prima), le
cartelle attuali **non** vengono cancellate: restano accanto rinominate
`.prima-ripristino-<data>`. Meglio farlo quando nessuno sta lavorando.

**Su un server nuovo** l'ordine è: installa → avvia il servizio (crea lo schema) →
ripristina il pacchetto completo.

### 7.3 Portare i dati dal PC di sviluppo al server

Il pulsante **«Carica pacchetto»** accetta uno zip creato su un'altra macchina: si
avvia il gestionale in locale, si crea il pacchetto completo, lo si carica qui e lo si
ripristina come gli altri. Tre cose da sapere prima di farlo:

- **Le utenze diventano quelle del PC di sviluppo.** Il ripristino del database
  sostituisce anche gli utenti: dopo, `admin`/`admin` non vale più, si entra con le
  credenziali che c'erano in locale.
- **La versione dello schema deve combaciare.** Il manifest del pacchetto la riporta;
  se il pacchetto è più recente del server, aggiorna prima il server
  (`aggiorna-server.bat`) e poi ripristina.
- **Se servono anche i modelli di cartella** (`MASTER_TEMPLATE`) usa «Ripristina
  tutto»: con «solo database» le cartelle restano quelle del server.

### 7.4 La copia FUORI dal server (indispensabile)

Un pacchetto che resta sul server non protegge da niente: se la macchina muore, muore
con lei.

> ✅ **FATTO il 26/08/2026 — i pacchetti nascono direttamente su Server-maga.**
> In produzione `Backup:PackagePath` punta a `\\Server-maga\d\ATEC_Backups\Pacchetti`:
> ogni backup completo creato dal gestionale finisce già fuori dal server, e la card
> Backup lo elenca/scarica/ripristina da lì come prima. Il servizio apre da solo la
> sessione SMB autenticata (stesso `NetworkShareConnector` della cartella immagini
> Danea, credenziali `DaneaSync:SmbUser/SmbPassword`; per un NAS DIVERSO da Server-maga
> impostare `Backup:ShareUser`/`Backup:SharePassword` con lo stesso meccanismo).
> Se la share non risponde, il backup si RIFIUTA con un errore parlante invece di
> scrivere chissà dove. Copia di sicurezza della config in
> `appsettings.json.prima-nas-20260826`.

Due modi, si possono combinare:

- **Scarica** il pacchetto dal gestionale e mettilo su NAS/disco esterno
- **Punta i pacchetti direttamente su una cartella di rete** (è la configurazione
  attuale, vedi riquadro): in `C:\ATEC_PM\Server\appsettings.json`
  `"Backup": { "Path": "C:\\ATEC_Backups", "AutoHour": 2, "PackagePath": "\\\\Server-maga\\d\\ATEC_Backups\\Pacchetti" }`
  e riavvia il servizio

In alternativa, da un altro PC, una copia periodica pianificata:

```powershell
robocopy \\192.168.2.150\C$\ATEC_Backups D:\CopieAtecPm\Backups /MIR /R:1 /W:1
```

---

## 8. Problemi tipici

| Sintomo | Causa probabile | Rimedio |
|---|---|---|
| Il sito non si apre | Servizio fermo | `Restart-Service AtecPmServer`, poi guarda i log |
| Non si apre **da un altro PC** ma sul server sì | Firewall o rete | Verifica la regola "ATEC PM Server (TCP 5150)" e che il PC sia in LAN/VPN |
| Il servizio parte e si ferma subito | MySQL non pronto o credenziali sbagliate | Log in `C:\ATEC_PM\Logs`; controlla che il servizio MySQL sia avviato |
| Dopo un riavvio della macchina il gestionale non c'è | Il server è partito prima di MySQL | L'installazione imposta la dipendenza; se manca: `sc.exe config AtecPmServer depend= MySQL84` |
| Login rifiutato per tutti | `appsettings.json` di produzione sovrascritto | Rimetti la chiave JWT da `C:\ATEC_PM\Config\appsettings.originale.json` e riavvia |
| Errori "accesso negato" sui documenti o allegati Danea | L'utente `atec` non ha accesso a quella cartella di rete (o il servizio gira come LocalSystem) | Vedi §8.2: si danno al programma credenziali valide **sul server della share** |
| Il servizio non parte dopo aver cambiato la password di `atec` | Il servizio usa ancora quella vecchia | Servizi → ATEC PM Server → Accesso → rimetti la password, poi riavvia |
| Pagina vecchia dopo un aggiornamento | Cache del browser | Ctrl+F5 |
| Aggiornamento fallito | Lo script ha già rimesso la versione precedente | Leggi i log stampati, correggi, rilancia `aggiorna-server.bat` |
| Il servizio non parte **dopo un aggiornamento**, e nel log c'è `[Migration vNN] FALLITA` | Una migrazione del database non è riuscita | Vedi §8.1 qui sotto |

### 8.1 «Il servizio non parte e nel log c'è una migrazione FALLITA»

Dal 14/08/2026 una migrazione del database che fallisce **interrompe l'avvio**. È voluto: prima
il server partiva lo stesso con lo schema a metà, continuava a lavorare per giorni e il guaio si
scopriva dai numeri sbagliati invece che da un errore.

**Cosa fare, nell'ordine:**

1. **Leggere l'errore.** In `C:\ATEC_PM\Logs\server-<data>.log` cerca `FALLITA`:

   ```powershell
   Select-String -Path C:\ATEC_PM\Logs\server-*.log -Pattern 'FALLITA|Avvio interrotto' | Select-Object -Last 5
   ```

   **Oppure — meglio — chiederlo al database.** Dal 15/08/2026 il motivo resta scritto lì, e non
   sparisce dopo 30 giorni come i log:

   ```powershell
   mysql -u atecpm -p -D atec_pm -e "SELECT version, description, error_text, duration_ms, applied_at FROM schema_migrations WHERE success = 0;"
   ```

   Nessuna riga = nessuna migrazione fallita. Una riga = quella versione **non è applicata** e
   viene ritentata a ogni riavvio; `error_text` dice perché, `duration_ms` quanto ci ha messo
   prima di rompersi.

   Per vedere a che punto è lo schema:

   ```powershell
   mysql -u atecpm -p -D atec_pm -e "SELECT COUNT(*) applicate, MAX(version) massimo FROM schema_migrations WHERE success = 1;"
   ```

2. **Se il gestionale deve tornare su SUBITO** (è orario di lavoro e non si può aspettare):
   in `C:\ATEC_PM\Server\appsettings.json` metti

   ```json
   "Migrations": { "StopOnError": false }
   ```

   e `Restart-Service AtecPmServer`. Si torna al comportamento di prima: il server parte, la
   migrazione fallita resta **pendente** e viene ritentata a ogni riavvio.
   ⚠️ È un cerotto, non una riparazione: lo schema resta incompleto finché la migrazione non passa.
   Rimetti `true` appena il problema è risolto.

3. **Far correggere la migrazione** (è lavoro da sviluppatore) e aggiornare di nuovo.

**Altro messaggio possibile all'avvio**, `schema_migrations è vuota ma il database contiene già
dati`: il registro delle migrazioni è andato perso, tipicamente per un ripristino da backup
interrotto a metà. **Non forzare l'avvio**: rifai il ripristino dal pacchetto completo. Il server
si ferma apposta, perché rieseguire le migrazioni su dati veri li rovinerebbe (importi IVA
riscritti, lavorazioni cancellate che tornano, cronistoria duplicata).

**Riga `[Migrations] Sanate N versioni mancanti`**: compare una volta sola, al primo avvio con la
versione nuova. Quelle migrazioni sono state date per applicate **senza essere eseguite** (era
l'unica scelta sicura: rieseguirle avrebbe potuto sovrascrivere dati). Se la riga compare,
segnalala allo sviluppatore: va verificato a mano che il loro effetto ci sia davvero.

**Messaggio `Un altro processo sta già aggiornando lo schema di questo database`**: dal 15/08/2026
le migrazioni girano sotto un lucchetto, e due processi insieme non possono più riscrivere lo
schema (il DDL di MySQL non torna indietro: due esecuzioni della stessa migrazione lasciano un
database che nessuno sa più rimettere a posto). Se compare:

1. controlla che non ci sia una **seconda istanza** viva — `Get-Process ATEC.PM.Server` deve dare
   una riga sola;
2. se l'altro processo sta davvero migrando, aspetta e riavvia: `Restart-Service AtecPmServer`.

Il servizio si riavvia comunque da solo dopo 5 secondi e ritenta. L'attesa è di 30 secondi e si
cambia in `C:\ATEC_PM\Server\appsettings.json` con `"Migrations": { "LockTimeoutSeconds": 30 }`.

**Riga `pulizie facoltative non riuscite`**: sette migrazioni sono **pulizie di dati** (righe
orfane, valori legacy) e il loro fallimento non ferma il gestionale, che funziona lo stesso. Si
ritentano da sole al riavvio dopo. Vanno segnalate allo sviluppatore, senza urgenza: il motivo è
in `schema_migrations.error_text`.

⚠️ **Finché `StopOnError` è a `false`** (il cerotto del punto 2) un aggiornamento viene dichiarato
riuscito anche con lo schema a metà: il server parte, risponde, e lo script stampa il messaggio
verde. È un motivo in più per rimetterlo a `true` appena possibile.

⚠️ **Ripristinare un pacchetto di backup NUOVO su una versione VECCHIA del server** (per esempio
dopo un rollback) può lasciare il registro delle migrazioni vuoto: il pacchetto contiene colonne
che la versione vecchia non conosce, e quelle righe vengono scartate. Se succede, il server
successivo si ferma con «schema_migrations è vuota». Ripristinare sempre su una versione **pari o
più recente** di quella che ha prodotto il pacchetto.

### 8.2 «Gli articoli Danea passano ma le immagini no» (cartelle di rete)

Sintomo: nella pagina **Trasferimento catalogo Danea** il report dice «N trasferiti · 0 errori ·
**0 file immagine copiati**» e in alto c'è il badge rosso «Cartella immagini non raggiungibile».
Gli articoli sono passati davvero: fallisce solo la copia dei file, che stanno su
`\\Server-maga\d\DANEA\...\<archivio> - Allegati\Prod`.

**Perché.** Il servizio gira come account **locale** `ATEC-FC\atec`. ATEC-FC e Server-maga sono in
WORKGROUP: Server-maga non conosce quell'utente, l'SMB ripiega su `guest` e Windows lo blocca.
Il database Danea invece funziona perché lì ci si connette in TCP con utente e password espliciti.
Dal PC di sviluppo sembra tutto a posto: là le credenziali di Server-maga sono in Gestione
credenziali di Windows.

**Rimedio (25/08/2026).** Si dà al programma un utente valido **sul server della share**, come già
si fa per Firebird. Sul server, in PowerShell da amministratore:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Strumenti\imposta-credenziali-share.ps1
```

Chiede utente e password, li **prova davvero sulla share**, li salva **cifrati** (DPAPI, ambito
macchina) in `appsettings.Secrets.json` e riavvia il servizio. Da lì in poi il programma apre da
solo una sessione SMB autenticata prima di leggere e scrivere i file: non serve nessun `cmdkey`,
nessun profilo utente, e regge ai riavvii.

L'utente va creato **su Server-maga** (Gestione computer → Utenti locali), con **lettura** su
`Srl-2020-2021 - Allegati` e **scrittura** su `Atec_PM - Allegati`. Va bene un account dedicato
tipo `atec_pm`: non serve che sia amministratore.

Se le credenziali non sono impostate il programma si comporta come prima e il badge rosso spiega
in chiaro qual è il problema.

---

## 9. Sicurezza — stato e cose da fare

- Il server **non è raggiungibile da internet**: si entra dalla LAN o via VPN.
- La cartella `C:\ATEC_PM` è accessibile solo ad **Administrators** e **SYSTEM**
  (più l'account del servizio, se se ne usa uno).
- Password del database e chiave JWT sono **cifrate a riposo** e diverse da quelle di
  sviluppo; l'utente MySQL dell'applicazione **non è root** e può toccare solo `atec_pm`.
- Traffico in **HTTP**: dentro la rete aziendale è una scelta consapevole (nessun
  certificato da gestire). Se un domani serve HTTPS: certificato sulla macchina,
  endpoint HTTPS in `appsettings.json` e `Security:RequireHttps` a `true`.
- **Da fare:** password di root di MySQL ancora quella storica → cambiarla e aggiornare
  chi la usa; portare i pacchetti di backup fuori dalla macchina (§7.4).
- I pacchetti completi contengono tutti i dati aziendali: trattali come documenti
  riservati (non contengono invece password né chiavi del server).

---

## 10. Riepilogo da frigorifero

- **Indirizzo:** http://192.168.2.150:5150 (o http://ATEC-FC:5150)
- **Aggiornare:** doppio click su `aggiorna-server.bat`
- **Entrare nel server:** `ssh -i "$env:USERPROFILE\.ssh\atec_vps" atec@192.168.2.150`
- **Riavviare:** `Restart-Service AtecPmServer`
- **Log:** `C:\ATEC_PM\Logs` · **Backup:** `C:\ATEC_Backups` — e una copia fuori dal server!
- **Costo:** zero, la macchina è già in azienda
