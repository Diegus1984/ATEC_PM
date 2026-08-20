# =============================================================================
# PRIMA INSTALLAZIONE — parte 1 (dal PC di sviluppo).
# Compila tutto e carica sul server il pacchetto + gli script di installazione.
# L'installazione vera si lancia poi SUL SERVER (istruzioni stampate alla fine).
# =============================================================================

. (Join-Path $PSScriptRoot '_comune.ps1')

Write-Host ''
Write-Host '=== ATEC PM — preparazione prima installazione ===' -ForegroundColor Cyan
Write-Host ''

Test-Prerequisiti
$pacchetto = New-PacchettoServer

# Prima installazione: si carica sempre il pacchetto COMPLETO (non c'è ancora niente
# sul server con cui confrontarsi).
Write-Host '[5/6] Carico pacchetto e script sul server...' -ForegroundColor Cyan
Send-FileAlServer -File @(
    $pacchetto,
    (Join-Path $PSScriptRoot 'install-server.ps1'),
    (Join-Path $PSScriptRoot 'applica-aggiornamento.ps1'),
    (Join-Path $PSScriptRoot 'disinstalla-server.ps1'),
    (Join-Path $PSScriptRoot 'appsettings.server.template.json')
)

Write-Host ''
Write-Host '[6/6] CARICATO. Ora completa l''installazione SUL SERVER:' -ForegroundColor Green
Write-Host ''
Write-Host "  1) ssh -i `"$Script:ChiaveSsh`" $($Script:ServerUser)@$($Script:ServerIp)" -ForegroundColor White
Write-Host '  2) powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\install-server.ps1' -ForegroundColor White
Write-Host ''
Write-Host '     Chiede due password: root di MySQL e utente ATEC-FC\atec (account del' -ForegroundColor DarkGray
Write-Host '     servizio, serve per leggere documenti e allegati sulle cartelle di rete).' -ForegroundColor DarkGray
Write-Host '     Il resto è automatico. Le sincronizzazioni Danea/Codex restano spente:' -ForegroundColor DarkGray
Write-Host '     si accendono dopo, a gestionale funzionante (vedi GUIDA-SERVER-LAN.md §5).' -ForegroundColor DarkGray
Write-Host ''
