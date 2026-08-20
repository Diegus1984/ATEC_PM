# =============================================================================
# ATEC PM — Installazione del server sulla macchina aziendale (ATEC-FC)
#
# SI ESEGUE SUL SERVER, UNA VOLTA SOLA, come amministratore:
#   powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\install-server.ps1
#
# Cosa fa: cartelle + database MySQL dedicato + configurazione di produzione
# (chiave JWT nuova) + servizio Windows + regola firewall + primo avvio verificato.
# Per gli aggiornamenti successivi si usa aggiorna-server.bat dal PC di sviluppo.
# =============================================================================

param(
    [string]$Pacchetto = '',                    # pacchetto .tar da installare (default: il più recente in questa cartella)
    [int]$Porta = 5150,
    [string]$DbHost = 'localhost',
    [int]$DbPorta = 3306,
    [string]$DbNome = 'atec_pm',
    [string]$DbUtente = 'atecpm',
    [string]$DbPassword = '',                   # vuota = generata automaticamente
    [string]$MySqlRoot = 'root',
    # Account del servizio: di default l'utente 'atec' della macchina, perché il servizio
    # deve leggere documenti commessa e allegati Danea su cartelle di rete (LocalSystem
    # esce in rete come computer e si prende "accesso negato"). Per usare comunque
    # l'account di sistema: -AccountServizio LocalSystem
    [string]$AccountServizio = "$env:COMPUTERNAME\atec",
    [switch]$AttivaDaneaSync,                   # meglio accenderle DOPO il primo avvio riuscito
    [switch]$AttivaCodexSync,
    [switch]$SaltaDb                            # database e utente già pronti
)

$ErrorActionPreference = 'Stop'

$Radice      = 'C:\ATEC_PM'          # fisso: il percorso dei log è cablato nel programma
$CartServer  = Join-Path $Radice 'Server'
$CartLog     = Join-Path $Radice 'Logs'
$CartConfig  = Join-Path $Radice 'Config'
$CartUpload  = Join-Path $Radice 'Uploads\cms'
$CartUpdates = Join-Path $Radice 'Updates'
$CartBackup  = 'C:\ATEC_Backups'
$CartCommesse= 'C:\ATEC_Commesse'
$NomeServizio= 'AtecPmServer'
$EtichettaServizio = 'ATEC PM Server'

function Test-Amministratore {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function New-PasswordCasuale {
    param([int]$Lunghezza = 24)
    # Solo lettere e cifre: la password finisce in una connection string e in un
    # comando SQL, i caratteri speciali sono solo una fonte di guai.
    $alfabeto = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] $Lunghezza
    $rng.GetBytes($bytes)
    $sb = New-Object System.Text.StringBuilder
    foreach ($b in $bytes) { [void]$sb.Append($alfabeto[$b % $alfabeto.Length]) }
    return $sb.ToString()
}

function New-ChiaveJwt {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 48
    $rng.GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Grant-DirittoServizio {
    param([Parameter(Mandatory)][string]$Account)

    # "Accedi come servizio" (SeServiceLogonRight): senza questo diritto il servizio
    # non parte con un account utente. Si passa da secedit, non c'è un cmdlet.
    $sid = (New-Object System.Security.Principal.NTAccount($Account)).Translate(
        [System.Security.Principal.SecurityIdentifier]).Value
    $inf = Join-Path $env:TEMP 'atecpm-secpol.inf'
    $db  = Join-Path $env:TEMP 'atecpm-secpol.sdb'
    $esporta = Join-Path $env:TEMP 'atecpm-secpol-export.inf'

    & secedit /export /cfg $esporta /areas USER_RIGHTS | Out-Null
    $riga = (Get-Content $esporta | Where-Object { $_ -like 'SeServiceLogonRight*' })
    if ($riga -and ($riga -like "*$sid*")) {
        Write-Host "      Diritto 'Accedi come servizio' già presente." -ForegroundColor DarkGray
        return
    }
    if ($riga) { $nuova = "$riga,*$sid" } else { $nuova = "SeServiceLogonRight = *$sid" }

    @"
[Unicode]
Unicode=yes
[Version]
signature="`$CHICAGO`$"
Revision=1
[Privilege Rights]
$nuova
"@ | Set-Content -Path $inf -Encoding Unicode

    & secedit /configure /db $db /cfg $inf /areas USER_RIGHTS | Out-Null
    Remove-Item $inf, $db, $esporta -ErrorAction SilentlyContinue
}

# --- 0. Controlli preliminari -------------------------------------------------

Write-Host ''
Write-Host '=== ATEC PM — installazione server ===' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Amministratore)) {
    throw 'Servono i privilegi di amministratore (apri PowerShell come amministratore, oppure collegati in SSH con un utente amministratore).'
}

if ([string]::IsNullOrWhiteSpace($Pacchetto)) {
    # .tar è il formato attuale; .tgz resta accettato per i pacchetti creati prima del 04/08/2026.
    # I pacchetti DIFFERENZIALI (atec-pm-delta*.tar) sono esclusi: contengono solo i file
    # cambiati e installarli da zero darebbe una versione monca.
    $scarta = { $_.Name -notlike 'atec-pm-delta*' }
    $candidato = Get-ChildItem (Join-Path $PSScriptRoot '*.tar'), (Join-Path $PSScriptRoot '*.tgz') -ErrorAction SilentlyContinue |
                 Where-Object $scarta | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $candidato) {
        $candidato = Get-ChildItem (Join-Path $CartUpdates '*.tar'), (Join-Path $CartUpdates '*.tgz') -ErrorAction SilentlyContinue |
                     Where-Object $scarta | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    }
    if (-not $candidato) { throw "Nessun pacchetto trovato. Caricalo prima con carica-installazione.bat dal PC di sviluppo." }
    $Pacchetto = $candidato.FullName
}
if (-not (Test-Path $Pacchetto)) { throw "Pacchetto non trovato: $Pacchetto" }
Write-Host "Pacchetto: $Pacchetto" -ForegroundColor DarkGray

if (Get-Service -Name $NomeServizio -ErrorAction SilentlyContinue) {
    throw "Il servizio '$NomeServizio' esiste già. Per aggiornare usa aggiorna-server.bat; per rifare l'installazione da zero esegui prima disinstalla-server.ps1."
}

# --- 1. Cartelle --------------------------------------------------------------

Write-Host '[1/9] Creo le cartelle...' -ForegroundColor Yellow
foreach ($d in @($Radice, $CartServer, $CartLog, $CartConfig, $CartUpload, $CartUpdates, $CartBackup, $CartCommesse)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

# --- 2. Database e utente MySQL ----------------------------------------------

if ($SaltaDb) {
    Write-Host '[2/9] Database: saltato su richiesta.' -ForegroundColor Yellow
    if ([string]::IsNullOrWhiteSpace($DbPassword)) {
        throw 'Con -SaltaDb devi passare -DbPassword (quella dell utente MySQL già esistente).'
    }
}
else {
    Write-Host '[2/9] Preparo database e utente MySQL...' -ForegroundColor Yellow

    $mysqlExe = Get-ChildItem 'C:\Program Files\MySQL\MySQL Server *\bin\mysql.exe' -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $mysqlExe) { throw "mysql.exe non trovato in 'C:\Program Files\MySQL'. Usa -SaltaDb e crea database e utente a mano (le istruzioni sono nella guida)." }

    if ([string]::IsNullOrWhiteSpace($DbPassword)) { $DbPassword = New-PasswordCasuale }

    $rootSecure = Read-Host "      Password di MySQL '$MySqlRoot'" -AsSecureString
    $rootPlain  = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($rootSecure))

    $cnf = Join-Path $env:TEMP 'atecpm-install.cnf'
    $sqlFile = Join-Path $env:TEMP 'atecpm-install.sql'
    try {
        # Credenziali in un file temporaneo: sulla riga di comando sarebbero visibili
        # nell'elenco processi (e MySQL stampa comunque un avviso).
        "[client]`nhost=$DbHost`nport=$DbPorta`nuser=$MySqlRoot`npassword=$rootPlain" |
            Set-Content -Path $cnf -Encoding ASCII

        @"
CREATE DATABASE IF NOT EXISTS ``$DbNome`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '$DbUtente'@'localhost' IDENTIFIED BY '$DbPassword';
ALTER USER '$DbUtente'@'localhost' IDENTIFIED BY '$DbPassword';
GRANT ALL PRIVILEGES ON ``$DbNome``.* TO '$DbUtente'@'localhost';
FLUSH PRIVILEGES;
"@ | Set-Content -Path $sqlFile -Encoding ASCII

        Get-Content $sqlFile -Raw | & $mysqlExe.FullName "--defaults-file=$cnf"
        if ($LASTEXITCODE -ne 0) { throw 'Comandi MySQL falliti: password di root sbagliata o servizio MySQL fermo.' }
        Write-Host "      Database '$DbNome' e utente '$DbUtente' pronti." -ForegroundColor DarkGray
    }
    finally {
        Remove-Item $cnf, $sqlFile -ErrorAction SilentlyContinue
        $rootPlain = $null
    }
}

# --- 3. Copia dei file del programma -----------------------------------------

Write-Host '[3/9] Estraggo il programma...' -ForegroundColor Yellow
# -xf: vale sia per il .tar attuale sia per un .tgz compresso (lo riconosce libarchive).
& tar -xf $Pacchetto -C $CartServer
if ($LASTEXITCODE -ne 0) { throw 'Estrazione del pacchetto fallita.' }
if (-not (Test-Path (Join-Path $CartServer 'ATEC.PM.Server.exe'))) {
    throw 'Pacchetto incompleto: ATEC.PM.Server.exe non trovato.'
}

# --- 4. Configurazione di produzione -----------------------------------------

Write-Host '[4/9] Scrivo la configurazione di produzione...' -ForegroundColor Yellow

$template = Join-Path $PSScriptRoot 'appsettings.server.template.json'
if (-not (Test-Path $template)) { $template = Join-Path $CartUpdates 'appsettings.server.template.json' }
if (-not (Test-Path $template)) { throw "Template di configurazione non trovato: appsettings.server.template.json" }

$connString = "Server=$DbHost;Port=$DbPorta;Database=$DbNome;User=$DbUtente;Password=$DbPassword;SslMode=None;AllowPublicKeyRetrieval=true;"
$jwt = New-ChiaveJwt

if ($AttivaDaneaSync) { $svcDanea = 'true' } else { $svcDanea = 'false' }
if ($AttivaCodexSync) { $svcCodex = 'true' } else { $svcCodex = 'false' }

$config = (Get-Content $template -Raw -Encoding UTF8).
    Replace('__CONNECTION_STRING__', $connString).
    Replace('__JWT_KEY__', $jwt).
    Replace('__PORTA__', "$Porta").
    Replace('__SVC_DANEA__', $svcDanea).
    Replace('__SVC_CODEX__', $svcCodex)

# Senza BOM: il parser JSON del programma non lo gradisce.
[IO.File]::WriteAllText((Join-Path $CartServer 'appsettings.json'), $config, (New-Object Text.UTF8Encoding($false)))

# Copia di sicurezza PRIMA del primo avvio: al primo avvio il programma cifra i
# segreti e sostituisce connessione e chiave JWT con "ENCRYPTED" in appsettings.json.
# Se un domani si reinstalla o si cambia macchina, questa è l'unica copia leggibile.
[IO.File]::WriteAllText((Join-Path $CartConfig 'appsettings.originale.json'), $config, (New-Object Text.UTF8Encoding($false)))

@"
ATEC PM — credenziali di produzione (generato dall'installazione)
Data installazione : $(Get-Date -Format 'dd/MM/yyyy HH:mm')
Database           : $DbNome su $DbHost`:$DbPorta
Utente MySQL       : $DbUtente
Password MySQL     : $DbPassword
Chiave JWT         : $jwt
Servizio Windows   : $NomeServizio (account: $AccountServizio)
Indirizzo          : http://$($env:COMPUTERNAME):$Porta

Questo file è leggibile solo dagli amministratori della macchina.
Tienine una copia FUORI da questo server (i backup che vivono qui muoiono col server).
"@ | Set-Content -Path (Join-Path $CartConfig 'credenziali.txt') -Encoding UTF8

# --- 5. Permessi --------------------------------------------------------------

Write-Host '[5/9] Blindo i permessi delle cartelle...' -ForegroundColor Yellow

# SID e non nomi di gruppo: su Windows italiano "Administrators"/"SYSTEM" non esistono.
# I permessi si scrivono SOLO sulla cartella radice (con ereditarietà OI/CI) e poi si
# rimettono i figli in ereditarietà con /reset. NIENTE "/inheritance:r ... /T": i flag
# (OI)(CI) non sono validi sui file, quindi sui figli l'assegnazione viene scartata ma
# l'ereditarietà è già stata tolta → file con ACL VUOTA e servizio che non parte più
# (errore 5, accesso negato, nemmeno all'eseguibile).
$permessi = @('*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F')
if ($AccountServizio -ne 'LocalSystem') {
    $accountIcacls = $AccountServizio -replace '^\.\\', "$env:COMPUTERNAME\"
    $permessi += "${accountIcacls}:(OI)(CI)M"
}

foreach ($cartella in @($Radice, $CartBackup)) {
    & icacls $cartella /inheritance:r /grant:r @permessi /Q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Impostazione permessi fallita su $cartella" }
    # I figli tornano a ereditare dalla radice (il carattere jolly esclude la radice stessa).
    & icacls "$cartella\*" /reset /T /C /Q | Out-Host
}

# Controllo esplicito: se l'eseguibile resta senza permessi il servizio non parte.
$aclExe = & icacls (Join-Path $CartServer 'ATEC.PM.Server.exe')
if (-not ($aclExe -match 'S-1-5-32-544|Administrators|Amministratori')) {
    throw "L'eseguibile è rimasto senza permessi (ACL vuota): controllare i permessi di $CartServer"
}

# --- 6. Servizio Windows ------------------------------------------------------

Write-Host '[6/9] Registro il servizio Windows...' -ForegroundColor Yellow
$exe = Join-Path $CartServer 'ATEC.PM.Server.exe'

if ($AccountServizio -eq 'LocalSystem') {
    New-Service -Name $NomeServizio -BinaryPathName "`"$exe`"" -DisplayName $EtichettaServizio `
        -Description 'ATEC PM — API e client web (gestione commesse).' -StartupType Automatic | Out-Null
}
else {
    # Read-Host e non Get-Credential: via SSH non c'è desktop per la finestra di dialogo.
    $pwdServizio = Read-Host "      Password dell'account $AccountServizio" -AsSecureString
    $cred = New-Object System.Management.Automation.PSCredential($AccountServizio, $pwdServizio)
    Grant-DirittoServizio -Account $AccountServizio
    New-Service -Name $NomeServizio -BinaryPathName "`"$exe`"" -DisplayName $EtichettaServizio `
        -Description 'ATEC PM — API e client web (gestione commesse).' -StartupType Automatic `
        -Credential $cred | Out-Null
}

# Variabili d'ambiente del servizio:
#  - ASPNETCORE_ENVIRONMENT=Production  → carica appsettings.Production.json
#  - ATECPM_PROTECT_MACHINE=1           → cifratura segreti a livello macchina; i servizi
#    non caricano il profilo utente, quindi la cifratura "per utente" non funzionerebbe.
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$NomeServizio" -Name Environment `
    -Value @('ASPNETCORE_ENVIRONMENT=Production', 'ATECPM_PROTECT_MACHINE=1') -Type MultiString

# Riavvio automatico in caso di crash (5s, 10s, 30s; contatore azzerato ogni giorno).
& sc.exe failure $NomeServizio reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

# Dipendenza da MySQL: senza, al riavvio della macchina il server parte prima del database.
$mysqlSvc = Get-Service | Where-Object { $_.Name -like 'MySQL*' } | Select-Object -First 1
if ($mysqlSvc) {
    & sc.exe config $NomeServizio depend= $($mysqlSvc.Name) | Out-Null
    Write-Host "      Dipendenza impostata su '$($mysqlSvc.Name)'." -ForegroundColor DarkGray
}
else {
    Write-Warning "Servizio MySQL non individuato: imposta a mano la dipendenza se il server non parte al riavvio."
}

# --- 7. Firewall --------------------------------------------------------------

Write-Host "[7/9] Apro la porta $Porta sul firewall..." -ForegroundColor Yellow
$regola = "ATEC PM Server (TCP $Porta)"
Get-NetFirewallRule -DisplayName $regola -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $regola -Direction Inbound -Action Allow -Protocol TCP `
    -LocalPort $Porta -Profile Any | Out-Null

# --- 8. Primo avvio -----------------------------------------------------------

Write-Host '[8/9] Avvio il servizio (il primo avvio crea le tabelle: può volerci un minuto)...' -ForegroundColor Yellow
Start-Service -Name $NomeServizio

$ok = $false
for ($i = 1; $i -le 40; $i++) {
    Start-Sleep -Seconds 3
    try {
        # /ready (non /health): fa una SELECT 1, quindi «il servizio è su» significa davvero
        # «raggiunge il database». Il 404 è il ripiego per le build precedenti alla sonda.
        $r = Invoke-WebRequest "http://localhost:$Porta/api/health/ready" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $ok = $true; break }
    }
    catch {
        $codice = $null
        if ($_.Exception.Response) { $codice = [int]$_.Exception.Response.StatusCode }
        if ($codice -eq 404) {
            try {
                $vecchia = Invoke-WebRequest "http://localhost:$Porta/api/health" -UseBasicParsing -TimeoutSec 5
                if ($vecchia.StatusCode -eq 200) { $ok = $true; break }
            }
            catch { }
        }
    }
    if ((Get-Service -Name $NomeServizio).Status -eq 'Stopped') { break }
}

if (-not $ok) {
    Write-Host ''
    Write-Host 'Il servizio non risponde. Ultime righe di log:' -ForegroundColor Red
    $log = Get-ChildItem (Join-Path $CartLog 'server-*.log') -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($log) { Get-Content $log.FullName -Tail 30 }
    else { Write-Host "(nessun file di log in $CartLog — controlla il Visualizzatore eventi, origine $NomeServizio)" }
    throw "Installazione completata ma il servizio non risponde su http://localhost:$Porta"
}

# --- 9. Riepilogo -------------------------------------------------------------

Write-Host '[9/9] Fatto.' -ForegroundColor Yellow
$ip = (Get-NetIPAddress -AddressFamily IPv4 |
       Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
       Select-Object -First 1).IPAddress

Write-Host ''
Write-Host '=== INSTALLAZIONE COMPLETATA ===' -ForegroundColor Green
Write-Host ''
Write-Host "  Indirizzo per gli utenti : http://$ip`:$Porta" -ForegroundColor Green
Write-Host "  Servizio Windows         : $NomeServizio ($EtichettaServizio)"
Write-Host "  Programma                : $CartServer"
Write-Host "  Log                      : $CartLog"
Write-Host "  Backup                   : $CartBackup"
Write-Host "  Credenziali generate     : $(Join-Path $CartConfig 'credenziali.txt')  <-- copiane una fuori dal server"
Write-Host ''
Write-Host '  Prossimi passi:' -ForegroundColor Cyan
Write-Host '   1. Entra nel gestionale e cambia subito la password di admin.'
Write-Host '   2. Controlla in Impostazioni il percorso dei documenti commessa (BasePath).'
Write-Host '   3. Quando tutto gira, accendi le sincronizzazioni Danea/Codex in'
Write-Host "      $CartServer\appsettings.json (sezione Services) e riavvia il servizio."
Write-Host '   4. Imposta la copia dei backup FUORI da questa macchina (vedi guida).'
Write-Host ''
