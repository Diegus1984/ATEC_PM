# =============================================================================
# Funzioni condivise dagli script che girano sul PC DI SVILUPPO (non sul server).
# Dot-sourced da carica-installazione.ps1 e aggiorna-server.ps1.
# =============================================================================

$ErrorActionPreference = 'Stop'

# --- Dati del server aziendale: se cambia la macchina, si cambia SOLO qui -----
$Script:ServerIp    = '192.168.2.150'
$Script:ServerUser  = 'atec'
$Script:ServerPorta = 5150
$Script:ChiaveSsh   = Join-Path $env:USERPROFILE '.ssh\atec_vps'
$Script:CartellaRemota = 'C:/ATEC_PM/Updates'

# Nomi dei due tipi di pacchetto. Il delta ha un nome DIVERSO apposta: install-server.ps1
# sceglie il pacchetto più recente e non deve mai finire per installare mezza versione.
$Script:NomePacchettoCompleto = 'atec-pm-server.tar'
$Script:NomePacchettoDelta    = 'atec-pm-delta.tar'

# File di configurazione che vivono SOLO sul server: restano fuori dal confronto,
# altrimenti il delta proverebbe a sovrascriverli o a cancellarli.
# NB: la stessa lista è ripetuta in applica-aggiornamento.ps1 (gira sul server, non
# può fare dot-source di questo file). Se cambia qui, cambiarla anche là.
$Script:FileFuoriConfronto = @(
    'appsettings.json',
    'appsettings.development.json',
    'appsettings.secrets.json',
    '_aggiornamento-delta.json'
)

function Get-RadiceProgetto {
    # deploy\ sta dentro ATEC_PM\ : la radice è la cartella padre di questa.
    return (Split-Path -Parent $PSScriptRoot)
}

# --- Cronometro delle fasi ---------------------------------------------------
# «Il deploy è lento» non si corregge a naso: senza sapere DOVE se ne va il tempo si
# ottimizza il pezzo sbagliato. Ogni fase stampa i suoi secondi e alla fine c'è la
# classifica: la prima riga è sempre il prossimo lavoro da fare.

function Start-Cronometro {
    $Script:CronoTotale = [System.Diagnostics.Stopwatch]::StartNew()
    $Script:CronoUltima = 0.0
    $Script:CronoTappe  = New-Object System.Collections.Generic.List[object]
}

function Add-Tappa {
    param([string]$Nome)
    if (-not $Script:CronoTotale) { return }
    $ora   = $Script:CronoTotale.Elapsed.TotalSeconds
    $delta = $ora - $Script:CronoUltima
    $Script:CronoUltima = $ora
    $Script:CronoTappe.Add([pscustomobject]@{ Fase = $Nome; Secondi = [math]::Round($delta, 1) })
    # ${Nome} con le graffe: senza, PowerShell legge "$Nome:" come una variabile
    # con namespace (tipo $env:PATH) e il file non si carica più — l'ha fatto davvero.
    Write-Host ("      · ${Nome}: {0:N1} s" -f $delta) -ForegroundColor DarkGray
}

function Show-Cronometro {
    if (-not $Script:CronoTotale -or $Script:CronoTappe.Count -eq 0) { return }
    $totale = $Script:CronoTotale.Elapsed.TotalSeconds
    Write-Host ''
    Write-Host ("DOVE SE N'E' ANDATO IL TEMPO (totale {0:N1} s)" -f $totale) -ForegroundColor Cyan
    foreach ($t in ($Script:CronoTappe | Sort-Object Secondi -Descending)) {
        $quota = 0
        if ($totale -gt 0) { $quota = [math]::Round(100 * $t.Secondi / $totale) }
        Write-Host ("   {0,6:N1} s  {1,3}%  {2}" -f $t.Secondi, $quota, $t.Fase)
    }
}

# --- Impronta dei sorgenti che i test possono vedere -------------------------

function Get-ImprontaSorgenti {
    <#
      SHA256 di tutto il codice da cui dipende l'esito dei test: server, DTO condivisi e
      i test stessi. Il client web NON c'è dentro apposta — i test sono in C# e non lo
      guardano, quindi «ho cambiato una scritta nella pagina» non ha motivo di far
      ripartire tre minuti e mezzo di test.
    #>
    $radice = Get-RadiceProgetto
    $righe  = New-Object System.Collections.Generic.List[string]

    foreach ($cartella in @('ATEC.PM.Server', 'ATEC.PM.Shared', 'ATEC.PM.Tests')) {
        $base = Join-Path $radice $cartella
        if (-not (Test-Path $base)) { continue }
        # 🪤 `wwwroot` va escluso: è il client COMPILATO copiato dentro il progetto server, e
        # contiene `version.json` con l'identificativo della build — cambia a ogni `npm build`.
        # Includendolo, l'impronta non tornava mai uguale e i test si rifacevano sempre: il
        # salto sembrava funzionare e invece non saltava niente (visto succedere, 16/08/2026).
        # 🪤 Stessa storia per `changelog.json`: sta in ATEC.PM.Server ed è il DEPLOY
        # stesso a riscriverlo (una voce per build). Contandolo, l'impronta appena registrata
        # verde moriva sul posto: il deploy dopo rifaceva i ~190 s di test anche per una
        # modifica di sola SPA. Nessun test lo legge (visto succedere, 29/08/2026).
        $file = Get-ChildItem -LiteralPath $base -Recurse -File -Force |
                Where-Object {
                    $_.Extension -in @('.cs', '.csproj', '.json') -and
                    $_.Name -ne 'changelog.json' -and
                    $_.FullName -notmatch '\\(bin|obj|wwwroot|node_modules)\\'
                }
        foreach ($f in $file) {
            $rel = $f.FullName.Substring($radice.Length + 1).Replace('\', '/')
            $righe.Add("$((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash)|$rel")
        }
    }

    $righe.Sort([StringComparer]::Ordinal)

    # L'elenco per esteso resta su disco: quando i test si rifanno e non dovrebbero, la
    # domanda è sempre «quale file è cambiato?», e senza questo non c'è modo di rispondere
    # (l'impronta è un numero solo). Costa una scrittura di poche decine di KB.
    $fileElenco = Join-Path $PSScriptRoot 'out\.sorgenti-ultimo-calcolo.txt'
    $cartella = Split-Path -Parent $fileElenco
    if (-not (Test-Path $cartella)) { New-Item -ItemType Directory -Path $cartella -Force | Out-Null }
    Set-Content -LiteralPath $fileElenco -Value $righe -Encoding ascii

    $sha   = [System.Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes([string]::Join("`n", $righe)))
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-ImprontaClient {
    <#
      SHA256 dei sorgenti del CLIENT WEB: `src`, `public` e i file di configurazione della
      radice di `atec-pm-web`. Serve a saltare `npm run build` quando non c'è niente di
      nuovo da compilare — nel deploy di prima quei 30 secondi (il 48% del totale) sono
      andati a ricostruire un bundle identico dopo una modifica di solo C#.

      Fuori dall'impronta: `node_modules` (non è codice nostro, e cambia solo con
      package-lock, che invece c'è) e `dist` (è il RISULTATO: includerlo vorrebbe dire che
      l'impronta non torna mai uguale — la stessa trappola in cui era caduta quella dei test
      con `wwwroot`).

      Guarda **percorso, dimensione e data di modifica**, non il contenuto: digerire tutti i
      sorgenti del client costava 15 secondi a freddo (misurati), cioè metà di quello che si
      vuole risparmiare. È lo stesso criterio già scelto per il runtime .NET (TODO §3 ④):
      perché sfugga una modifica bisogna cambiare un file lasciandogli dimensione e data
      identiche, che scrivendo codice non capita.
    #>
    $radice = Get-RadiceProgetto
    $webDir = Join-Path $radice 'atec-pm-web'
    if (-not (Test-Path $webDir)) { return '' }

    $righe = New-Object System.Collections.Generic.List[string]
    $file = Get-ChildItem -LiteralPath $webDir -Recurse -File -Force |
            Where-Object { $_.FullName -notmatch '\\(node_modules|dist|\.vite|coverage)\\' }
    foreach ($f in $file) {
        $rel = $f.FullName.Substring($webDir.Length + 1).Replace('\', '/')
        $righe.Add("$rel|$($f.Length)|$($f.LastWriteTimeUtc.Ticks)")
    }
    $righe.Sort([StringComparer]::Ordinal)

    $sha   = [System.Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes([string]::Join("`n", $righe)))
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-FileBuildClient {
    return (Join-Path $PSScriptRoot 'out\.ultima-build-client')
}

function Get-FileEsitoTest {
    return (Join-Path $PSScriptRoot 'out\.ultimo-test-verde')
}

function Test-Prerequisiti {
    foreach ($cmd in @('dotnet', 'ssh', 'scp', 'tar')) {
        if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
            throw "Comando '$cmd' non trovato nel PATH."
        }
    }
    if (-not (Test-Path $Script:ChiaveSsh)) {
        throw "Chiave SSH non trovata: $Script:ChiaveSsh"
    }
}

# --- Lavoro non committato ---------------------------------------------------
# Il deploy compila dal WORKING TREE, non dall'ultimo commit: tutto quello che sta nella
# cartella parte per il server, committato o no. Il 28/08/2026 è andato in produzione il
# lavoro non committato di un'ALTRA sessione aperta sullo stesso repo (v117 e v118), senza
# che nessuno l'avesse deciso. Da qui il deploy si ferma se c'è qualcosa di non committato —
# e lo elenca, così si sa cosa si sta per spedire. `-AncheSporco` lo lascia passare.
#
# Fanno eccezione i due file che il deploy STESSO riscrive (changelog e mappa endpoint):
# restano modificati fino al commit di servizio che li registra, e fermare il deploy dopo
# per colpa del deploy prima sarebbe un cane che si morde la coda.
function Get-LavoroNonCommittato {
    $radice = Get-RadiceProgetto
    # core.safecrlf=false: senza, git avvisa su stderr dei file con LF («will be replaced by
    # CRLF») e con $ErrorActionPreference = 'Stop' un avviso su stderr può diventare un errore.
    $righe = @(& git -C $radice -c core.safecrlf=false status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'git status fallito: la cartella del progetto non è un repository?' }
    $esclusi = @('ATEC.PM.Server/changelog.json', 'docs/archivio/PERMESSI-MAPPA-ENDPOINT.gen.md')
    return @($righe | Where-Object {
        # Formato: due lettere di stato, uno spazio, il percorso (con «vecchio -> nuovo» se rinominato).
        $percorso = $_.Substring(3).Trim()
        -not ($esclusi -contains $percorso)
    })
}

function Test-LavoroNonCommittato {
    param([switch]$AncheSporco)
    $sporco = Get-LavoroNonCommittato
    if ($sporco.Count -eq 0) { return }
    if ($AncheSporco) {
        Write-Host "[Git] $($sporco.Count) file non committati: si pubblica lo stesso (-AncheSporco)." -ForegroundColor Yellow
        $sporco | ForEach-Object { Write-Host "      $_" -ForegroundColor Yellow }
        return
    }
    Write-Host ''
    Write-Host "FERMATO: $($sporco.Count) file non committati, e il deploy compila dalla cartella, non dal commit." -ForegroundColor Red
    $sporco | ForEach-Object { Write-Host "      $_" -ForegroundColor Red }
    Write-Host '      Committa (o scarta) prima di pubblicare; -AncheSporco pubblica lo stesso, sapendo cosa parte.' -ForegroundColor Red
    throw 'Lavoro non committato nel working tree: deploy fermato prima di toccare qualsiasi cosa.'
}

function Invoke-TestAutomatici {
    <#
      I test automatici PRIMA di toccare il server: se qualcosa si è rotto, l'aggiornamento
      si ferma qui e in azienda non cambia niente.

      Sta prima della compilazione apposta — npm build + dotnet publish durano minuti, e
      scoprire il guasto dopo averli aspettati non serve a nessuno.

      Il passo non è numerato ([Test] e non [1/7]) perché la numerazione 1..6 è condivisa con
      carica-installazione.ps1, che i test non li esegue: rinumerarla qui la sfaserebbe lì.

      I test che hanno bisogno di MySQL si saltano DA SOLI se non lo trovano (vedi
      FactRichiedeMySqlAttribute): su un PC senza database l'aggiornamento prosegue lo stesso,
      con i soli test puri.

      Dal 16/08/2026 si saltano anche quando NON C'E' NIENTE DA PROVARE: se l'impronta dei
      sorgenti C# è identica a quella dell'ultima esecuzione verde, l'esito non può essere
      diverso. È il caso di tutti i giorni — «ho corretto una scritta nel client, ripubblico» —
      e prima costava tre minuti e mezzo per riscoprire la stessa risposta.
      Con -ConTest si rifanno comunque.
    #>
    param([switch]$Forza)

    $radice = Get-RadiceProgetto
    $proj   = Join-Path $radice 'ATEC.PM.Tests\ATEC.PM.Tests.csproj'

    if (-not (Test-Path $proj)) {
        Write-Host '[Test] Progetto dei test non trovato: passo oltre.' -ForegroundColor DarkGray
        return
    }

    $impronta = Get-ImprontaSorgenti
    $fileEsito = Get-FileEsitoTest

    if (-not $Forza -and (Test-Path $fileEsito)) {
        $precedente = (Get-Content -LiteralPath $fileEsito -Raw).Trim()
        if ($precedente -eq $impronta) {
            Write-Host '[Test] Codice C# identico all''ultima esecuzione verde: niente da riprovare.' -ForegroundColor Green
            Write-Host '       (per rifarli comunque: aggiorna-server.bat -ConTest)' -ForegroundColor DarkGray
            return
        }
    }

    Write-Host '[Test] Eseguo i test automatici...' -ForegroundColor Cyan
    & dotnet test $proj --nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) {
        # L'esito precedente non vale più: la prossima volta si riprova, non si salta.
        if (Test-Path $fileEsito) { Remove-Item -LiteralPath $fileEsito -Force }
        throw 'TEST FALLITI: niente è stato toccato sul server. Correggi il problema e rilancia (in emergenza: aggiorna-server.bat -SenzaTest).'
    }

    $cartellaEsito = Split-Path -Parent $fileEsito
    if (-not (Test-Path $cartellaEsito)) { New-Item -ItemType Directory -Path $cartellaEsito -Force | Out-Null }
    Set-Content -LiteralPath $fileEsito -Value $impronta -Encoding ascii
}

function Update-Changelog {
    <#
      Arricchisce ATEC.PM.Server\changelog.json con i commit dall'ultima voce: è la
      fonte della pagina «Changelog versioni» (chiave nav.changelog, di default solo
      Admin). Va chiamata DOPO la build del client — il build id si legge da
      wwwroot\version.json — e PRIMA di dotnet publish, che spedisce il file insieme
      alle DLL. Un errore qui NON ferma il deploy: il changelog è cronaca, non funzione.
    #>
    param([string]$Radice)
    try {
        $percorso    = Join-Path $Radice 'ATEC.PM.Server\changelog.json'
        $versionFile = Join-Path $Radice 'ATEC.PM.Server\wwwroot\version.json'

        # TRAPPOLA: Get-Content in PowerShell 5.1, senza -Encoding, decodifica con la
        # codepage ANSI di sistema (Windows-1252). Su questi JSON — scritti in UTF-8 —
        # rileggere così spezza ogni accento, e la riscrittura in UTF-8 ricodifica i
        # byte già codificati: la corruzione si ALLUNGA a ogni deploy. Si legge con
        # ReadAllText, che è UTF-8 (e toglie il BOM se qualcuno lo ha messo).
        $build = ''
        if (Test-Path $versionFile) {
            $v = [IO.File]::ReadAllText($versionFile, [Text.Encoding]::UTF8) | ConvertFrom-Json
            if ($v -and $v.build) { $build = [string]$v.build }
        }
        if (-not $build) {
            Write-Host '[Changelog] version.json senza build id: voce saltata.' -ForegroundColor Yellow
            return
        }

        $voci = @()
        if (Test-Path $percorso) {
            $doc = [IO.File]::ReadAllText($percorso, [Text.Encoding]::UTF8) | ConvertFrom-Json
            if ($doc -and $doc.voci) { $voci = @($doc.voci) }
        }
        $ultimoHash = ''
        if ($voci.Count -gt 0 -and $voci[0].hash) { $ultimoHash = [string]$voci[0].hash }

        # TRAPPOLA (28/08/2026): l'ancora dev'essere un hash VERO, non una parola.
        # Una voce scritta a mano con hash = «head» ha bloccato il changelog per tre
        # deploy di fila: git risolve `head` come HEAD, quindi `head..HEAD` è sempre
        # vuoto e la funzione usciva dicendo «nessun commit nuovo» — sempre, per
        # sempre, senza che niente sembrasse rotto. Qui si controlla che l'ancora sia
        # un commit raggiungibile e DIVERSO da HEAD; se non lo è si dice ad alta voce
        # invece di registrare il silenzio.
        if ($ultimoHash -and $ultimoHash -notmatch '^[0-9a-fA-F]{7,40}$') {
            Write-Host ("[Changelog] ATTENZIONE: l'ancora dell'ultima voce non è un hash ('{0}'): " -f $ultimoHash) -ForegroundColor Yellow
            Write-Host '            nessuna voce verrà più registrata finché non la si corregge in changelog.json.' -ForegroundColor Yellow
            return
        }

        # I messaggi dei commit sono UTF-8: senza questo, gli accenti arrivano rotti
        # (PowerShell 5.1 decodifica l'output dei comandi esterni con la codepage OEM).
        $vecchiaEnc = [Console]::OutputEncoding
        try {
            [Console]::OutputEncoding = [Text.Encoding]::UTF8
            # Solo la prima riga di ogni commit, niente merge: è la riga "da changelog".
            # Prima voce in assoluto: gli ultimi 15 commit come storia di partenza.
            $intervallo = if ($ultimoHash) { "$ultimoHash..HEAD" } else { '-15' }
            $modifiche = @(& git -C $Radice log $intervallo --no-merges --format='%s')
            if ($LASTEXITCODE -ne 0) { throw "git log fallito su '$intervallo'" }
            $head = [string](& git -C $Radice rev-parse HEAD)
            if ($LASTEXITCODE -ne 0) { throw 'git rev-parse HEAD fallito' }
        }
        finally { [Console]::OutputEncoding = $vecchiaEnc }

        # I commit di servizio del changelog stesso (convenzione: prima riga che inizia
        # con «Changelog:») non sono modifiche del gestionale: fuori dalle voci.
        $modifiche = @($modifiche | Where-Object { $_ -and $_.Trim() -and $_ -notmatch '^(?i)changelog:' })
        if ($modifiche.Count -eq 0) {
            Write-Host '[Changelog] Nessun commit nuovo dall''ultima voce: niente da registrare.' -ForegroundColor DarkGray
            return
        }

        $nuova = [pscustomobject]@{
            build     = $build
            data      = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss')
            hash      = $head.Trim()
            modifiche = $modifiche
        }
        $tutte = @($nuova) + $voci
        $json = ConvertTo-Json @{ voci = $tutte } -Depth 6
        # UTF-8 SENZA BOM: il file lo rilegge System.Text.Json sul server.
        [IO.File]::WriteAllText($percorso, $json, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ("[Changelog] Registrata la build {0} ({1} modifiche)." -f $build, $modifiche.Count) -ForegroundColor Cyan
    }
    catch {
        Write-Host ("[Changelog] Aggiornamento saltato: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
    }
}

function Publish-Server {
    <#
      Compila client web + server e lascia il risultato in deploy\out\server.
      Ritorna il percorso della cartella pubblicata (NIENTE pacchetto: lo decide
      chi chiama, completo o solo differenze).

      `npm run build` si salta quando i sorgenti del client sono identici all'ultima build
      riuscita (stessa idea dei test): 30 secondi che non servono se si è toccato solo C#.
      Con -ConClient si ricompila comunque.
    #>
    param([switch]$ConClient)
    $radice   = Get-RadiceProgetto
    $webDir   = Join-Path $radice 'atec-pm-web'
    $srvProj  = Join-Path $radice 'ATEC.PM.Server\ATEC.PM.Server.csproj'
    $wwwroot  = Join-Path $radice 'ATEC.PM.Server\wwwroot'
    $outDir   = Join-Path $PSScriptRoot 'out\server'

    if (-not (Test-Path $srvProj)) { throw "Progetto server non trovato: $srvProj" }

    # Node non è nel PATH di sistema su questo PC.
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        $env:Path = 'C:\Program Files\nodejs;' + $env:Path
    }
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw 'npm non trovato: installa Node.js o aggiungilo al PATH.'
    }

    $distDir = Join-Path $webDir 'dist'
    $improntaClient = Get-ImprontaClient
    $fileBuild = Get-FileBuildClient
    $clientGiaPronto = $false
    if (-not $ConClient -and (Test-Path $fileBuild) -and (Test-Path (Join-Path $distDir 'index.html'))) {
        $precedente = (Get-Content -LiteralPath $fileBuild -Raw).Trim()
        $clientGiaPronto = ($precedente -eq $improntaClient)
    }

    if ($clientGiaPronto) {
        Write-Host '[1/6] Client web identico all''ultima build: riuso quella (niente npm build).' -ForegroundColor Green
        Write-Host '      (per ricompilarlo comunque: aggiorna-server.bat -ConClient)' -ForegroundColor DarkGray
        # NB: `version.json` e il `<meta app-build>` restano quelli della build precedente, ed
        # è giusto così — identificano la versione del CLIENT, non del deploy. Nessun banner
        # «aggiorna adesso» agli utenti: non c'è niente di nuovo da ricaricare.
    }
    else {
        Write-Host '[1/6] Compilo il client web (atec-pm-web)...' -ForegroundColor Cyan
        Push-Location $webDir
        try {
            # | Out-Host su TUTTI i comandi esterni: quello che stampano finirebbe altrimenti
            # nel valore di ritorno della funzione, e il percorso della cartella arriverebbe a
            # chi chiama appiccicato all'output di npm.
            if (-not (Test-Path (Join-Path $webDir 'node_modules'))) {
                & npm install | Out-Host
                if ($LASTEXITCODE -ne 0) { throw 'npm install fallito.' }
            }
            # I test della logica pura del client (vitest, ~1 s) girano PRIMA della build: se un
            # calcolo o una formattazione si rompe, il server non viene toccato.
            & npm test | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'Test del client web falliti: niente è stato toccato sul server.' }
            & npm run build | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'Build del client web fallita: niente è stato toccato sul server.' }
        }
        finally { Pop-Location }

        # Registrata DOPO la build, e ricalcolata: se qualcuno ha salvato un file mentre
        # Vite girava, l'impronta di prima darebbe per compilato codice che non lo è.
        $cartellaBuild = Split-Path -Parent $fileBuild
        if (-not (Test-Path $cartellaBuild)) { New-Item -ItemType Directory -Path $cartellaBuild -Force | Out-Null }
        Set-Content -LiteralPath $fileBuild -Value (Get-ImprontaClient) -Encoding ascii
    }

    Add-Tappa 'npm build (client)'

    Write-Host '[2/6] Allineo wwwroot alla build appena fatta...' -ForegroundColor Cyan
    # /MIR cancella i file vecchi: mescolare asset di build diverse fa fallire il
    # caricamento della pagina nel browser (errore di integrità sui file hashati).
    & robocopy (Join-Path $webDir 'dist') $wwwroot /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copia dist -> wwwroot fallita (robocopy $LASTEXITCODE)." }

    Add-Tappa 'copia in wwwroot'

    # Changelog versioni: si arricchisce QUI — dopo la build del client (per il build id)
    # e prima del publish, che lo spedisce insieme alle DLL.
    Update-Changelog -Radice $radice

    Write-Host '[3/6] Pubblico il server (win-x64 self-contained)...' -ForegroundColor Cyan
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    # BuildWebClient=false: il client web l'abbiamo già compilato noi al passo 1.
    & dotnet publish $srvProj -c Release -r win-x64 --self-contained true -p:BuildWebClient=false -o $outDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fallito: niente è stato toccato sul server.' }

    # La configurazione di produzione (connessione DB, chiave JWT, percorsi) vive SOLO
    # sul server: se finisse nel pacchetto, il primo aggiornamento butterebbe giù i login.
    Remove-Item (Join-Path $outDir 'appsettings.json') -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $outDir 'appsettings.Development.json') -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $outDir 'appsettings.Secrets.json') -ErrorAction SilentlyContinue

    if (-not (Test-Path (Join-Path $outDir 'ATEC.PM.Server.exe'))) {
        throw 'Pubblicazione incompleta: ATEC.PM.Server.exe non trovato.'
    }
    if (-not (Test-Path (Join-Path $outDir 'wwwroot\index.html'))) {
        throw 'Pubblicazione incompleta: manca il client web in wwwroot.'
    }

    Add-Tappa 'dotnet publish (server)'

    return $outDir
}

function New-PacchettoCompleto {
    <#
      Impacchetta TUTTA la cartella pubblicata. Serve per la prima installazione e
      come rete di sicurezza quando il confronto con il server non è possibile.
      Ritorna il percorso del pacchetto.
    #>
    param([string]$Publish)

    $pacchetto = Join-Path $PSScriptRoot "out\$($Script:NomePacchettoCompleto)"

    # Pacchetto NON compresso, di proposito (misurato il 04/08/2026 su questa LAN):
    #   tar -czf → 14,8 s per comprimere, 67,6 MB, 2,1 s di trasferimento
    #   tar -cf  →  0,2 s,                160,7 MB, 2,0 s di trasferimento
    # La rete regge 80 MB/s sui file grandi, quindi la gzip costava ~15 secondi per non
    # far risparmiare niente. In estrazione non cambia nulla: `tar -xf` riconosce da sé
    # sia i file compressi sia quelli non compressi (bsdtar/libarchive).
    if (Test-Path $pacchetto) { Remove-Item $pacchetto -Force }
    & tar -cf $pacchetto -C $Publish . | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Creazione del pacchetto fallita.' }

    $mb = [math]::Round((Get-Item $pacchetto).Length / 1MB, 1)
    Write-Host "      Pacchetto COMPLETO pronto: $mb MB" -ForegroundColor DarkGray
    return $pacchetto
}

function New-PacchettoServer {
    <#
      Compatibilità: compila e produce il pacchetto completo in un colpo solo
      (usata da carica-installazione.ps1, dove il delta non avrebbe senso).
    #>
    $publish = Publish-Server
    Write-Host '[4/6] Preparo il pacchetto...' -ForegroundColor Cyan
    return (New-PacchettoCompleto -Publish $publish)
}

# --- Confronto fra la cartella pubblicata e quella già installata sul server ---

function Get-ManifestCartella {
    <#
      Elenco ordinato "SHA256|percorso/relativo" dei file di una cartella.
      Stessa logica in applica-aggiornamento.ps1: le due liste devono coincidere
      file per file, altrimenti il delta viene scartato.
    #>
    param([Parameter(Mandatory)][string]$Cartella)

    $radice = (Resolve-Path $Cartella).Path.TrimEnd('\')
    $righe  = New-Object System.Collections.Generic.List[string]
    # SHA256 con memoria (16/08/2026): il confronto pesava 33 s su 228 di aggiornamento —
    # 624 file, 163 MB, digeriti tutti a ogni pubblicazione per riscoprire che i ~150 MB del
    # runtime .NET non cambiano mai. Ora l'impronta di un file si ricalcola SOLO se sono
    # cambiate dimensione o data di modifica; altrimenti si riusa quella già nota.
    # La riga di memoria è "relativo|dimensione|ticks|hash": se uno dei tre non torna, si
    # ricalcola. Perché un file diverso passi inosservato dovrebbe avere dimensione identica
    # E data identica al decimo di microsecondo.
    $fileMemoria = Join-Path $PSScriptRoot 'out\.impronte-pubblicate.txt'
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
        if ($Script:FileFuoriConfronto -contains $rel.ToLowerInvariant()) { continue }

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

    $cartellaMemoria = Split-Path -Parent $fileMemoria
    if (-not (Test-Path $cartellaMemoria)) { New-Item -ItemType Directory -Path $cartellaMemoria -Force | Out-Null }
    Set-Content -LiteralPath $fileMemoria -Value $nuovaMemoria -Encoding ascii
    # Ordinamento ORDINALE (non quello della lingua di Windows): l'impronta deve venire
    # identica qui e sul server, che potrebbero avere impostazioni locali diverse.
    $righe.Sort([StringComparer]::Ordinal)
    return $righe
}

function Get-ImprontaManifest {
    <# Un solo hash che riassume tutto il manifest: serve al server per verificare di
       partire davvero dalla versione su cui abbiamo calcolato le differenze. #>
    param([string[]]$Righe)

    $lista = New-Object System.Collections.Generic.List[string]
    foreach ($r in $Righe) { if (-not [string]::IsNullOrWhiteSpace($r)) { $lista.Add($r.Trim()) } }
    $lista.Sort([StringComparer]::Ordinal)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try   { $b = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]::Join("`n", $lista))) }
    finally { $sha.Dispose() }
    return ([BitConverter]::ToString($b) -replace '-', '')
}

function Get-ManifestRemoto {
    <#
      Chiede al server l'elenco dei file attualmente installati.
      Ritorna $null se non è possibile (prima installazione, script non ancora
      caricato, servizio mai installato): in quel caso si spedisce tutto.
    #>
    $out = Invoke-SshServer 'powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\applica-aggiornamento.ps1 -SoloManifest'
    if ($LASTEXITCODE -ne 0) { return $null }

    # Si tiene solo ciò che ha la forma "hash|percorso": qualunque banner o messaggio
    # della shell remota finirebbe altrimenti dentro il manifest.
    $righe = @($out | Where-Object { $_ -match '^[0-9A-Fa-f]{64}\|' })
    if ($righe.Count -lt 5) { return $null }
    return $righe
}

function New-PacchettoDelta {
    <#
      Pacchetto con i SOLI file nuovi o cambiati rispetto a quelli sul server, più
      l'elenco di quelli da cancellare. Ritorna $null se il risparmio non vale la
      pena (allora si spedisce il pacchetto completo).
    #>
    param(
        [Parameter(Mandatory)][string]$Publish,
        [Parameter(Mandatory)][string[]]$ManifestRemoto
    )

    # Le hashtable di PowerShell confrontano le chiavi senza distinguere maiuscole e
    # minuscole: è esattamente come si comportano i percorsi su Windows.
    $remoto = @{}
    foreach ($r in $ManifestRemoto) {
        $p = $r.Split('|', 2)
        if ($p.Count -eq 2) { $remoto[$p[1]] = $p[0] }
    }

    $localeRighe = Get-ManifestCartella -Cartella $Publish
    $locale = @{}
    foreach ($r in $localeRighe) {
        $p = $r.Split('|', 2)
        if ($p.Count -eq 2) { $locale[$p[1]] = $p[0] }
    }

    $daInviare = New-Object System.Collections.Generic.List[string]
    foreach ($rel in $locale.Keys) {
        if (-not $remoto.ContainsKey($rel) -or $remoto[$rel] -ne $locale[$rel]) { $daInviare.Add($rel) }
    }
    $daEliminare = New-Object System.Collections.Generic.List[string]
    foreach ($rel in $remoto.Keys) {
        if (-not $locale.ContainsKey($rel)) { $daEliminare.Add($rel) }
    }
    $daInviare.Sort([StringComparer]::Ordinal)
    $daEliminare.Sort([StringComparer]::Ordinal)

    $bytesDelta = 0L
    foreach ($rel in $daInviare) { $bytesDelta += (Get-Item -LiteralPath (Join-Path $Publish $rel.Replace('/', '\'))).Length }
    $bytesTotali = 0L
    foreach ($f in Get-ChildItem -LiteralPath $Publish -Recurse -File -Force) { $bytesTotali += $f.Length }

    Write-Host ("      Differenze: {0} file da aggiornare, {1} da rimuovere ({2} MB su {3} MB)" -f `
        $daInviare.Count, $daEliminare.Count,
        [math]::Round($bytesDelta / 1MB, 1), [math]::Round($bytesTotali / 1MB, 1)) -ForegroundColor DarkGray

    # Niente da fare: senza questo controllo si spedirebbe un delta vuoto e sul server
    # (fileNelPacchetto = 0) si cadrebbe nel percorso «a freddo» — stop del servizio
    # e swap di tutta la cartella per zero cambiamenti.
    if ($daInviare.Count -eq 0 -and $daEliminare.Count -eq 0) {
        Write-Host '      Nessuna differenza rispetto al server: niente da caricare.' -ForegroundColor Green
        return [pscustomobject]@{ NienteDaFare = $true }
    }

    # Se è cambiato quasi tutto (aggiornamento del runtime .NET, prima volta dopo un
    # cambio di macchina) il delta costa più di quanto risparmia: meglio il completo,
    # che sul server non deve nemmeno ricopiare la versione precedente.
    if ($bytesTotali -gt 0 -and $bytesDelta -gt ($bytesTotali * 0.6)) {
        Write-Host '      Cambiato troppo: uso il pacchetto completo.' -ForegroundColor DarkGray
        return $null
    }

    # Se si tocca solo la parte web, il server la sostituisce SENZA fermarsi (lo
    # decide comunque lui guardando il contenuto del pacchetto: qui è solo per dirlo
    # subito a chi sta guardando il deploy).
    $soloClient = -not (@($daInviare) + @($daEliminare) | Where-Object {
        $_ -notlike 'wwwroot/*' -and $_ -ne 'ATEC.PM.Server.staticwebassets.endpoints.json'
    })
    if ($soloClient) {
        Write-Host '      Solo client: il server non verra fermato (nessuna interruzione).' -ForegroundColor Green
    }

    $descrittore = [ordered]@{
        tipo          = 'delta'
        creato        = (Get-Date).ToString('s')
        baseImpronta  = (Get-ImprontaManifest -Righe $ManifestRemoto)
        aggiornati    = $daInviare.Count
        soloClient    = [bool]$soloClient
        elimina       = @($daEliminare)
    }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $Publish '_aggiornamento-delta.json'),
        ($descrittore | ConvertTo-Json -Depth 3), $utf8)

    # tar prende l'elenco dei file da un file di testo (-T): la riga di comando non
    # reggerebbe migliaia di percorsi.
    $elenco = Join-Path $PSScriptRoot 'out\delta-files.txt'
    $tutti  = New-Object System.Collections.Generic.List[string]
    $tutti.Add('_aggiornamento-delta.json')
    foreach ($rel in $daInviare) { $tutti.Add($rel) }
    [IO.File]::WriteAllLines($elenco, $tutti, $utf8)

    $pacchetto = Join-Path $PSScriptRoot "out\$($Script:NomePacchettoDelta)"
    if (Test-Path $pacchetto) { Remove-Item $pacchetto -Force }
    & tar -cf $pacchetto -C $Publish -T $elenco | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Creazione del pacchetto differenziale fallita.' }
    Remove-Item $elenco -Force -ErrorAction SilentlyContinue

    $mb = [math]::Round((Get-Item $pacchetto).Length / 1MB, 1)
    Write-Host "      Pacchetto DIFFERENZIALE pronto: $mb MB" -ForegroundColor DarkGray
    return $pacchetto
}

function Send-FileAlServer {
    param([Parameter(Mandatory)][string[]]$File)

    # Niente virgolette dentro il comando remoto: la shell di default via SSH può essere
    # cmd o PowerShell e l'annidamento degli apici si rompe. I percorsi non hanno spazi.
    Invoke-SshServer 'powershell -NoProfile -Command New-Item -ItemType Directory -Force -Path C:\ATEC_PM\Updates' | Out-Null
    foreach ($f in $File) {
        if (-not (Test-Path $f)) { throw "File da caricare non trovato: $f" }
        & scp -i $Script:ChiaveSsh -o BatchMode=yes $f "$($Script:ServerUser)@$($Script:ServerIp):$($Script:CartellaRemota)/"
        if ($LASTEXITCODE -ne 0) {
            throw "Caricamento di $(Split-Path -Leaf $f) fallito: il server sta ancora usando la versione precedente."
        }
    }
}

function Invoke-SshServer {
    param([Parameter(Mandatory)][string]$Comando)
    & ssh -i $Script:ChiaveSsh -o BatchMode=yes "$($Script:ServerUser)@$($Script:ServerIp)" $Comando
}
