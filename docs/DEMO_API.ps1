# API-only golden path smoke (requires API on http://localhost:5101)
# Usage: pwsh -File docs/DEMO_API.ps1
$ErrorActionPreference = "Stop"
$base = "http://localhost:5101"

function Login($user, $pass) {
  $r = Invoke-RestMethod -Uri "$base/api/auth/login" -Method POST -ContentType "application/json" `
    -Body (@{ userName = $user; password = $pass } | ConvertTo-Json)
  return $r.accessToken
}

function Api($method, $path, $token, $body = $null) {
  $h = @{ Authorization = "Bearer $token" }
  if ($null -eq $body) {
    return Invoke-RestMethod -Uri "$base$path" -Method $method -Headers $h
  }
  return Invoke-RestMethod -Uri "$base$path" -Method $method -Headers $h -ContentType "application/json" `
    -Body ($body | ConvertTo-Json -Depth 8)
}

Write-Host "Health..."
Invoke-RestMethod "$base/health" | Out-Host

$planner = Login "planner" "Planner@123"
$op = Login "operator" "Operator@123"

$mats = Api GET "/api/materials" $planner
$fg = $mats | Where-Object { $_.code -eq "FG-ROUTER" } | Select-Object -First 1
$pcb = $mats | Where-Object { $_.code -eq "PCB-MAIN" } | Select-Object -First 1

$wo = Api POST "/api/work-orders" $planner @{
  finishedMaterialId = $fg.id
  plannedQty = 1
}
Api POST "/api/work-orders/$($wo.id)/release" $planner @{} | Out-Null
Api POST "/api/work-orders/$($wo.id)/issue" $planner @{} | Out-Null
Write-Host "WO $($wo.orderNo) released + issued"

$stations = Api GET "/api/stations/active" $op
$sn = "SN-PS1-" + (Get-Random -Maximum 999999)
foreach ($code in @("ONLINE","FLASH","ASSEMBLY","FQC","PACK")) {
  $st = $stations | Where-Object { $_.stepCode -eq $code } | Select-Object -First 1
  $body = @{ workStationId = $st.id; serialNo = $sn; workOrderId = $null }
  if ($code -eq "ONLINE") { $body.workOrderId = $wo.id }
  Api POST "/api/station/pass" $op $body | Out-Null
  if ($code -eq "ONLINE") {
    Api POST "/api/station/bind-component" $op @{
      productSerialNo = $sn
      componentMaterialId = $pcb.id
      componentSerialNo = "PCB-PS1-" + (Get-Random -Maximum 99999)
      workStationId = $st.id
    } | Out-Null
  }
  Write-Host "  passed $code"
}

Api POST "/api/completion/receive" $op @{ serialNo = $sn } | Out-Null
Api POST "/api/work-orders/$($wo.id)/close" $planner @{} | Out-Null
$g = Api GET "/api/genealogy/$sn" $planner
$outbox = Api GET "/api/erp/outbox" $planner
Write-Host "Genealogy status=$($g.status) passes=$($g.passes.Count) bindings=$($g.bindings.Count)"
Write-Host "Outbox types: $(($outbox | ForEach-Object { $_.messageType }) -join ', ')"
Write-Host "DEMO_API OK"
