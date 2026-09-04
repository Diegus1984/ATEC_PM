# =============================================================================
# AGGIORNAMENTO del server aziendale (dal PC di sviluppo).
# Doppio click su aggiorna-server.bat nella cartella del progetto.
#
# Compila da pulito, CONFRONTA con quello che c'è già sul server e spedisce solo
# i file cambiati, poi sostituisce, riavvia e verifica. Se la compilazione fallisce
# si ferma senza toccare il server; se il nuovo server non risponde, lo script sul
# server rimette da solo la versione precedente.
#
# Se l'aggiornamento tocca SOLO la parte web (`wwwroot`), il server la sostituisce a
# caldo: niente stop, niente riavvio, nessuno perde quello che sta facendo. Appena
# c'è di mezzo una riga di C#, si torna da solo alla procedura con stop e rollback.
#
# Prima di tutto girano i TEST AUTOMATICI: se falliscono, il server non viene toccato.
#
# I test si saltano DA SOLI quando il codice C# non è cambiato dall'ultima volta che sono
# stati verdi: l'esito non potrebbe essere diverso. Alla fine lo script stampa quanto è
# durata ogni fase, in ordine — è da lì che si decide cosa ottimizzare la volta dopo.
#
#   -Completo         spedisce comunque tutto (rete di sicurezza: si usa se si
#                     sospetta che la cartella sul server sia stata toccata a mano).
#   -ConServizioFermo niente scambio a caldo, ferma il servizio anche per il client.
#   -SenzaTest        salta i test. SOLO per emergenze (bisogna pubblicare subito e si sa
#                     già perché i test sono rossi): è la rete che si toglie di mezzo.
#   -ConTest          rifà i test anche se il codice C# non è cambiato.
#   -ConClient        ricompila il client web anche se i sorgenti non sono cambiati.
#                     (di norma `npm build` si salta: sono 30 secondi per rifare un bundle
#                     identico quando si è toccato solo C#)
#   -AncheSporco      pubblica anche con lavoro NON committato nella cartella. Di norma lo
#                     script si ferma e lo elenca: compila dalla cartella, non dal commit, e
#                     il 28/08/2026 è partito il lavoro non committato di un'altra sessione.
# =============================================================================

param(
    [switch]$Completo,
    [switch]$ConServizioFermo,
    [switch]$SenzaTest,
    [switch]$ConTest,
    [switch]$ConClient,
    [switch]$AncheSporco
)

. (Join-Path $PSScriptRoot '_comune.ps1')

Write-Host ''
Write-Host '=== ATEC PM — aggiornamento server ===' -ForegroundColor Cyan
Write-Host ''

Start-Cronometro
Test-Prerequisiti
Test-LavoroNonCommittato -AncheSporco:$AncheSporco

if ($SenzaTest) {
    Write-Host '[Test] SALTATI su richiesta (-SenzaTest): si pubblica senza rete.' -ForegroundColor Yellow
}
else {
    Invoke-TestAutomatici -Forza:$ConTest
}
Add-Tappa 'test'

$publish = Publish-Server -ConClient:$ConClient

Write-Host '[4/6] Confronto con la versione installata sul server...' -ForegroundColor Cyan
# Lo script che applica l'aggiornamento va caricato PRIMA: è lui che, con -SoloManifest,
# elenca i file installati. Così il confronto funziona anche la prima volta che si usa
# questa versione degli script.
Send-FileAlServer -File @((Join-Path $PSScriptRoot 'applica-aggiornamento.ps1'))

$pacchetto = $null
if ($Completo) {
    Write-Host '      Richiesto pacchetto completo (-Completo).' -ForegroundColor DarkGray
}
else {
    $manifest = Get-ManifestRemoto
    if (-not $manifest) {
        Write-Host '      Il server non ha ancora una versione confrontabile: spedisco tutto.' -ForegroundColor DarkGray
    }
    else {
        $pacchetto = New-PacchettoDelta -Publish $publish -ManifestRemoto $manifest
        # Oggetto sentinella (non una stringa-percorso): build identica a quella online.
        if ($pacchetto -is [pscustomobject] -and $pacchetto.NienteDaFare) {
            Add-Tappa 'confronto'
            Write-Host ''
            Write-Host "GIA AGGIORNATO — http://$($Script:ServerIp):$($Script:ServerPorta) ha gia questa build" -ForegroundColor Green
            Show-Cronometro
            Write-Host ''
            return
        }
    }
}
if (-not $pacchetto) { $pacchetto = New-PacchettoCompleto -Publish $publish }
Add-Tappa 'confronto e pacchetto'

Write-Host '[5/6] Carico sul server...' -ForegroundColor Cyan
Send-FileAlServer -File @($pacchetto)
Add-Tappa 'upload'

Write-Host '[6/6] Applico e verifico...' -ForegroundColor Cyan
# Il pacchetto si passa per nome: sul server ce ne può essere più d'uno (completo e
# differenziale) e "il più recente" non basta più a distinguerli.
$nomePacchetto = Split-Path -Leaf $pacchetto
$opzioni = ''
if ($ConServizioFermo) { $opzioni = ' -ConServizioFermo' }
Invoke-SshServer "powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\applica-aggiornamento.ps1 -Porta $Script:ServerPorta -Pacchetto C:\ATEC_PM\Updates\$nomePacchetto$opzioni"
if ($LASTEXITCODE -ne 0) {
    throw 'Aggiornamento NON riuscito: qui sopra ci sono i messaggi del server.'
}
Add-Tappa 'applica sul server'

Show-Cronometro
Write-Host ''
Write-Host "FATTO — versione nuova online su http://$($Script:ServerIp):$($Script:ServerPorta)" -ForegroundColor Green
Write-Host '(nel browser può servire Ctrl+F5 per vedere subito il client aggiornato)' -ForegroundColor DarkGray
Write-Host ''
