[CmdletBinding()]
param()

# The release gate must exercise real SQL Server behavior. Without an external connection, the fixture may use Docker.
if ([string]::IsNullOrWhiteSpace($env:NEWMES_SQLSERVER_TEST_CONNECTION)) {
    $ErrorActionPreference = 'SilentlyContinue'
    docker info *> $null
    $dockerExitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($dockerExitCode -ne 0) {
        throw 'Set NEWMES_SQLSERVER_TEST_CONNECTION or start Docker for the real SQL Server release gate.'
    }
}

# Enable the explicit gate only for this process, then restore the caller's environment in finally.
$previousGateValue = $env:NEWMES_RUN_SQLSERVER_TESTS
$env:NEWMES_RUN_SQLSERVER_TESTS = 'true'

try {
    dotnet test "$PSScriptRoot\..\tests\Mes.SqlServer.IntegrationTests\Mes.SqlServer.IntegrationTests.csproj" `
        --logger "trx;LogFileName=sqlserver-gate.trx" `
        --results-directory "$PSScriptRoot\..\.artifacts\sqlserver-gate"
    if ($LASTEXITCODE -ne 0) {
        throw "SQL Server release gate failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:NEWMES_RUN_SQLSERVER_TESTS = $previousGateValue
}
