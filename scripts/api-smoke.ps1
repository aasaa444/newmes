$ErrorActionPreference = "Stop"
$base = "http://localhost:5101"

function Login([string]$u, [string]$p) {
  $r = Invoke-RestMethod "$base/api/auth/login" -Method POST -ContentType "application/json" -Body (@{userName=$u; password=$p} | ConvertTo-Json) -TimeoutSec 5
  return $r.accessToken
}

$owner = Login "owner" "Owner@123"
$planner = Login "planner" "Planner@123"
$op = Login "operator" "Operator@123"

Write-Host "== owner overview =="
$h = Invoke-RestMethod "$base/api/ops/overview" -Headers @{Authorization="Bearer $owner"} -TimeoutSec 5
$h | ConvertTo-Json -Depth 4 | Write-Host

Write-Host "== seed data =="
$mat = Invoke-RestMethod "$base/api/materials" -Headers @{Authorization="Bearer $planner"} -TimeoutSec 5
$fg = ($mat | Where-Object { $_.code -eq "FG-ROUTER" })[0].id

$body = @{finishedMaterialId=$fg; plannedQty=2} | ConvertTo-Json
$wo = Invoke-RestMethod "$base/api/work-orders" -Method POST -ContentType "application/json" -Headers @{Authorization="Bearer $planner"} -Body $body -TimeoutSec 5
Invoke-RestMethod "$base/api/work-orders/$($wo.id)/release" -Method POST -ContentType "application/json" -Headers @{Authorization="Bearer $planner"} -Body '{}' -TimeoutSec 5 | Out-Null
Invoke-RestMethod "$base/api/work-orders/$($wo.id)/issue" -Method POST -ContentType "application/json" -Headers @{Authorization="Bearer $planner"} -Body '{}' -TimeoutSec 5 | Out-Null

$st = Invoke-RestMethod "$base/api/stations/active" -Headers @{Authorization="Bearer $op"} -TimeoutSec 5
$online = ($st | Where-Object { $_.stepCode -eq "ONLINE" })[0].id
$sn = "SN-DEMO-" + (Get-Random -Maximum 999999)
$body = @{workStationId=$online; serialNo=$sn; workOrderId=$wo.id} | ConvertTo-Json
Invoke-RestMethod "$base/api/station/pass" -Method POST -ContentType "application/json" -Headers @{Authorization="Bearer $op"} -Body $body -TimeoutSec 5 | Out-Null

Write-Host "== overview after seed =="
$h2 = Invoke-RestMethod "$base/api/ops/overview" -Headers @{Authorization="Bearer $owner"} -TimeoutSec 5
$h2 | ConvertTo-Json -Depth 4 | Write-Host
