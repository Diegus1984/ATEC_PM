# =============================================================================
# ATEC PM — Disinstallazione dal server (gira SUL SERVER, come amministratore).
#
# Di default rimuove SOLO servizio, regola firewall e programma: database,
# documenti, backup e configurazione restano dove sono.
# Con -CancellaDati rimuove anche cartelle e database (chiede conferma esplicita).
# =============================================================================

param(
    [switch]$CancellaDati,
    [int]$Porta = 5150,
    [string]$DbNome = 'atec_pm',
    [string]$DbUtente = 'atecpm',
    [string]$MySqlRoot = 'root'
)

$ErrorActionPreference = 'Stop'

$Radice       = 'C:\ATEC_PM'
$NomeServizio = 'AtecPmServer'

function Test-Amministratore {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Amministratore)) { throw 'Servono i privilegi di amministratore.' }

Write-Host ''
Write-Host '=== ATEC PM — disinstallazione server ===' -ForegroundColor Cyan
Write-Host ''

if ($CancellaDati) {
    Write-Host 'ATTENZIONE: con -CancellaDati verranno eliminati DEFINITIVAMENTE:' -ForegroundColor Red
    Write-Host "  - il database MySQL '$DbNome' (tutte le commesse, i preventivi, i verbali...)"
    Write-Host "  - la cartella $Radice (programma, configurazione, log, upload)"
    Write-Host '  - i backup NON vengono toccati (C:\ATEC_Backups resta)'
    Write-Host ''
    $risposta = Read-Host 'Scrivi CANCELLA per confermare (qualsiasi altra cosa annulla)'
    if ($risposta -cne 'CANCELLA') {
        Write-Host 'Annullato: non è stato toccato nulla.' -ForegroundColor Green
        return
    }
}

Write-Host '[1/4] Fermo e rimuovo il servizio...' -ForegroundColor Yellow
$svc = Get-Service -Name $NomeServizio -ErrorAction SilentlyContinue
if ($svc) {
    Stop-Service -Name $NomeServizio -Force -ErrorAction SilentlyContinue
    (Get-Service -Name $NomeServizio).WaitForStatus('Stopped', (New-TimeSpan -Seconds 60))
    & sc.exe delete $NomeServizio | Out-Null
    Start-Sleep -Seconds 2
}
else { Write-Host '      (servizio non presente)' -ForegroundColor DarkGray }

Write-Host '[2/4] Rimuovo la regola del firewall...' -ForegroundColor Yellow
Get-NetFirewallRule -DisplayName "ATEC PM Server (TCP $Porta)" -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction SilentlyContinue

Write-Host '[3/4] Rimuovo i file del programma...' -ForegroundColor Yellow
foreach ($d in @((Join-Path $Radice 'Server'), (Join-Path $Radice 'Server.precedente'), (Join-Path $Radice 'Updates\staging'))) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}

if (-not $CancellaDati) {
    Write-Host '[4/4] Dati conservati.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "Fatto. Restano: database '$DbNome', $Radice\Config, $Radice\Uploads, C:\ATEC_Backups" -ForegroundColor Green
    return
}

Write-Host '[4/4] Elimino dati e database...' -ForegroundColor Yellow
$mysqlExe = Get-ChildItem 'C:\Program Files\MySQL\MySQL Server *\bin\mysql.exe' -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
if ($mysqlExe) {
    $rootSecure = Read-Host "      Password di MySQL '$MySqlRoot'" -AsSecureString
    $rootPlain  = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($rootSecure))
    $cnf = Join-Path $env:TEMP 'atecpm-uninstall.cnf'
    try {
        "[client]`nuser=$MySqlRoot`npassword=$rootPlain" | Set-Content -Path $cnf -Encoding ASCII
        "DROP DATABASE IF EXISTS ``$DbNome``; DROP USER IF EXISTS '$DbUtente'@'localhost';" |
            & $mysqlExe.FullName "--defaults-file=$cnf"
    }
    finally { Remove-Item $cnf -ErrorAction SilentlyContinue }
}
else { Write-Warning "mysql.exe non trovato: elimina database e utente a mano." }

if (Test-Path $Radice) { Remove-Item $Radice -Recurse -Force }

Write-Host ''
Write-Host 'Disinstallazione completata. I backup in C:\ATEC_Backups sono stati lasciati.' -ForegroundColor Green
