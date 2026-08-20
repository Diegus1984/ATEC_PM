#
# TEST AUTOMATICI FUORI DAL DEPLOY (dal PC di sviluppo).
# Doppio click su prova-test.bat nella cartella del progetto.
#
# Fa esattamente quello che farebbe l'aggiornamento — stessi test, stesso database MySQL
# locale, stessa registrazione dell'esito — ma QUANDO VUOI TU: mentre continui a lavorare,
# o appena finita una modifica. Se sono verdi registra l'impronta dei sorgenti C#, e da
# quel momento `aggiorna-server.bat` li salta perché non c'è più niente da riprovare:
# il deploy scende da minuti a una cinquantina di secondi.
#
# Perché serve: i test NON girano in azienda e non hanno mai toccato il server — girano
# qui, sul PC, su database usa-e-getta creati in locale. Il problema non era dove
# giravano, era che il deploy li rifaceva IN SERIE mentre si aspetta davanti allo schermo.
# Spostandoli fuori, l'attesa sparisce senza togliere la rete di sicurezza.
#
# ⚠ L'impronta si registra solo se i test sono verdi. Se cambi anche una riga di C# DOPO
# averli lanciati, l'impronta non coincide più e il deploy li rifà da solo: è voluto, è
# esattamente il caso in cui l'esito potrebbe essere diverso.
#
#   -Comunque    rifà i test anche se l'impronta è già registrata verde.
#   -Filtro X    esegue solo i test che corrispondono (come `dotnet test --filter X`).
#                NON registra il verde: una parte dei test non dice niente sul resto.
#
param(
    [switch]$Comunque,
    [string]$Filtro
)

. (Join-Path $PSScriptRoot '_comune.ps1')

Write-Host ''
Write-Host '=== ATEC PM — test automatici (in locale) ===' -ForegroundColor Cyan
Write-Host ''

$radice = Get-RadiceProgetto
$proj = Join-Path $radice 'ATEC.PM.Tests\ATEC.PM.Tests.csproj'
if (-not (Test-Path $proj)) { throw "Progetto dei test non trovato: $proj" }

$inizio = Get-Date

# ── Parziale: si esegue e basta, senza toccare il registro ────────────────────
if ($Filtro) {
    Write-Host "Eseguo i soli test che corrispondono a: $Filtro" -ForegroundColor Cyan
    Write-Host ''
    & dotnet test $proj --nologo -v q --filter $Filtro | Out-Host
    $durata = [math]::Round(((Get-Date) - $inizio).TotalSeconds)
    Write-Host ''
    if ($LASTEXITCODE -ne 0) {
        Write-Host "TEST ROSSI (parziale, ${durata}s)." -ForegroundColor Red
        exit 1
    }
    Write-Host "Verdi (parziale, ${durata}s) — il deploy rifarà comunque la suite intera." -ForegroundColor Yellow
    Write-Host '(per registrare il verde serve la suite completa: prova-test.bat)' -ForegroundColor DarkGray
    Write-Host ''
    exit 0
}

# ── Suite completa: è questa che vale come lasciapassare per il deploy ────────
$impronta = Get-ImprontaSorgenti
$fileEsito = Get-FileEsitoTest

if (-not $Comunque -and (Test-Path $fileEsito)) {
    $precedente = (Get-Content -LiteralPath $fileEsito -Raw).Trim()
    if ($precedente -eq $impronta) {
        Write-Host 'Codice C# identico all''ultima esecuzione verde: niente da riprovare.' -ForegroundColor Green
        Write-Host 'Il deploy partirà senza test.' -ForegroundColor Green
        Write-Host '(per rifarli comunque: prova-test.bat -Comunque)' -ForegroundColor DarkGray
        Write-Host ''
        exit 0
    }
}

Write-Host 'Eseguo la suite completa. Puoi continuare a lavorare: al deploy servirà solo' -ForegroundColor Cyan
Write-Host 'che il C# non cambi più dopo questo momento.' -ForegroundColor Cyan
Write-Host ''

& dotnet test $proj --nologo -v q | Out-Host
$durata = [math]::Round(((Get-Date) - $inizio).TotalSeconds)

if ($LASTEXITCODE -ne 0) {
    # Stesso comportamento del deploy: un verde vecchio non si eredita mai.
    if (Test-Path $fileEsito) { Remove-Item -LiteralPath $fileEsito -Force }
    Write-Host ''
    Write-Host "TEST ROSSI (${durata}s). Il registro è stato azzerato: il deploy li rifarà." -ForegroundColor Red
    Write-Host ''
    exit 1
}

# L'impronta si ricalcola ADESSO, non si riusa quella di prima: fra l'avvio dei test e
# la loro fine possono essere passati minuti, e in quei minuti il codice può essere
# cambiato. Registrare l'impronta vecchia darebbe al deploy un lasciapassare per codice
# che nessuno ha provato.
$improntaFine = Get-ImprontaSorgenti
$cartella = Split-Path -Parent $fileEsito
if (-not (Test-Path $cartella)) { New-Item -ItemType Directory -Path $cartella -Force | Out-Null }

Write-Host ''
if ($improntaFine -ne $impronta) {
    Write-Host "TEST VERDI (${durata}s), ma il codice C# è cambiato mentre giravano:" -ForegroundColor Yellow
    Write-Host 'il verde vale per la versione di prima, quindi NON lo registro.' -ForegroundColor Yellow
    Write-Host 'Rilancia prova-test.bat quando hai finito di modificare.' -ForegroundColor Yellow
    Write-Host ''
    exit 2
}

Set-Content -LiteralPath $fileEsito -Value $improntaFine -Encoding ascii
Write-Host "TEST VERDI (${durata}s) — registrati." -ForegroundColor Green
Write-Host 'Il prossimo aggiorna-server.bat NON li rifarà: parte diretto.' -ForegroundColor Green
Write-Host ''
exit 0
