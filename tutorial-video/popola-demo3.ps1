# Parte 3: taratura dei numeri perché la commessa racconti una storia leggibile —
# preventivo appena sopra la soglia del 20%, consuntivo che ci scivola sotto.
$ErrorActionPreference = "Stop"
$base = "C:\Users\diego\AppData\Local\Temp\claude\C--Users-diego-Desktop-ATEC-PM-CSharp-v5\55e82440-f727-4ca7-bba7-069c58ab2b91\scratchpad"
$token = Get-Content "$base\token.txt" -Raw
$H = @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" }
$API = "http://localhost:5150/api"
$projId = 29
$mysql = "C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe"
$env:MYSQL_PWD = "Atec2005"

function Get-Api($path) { Invoke-RestMethod -Uri "$API$path" -Headers $H -TimeoutSec 60 }
function Invoke-Api($method, $path, $body) {
  try {
    return Invoke-RestMethod -Uri "$API$path" -Headers $H -Method $method -TimeoutSec 60 `
      -Body ($body | ConvertTo-Json -Depth 6 -Compress)
  } catch {
    $resp = $_.Exception.Response
    if ($resp) {
      $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
      throw "$method $path -> $($resp.StatusCode.value__): $($reader.ReadToEnd())"
    }
    throw
  }
}
function Sql($query) { & $mysql -u root -D atec_pm -B -e $query }

# ── 1. ordine cliente: 70.000 € su due posizioni ───────────────────────────
# Sotto il Totale Vendita calcolato dal preventivo (74.551 €): così il Delta Ordine
# è negativo e si vede a cosa serve.
$bva = (Get-Api "/projects/$projId/budget-vs-actual").data
$righe = $bva.orderLines | Sort-Object orderPosition
$importi = @(55000, 15000)
for ($i = 0; $i -lt $righe.Count -and $i -lt 2; $i++) {
  Invoke-Api "Put" "/projects/$projId/budget-vs-actual/order-lines/$($righe[$i].id)" @{
    orderRef = "4500123"
    orderPosition = $righe[$i].orderPosition
    amount = $importi[$i]
    rowVersion = $righe[$i].rowVersion
  } | Out-Null
}
"Ordine aggiornato: 55.000 + 15.000 = 70.000 EUR"

# ── 2. ore in più: il cantiere e il montaggio sforano ──────────────────────
$fasi = Sql "SELECT pp.id, pt.id AS tpl FROM project_phases pp JOIN phase_templates pt ON pt.id=pp.phase_template_id WHERE pp.project_id=$projId;"
$mapFasi = @{}
foreach ($riga in ($fasi | Select-Object -Skip 1)) {
  $c = $riga -split "`t"
  if ($c.Count -ge 2) { $mapFasi[[int]$c[1]] = [int]$c[0] }
}

$extra = @(
  @{ tpl = 9;  h = 80 }   # montaggio meccanico in ATEC
  @{ tpl = 11; h = 60 }   # installazione meccanica in cantiere
  @{ tpl = 5;  h = 50 }   # installazione elettrica in cantiere
  @{ tpl = 37; h = 30 }   # project management
  @{ tpl = 2;  h = 30 }   # cablaggio quadro
)
$dip = (Get-Api "/employees").data | Where-Object { $_.status -eq "ACTIVE" }
$oggi = Get-Date
$tot = 0
$i = 7   # dipendenti diversi da quelli già usati nella parte 2
foreach ($voce in $extra) {
  $faseId = $mapFasi[$voce.tpl]
  if (-not $faseId) { continue }
  $emp = $dip[$i % $dip.Count]; $i++
  $restanti = $voce.h
  $giorno = 40   # giorni ancora liberi per questi dipendenti
  while ($restanti -gt 0) {
    $q = [Math]::Min(8, $restanti)
    Invoke-Api "Post" "/timesheet" @{
      id = 0; employeeId = $emp.id; projectPhaseId = $faseId
      workDate = $oggi.AddDays(-$giorno).ToString("yyyy-MM-dd")
      hours = $q; entryType = "REGULAR"; notes = ""
    } | Out-Null
    $restanti -= $q; $giorno++; $tot += $q
  }
}
"Ore aggiuntive: $tot h"

# ── 3. trasferta a consuntivo (si imputa a mano dal Conto Economico) ───────
Invoke-Api "Patch" "/projects/$projId/budget-vs-actual/actual-travel-cost" 7800 | Out-Null
"Trasferta consuntivo: 7.800 EUR"

# ── 4. risultato ───────────────────────────────────────────────────────────
$b = (Get-Api "/projects/$projId/budget-vs-actual").data
$e = $b.economic
""
"=== BILANCIO RISULTANTE ==="
"Totale Ordine          {0,12:N2}" -f $e.orderPrice
"Totale Vendita         {0,12:N2}   (Delta Ordine {1:N2})" -f $e.saleTotal, $e.orderDelta
"Totale Costi           {0,12:N2}" -f $e.budgetCost
"Consuntivo Costi       {0,12:N2}" -f $e.actualTotalCost
"Redditivita teorica    {0,12:N2}   {1:N2} %" -f $e.budgetProfitability, $e.budgetProfitabilityPct
"Redditivita effettiva  {0,12:N2}   {1:N2} %" -f $e.profitability, $e.profitabilityPct
"Ore prev/cons          {0} / {1}" -f $b.totalBudgetHours, $b.totalActualHours
