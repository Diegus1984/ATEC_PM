# =============================================================================
# Accensione automatica dello slow query log — blocco E1 del piano tecnico.
#
# Gira SUL SERVER, lanciato da un'attività pianificata (AtecPm-SlowQueryLogOn).
# Non riceve nessuna password: se la legge da C:\ATEC_PM\Config\credenziali.txt,
# che è leggibile solo agli amministratori della macchina. Così la password di
# root non finisce né in un'attività pianificata, né in un file di script, né
# nella cronologia di un terminale.
#
#   -SoloProva   verifica di potersi collegare e NON cambia niente (usato per
#                collaudare l'estrazione della password senza accendere nulla).
# =============================================================================

param(
    [switch]$SoloProva
)

$ErrorActionPreference = 'Stop'

$Registro = 'C:\ATEC_PM\Logs\slow-log-accensione.txt'
$Credenziali = 'C:\ATEC_PM\Config\credenziali.txt'
$Misura = 'C:\ATEC_PM\Updates\misura-prestazioni.ps1'

function Scrivi {
    param([string]$Testo)
    $riga = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Testo
    Add-Content -Path $Registro -Value $riga -Encoding UTF8
    Write-Host $riga
}

function Get-PasswordRoot {
    <#
      La riga scritta dalla rotazione del 14/08/2026 ha la forma
        [gg/mm/aaaa hh:mm] MySQL root (...): password
      Si prende quello che sta dopo l'ULTIMO ':' — l'orario fra parentesi quadre ne
      contiene uno, e prendere il primo restituirebbe l'orario invece della password.
    #>
    if (-not (Test-Path $Credenziali)) { throw "File credenziali non trovato: $Credenziali" }

    $riga = @(Get-Content $Credenziali | Where-Object { $_ -match 'MySQL root' })
    if ($riga.Count -eq 0) { throw 'Nel file credenziali non c''è nessuna riga «MySQL root».' }

    # L'ultima, se un domani la password venisse ruotata di nuovo e accodata.
    $ultima = $riga[$riga.Count - 1]
    $pwd = $ultima.Substring($ultima.LastIndexOf(':') + 1).Trim()
    if ([string]::IsNullOrWhiteSpace($pwd)) { throw 'Riga «MySQL root» trovata ma senza password dopo i due punti.' }
    return $pwd
}

try {
    $pwd = Get-PasswordRoot
    Scrivi "Password root letta dal file credenziali ($($pwd.Length) caratteri)."

    if ($SoloProva) {
        # Nessuna modifica: solo la prova che root si collega davvero.
        & $Misura -Azione stato -Utente root -Password $pwd | Out-Host
        Scrivi 'PROVA riuscita: la password funziona e la configurazione è leggibile. Niente è stato cambiato.'
        exit 0
    }

    & $Misura -Azione accendi -Utente root -Password $pwd | Out-Host
    Scrivi 'Slow query log ACCESO (soglia 0,5 s, registrato su tabella mysql.slow_log).'
    Scrivi 'Da leggere fra una settimana: misura-prestazioni.ps1 -Azione lente / classifica / richieste.'
    Scrivi 'Poi SPEGNERE: -Azione spegni e -Azione svuota (la tabella cresce e non si pulisce da sola).'

    # L'attività ha finito il suo lavoro: si toglie di mezzo da sola, così non resta
    # in giro un lavoro pianificato che nessuno ricorda di aver creato.
    # Via cmd e non nudo: con $ErrorActionPreference = 'Stop' lo stderr di un comando nativo
    # diventa un errore terminante, e se l'attività non c'è più (già rimossa il 24/08/2026,
    # o script lanciato a mano) il registro direbbe «FALLITO» a slow log già acceso.
    cmd /c 'schtasks /delete /tn AtecPm-SlowQueryLogOn /f >nul 2>&1'
    if ($LASTEXITCODE -eq 0) { Scrivi 'Attività pianificata rimossa (aveva un solo compito).' }
    else { Scrivi 'Nessuna attività pianificata da rimuovere (lancio a mano).' }
    exit 0
}
catch {
    Scrivi "FALLITO: $($_.Exception.Message)"
    exit 1
}
