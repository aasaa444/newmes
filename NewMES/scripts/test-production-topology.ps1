[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$composePath = Join-Path $PSScriptRoot '..\deploy\compose.production.yml'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("newmes-topology-" + [Guid]::NewGuid().ToString('N'))
$secretFileEnvironment = @{
    NEWMES_APP_CONNECTION_FILE = (Join-Path $temporaryDirectory 'app-connection')
    NEWMES_MIGRATION_CONNECTION_FILE = (Join-Path $temporaryDirectory 'migration-connection')
    NEWMES_JWT_SIGNING_KEY_FILE = (Join-Path $temporaryDirectory 'jwt-key')
    NEWMES_SQL_SA_PASSWORD_FILE = (Join-Path $temporaryDirectory 'sql-password')
    NEWMES_TLS_CERTIFICATE_FILE = (Join-Path $temporaryDirectory 'tls-certificate')
    NEWMES_TLS_PRIVATE_KEY_FILE = (Join-Path $temporaryDirectory 'tls-private-key')
}
$requiredEnvironment = @{
    NEWMES_IMAGE_TAG = 'security-check'
}
foreach ($entry in $secretFileEnvironment.GetEnumerator()) {
    $requiredEnvironment[$entry.Key] = $entry.Value
}
$previousEnvironment = @{}

function Assert-Topology {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

try {
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    foreach ($entry in $requiredEnvironment.GetEnumerator()) {
        $previousEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
    foreach ($entry in $secretFileEnvironment.GetEnumerator()) {
        Set-Content -LiteralPath $entry.Value -Value 'topology-check-only' -NoNewline
    }

    $configurationJson = docker compose --file $composePath --profile operations config --format json
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose configuration failed with exit code $LASTEXITCODE."
    }

    $configuration = $configurationJson | ConvertFrom-Json
    $services = $configuration.services
    Assert-Topology ($null -ne $services.proxy.ports) 'The reverse proxy must publish HTTPS.'
    Assert-Topology ($services.proxy.ports.Count -eq 1) 'Only one proxy port may be published.'
    Assert-Topology ($services.proxy.ports[0].target -eq 443) 'The proxy must publish container port 443.'
    Assert-Topology ($null -eq $services.api.ports) 'The API must not publish a host port.'
    Assert-Topology ($null -eq $services.sqlserver.ports) 'SQL Server must not publish a host port.'
    Assert-Topology ($null -eq $services.migrator.ports) 'The migrator must not publish a host port.'
    Assert-Topology ($configuration.networks.backend.internal -eq $true) 'The backend network must be internal.'
    Assert-Topology ($services.api.environment.ASPNETCORE_ENVIRONMENT -eq 'Production') 'The API environment must be Production.'
    Assert-Topology ($services.api.environment.Security__ExternalHttpsOnly -eq 'true') 'External HTTPS must be required.'
    Assert-Topology ($services.api.environment.Security__DemoInitializationEnabled -eq 'false') 'Demo initialization must be disabled.'
    Assert-Topology ($services.api.environment.Security__SecretsSource -eq 'ExternalFiles') 'Secrets must come from external files.'
    Assert-Topology ($null -eq $services.api.environment.ConnectionStrings__MesDatabase) 'The API connection string must not be an environment value.'
    Assert-Topology ($null -eq $services.api.environment.Security__JwtSigningKey) 'The signing key must not be an environment value.'
    Assert-Topology ($null -eq $services.sqlserver.environment.MSSQL_SA_PASSWORD) 'The SQL administrator password must not be an environment value.'
    Assert-Topology ($services.sqlserver.environment.MSSQL_PID -ne 'Developer') 'The production topology cannot use SQL Server Developer edition.'
    Assert-Topology ($services.api.secrets.target -contains 'ConnectionStrings__MesDatabase') 'The API connection string secret mount is missing.'
    Assert-Topology ($services.api.secrets.target -contains 'Security__JwtSigningKey') 'The signing key secret mount is missing.'
    Assert-Topology ($services.migrator.secrets.target -contains 'ConnectionStrings__MesDatabase') 'The Migration connection secret mount is missing.'
    Assert-Topology ($services.proxy.secrets.target -contains 'tls_certificate') 'The TLS certificate secret mount is missing.'
    Assert-Topology ($services.proxy.secrets.target -contains 'tls_private_key') 'The TLS private key secret mount is missing.'
    Assert-Topology ($services.api.logging.driver -eq 'json-file') 'API logs must use the bounded JSON log driver.'
    Assert-Topology ($services.api.logging.options.'max-size' -eq '10m') 'API log size rotation is missing.'
    Assert-Topology ($services.api.logging.options.'max-file' -eq '5') 'API log retention count is missing.'
    Assert-Topology ($services.proxy.logging.options.'max-size' -eq '10m') 'Proxy log size rotation is missing.'
    Assert-Topology ($services.proxy.logging.options.'max-file' -eq '5') 'Proxy log retention count is missing.'

    Write-Output 'Production topology security check passed.'
}
finally {
    foreach ($entry in $requiredEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $previousEnvironment[$entry.Key])
    }
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
