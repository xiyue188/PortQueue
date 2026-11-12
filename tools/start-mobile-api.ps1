# Isolated mobile database and port only; LAN binding is explicit and opt-in.
[CmdletBinding()]
param([switch]$SeedDemoAccounts, [switch]$NoBuild,[ValidateSet('Release','Debug')][string]$Configuration='Release', [string]$LanAddress)
$ErrorActionPreference = 'Stop'
if ($LanAddress) {
    $address = $null
    if (-not [Net.IPAddress]::TryParse($LanAddress, [ref]$address) -or
        $address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) { throw 'LanAddress must be an IPv4 address.' }
    $b = $address.GetAddressBytes()
    if (-not ($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))) {
        throw 'LanAddress must be an explicit private LAN address; public/all-interface binding is prohibited.'
    }
    if ($LanAddress -notin @([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
        ForEach-Object { $_.GetIPProperties().UnicastAddresses.Address.IPAddressToString })) {
        throw 'LanAddress is not assigned to this computer.'
    }
}
$mobileRoot = Split-Path $PSScriptRoot -Parent
$names = @('ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'ConnectionStrings__PortQueue', 'Mobile__Enabled', 'Mobile__PhotoDirectory', 'DemoAccounts__Enabled')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5181'
    if ($LanAddress) { $env:ASPNETCORE_URLS += ";http://$($LanAddress):5181" }
    $env:ConnectionStrings__PortQueue = 'Server=(localdb)\PortQueueDev;Database=PortQueueMobileDev;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5'
    $env:Mobile__Enabled = 'true'
    $env:Mobile__PhotoDirectory = Join-Path $mobileRoot 'artifacts/mobile-private-photos'
    $env:DemoAccounts__Enabled = 'true'
    $runArgs = @('run', '--project', (Join-Path $mobileRoot 'src/PortQueue.Api/PortQueue.Api.csproj'), '--configuration',$Configuration, '--no-launch-profile')
    if ($NoBuild) { $runArgs += '--no-build' }
    if ($SeedDemoAccounts) { $runArgs += @('--', 'account', 'seed-demo') }
    & dotnet @runArgs
    if ($LASTEXITCODE -ne 0) { throw "Mobile API exited with code $LASTEXITCODE." }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
