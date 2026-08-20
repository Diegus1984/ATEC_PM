# Come si fanno i video tutorial di ATEC PM

> Procedura usata il **07/08/2026** per produrre i due tutorial «Configurazione sezioni»
> e «Bilancio commessa». Ogni video è un **mp4 con voce narrante italiana**, registrato
> pilotando l'applicazione vera: schermo in movimento, non slide.

## L'idea in una riga

Si scrive un **copione a passi**, si trasforma in **audio** con una voce sintetica, si
**registra il browser** facendo durare ogni passo esattamente quanto il suo audio, e si
**incolla** l'audio sul video. Siccome la durata dei passi è la stessa, il montaggio non
richiede nessuna sincronizzazione a mano.

```
copione.json ──► genera_audio.py ──► audio/*.mp3 + durate.json
                                              │
                                              ▼
                                      registra.js  ──► video/*.webm
                                              │
                                              ▼
                                        monta.py  ──► tutorial.mp4 + .srt
```

---

## 1. Cosa serve installato

| Strumento | A cosa serve | Come si installa |
|---|---|---|
| **ffmpeg** | montare video e audio | `winget install --id Gyan.FFmpeg -e` |
| **edge-tts** | voce italiana neurale | `python -m pip install edge-tts` |
| **playwright-core** | pilotare Chrome | `npm install playwright-core` (nella cartella degli script) |
| ffmpeg di Playwright | registrare il video | `node node_modules/playwright-core/cli.js install ffmpeg` (0,1 MB) |
| **Google Chrome** | il browser pilotato | già presente |

`playwright-core` **non scarica browser**: usa il Chrome di sistema. Sono pochi MB in tutto.

**Voci italiane disponibili** (`python -m edge_tts --list-voices | Select-String it-IT`):
`it-IT-DiegoNeural` (usata), `it-IT-ElsaNeural`, `it-IT-IsabellaNeural`, `it-IT-GiuseppeMultilingualNeural`.
Esiste anche la voce Windows «Microsoft Elsa», offline ma robotica: buona solo per una prova.

> ⚠️ **edge-tts manda il testo del copione ai server Microsoft.** Va bene per descrizioni
> d'uso del gestionale; non ci si mettano dati di commessa o nomi di clienti.

---

## 2. Il copione

Un file JSON, un oggetto per passo:

```json
{
  "id": "04-contatore",
  "sub": "Il contatore in alto",
  "say": "Questa riga in alto è la prima cosa da guardare quando apri la pagina…"
}
```

- `id` — nome del passo, diventa il nome del file audio
- `sub` — sottotitolo breve che appare in sovrimpressione durante il passo
- `say` — quello che la voce dice

**Regole imparate scrivendole:**

- **Frasi brevi**, come si parla. La voce sintetica inciampa sui periodi lunghi.
- **Niente simboli**: `⋮` diventa «il menu dei tre puntini», `K` diventa «il ricarico».
- **Gli importi si scrivono per esteso** dove contano («settantamila euro»): letti dalle
  cifre a volte escono male.
- **I nomi devono essere quelli veri a video.** Errore fatto e corretto: il copione diceva
  «Delta Ordine» ma l'etichetta sullo schermo è «Margine di Sicurezza». Prima di registrare,
  rileggere il copione con la pagina davanti.
- **Citare i numeri** rende il tutorial molto più concreto, ma lega il copione a quei dati:
  se si rigira su dati diversi va riscritto.
- Ogni passo dura quanto il suo audio: 15–30 secondi è la misura giusta. Un passo da 30
  secondi richiede due o tre cose da mostrare, altrimenti l'immagine resta ferma troppo.

---

## 3. Generare l'audio

```powershell
python genera_audio.py copione            # legge copione.json
python genera_audio.py copione-bilancio   # legge copione-bilancio.json
```

Produce `audio*/` con un mp3 per passo e `durate*.json` con la durata di ciascuno —
è quel file che tiene in riga tutto il resto.

---

## 4. Registrare il video

Serve un **token JWT**, che la registrazione mette in `localStorage` prima di aprire la
pagina: così parte già dentro l'applicazione e non c'è nessun login da tagliare. Va messo in
`token.txt` in questa cartella (oppure nella variabile `TUT_TOKEN`).

**In sviluppo** si conia con la chiave che sta in chiaro in `ATEC.PM.Server/appsettings.json`
(`Jwt:Key`), HS256, `iss`/`aud` = `ATEC.PM`, claim URI `nameidentifier`/`name`/`role`:

```powershell
$key = (Get-Content ATEC.PM.Server\appsettings.json -Raw | ConvertFrom-Json).Jwt.Key
function B64Url([byte[]]$b) { [Convert]::ToBase64String($b).TrimEnd('=').Replace('+','-').Replace('/','_') }
$exp = [DateTimeOffset]::UtcNow.AddHours(8).ToUnixTimeSeconds()
$payload = '{"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier":"1","http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name":"admin","http://schemas.microsoft.com/ws/2008/06/identity/claims/role":"ADMIN","exp":' + $exp + ',"iss":"ATEC.PM","aud":"ATEC.PM"}'
$h = B64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$p = B64Url ([Text.Encoding]::UTF8.GetBytes($payload))
$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [Text.Encoding]::UTF8.GetBytes($key)
"$h.$p." + (B64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$h.$p")))) |
  Out-File token.txt -Encoding ascii -NoNewline
```

**In produzione** la chiave non è disponibile da fuori: si copia il token dalla sessione del
browser (`F12` → Console → `localStorage.atec_pm_token`; se Chrome rifiuta l'incolla, scrivere
prima `allow pasting`, oppure prenderlo da `F12` → Application → Local Storage).

```powershell
node registra.js              # Configurazione sezioni
node registra-bilancio.js     # Bilancio commessa
```

Ogni passo è scritto così:

```js
await step("04-contatore", async () => {
  await show([contatore])     // porta in vista, evidenzia, ci porta il cursore
})
```

`step()` mostra il sottotitolo, esegue le azioni e **aspetta il tempo che avanza** fino a
coprire la durata dell'audio. Le sovrimpressioni (sottotitolo, riquadro arancione, cursore
finto, cartello del titolo) sono in `overlay.js`, iniettato nella pagina.

### Trappole, tutte pagate almeno una volta

- **Serve `headless: true`.** Con la finestra vera Chrome non rispetta il viewport richiesto
  e il video esce con una fascia bianca attorno al contenuto.
- **`context.setDefaultTimeout(3000)`.** Senza, un selettore che non trova nulla blocca il suo
  passo per 30 secondi e sfasa la voce per tutto il resto del video (vedi la sezione sul
  disallineamento).
- **Playwright non registra il puntatore del mouse**: il cursore che si vede nei video è un
  `div` disegnato da `overlay.js` e mosso insieme al mouse vero.
- **Portare l'elemento in vista PRIMA di evidenziarlo** (`show()` lo fa): altrimenti il
  riquadro arancione finisce fuori schermo e si evidenzia il vuoto.
- **Risalire agli antenati con XPath è fragile.** Per i riquadri KPI ha funzionato solo
  cercare l'elemento dentro la pagina e risalire con `closest('[data-slot="card"]')`.
- **Il token** va messo in `localStorage` tramite `storageState` alla creazione del contesto:
  così la registrazione parte già dentro l'applicazione e non c'è login da tagliare.
- **`marks.json`** salva l'`offsetMs`, cioè quanto tempo passa tra l'apertura della pagina e
  l'inizio del primo passo: è quello che il montaggio taglia dall'inizio.
- Per i **tooltip** basta fermare il mouse sull'elemento e aspettare: compaiono da soli.
- **Non completare mai un drag&drop** e chiudere i dialoghi con «Annulla»: la registrazione
  non deve lasciare modifiche. Verificare i conteggi prima e dopo.

---

## 5. Montare

```powershell
python monta.py copione-bilancio video-bilancio marks-bilancio.json "ATEC-PM-tutorial-Bilancio-Commessa"
```

Taglia l'`offsetMs` iniziale dal video, costruisce la traccia voce, codifica in H.264 e
produce **`.mp4` + `.srt`** (i sottotitoli riportano il parlato completo).

Se i due file stanno nella stessa cartella e hanno lo stesso nome, VLC aggancia i sottotitoli
da solo.

### ⚠️ Il disallineamento fra voce e video — letto e riletto

È il difetto che rovina un tutorial, e si presenta in **due forme diverse**. Il primo video
consegnato ne è andato esente per fortuna (scarto 0,1 s); il secondo aveva **16 secondi** di
sfasamento a fine filmato.

**Forma 1 — un passo dura più del suo parlato.** `step()` esegue le azioni e poi aspetta il
tempo che avanza. Se le azioni durano *più* del parlato non c'è modo di recuperare, e da lì
in poi tutto slitta. Nel tutorial del Bilancio un `getByRole` che non trovava la voce di menu
ha aspettato il **timeout di 30 secondi** di Playwright: quel passo si è allungato di 19
secondi e i quindici successivi sono slittati in blocco.

Le due difese, da tenere entrambe:

- `context.setDefaultTimeout(3000)` — un selettore sbagliato fallisce subito invece di
  mangiarsi mezzo minuto;
- lo script **stampa a fine registrazione i passi che hanno sforato**. Se compare quell'elenco
  il video non va montato così: si corregge il selettore e si ri-registra.

**Forma 2 — il montaggio assume tempi che non sono quelli veri.** Anche a registrazione
pulita, incollare le clip audio una dopo l'altra presume che ogni passo sia durato
*esattamente* quanto il suo parlato. Per questo `monta.py` non concatena e basta: legge
`marks.json`, che contiene **l'istante reale in cui ogni passo è partito**, e inserisce fra
una clip e l'altra il silenzio necessario perché la successiva ricada su quell'istante. Così
un eventuale sforo resta confinato al suo passo e non si propaga.

**Forma 2-bis — il ritardo dell'encoder MP3.** Concatenando MP3, ogni giunzione aggiunge una
manciata di millisecondi di silenzio: su sedici clip diventa **un secondo** di sfasamento a
fine video. Per questo il riempimento e la concatenazione si fanno in **WAV**, e si comprime
solo alla fine.

**Come si verifica senza guardarsi sei minuti di video:**

```powershell
ffmpeg -i tutorial.mp4 -af "silencedetect=noise=-40dB:d=0.7" -f null NUL 2>&1 |
  Select-String "silence_end"
```

Gli istanti in cui la voce riparte vanno confrontati con gli `atMs` di `marks.json`: il
ritardo deve essere **piccolo e soprattutto costante** (~0,2 s, la microspausa che edge-tts
mette in testa a ogni clip). Se cresce passo dopo passo, c'è drift e il montaggio è da rifare.

---

## 6. Su quali dati registrare

Tre strade, in ordine di preferenza:

1. **Commessa dimostrativa** (usata per il Bilancio). Dati inventati ma coerenti: nel video
   non compare nessun cliente reale, quindi si può girare a chiunque. Si costruisce con
   `popola-demo*.ps1`. Serve una **storia**: nel tutorial del Bilancio la commessa è stata
   tarata perché la redditività preventiva stesse sopra la soglia del 20% e quella
   consuntiva ci scivolasse sotto — senza quello scarto non c'era niente da spiegare.
2. **Database di sviluppo così com'è**: va bene per le anagrafiche (Configurazione sezioni),
   ma per i moduli operativi è quasi vuoto — zero ore registrate, nessun ordine.
3. **Produzione in sola lettura**: `TUT_ORIGIN=http://192.168.2.150:5150` e `TUT_READONLY=1`.
   Il lucchetto **abortisce ogni richiesta che non sia GET**, quindi nessun clic può scrivere
   nemmeno per sbaglio. Serve però un token valido per quel server, e la chiave JWT vive solo
   sulla macchina server: si prende il token dalla sessione del browser
   (`localStorage.atec_pm_token`). Il MySQL di produzione **non accetta connessioni da fuori**
   (`ERROR 1130`), quindi da un altro PC non si può né leggerlo né copiarlo.

### Costruire i dati demo — cosa è andato storto

- `POST /api/customers` fallisce con vincolo di unicità se la **partita IVA è vuota**.
- `/api/employees` espone `status = "ACTIVE"`, **non** `isActive`.
- Il timesheet rifiuta più di **24 ore al giorno per dipendente**: distribuire su dipendenti
  e giorni diversi.
- Le fasi standard di una commessa nuova **non hanno una sezione di costo** se non ce l'ha il
  loro modello in anagrafica: senza quello le ore non entrano nella ripartizione del Bilancio
  e il video mostra colonne vuote.
- Una commessa **senza codice** non compare nella pagina `/bilancio`: il filtro tiene solo i
  codici con una data (`C{aammgg}.{NNN}`, es. `C260807.001`).
- In PowerShell **`$pid` è riservato** (è il PID del processo) e le variabili **non
  distinguono maiuscole e minuscole**: `$T` e `$t` sono la stessa cosa. Due bug arrivati da qui.
- Per far tacere il warning di `mysql.exe` che altrimenti aborta lo script:
  `$env:MYSQL_PWD = "…"` invece di `-p…`.

---

## 7. Quanto costa

Un tutorial da ~6 minuti: **15/16 passi**, circa **27 MB** di mp4, e il grosso del tempo se ne
va nello scrivere il copione e nel tarare i dati — la registrazione e il montaggio insieme
stanno sotto i due minuti. Rifare il video dopo una correzione al copione costa quanto una
rigenerazione dell'audio più una registrazione: pochi minuti.

## File in questa cartella

| File | Cosa fa |
|---|---|
| `genera_audio.py` | copione → mp3 per passo + durate |
| `overlay.js` | sottotitoli, riquadri, cursore finto, cartello titolo |
| `registra.js` | registrazione «Configurazione sezioni» |
| `registra-bilancio.js` | registrazione «Bilancio commessa» |
| `monta.py` | audio + video → mp4 e srt |
| `copione.json`, `copione-bilancio.json` | i due copioni girati |
| `popola-demo.ps1`, `popola-demo2.ps1`, `popola-demo3.ps1` | costruzione della commessa dimostrativa |
