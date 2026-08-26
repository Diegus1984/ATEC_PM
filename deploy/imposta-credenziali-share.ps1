<#
.SYNOPSIS
    Imposta le credenziali con cui il SERVIZIO ATEC PM accede alla share di rete degli
    allegati Danea (le immagini degli articoli).

.DESCRIPTION
    Il servizio AtecPmServer gira come account LOCALE della macchina (.\atec). Il server che
    ospita la share (Server-maga) non conosce quell'utente perche' non c'e' un dominio:
    l'SMB ripiega su "guest", Windows lo blocca, e ogni accesso ai file risponde "accesso
    negato" - mentre il database Danea funziona, perche' li' le credenziali ci sono.
    Il rimedio e' dare al programma un utente valido SUL SERVER DELLA SHARE, esattamente
    come gia' si fa per Firebird.

    La password non finisce in chiaro da nessuna parte: viene cifrata (DPAPI, ambito
    macchina) dentro appsettings.Secrets.json, lo stesso file dove stanno la stringa di
    connessione e la chiave JWT. Il servizio la legge all'avvio.

    Prima di salvare, quando possibile, le credenziali vengono provate davvero sulla share.

.EXAMPLE
    Da PowerShell come amministratore SUL SERVER. Il -ExecutionPolicy Bypass serve perche' gli
    script non firmati sono bloccati (stessa riga di install-server.ps1); vale solo per il lancio.

    powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Strumenti\imposta-credenziali-share.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Strumenti\imposta-credenziali-share.ps1 -Utente 'Server-maga\atec_pm'
#>
[CmdletBinding()]
param(
    [string]$CartellaServer = 'C:\ATEC_PM\Server',
    [string]$NomeServizio   = 'AtecPmServer',
    [string]$Utente         = '',
    [string]$Share          = '',      # es. \\Server-maga\d - vuoto = si legge da appsettings.json
    [switch]$NonRiavviare
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

function Scrivi($testo, $colore = 'Gray') { Write-Host $testo -ForegroundColor $colore }

# --- 0. Controlli -------------------------------------------------------------
$identita = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($identita)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Serve una console di PowerShell aperta come amministratore.'
}

$fileSegreti = Join-Path $CartellaServer 'appsettings.Secrets.json'
$fileConfig  = Join-Path $CartellaServer 'appsettings.json'
if (-not (Test-Path $fileConfig)) { throw "Non trovo $fileConfig : e' la cartella giusta del server?" }
if (-not (Test-Path $fileSegreti)) {
    throw @"
Non trovo $fileSegreti.
Quel file lo crea il programma al PRIMO avvio, quando cifra i segreti. Avvia una volta il
servizio ($NomeServizio), verifica che l'applicazione risponda, poi rilancia questo script.
"@
}

# --- 1. Share da usare per la prova -------------------------------------------
if ([string]::IsNullOrWhiteSpace($Share)) {
    try {
        $percorso = (Get-Content $fileConfig -Raw | ConvertFrom-Json).DaneaSync.AllegatiPathOld
        if ($percorso -like '\\*') {
            $pezzi = $percorso.TrimStart('\').Split('\')
            if ($pezzi.Count -ge 2) { $Share = '\\' + $pezzi[0] + '\' + $pezzi[1] }
        }
    } catch {
        Scrivi "Non riesco a leggere DaneaSync:AllegatiPathOld da appsettings.json: $($_.Exception.Message)" 'Yellow'
    }
}
if ($Share) { Scrivi "Share di destinazione : $Share" 'Cyan' }

# --- 2. Credenziali -----------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Utente)) {
    $suggerito = ''
    if ($Share) { $suggerito = ' (es. ' + $Share.TrimStart('\').Split('\')[0] + '\atec_pm)' }
    $Utente = Read-Host "Utente con cui accedere alla share$suggerito"
}
if ([string]::IsNullOrWhiteSpace($Utente)) { throw 'Nessun utente indicato.' }

# Senza il nome del server davanti, Windows cerca l'utente sulla macchina LOCALE e la share
# risponde "accesso negato" anche con la password giusta: si mette da soli.
if ($Share -and $Utente -notmatch '[\\@]') {
    $Utente = $Share.TrimStart('\').Split('\')[0] + '\' + $Utente
    Scrivi "Uso l'utente $Utente (senza il nome del server, Windows lo cercherebbe su questa macchina)." 'Yellow'
}

$sicura = Read-Host "Password di $Utente" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sicura)
try     { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
if ([string]::IsNullOrWhiteSpace($password)) { throw 'Password vuota: la share la rifiuterebbe comunque.' }

# --- 3. Prova sul campo -------------------------------------------------------
# Qui si verifica che utente e password siano buoni e che la share risponda. L'identita' con
# cui gira il servizio non conta piu': da adesso la sessione la apre il programma, con queste.
#
# TRAPPOLA: le redirezioni di PowerShell (> $null, 2>&1) sui comandi ESTERNI trasformano ogni
# riga di stderr in un errore vero; con $ErrorActionPreference = 'Stop' lo script muore. E
# "net use /delete" scrive su stderr tutte le volte che non c'e' niente da chiudere, cioe'
# quasi sempre. Le redirezioni vanno DENTRO la stringa passata a cmd.exe: le gestisce cmd.
function Invoke-Net($argomenti) {
    $vecchio = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try   { return (cmd.exe /c "net use $argomenti 2>&1") }
    finally { $ErrorActionPreference = $vecchio }
}

if ($Share) {
    Scrivi 'Provo le credenziali sulla share...' 'Yellow'
    $null = Invoke-Net """$Share"" /delete /y"
    $esito = Invoke-Net """$Share"" ""$password"" /user:""$Utente"""
    if ($LASTEXITCODE -ne 0) {
        Scrivi "  FALLITA: $esito" 'Red'
        $vaiAvanti = Read-Host 'Salvo lo stesso le credenziali? (s/N)'
        if ($vaiAvanti -notmatch '^[sS]') { throw 'Interrotto: credenziali non salvate.' }
    } else {
        $leggibile = Test-Path $Share
        $null = Invoke-Net """$Share"" /delete /y"
        if ($leggibile) { Scrivi '  OK: connessione riuscita e cartella leggibile.' 'Green' }
        else            { Scrivi '  Connessione riuscita ma la cartella non si legge: controlla i permessi NTFS.' 'Yellow' }
    }
}

# --- 4. Scrittura cifrata -----------------------------------------------------
function Cifra($testo) {
    $byte = [Text.Encoding]::UTF8.GetBytes($testo)
    # Ambito MACCHINA: i servizi Windows non caricano il profilo utente, quindi con l'ambito
    # utente la chiave DPAPI non sarebbe disponibile e il segreto risulterebbe illeggibile.
    $cifrato = [Security.Cryptography.ProtectedData]::Protect(
        $byte, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    return [Convert]::ToBase64String($cifrato)
}

Copy-Item $fileSegreti "$fileSegreti.bak" -Force
$segreti = [ordered]@{}
(Get-Content $fileSegreti -Raw | ConvertFrom-Json).PSObject.Properties |
    ForEach-Object { $segreti[$_.Name] = $_.Value }

$segreti['DaneaSync:SmbUser']     = Cifra $Utente
$segreti['DaneaSync:SmbPassword'] = Cifra $password

# Senza BOM: il parser JSON del programma non lo gradisce.
$json = $segreti | ConvertTo-Json -Depth 3
[IO.File]::WriteAllText($fileSegreti, $json, (New-Object Text.UTF8Encoding($false)))
Scrivi "Credenziali salvate cifrate in $fileSegreti (il file precedente e' in .bak)." 'Green'

# --- 5. Riavvio ---------------------------------------------------------------
if ($NonRiavviare) {
    Scrivi "Riavvia il servizio $NomeServizio per applicarle." 'Yellow'
    return
}
if (Get-Service -Name $NomeServizio -ErrorAction SilentlyContinue) {
    Scrivi "Riavvio $NomeServizio..." 'Yellow'
    Restart-Service -Name $NomeServizio -Force
    Scrivi "Fatto. Apri 'Trasferimento catalogo Danea': il badge rosso deve sparire." 'Green'
} else {
    Scrivi "Servizio $NomeServizio non trovato: riavvialo a mano." 'Yellow'
}
