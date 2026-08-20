# Costruisce la commessa dimostrativa per il tutorial del Bilancio.
# Dati inventati: nessun cliente e nessun importo reale.
$ErrorActionPreference = "Stop"
$token = Get-Content "C:\Users\diego\AppData\Local\Temp\claude\C--Users-diego-Desktop-ATEC-PM-CSharp-v5\55e82440-f727-4ca7-bba7-069c58ab2b91\scratchpad\token.txt" -Raw
$H = @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" }
$API = "http://localhost:5150/api"

function Get-Api($path) { Invoke-RestMethod -Uri "$API$path" -Headers $H -TimeoutSec 60 }

# Il corpo della risposta d'errore è dove il server spiega cosa non va: senza questo
# si vede solo «400 Richiesta non valida» e si procede alla cieca.
function Invoke-Api($method, $path, $body) {
  try {
    return Invoke-RestMethod -Uri "$API$path" -Headers $H -Method $method -TimeoutSec 60 `
      -Body ($body | ConvertTo-Json -Depth 6 -Compress)
  } catch {
    $resp = $_.Exception.Response
    if ($resp) {
      $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
      $testo = $reader.ReadToEnd()
      throw "$method $path -> $($resp.StatusCode.value__): $testo"
    }
    throw
  }
}
function Post-Api($path, $body) { Invoke-Api "Post" $path $body }
function Put-Api($path, $body) { Invoke-Api "Put" $path $body }

# ── 1. cliente dimostrativo ────────────────────────────────────────────────
$clienti = (Get-Api "/customers").data
$demo = $clienti | Where-Object { $_.companyName -like "DEMO*" } | Select-Object -First 1
if (-not $demo) {
  $nuovo = @{
    id = 0; companyName = "DEMO S.p.A. (cliente dimostrativo)"; contactName = ""
    email = ""; pec = ""; phone = ""; cell = ""; vatNumber = "IT00000000999"; fiscalCode = ""
    sdiCode = ""; address = ""; notes = "Cliente finto per i tutorial."
    paymentTerms = ""; isActive = $true; initials = "DEMO"
  }
  Post-Api "/customers" $nuovo | Out-Null
  $clienti = (Get-Api "/customers").data
  $demo = $clienti | Where-Object { $_.companyName -like "DEMO*" } | Select-Object -First 1
}
"CLIENTE: id=$($demo.id)  $($demo.companyName)"

# ── 2. commessa ────────────────────────────────────────────────────────────
$emp = (Get-Api "/employees").data
$pm = $emp | Where-Object { $_.status -eq "ACTIVE" } | Select-Object -First 1
"PM: id=$($pm.id) $($pm.fullName)"
$oggi = Get-Date
$commessa = @{
  id = 0
  code = ""
  title = "Cella robotizzata di saldatura"
  customerId = $demo.id
  pmId = $pm.id
  description = "Commessa dimostrativa per il tutorial del Bilancio."
  startDate = $oggi.AddDays(-90).ToString("yyyy-MM-dd")
  endDatePlanned = $oggi.AddDays(30).ToString("yyyy-MM-dd")
  budgetTotal = 0
  budgetHoursTotal = 0
  revenue = 0
  status = "ACTIVE"
  priority = "HIGH"
  createDefaultPhases = $true
  linkedQuoteId = 0
}
$creata = Post-Api "/projects" $commessa
$projId = $creata.data
"COMMESSA creata: id=$projId"
$dett = (Get-Api "/projects/$projId").data
"  codice: $($dett.code)"
$projId | Out-File "$PSScriptRoot\demo-project-id.txt" -Encoding ascii -NoNewline

# ── 3. preventivo dai modelli standard ─────────────────────────────────────
Post-Api "/projects/$projId/costing/init" @{} | Out-Null
$costing = (Get-Api "/projects/$projId/costing").data
"SEZIONI preventivo: $($costing.costSections.Count)"
$costing.costSections | Select-Object -First 30 | ForEach-Object { "   [$($_.id)] $($_.sectionName)" }
