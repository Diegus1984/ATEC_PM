# Parte 2: preventivo, ordine cliente e ore consuntivate della commessa dimostrativa.
$ErrorActionPreference = "Stop"
$base = "C:\Users\diego\AppData\Local\Temp\claude\C--Users-diego-Desktop-ATEC-PM-CSharp-v5\55e82440-f727-4ca7-bba7-069c58ab2b91\scratchpad"
$token = Get-Content "$base\token.txt" -Raw
$H = @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" }
$API = "http://localhost:5150/api"
$projId = 29
$mysql = "C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe"

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
function Post-Api($path, $body) { Invoke-Api "Post" $path $body }
# MYSQL_PWD evita il warning su stderr che, con ErrorActionPreference=Stop, farebbe
# abortire lo script pur essendo innocuo.
$env:MYSQL_PWD = "Atec2005"
function Sql($query) { & $mysql -u root -D atec_pm -B -e $query }

# ── 1. le fasi standard devono avere una sezione, o le ore non entrano nella
#       ripartizione del Bilancio (è il caso spiegato nel primo tutorial).
$mappa = @{
  31 = 44; 28 = 44; 27 = 44; 29 = 44; 8 = 44      # → Ufficio Tecnico Meccanico
  1 = 45                                            # → Ufficio Tecnico Elettrico
  2 = 55; 3 = 55; 4 = 55; 6 = 55; 9 = 55; 10 = 55; 38 = 55   # → Allestimento
  5 = 60; 11 = 60; 39 = 60                          # → Installazione da cliente
  15 = 54; 16 = 54; 17 = 54; 22 = 54                # → Sviluppo SW Back Office
  23 = 42                                           # → Robot Studio
  18 = 56; 24 = 57                                  # → Commissioning in sede
  19 = 61; 25 = 62                                  # → Commissioning da cliente
  37 = 73                                           # → Program Manager
}
foreach ($k in $mappa.Keys) {
  Sql "UPDATE phase_templates SET cost_section_template_id=$($mappa[$k]) WHERE id=$k;" | Out-Null
}
"Fasi template mappate: $($mappa.Count)"

# ── 2. preventivo: una risorsa per sezione ─────────────────────────────────
$sezioni = (Get-Api "/projects/$projId/costing").data.costSections
function SezId($nome, $tipo) {
  ($sezioni | Where-Object { $_.name -eq $nome -and $_.sectionType -eq $tipo } | Select-Object -First 1).id
}

$risorse = @(
  @{ sez = SezId "Progettazione Ufficio Tecnico Meccanico" "IN_SEDE"; nome = "Progettista meccanico"; gg = 15; hg = 8; costo = 45 }
  @{ sez = SezId "Progettazione Ufficio Tecnico Elettrico" "IN_SEDE"; nome = "Progettista elettrico"; gg = 10; hg = 8; costo = 45 }
  @{ sez = SezId "Sviluppo SW Back Office" "IN_SEDE"; nome = "Programmatore PLC e robot"; gg = 18; hg = 8; costo = 50 }
  @{ sez = SezId "Robot Studio - Cella Simulazioni" "IN_SEDE"; nome = "Specialista simulazione"; gg = 8; hg = 8; costo = 50 }
  @{ sez = SezId "Allestimento Meccanico / Elettrico" "IN_SEDE"; nome = "Squadra montaggio"; gg = 35; hg = 8; costo = 38 }
  @{ sez = SezId "Commissioning PLC / HMI" "IN_SEDE"; nome = "Tecnico commissioning PLC"; gg = 10; hg = 8; costo = 50 }
  @{ sez = SezId "Commissioning Robot" "IN_SEDE"; nome = "Tecnico commissioning robot"; gg = 10; hg = 8; costo = 50 }
  @{ sez = SezId "Program Manager" "IN_SEDE"; nome = "Program manager"; gg = 25; hg = 4; costo = 45 }
)
foreach ($r in $risorse) {
  if (-not $r.sez) { "  ATTENZIONE sezione non trovata per $($r.nome)"; continue }
  Post-Api "/projects/$projId/costing/resources" @{
    id = 0; sectionId = $r.sez; employeeId = $null; resourceName = $r.nome
    workDays = $r.gg; hoursPerDay = $r.hg; hourlyCost = $r.costo; markupValue = 1.45
    numTrips = 0; kmPerTrip = 0; costPerKm = 0.90; dailyFood = 0; dailyHotel = 0
    allowanceDays = 0; dailyAllowance = 0; sortOrder = 0
  } | Out-Null
}
# la squadra che va dal cliente ha anche la trasferta
$sezInst = SezId "Installazione Meccanica / Elettrica" "DA_CLIENTE"
Post-Api "/projects/$projId/costing/resources" @{
  id = 0; sectionId = $sezInst; employeeId = $null; resourceName = "Squadra installazione"
  workDays = 20; hoursPerDay = 8; hourlyCost = 38; markupValue = 1.45
  numTrips = 4; kmPerTrip = 320; costPerKm = 0.90; dailyFood = 30; dailyHotel = 90
  allowanceDays = 20; dailyAllowance = 25; sortOrder = 0
} | Out-Null
"Risorse preventivate: $($risorse.Count + 1)"

# ── 3. ordine cliente: due posizioni ───────────────────────────────────────
Post-Api "/projects/$projId/budget-vs-actual/order-lines" @{
  orderRef = "4500123"; orderPosition = "00010"; amount = 95000
} | Out-Null
Post-Api "/projects/$projId/budget-vs-actual/order-lines" @{
  orderRef = "4500123"; orderPosition = "00020"; amount = 25000
} | Out-Null
"Righe ordine: 2 (totale 120.000 €)"

# ── 4. ore consuntivate ────────────────────────────────────────────────────
# Un dipendente diverso per fase, giorni consecutivi nel passato: così nessuno
# supera il limite di 24 ore al giorno.
$fasi = Sql "SELECT pp.id, pt.id AS tpl FROM project_phases pp JOIN phase_templates pt ON pt.id=pp.phase_template_id WHERE pp.project_id=$projId;"
$mapFasi = @{}
foreach ($riga in ($fasi | Select-Object -Skip 1)) {
  $c = $riga -split "`t"
  if ($c.Count -ge 2) { $mapFasi[[int]$c[1]] = [int]$c[0] }
}

$ore = @(
  @{ tpl = 27; h = 60 }; @{ tpl = 28; h = 40 }; @{ tpl = 29; h = 25 }; @{ tpl = 8; h = 40 }
  @{ tpl = 1; h = 85 }
  @{ tpl = 15; h = 70 }; @{ tpl = 16; h = 40 }; @{ tpl = 17; h = 30 }; @{ tpl = 22; h = 45 }
  @{ tpl = 23; h = 55 }
  @{ tpl = 9; h = 120 }; @{ tpl = 10; h = 60 }; @{ tpl = 2; h = 55 }; @{ tpl = 3; h = 50 }; @{ tpl = 6; h = 30 }
  @{ tpl = 18; h = 65 }; @{ tpl = 24; h = 70 }
  @{ tpl = 11; h = 95 }; @{ tpl = 5; h = 80 }
  @{ tpl = 37; h = 110 }
)

$dip = (Get-Api "/employees").data | Where-Object { $_.status -eq "ACTIVE" }
$oggi = Get-Date
$totOre = 0
$i = 0
foreach ($voce in $ore) {
  $faseId = $mapFasi[$voce.tpl]
  if (-not $faseId) { "  fase template $($voce.tpl) non presente"; continue }
  $emp = $dip[$i % $dip.Count]
  $i++
  $restanti = $voce.h
  $giorno = 1
  while ($restanti -gt 0) {
    $q = [Math]::Min(8, $restanti)
    Post-Api "/timesheet" @{
      id = 0; employeeId = $emp.id; projectPhaseId = $faseId
      workDate = $oggi.AddDays(-$giorno).ToString("yyyy-MM-dd")
      hours = $q; entryType = "REGULAR"; notes = ""
    } | Out-Null
    $restanti -= $q
    $giorno++
    $totOre += $q
  }
}
"Ore consuntivate registrate: $totOre h"
"FATTO."
