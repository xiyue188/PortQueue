# Run from any directory; only the isolated mobile database and loopback port are used.
[CmdletBinding()]
param([switch]$SeedDemoAccounts, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$mobileRoot = Split-Path $PSScriptRoot -Parent
$names = @('ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'ConnectionStrings__PortQueue', 'Mobile__Enabled', 'DemoAccounts__Enabled')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5181'
    $env:ConnectionStrings__PortQueue = 'Server=(localdb)\PortQueueDev;Database=PortQueueMobileDev;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5'
    $env:Mobile__Enabled = 'true'
    $env:DemoAccounts__Enabled = 'true'
    $runArgs = @('run', '--project', (Join-Path $mobileRoot 'src/PortQueue.Api/PortQueue.Api.csproj'), '--no-launch-profile')
    if ($NoBuild) { $runArgs += '--no-build' }
    if ($SeedDemoAccounts) { $runArgs += @('--', 'account', 'seed-demo') }
    & dotnet @runArgs
    if ($LASTEXITCODE -ne 0) { throw "Mobile API exited with code $LASTEXITCODE." }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
