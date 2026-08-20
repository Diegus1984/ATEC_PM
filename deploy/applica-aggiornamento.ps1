# =============================================================================
# ATEC PM — Applica un aggiornamento GIÀ CARICATO sul server.
#
# Gira SUL SERVER, di solito lanciato da aggiorna-server.bat via SSH.
# Ferma il servizio, sostituisce il programma tenendo la configurazione di
# produzione, riavvia e verifica. Se il nuovo non risponde, torna al precedente.
#
# Accetta due tipi di pacchetto:
#   - COMPLETO      : contiene tutta la cartella del programma;
#   - DIFFERENZIALE : contiene solo i file cambiati più _aggiornamento-delta.json
#                     (elenco dei file da rimuovere e impronta della versione da
#                     cui è stato calcolato). La versione nuova si compone qui sul
#                     server, copiando quella attuale e applicandoci sopra i cambi.
#
# Con -SoloManifest non applica niente: stampa l'elenco "SHA256|percorso" dei file
# installati, che il PC di sviluppo usa per calcolare le differenze.
# =============================================================================

param(
    [string]$Pacchetto = '',     # default: il pacchetto più recente in C:\ATEC_PM\Updates
    [int]$Porta = 5150,
    [switch]$SoloManifest,
    [switch]$ConServizioFermo    # niente scambio a caldo: ferma il servizio comunque
)

$ErrorActionPreference = 'Stop'

$Radice       = 'C:\ATEC_PM'
$CartServer   = Join-Path $Radice 'Server'
$CartPrec     = Join-Path $Radice 'Server.precedente'
$CartStaging  = Join-Path $Radice 'Updates\staging'
$CartEstratto = Join-Path $Radice 'Updates\estratto'
$CartUpdates  = Join-Path $Radice 'Updates'
$CartBackupCli= Join-Path $Radice 'Updates\client-precedente'
$FileScaduti  = Join-Path $Radice 'Updates\client-scaduti.json'
$CartLog      = Join-Path $Radice 'Logs'
$NomeServizio = 'AtecPmServer'

# File che vivono SOLO sul server e non devono mai essere sovrascritti.
$FileDaTenere = @('appsettings.json', 'appsettings.Secrets.json')

# Restano fuori dal confronto fra versioni: la stessa lista è in _comune.ps1 sul PC
# di sviluppo (questo script gira sul server e non può fare dot-source di quel file).
# Se cambia qui, cambiarla anche là, altrimenti ogni delta viene scartato.
$FileFuoriConfronto = @(
    'appsettings.json',
    'appsettings.development.json',
    'appsettings.secrets.json',
    '_aggiornamento-delta.json'
)

function Get-ManifestCartella {
    param([Parameter(Mandatory)][string]$Cartella)

    $radice = (Resolve-Path $Cartella).Path.TrimEnd('\')
    $righe  = New-Object System.Collections.Generic.List[string]

    # SHA256 con memoria, come sul PC di sviluppo (_comune.ps1): senza, il server digerisce
    # 163 MB a ogni aggiornamento solo per dire quali file ha già. La riga di memoria è
    # "relativo|dimensione|ticks|hash"; se dimensione o data non tornano si ricalcola.
    # La memoria sta in Updates\ e NON dentro la cartella del server, che a ogni
    # aggiornamento viene ricomposta da capo (robocopy /MIR) e se la porterebbe via.
    $fileMemoria = Join-Path $PSScriptRoot '.impronte-installate.txt'
    $memoria = @{}
    if (Test-Path $fileMemoria) {
        foreach ($riga in (Get-Content -LiteralPath $fileMemoria)) {
            $p = $riga -split '\|', 4
            if ($p.Count -eq 4) { $memoria[$p[0]] = @{ Dim = $p[1]; Ticks = $p[2]; Hash = $p[3] } }
        }
    }

    $nuovaMemoria = New-Object System.Collections.Generic.List[string]
    foreach ($f in Get-ChildItem -LiteralPath $radice -Recurse -File -Force) {
        $rel = $f.FullName.Substring($radice.Length + 1).Replace('\', '/')
        if ($FileFuoriConfronto -contains $rel.ToLowerInvariant()) { continue }

        $dim   = $f.Length.ToString()
        $ticks = $f.LastWriteTimeUtc.Ticks.ToString()
        $nota  = $memoria[$rel]
        if ($nota -and $nota.Dim -eq $dim -and $nota.Ticks -eq $ticks) {
            $hash = $nota.Hash
        }
        else {
            $hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
        }

        $righe.Add("$hash|$rel")
        $nuovaMemoria.Add("$rel|$dim|$ticks|$hash")
    }

    Set-Content -LiteralPath $fileMemoria -Value $nuovaMemoria -Encoding ascii
    # Ordinamento ORDINALE (non quello della lingua di Windows): l'impronta deve venire
    # identica qui e sul PC di sviluppo.
    $righe.Sort([StringComparer]::Ordinal)
    return $righe
}

function Get-ImprontaManifest {
    param([string[]]$Righe)

    $lista = New-Object System.Collections.Generic.List[string]
    foreach ($r in $Righe) { if (-not [string]::IsNullOrWhiteSpace($r)) { $lista.Add($r.Trim()) } }
    $lista.Sort([StringComparer]::Ordinal)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try   { $b = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]::Join("`n", $lista))) }
    finally { $sha.Dispose() }
    return ([BitConverter]::ToString($b) -replace '-', '')
}

# --- Modo "elenco": nessuna modifica, nessun privilegio di amministratore --------
if ($SoloManifest) {
    if (-not (Test-Path $CartServer)) { exit 1 }
    Get-ManifestCartella -Cartella $CartServer | ForEach-Object { Write-Output $_ }
    exit 0
}

function Test-Amministratore {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-ServerVivo {
    <#
      «Il gestionale funziona» = risponde E raggiunge il database.

      Fino al 14/08/2026 si guardava /api/health, che dice «ok» anche con MySQL spento: un
      aggiornamento che rompeva la connessione al database veniva dichiarato riuscito e il
      rollback automatico non scattava. Ora si interroga /api/health/ready, che fa una SELECT 1.

      🪤 COMPATIBILITÀ CON LE VERSIONI PRECEDENTI — non togliere il ramo del 404. In caso di
      rollback torna in servizio la versione DI PRIMA, che /api/health/ready non ce l'ha e
      risponde 404: senza questo ripiego un ripristino perfettamente riuscito verrebbe
      dichiarato fallito, e lo script chiuderebbe con «nemmeno il ripristino risponde».
      Il 503 invece è un fallimento vero (server su, database irraggiungibile) e non ricade.

      🪤 QUANTO ASPETTARE — non abbassare i tentativi. Il server comincia a rispondere solo DOPO
      aver applicato le migrazioni (Program.cs chiama InitDatabase prima di app.Run), e una
      migrazione su dati veri può durare minuti. Con la vecchia attesa di 60 secondi (20 × 3s) lo
      script arrivava al rollback MENTRE una migrazione era in corso: Stop-Service -Force sul
      processo che stava scrivendo lo schema, e il DDL di MySQL non torna indietro.
      Tre minuti sono la pazienza minima ragionevole: un aggiornamento che fallisce davvero ci
      mette due minuti in più a dirlo, uno schema rotto a metà costa molto di più.
    #>
    param([int]$Tentativi = 60)
    for ($i = 1; $i -le $Tentativi; $i++) {
        Start-Sleep -Seconds 3
        try {
            $r = Invoke-WebRequest "http://localhost:$Porta/api/health/ready" -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { return $true }
        }
        catch {
            $codice = $null
            if ($_.Exception.Response) { $codice = [int]$_.Exception.Response.StatusCode }

            if ($codice -eq 404) {
                # Versione precedente alla sonda: ci si accontenta di «il processo risponde».
                try {
                    $vecchia = Invoke-WebRequest "http://localhost:$Porta/api/health" -UseBasicParsing -TimeoutSec 5
                    if ($vecchia.StatusCode -eq 200) { return $true }
                }
                catch { }
            }
            elseif ($codice -eq 503) {
                Write-Host '      Il server risponde ma NON raggiunge il database.' -ForegroundColor Yellow
            }
        }
    }
    return $false
}

function Show-UltimoLog {
    $log = Get-ChildItem (Join-Path $CartLog 'server-*.log') -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($log) {
        Write-Host "--- ultime righe di $($log.Name) ---" -ForegroundColor DarkGray
        Get-Content $log.FullName -Tail 30
    }
}

# --- Aggiornamento del solo client, a servizio acceso -------------------------
#
# Perché si può fare senza fermare niente: il server legge `wwwroot` dal disco a ogni
# richiesta (`UseStaticFiles`, nessun manifest in memoria), JS e CSS hanno l'hash nel
# nome (quindi i file nuovi si aggiungono senza dare fastidio a quelli vecchi) e
# `index.html`/`version.json` sono serviti con `no-store`. L'ordine conta:
#   1) prima gli asset nuovi, che nessuno sta ancora chiedendo;
#   2) si controlla che l'index nuovo trovi TUTTI i suoi asset già sul disco;
#   3) poi `index.html` (sostituito in un colpo solo) e infine `version.json`, che è
#      la sveglia delle schede aperte.
# I file vecchi NON si cancellano subito: una scheda ancora aperta sulla build
# precedente deve poter scaricare i suoi chunk. Vengono segnati e rimossi al deploy
# successivo, quando quella scheda ha già visto la barra «Aggiorna adesso».

function Test-FileDiClient {
    param([string]$Rel)
    $r = $Rel.Replace('\', '/')
    if ($r -like 'wwwroot/*') { return $true }
    # Manifest degli asset: lo si legge semmai all'avvio, mai mentre si servono i file.
    # Va comunque aggiornato, altrimenti al primo riavvio resterebbe indietro.
    if ($r -eq 'ATEC.PM.Server.staticwebassets.endpoints.json') { return $true }
    return $false
}

function Copy-FileClient {
    param(
        [string]$Rel, [string]$Sorgente, [string]$Destinazione, [string]$Backup,
        [System.Collections.Generic.List[string]]$Aggiunti
    )
    $relWin = $Rel -replace '/', '\'
    $src = Join-Path $Sorgente $relWin
    $dst = Join-Path $Destinazione $relWin

    if (Test-Path $dst) {
        $bkp = Join-Path $Backup $relWin
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $bkp) | Out-Null
        Copy-Item $dst $bkp -Force
        # Scritto a fianco e poi scambiato: chi sta scaricando index.html non deve mai
        # vederne una versione a metà. Se il file è occupato si ripiega sulla copia
        # normale (la finestra è di microsecondi su file da mezzo kB).
        $tmp = "$dst.nuovo"
        Copy-Item $src $tmp -Force
        try { [IO.File]::Replace($tmp, $dst, $null) }
        catch {
            Copy-Item $src $dst -Force
            Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        # $null -ne, non `if ($Aggiunti)`: una lista VUOTA in PowerShell è falsa, e il
        # primo file aggiunto non finirebbe mai nell'elenco da rimuovere in ripristino.
        if ($null -ne $Aggiunti) { $Aggiunti.Add($Rel) }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
        Copy-Item $src $dst -Force
    }
}

function Test-ClientServito {
    <# Il client nuovo è davvero quello servito? Confronta il build id dell'index
       servito con quello di version.json: se combaciano, la sostituzione è completa. #>
    for ($i = 1; $i -le 5; $i++) {
        try {
            $h = Invoke-RestMethod "http://localhost:$Porta/api/health" -TimeoutSec 5
            $v = Invoke-RestMethod "http://localhost:$Porta/version.json?t=$i" -TimeoutSec 5
            $idx = Invoke-WebRequest "http://localhost:$Porta/" -UseBasicParsing -TimeoutSec 5
            $meta = [regex]::Match($idx.Content, '<meta name="app-build" content="([^"]+)"').Groups[1].Value
            if ($h.status -eq 'ok' -and $v.build -and $meta -eq $v.build) { return $v.build }
        }
        catch { }
        Start-Sleep -Seconds 2
    }
    return $null
}

if (-not (Test-Amministratore)) {
    throw 'Servono i privilegi di amministratore.'
}

if ([string]::IsNullOrWhiteSpace($Pacchetto)) {
    # .tar è il formato attuale (dal 04/08/2026 il pacchetto non viene più compresso:
    # su questa LAN la gzip costava 15 secondi e non faceva risparmiare trasferimento).
    # .tgz resta accettato per i pacchetti eventualmente rimasti da prima.
    $c = Get-ChildItem (Join-Path $CartUpdates '*.tar'), (Join-Path $CartUpdates '*.tgz') -ErrorAction SilentlyContinue |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $c) { throw "Nessun pacchetto .tar in $CartUpdates" }
    $Pacchetto = $c.FullName
}
if (-not (Test-Path $Pacchetto)) { throw "Pacchetto non trovato: $Pacchetto" }
if (-not (Get-Service -Name $NomeServizio -ErrorAction SilentlyContinue)) {
    throw "Servizio '$NomeServizio' non installato: esegui prima install-server.ps1."
}

Write-Host "[1/6] Preparo la nuova versione da $(Split-Path -Leaf $Pacchetto)..." -ForegroundColor Yellow
foreach ($c in @($CartStaging, $CartEstratto)) {
    if (Test-Path $c) { Remove-Item $c -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $CartEstratto | Out-Null
# -xf, non -xzf: libarchive riconosce da sé se il pacchetto è compresso o no, quindi
# funziona sia con i .tar attuali sia con i .tgz eventualmente rimasti da prima.
& tar -xf $Pacchetto -C $CartEstratto
if ($LASTEXITCODE -ne 0) { throw 'Estrazione fallita: il server sta ancora usando la versione precedente.' }

$descrittore = Join-Path $CartEstratto '_aggiornamento-delta.json'
if (Test-Path $descrittore) {
    # ---- pacchetto DIFFERENZIALE: la versione nuova si compone qui ----------------
    $delta = Get-Content $descrittore -Raw | ConvertFrom-Json

    # Il delta vale solo se il server parte ESATTAMENTE dalla versione su cui è stato
    # calcolato: se qualcuno ha toccato la cartella nel frattempo, meglio fermarsi che
    # comporre una versione ibrida.
    $improntaAttuale = Get-ImprontaManifest -Righe (Get-ManifestCartella -Cartella $CartServer)
    if ($improntaAttuale -ne $delta.baseImpronta) {
        throw 'La versione installata non corrisponde a quella attesa dal pacchetto differenziale: il server non è stato toccato. Rilancia con: aggiorna-server.bat -Completo'
    }

    # --- si può fare a caldo? Lo decide il CONTENUTO del pacchetto, non un flag ----
    $radiceEstratto = (Resolve-Path $CartEstratto).Path.TrimEnd('\')
    $fileNelPacchetto = @(
        Get-ChildItem -LiteralPath $radiceEstratto -Recurse -File -Force |
        ForEach-Object { $_.FullName.Substring($radiceEstratto.Length + 1).Replace('\', '/') } |
        Where-Object { $_ -ne '_aggiornamento-delta.json' }
    )
    $tocca = @($fileNelPacchetto) + @($delta.elimina | Where-Object { $_ })
    $estranei = @($tocca | Where-Object { -not (Test-FileDiClient $_) })
    $servizioSu = (Get-Service -Name $NomeServizio).Status -eq 'Running'
    # Anche un delta con SOLO rimozioni (tipico: secondo aggiorna consecutivo che
    # ripulisce gli asset hashati lasciati dal giro a caldo precedente) resta a caldo:
    # senza questa OR si cadrebbe nello stop del servizio per cancellare due file.
    $haLavoroClient = $fileNelPacchetto.Count -gt 0 -or @($delta.elimina | Where-Object { $_ }).Count -gt 0

    if ($estranei.Count -eq 0 -and $haLavoroClient -and $servizioSu -and -not $ConServizioFermo) {
        Write-Host '      Solo client: sostituisco SENZA fermare il servizio.' -ForegroundColor Green

        if (Test-Path $CartBackupCli) { Remove-Item $CartBackupCli -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $CartBackupCli | Out-Null

        # index.html e version.json per ultimi, in quest'ordine: il primo fa passare i
        # browser alla build nuova, il secondo sveglia le schede già aperte.
        $ultimi   = @('wwwroot/index.html', 'wwwroot/version.json')
        $prima    = @($fileNelPacchetto | Where-Object { $ultimi -notcontains $_.ToLowerInvariant() })
        $poi      = @($ultimi | Where-Object { $fileNelPacchetto -contains $_ })
        $aggiunti = New-Object System.Collections.Generic.List[string]

        try {
            foreach ($rel in $prima) {
                Copy-FileClient -Rel $rel -Sorgente $CartEstratto -Destinazione $CartServer `
                                -Backup $CartBackupCli -Aggiunti $aggiunti
            }

            # Prima di far passare i browser alla pagina nuova: tutti gli asset che
            # cita devono essere GIÀ sul disco, altrimenti si spalanca la finestra in
            # cui un utente prende index.html nuovo e un bundle che non esiste.
            $indexNuovo = Join-Path $CartEstratto 'wwwroot\index.html'
            if (Test-Path $indexNuovo) {
                $html = Get-Content $indexNuovo -Raw
                foreach ($m in [regex]::Matches($html, '(?:src|href)="([^"]+)"')) {
                    $u = $m.Groups[1].Value
                    if ($u -notmatch '^\.?/') { continue }        # solo riferimenti locali
                    $rel = (($u -replace '^\./', '') -replace '^/', '').Split('?')[0]
                    if (-not $rel) { continue }
                    if (-not (Test-Path (Join-Path (Join-Path $CartServer 'wwwroot') ($rel -replace '/', '\')))) {
                        throw "L'index nuovo cita un file che non è arrivato: $rel"
                    }
                }
            }

            foreach ($rel in $poi) {
                Copy-FileClient -Rel $rel -Sorgente $CartEstratto -Destinazione $CartServer `
                                -Backup $CartBackupCli -Aggiunti $aggiunti
            }

            $build = Test-ClientServito
            if (-not $build) { throw 'Il client nuovo non risulta servito.' }
        }
        catch {
            Write-Host "Sostituzione a caldo fallita ($($_.Exception.Message)): rimetto il client precedente." -ForegroundColor Red
            $radiceBkp = (Resolve-Path $CartBackupCli).Path.TrimEnd('\')
            foreach ($f in Get-ChildItem -LiteralPath $radiceBkp -Recurse -File -Force -ErrorAction SilentlyContinue) {
                $rel = $f.FullName.Substring($radiceBkp.Length + 1)
                Copy-Item $f.FullName (Join-Path $CartServer $rel) -Force
            }
            foreach ($rel in $aggiunti) {
                Remove-Item -LiteralPath (Join-Path $CartServer ($rel -replace '/', '\')) -Force -ErrorAction SilentlyContinue
            }
            if (Test-ClientServito) {
                throw 'Aggiornamento del client fallito: rimesso quello precedente, il gestionale funziona (nessuna interruzione).'
            }
            throw 'Aggiornamento del client fallito E il ripristino non risponde: intervenire sul server (log in C:\ATEC_PM\Logs).'
        }

        # Rimozioni: prima si esegue quella segnata al giro precedente, poi si segna
        # quella di adesso. Ogni file vecchio sopravvive a un deploy intero.
        $radiceWww = (Resolve-Path (Join-Path $CartServer 'wwwroot')).Path.TrimEnd('\')
        $rimossi = 0
        if (Test-Path $FileScaduti) {
            foreach ($rel in @((Get-Content $FileScaduti -Raw | ConvertFrom-Json).file)) {
                if ([string]::IsNullOrWhiteSpace($rel)) { continue }
                $t = Join-Path $CartServer ($rel -replace '/', '\')
                if (-not $t.StartsWith($radiceWww + '\', [StringComparison]::OrdinalIgnoreCase)) { continue }
                if (Test-Path $t) { Remove-Item -LiteralPath $t -Force -ErrorAction SilentlyContinue; $rimossi++ }
            }
        }
        @{ file = @($delta.elimina | Where-Object { $_ }) } | ConvertTo-Json | Set-Content $FileScaduti -Encoding UTF8

        Remove-Item $CartEstratto -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $Pacchetto -Force -ErrorAction SilentlyContinue
        Get-ChildItem (Join-Path $CartUpdates '*.tar'), (Join-Path $CartUpdates '*.tgz') -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -Skip 2 | Remove-Item -Force -ErrorAction SilentlyContinue

        Write-Host ''
        Write-Host "CLIENT AGGIORNATO A CALDO — build $build, nessuna interruzione del servizio" -ForegroundColor Green
        Write-Host "($($fileNelPacchetto.Count) file sostituiti, $rimossi asset del giro precedente rimossi;" -ForegroundColor DarkGray
        Write-Host " chi ha il gestionale aperto vede la barra «Aggiorna adesso» entro un minuto)" -ForegroundColor DarkGray
        exit 0
    }

    if ($estranei.Count -gt 0) {
        Write-Host "      Tocca anche il server ($($estranei.Count) file fuori da wwwroot): serve fermare il servizio." -ForegroundColor DarkGray
    }
    Write-Host "      Copio la versione attuale e ci applico $($delta.aggiornati) file..." -ForegroundColor DarkGray
    & robocopy $CartServer $CartStaging /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copia della versione attuale fallita (robocopy $LASTEXITCODE): il server non è stato toccato." }

    & robocopy $CartEstratto $CartStaging /E /XF '_aggiornamento-delta.json' /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Applicazione delle differenze fallita (robocopy $LASTEXITCODE): il server non è stato toccato." }

    $radiceStaging = (Resolve-Path $CartStaging).Path.TrimEnd('\')
    foreach ($rel in @($delta.elimina)) {
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $target = Join-Path $radiceStaging ($rel -replace '/', '\')
        # Rete di sicurezza: un percorso che esce dalla cartella di lavoro non si tocca.
        if (-not $target.StartsWith($radiceStaging + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Percorso non valido nell'elenco dei file da rimuovere: $rel"
        }
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
    }
}
else {
    # ---- pacchetto COMPLETO: quello estratto è già tutto -------------------------
    Move-Item $CartEstratto $CartStaging
}
if (Test-Path $CartEstratto) { Remove-Item $CartEstratto -Recurse -Force }

if (-not (Test-Path (Join-Path $CartStaging 'ATEC.PM.Server.exe'))) {
    throw 'Versione nuova incompleta: il server non è stato toccato.'
}
if (-not (Test-Path (Join-Path $CartStaging 'wwwroot\index.html'))) {
    throw 'Versione nuova incompleta: manca il client web. Il server non è stato toccato.'
}

Write-Host '[2/6] Fermo il servizio...' -ForegroundColor Yellow
Stop-Service -Name $NomeServizio -Force -ErrorAction SilentlyContinue
$svc = Get-Service -Name $NomeServizio
$svc.WaitForStatus('Stopped', (New-TimeSpan -Seconds 60))
if ((Get-Service -Name $NomeServizio).Status -ne 'Stopped') {
    throw 'Il servizio non si ferma: aggiornamento annullato.'
}

Write-Host '[3/6] Metto da parte la versione attuale...' -ForegroundColor Yellow
if (Test-Path $CartPrec) { Remove-Item $CartPrec -Recurse -Force }
Move-Item $CartServer $CartPrec
Move-Item $CartStaging $CartServer

Write-Host '[4/6] Rimetto la configurazione di produzione...' -ForegroundColor Yellow
foreach ($f in $FileDaTenere) {
    $src = Join-Path $CartPrec $f
    if (Test-Path $src) { Copy-Item $src (Join-Path $CartServer $f) -Force }
}
Remove-Item (Join-Path $CartServer '_aggiornamento-delta.json') -Force -ErrorAction SilentlyContinue
# La cartella è stata rifatta da capo: gli asset segnati per la rimozione differita
# non ci sono più, l'elenco non ha più senso.
Remove-Item $FileScaduti -Force -ErrorAction SilentlyContinue

# I file arrivano da Updates\staging e si portano dietro i permessi di quella cartella:
# /reset li rimette in ereditarietà dalla radice, così l'account del servizio continua
# a poter leggere l'eseguibile anche se un domani i permessi di Updates cambiassero.
& icacls $CartServer /reset /T /C /Q | Out-Null

Write-Host '[5/6] Riavvio e verifico...' -ForegroundColor Yellow

# try/catch obbligatorio: con $ErrorActionPreference='Stop' (riga 26) un Start-Service che fallisce
# ucciderebbe lo script QUI, cioè prima del ripristino automatico qui sotto — lasciando il server
# aggiornato e spento. È il caso diventato probabile da quando una migrazione fallita interrompe
# l'avvio (Migrations:StopOnError): il processo parte, la migrazione fallisce, il processo esce.
# Se non parte, non si esce: si prosegue e Test-ServerVivo farà scattare il ritorno alla versione
# precedente, che è esattamente il comportamento voluto.
try { Start-Service -Name $NomeServizio }
catch { Write-Host "Il servizio non si è avviato: $($_.Exception.Message)" -ForegroundColor Red }

if (-not (Test-ServerVivo)) {
    Write-Host 'Il nuovo server NON risponde: torno alla versione precedente.' -ForegroundColor Red
    Show-UltimoLog
    Stop-Service -Name $NomeServizio -Force -ErrorAction SilentlyContinue
    (Get-Service -Name $NomeServizio).WaitForStatus('Stopped', (New-TimeSpan -Seconds 60))
    Remove-Item $CartServer -Recurse -Force
    Move-Item $CartPrec $CartServer
    # Stesso motivo di sopra: se anche il ripristino non si avvia, deve arrivare il messaggio
    # esplicito qui sotto, non un errore di PowerShell che non dice cosa è successo.
    try { Start-Service -Name $NomeServizio }
    catch { Write-Host "Nemmeno la versione precedente si avvia: $($_.Exception.Message)" -ForegroundColor Red }
    if (Test-ServerVivo -Tentativi 10) {
        throw 'Aggiornamento fallito: ripristinata la versione precedente, il gestionale funziona.'
    }
    throw 'Aggiornamento fallito E il ripristino non risponde: intervenire sul server (log in C:\ATEC_PM\Logs).'
}

Write-Host '[6/6] Pulizia...' -ForegroundColor Yellow
Remove-Item $Pacchetto -Force -ErrorAction SilentlyContinue
Get-ChildItem (Join-Path $CartUpdates '*.tar'), (Join-Path $CartUpdates '*.tgz') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -Skip 2 | Remove-Item -Force -ErrorAction SilentlyContinue

$health = Invoke-RestMethod "http://localhost:$Porta/api/health" -TimeoutSec 5
Write-Host ''
Write-Host "AGGIORNAMENTO OK — versione $($health.version), ambiente $($health.environment)" -ForegroundColor Green
Write-Host "(la versione precedente resta in $CartPrec finché non se ne installa un'altra)" -ForegroundColor DarkGray
