# =============================================================================
# BLOCCO E1 — Misurare prima di correggere.
#
# Accende lo slow query log di MySQL e legge le due classifiche che dicono DOVE
# intervenire (E2 indici, E3 N+1, E5 async). Senza questa settimana di misura si
# ottimizza a naso, e a naso si ottimizza quasi sempre la cosa sbagliata.
#
# GIRA DOVE C'È IL DATABASE. In sviluppo: da questa cartella. Sul server ATEC-FC:
#   scp -i "$env:USERPROFILE\.ssh\atec_vps" .\misura-prestazioni.ps1 atec@192.168.2.150:C:/ATEC_PM/Updates/
#   ssh -i "$env:USERPROFILE\.ssh\atec_vps" atec@192.168.2.150
#   powershell -NoProfile -ExecutionPolicy Bypass -File C:\ATEC_PM\Updates\misura-prestazioni.ps1 -Azione accendi
#
# Azioni:
#   stato       com'è configurata la misura adesso (default)
#   accendi     slow query log a 0,5 s, registrato su TABELLA (mysql.slow_log)
#   spegni      lo rimette com'era
#   lente       le query oltre soglia, raggruppate: la lista di E2
#   classifica  le query per TEMPO TOTALE (performance_schema): la lista di E3
#   svuota      azzera il registro delle query lente
#   richieste   le richieste HTTP oltre 500 ms, dai log del server (nessun database)
# =============================================================================

param(
    [ValidateSet('stato', 'accendi', 'spegni', 'lente', 'classifica', 'svuota', 'richieste')]
    [string]$Azione = 'stato',
    [string]$Utente = 'root',
    [string]$Password,
    [string]$Database = 'atec_pm',
    [double]$SogliaSecondi = 0.5,
    [int]$Righe = 25,
    [string]$CartellaLog = 'C:\ATEC_PM\Logs'
)

$ErrorActionPreference = 'Stop'

function Get-MysqlExe {
    $inPath = Get-Command mysql.exe -ErrorAction SilentlyContinue
    if ($inPath) { return $inPath.Source }

    # Il client non è quasi mai nel PATH: si cerca l'installazione più recente.
    # @(...) obbligatorio: con UNA sola installazione la pipeline torna una stringa, e [0] su
    # una stringa da' la prima LETTERA — il percorso diventerebbe "C".
    $candidati = @(Get-ChildItem 'C:\Program Files\MySQL' -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'MySQL Server*' } |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'bin\mysql.exe' } |
        Where-Object { Test-Path $_ })

    if ($candidati.Count -gt 0) { return $candidati[0] }
    throw 'mysql.exe non trovato (cercato nel PATH e in C:\Program Files\MySQL).'
}

function Invoke-Sql {
    <#
      La password NON va sulla riga di comando: `-pSegreta` è visibile a chiunque guardi i
      processi e resta nella cronologia del terminale. Va in un file temporaneo che si
      cancella comunque, anche se la query fallisce.
    #>
    param([Parameter(Mandatory)][string]$Sql)

    $mysql = Get-MysqlExe
    $file = Join-Path $env:TEMP ("atecpm-my-" + [guid]::NewGuid().ToString('N') + ".cnf")
    try {
        # ASCII e non UTF8: in PowerShell 5.1 `-Encoding UTF8` scrive il BOM, e con tre byte
        # davanti MySQL non riconosce piu la sezione [client] — il file verrebbe ignorato e
        # la password chiesta di nuovo, senza spiegazioni.
        Set-Content -Path $file -Encoding ASCII -Value @(
            '[client]',
            "user=$Utente",
            "password=$Password"
        )
        & $mysql "--defaults-extra-file=$file" --table -e $Sql | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "MySQL ha risposto con errore (codice $LASTEXITCODE)." }
    }
    finally {
        Remove-Item $file -Force -ErrorAction SilentlyContinue
    }
}

function Read-PasswordSeManca {
    if ($Password) { return }
    $sec = Read-Host "Password MySQL per '$Utente'" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { $Script:Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# --- Le richieste HTTP: si leggono dai log del server, senza toccare il database ----------
function Show-RichiesteLente {
    <#
      Aggrega le righe scritte da RichiesteLenteMiddleware. È la metà della misura che lo
      slow query log NON può dare: una richiesta che fa 300 query da 5 ms non compare in
      nessuno slow query log, eppure impiega un secondo e mezzo. È la forma degli N+1.
    #>
    if (-not (Test-Path $CartellaLog)) { throw "Cartella dei log non trovata: $CartellaLog" }

    $file = @(Get-ChildItem $CartellaLog -Filter 'server-*.log' -ErrorAction SilentlyContinue)
    if ($file.Count -eq 0) { throw "Nessun file server-*.log in $CartellaLog." }

    # Niente caratteri non ASCII nel modello: il separatore del log è un punto medio, e una
    # regex che lo contenesse dipenderebbe dalla codifica con cui PowerShell legge il file.
    $modello = '\[Lenta\]\s+(?<ms>\d+)\s+ms\s+\S+\s+(?<met>[A-Z]+)\s+(?<rotta>\S+)\s+\(status\s+(?<st>\d+)'

    # @(...) come sopra: con UNA sola richiesta lenta la pipeline torna un oggetto singolo,
    # .Count non esisterebbe e il totale uscirebbe vuoto.
    $voci = @(Select-String -Path $file.FullName -Pattern $modello | ForEach-Object {
        $g = $_.Matches[0].Groups
        [pscustomobject]@{
            Chiave = "$($g['met'].Value) $($g['rotta'].Value)"
            Ms     = [int]$g['ms'].Value
            Status = $g['st'].Value
        }
    })

    if ($voci.Count -eq 0) {
        Write-Host "Nessuna richiesta oltre soglia nei log di $CartellaLog." -ForegroundColor Green
        Write-Host 'Se il server e acceso da poco puo essere normale; se sono giorni, la misura e spenta (Diagnostics:SlowRequestMs).' -ForegroundColor DarkGray
        return
    }

    Write-Host "Richieste lente: $($voci.Count) occorrenze in $($file.Count) file di log." -ForegroundColor Cyan

    # Ordinate per TEMPO TOTALE, non per la piu lenta: un endpoint da 600 ms chiamato 400
    # volte pesa piu di uno da 9 secondi chiamato una volta, e si corregge una volta sola.
    $voci | Group-Object Chiave | ForEach-Object {
        $ms = @($_.Group.Ms | Sort-Object)
        [pscustomobject]@{
            Rotta     = $_.Name
            Volte     = $_.Count
            Totale_s  = [math]::Round(($ms | Measure-Object -Sum).Sum / 1000, 1)
            # Percentile per rango (ceil(p * n) - 1) e non floor((n-1) * p): su pochi campioni
            # il secondo restituisce un P95 piu' basso del massimo osservato, cioe' dice che
            # va tutto bene proprio quando l'unico dato che conta e' il caso peggiore.
            Mediana_ms= $ms[[int][math]::Ceiling(0.5 * $ms.Count) - 1]
            P95_ms    = $ms[[int][math]::Ceiling(0.95 * $ms.Count) - 1]
            Max_ms    = $ms[-1]
        }
    } | Sort-Object Totale_s -Descending | Select-Object -First $Righe | Format-Table -AutoSize | Out-Host
}

# --- Azioni sul database ------------------------------------------------------------------
switch ($Azione) {

    'richieste' { Show-RichiesteLente; break }

    'stato' {
        Read-PasswordSeManca
        Write-Host 'Configurazione della misura:' -ForegroundColor Cyan
        # Il testo va in una variabile e POI si passa: `Invoke-Sql @'` verrebbe letto come
        # splatting e PowerShell proverebbe a eseguire l'SQL come se fossero comandi.
        $sql = @'
SELECT @@slow_query_log AS attivo, @@GLOBAL.long_query_time AS soglia_secondi,
       @@log_output AS destinazione, @@log_queries_not_using_indexes AS anche_senza_indice;
SELECT COUNT(*) AS query_lente_registrate, MIN(start_time) AS dalla, MAX(start_time) AS alla
FROM mysql.slow_log;
'@
        Invoke-Sql $sql
        break
    }

    'accendi' {
        Read-PasswordSeManca
        Write-Host "Accendo lo slow query log a $SogliaSecondi s..." -ForegroundColor Cyan
        # SET PERSIST e non SET GLOBAL: la misura deve sopravvivere a un riavvio del servizio
        # MySQL, altrimenti la settimana di dati si interrompe senza che nessuno se ne accorga.
        #
        # log_output=TABLE e non FILE: le occorrenze finiscono in mysql.slow_log e si
        # interrogano con SQL. Su Windows l'alternativa sarebbe mysqldumpslow, che e uno script
        # Perl non installato.
        #
        # log_queries_not_using_indexes resta SPENTO apposta: acceso, ogni SELECT su una
        # tabella piccola finisce nel registro (le anagrafiche sono decine di righe: MySQL le
        # legge tutte perche conviene, non perche manchi un indice). Il registro diventerebbe
        # illeggibile proprio dove serve leggerlo.
        #
        # Si rilegge @@GLOBAL.long_query_time e NON @@long_query_time: quest'ultima e la copia
        # di SESSIONE, presa quando la connessione si e aperta. Sarebbe rimasta al valore
        # vecchio e avrebbe fatto credere che l'accensione non avesse funzionato.
        $sql = @"
SET PERSIST slow_query_log = ON;
SET PERSIST long_query_time = $SogliaSecondi;
SET PERSIST log_output = 'TABLE';
SET PERSIST log_queries_not_using_indexes = OFF;
SELECT @@slow_query_log AS attivo, @@GLOBAL.long_query_time AS soglia_secondi, @@log_output AS destinazione;
"@
        Invoke-Sql $sql
        Write-Host 'Acceso. Lasciarlo una settimana di lavoro vero, poi: -Azione lente' -ForegroundColor Green
        Write-Host 'Ricordarsi di spegnerlo dopo (-Azione spegni): la tabella cresce e non si svuota da sola.' -ForegroundColor Yellow
        break
    }

    'spegni' {
        Read-PasswordSeManca
        $sql = @'
SET PERSIST slow_query_log = OFF;
SELECT @@slow_query_log AS attivo,
       (SELECT COUNT(*) FROM mysql.slow_log) AS registrate_da_leggere;
'@
        Invoke-Sql $sql
        Write-Host 'Spento. Il registro resta: leggerlo con -Azione lente, poi -Azione svuota.' -ForegroundColor Green
        break
    }

    'lente' {
        Read-PasswordSeManca
        Write-Host "Query oltre soglia, raggruppate (prime $Righe per numero di occorrenze):" -ForegroundColor Cyan
        # I numeri diventano '?' per raggruppare: senza, la stessa query su due commesse
        # diverse conta come due query diverse e la classifica non dice niente.
        # righe_lette e la colonna che indica un indice mancante (E2): tante righe esaminate
        # per poche restituite significa che MySQL sta scorrendo la tabella.
        # 🪤 TIME_TO_SEC da solo TRONCA ai secondi interi: con la soglia a 0,5 s tutte le query
        # fra mezzo secondo e un secondo — cioe' quasi tutte quelle che stiamo cercando —
        # sarebbero comparse come «0 secondi». I microsecondi vanno rimessi a mano.
        $sql = @"
SELECT COUNT(*) AS volte,
       ROUND(AVG(TIME_TO_SEC(query_time) + MICROSECOND(query_time) / 1000000), 3) AS media_s,
       ROUND(MAX(TIME_TO_SEC(query_time) + MICROSECOND(query_time) / 1000000), 3) AS massimo_s,
       MAX(rows_examined) AS righe_lette,
       MAX(rows_sent) AS righe_rese,
       LEFT(REGEXP_REPLACE(CONVERT(sql_text USING utf8mb4), '[0-9]+', '?'), 110) AS query
FROM mysql.slow_log
WHERE db = '$Database' OR db = ''
GROUP BY query
ORDER BY volte DESC
LIMIT $Righe;
"@
        Invoke-Sql $sql
        break
    }

    'classifica' {
        Read-PasswordSeManca
        Write-Host "Query per tempo totale (performance_schema, prime $Righe):" -ForegroundColor Cyan
        # Questa NON dipende dallo slow query log: performance_schema conta tutto da sempre.
        # Ordinata per tempo TOTALE perche e li che si vedono gli N+1 di E3: una query da 4 ms
        # eseguita 20.000 volte non e lenta, ma e il motivo per cui una pagina impiega un
        # minuto — e nello slow query log non compare mai.
        # Si azzera al riavvio di MySQL: se i numeri sembrano bassi, guardare da quando conta.
        $sql = @"
SELECT COUNT_STAR AS volte,
       ROUND(SUM_TIMER_WAIT / 1000000000000, 1) AS totale_s,
       ROUND(AVG_TIMER_WAIT / 1000000000, 1) AS media_ms,
       ROUND(MAX_TIMER_WAIT / 1000000000, 1) AS massimo_ms,
       SUM_ROWS_EXAMINED AS righe_lette,
       LEFT(DIGEST_TEXT, 110) AS query
FROM performance_schema.events_statements_summary_by_digest
WHERE SCHEMA_NAME = '$Database'
ORDER BY SUM_TIMER_WAIT DESC
LIMIT $Righe;
"@
        Invoke-Sql $sql
        break
    }

    'svuota' {
        Read-PasswordSeManca
        Invoke-Sql 'TRUNCATE TABLE mysql.slow_log; SELECT COUNT(*) AS rimaste FROM mysql.slow_log;'
        Write-Host 'Registro azzerato.' -ForegroundColor Green
        break
    }
}
