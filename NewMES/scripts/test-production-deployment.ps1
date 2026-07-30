[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [Uri]$BaseUri
)

$ErrorActionPreference = 'Stop'
# Accept only the production HTTPS entry point so the smoke test cannot bypass the reverse proxy.
if ($BaseUri.Scheme -ne 'https') {
    throw 'Production smoke tests require an HTTPS BaseUri.'
}

function Invoke-NewMesGet {
    param([string]$Path)

    # Correlation ID echoing proves the deployment preserved the proxy-to-API observability path.
    $correlationId = 'deployment-smoke-' + [Guid]::NewGuid().ToString('N')
    $response = Invoke-WebRequest `
        -Uri ([Uri]::new($BaseUri, $Path)) `
        -Headers @{ 'X-Correlation-ID' = $correlationId } `
        -UseBasicParsing
    if ($response.StatusCode -ne 200) {
        throw "$Path returned HTTP $($response.StatusCode)."
    }
    if ($response.Headers['X-Correlation-ID'] -ne $correlationId) {
        throw "$Path did not preserve the correlation ID."
    }
    return $response
}

# Verify process liveness, business readiness, and the minimal application response independently.
$live = Invoke-NewMesGet '/health/live'
$ready = Invoke-NewMesGet '/health/ready'
$systemInfo = Invoke-NewMesGet '/api/system/info'
$readyPayload = $ready.Content | ConvertFrom-Json
$systemPayload = $systemInfo.Content | ConvertFrom-Json

if ($readyPayload.status -ne 'Healthy') {
    throw 'Production readiness is not Healthy.'
}
if ($systemPayload.service -ne 'NewMES.Api') {
    throw 'The system information API returned an unexpected service identity.'
}

Write-Output 'Production HTTPS deployment smoke test passed.'
