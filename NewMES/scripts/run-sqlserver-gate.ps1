[CmdletBinding()]
param()

$ErrorActionPreference = 'SilentlyContinue'
docker info *> $null
$dockerExitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($dockerExitCode -ne 0) {
    throw 'Docker engine is required for the real SQL Server release gate.'
}

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
