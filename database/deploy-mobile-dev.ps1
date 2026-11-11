# Render the existing authoritative SQL for the isolated mobile development DB.
# Never reset/drop an existing DB. -PlanOnly renders files without contacting SQL.
[CmdletBinding()]
param(
    [ValidateSet('PortQueueMobileDev')]
    [string]$DatabaseName = 'PortQueueMobileDev',
    [switch]$IncludeDemoData,
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$mobileRoot = Split-Path $PSScriptRoot -Parent
$renderDirectory = Join-Path $mobileRoot "artifacts/mobile-deployment/$DatabaseName"
New-Item -ItemType Directory -Path $renderDirectory -Force | Out-Null
$scriptNames = @('00_create_database.sql', '01_schema.sql', '02_views.sql', '03_triggers.sql',
    '04_procedures.sql', '14_appointment_registration.sql', '05_seed_reference.sql',
    '15_yangshan_reference.sql', '09_permissions.sql', '06_smoke_tests.sql')
if ($IncludeDemoData) { $scriptNames += @('10_seed_demo.sql', '12_refresh_sample_labels.sql', '13_seed_port_scenarios.sql') }
$rendered = foreach ($name in $scriptNames) {
    $source = Join-Path $PSScriptRoot $name
    $sql = [IO.File]::ReadAllText($source)
    $sql = $sql.Replace('[PortQueue]', "[$DatabaseName]").Replace("N'PortQueue'", "N'$DatabaseName'")
    # SQL sources remain unchanged. Reject unexpected DB-context directives in rendered copies.
    $contexts = [regex]::Matches($sql, '(?im)^\s*USE\s+\[([^\]]+)\]\s*;')
    foreach ($context in $contexts) {
        if ($context.Groups[1].Value -notin @('master', $DatabaseName)) { throw "Unsafe SQL database context in $name" }
    }
    if ($sql.Contains('[PortQueue]') -or $sql.Contains("N'PortQueue'")) { throw "Untranslated database identifier in $name" }
    $output = Join-Path $renderDirectory $name
    [IO.File]::WriteAllText($output, $sql, [Text.UTF8Encoding]::new($false))
    [pscustomobject]@{ name = $name; renderedPath = $output }
}
if ($PlanOnly) {
    [pscustomobject]@{ database = $DatabaseName; executed = $false; scripts = @($rendered) } | ConvertTo-Json -Depth 4
    return
}

$sqlCommand = Get-Command sqlcmd -ErrorAction SilentlyContinue
$mobileSqlCmd = if ($sqlCommand) { $sqlCommand.Source } else { 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' }
if (-not (Test-Path -LiteralPath $mobileSqlCmd)) { throw 'Install SQL Server sqlcmd first.' }
$mobileServer = '(localdb)\PortQueueDev'
function Invoke-MobileQuery([string]$query, [string]$database = 'master') {
    $result = & $mobileSqlCmd -C -S $mobileServer -E -b -I -d $database -h -1 -W -l 8 -t 60 -Q $query 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "SQL verification failed: $result" }
    return $result.Trim()
}
function Invoke-MobileScript([string]$path, [string]$database = $DatabaseName) {
    $output = & $mobileSqlCmd -C -S $mobileServer -E -b -f 65001 -d $database -l 8 -t 120 -i $path 2>&1 | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText("$path.log", $output, [Text.UTF8Encoding]::new($false))
    if ($code -ne 0) { throw "Deployment stopped at $path. Existing data is preserved; inspect $path.log and the partial mobile DB before retrying." }
    Write-Host "PASS $([IO.Path]::GetFileName($path))"
}
$exists = Invoke-MobileQuery "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$DatabaseName') IS NULL THEN 'MISSING' ELSE 'EXISTS' END;"
if ($exists -eq 'MISSING') {
    # 00 creates the DB, so it must initially run on master.
    $createPath = ($rendered | Where-Object name -eq '00_create_database.sql').renderedPath
    Invoke-MobileScript $createPath 'master'
    foreach ($item in $rendered | Where-Object name -ne '00_create_database.sql') { Invoke-MobileScript $item.renderedPath }
    Write-Host "Created isolated database $DatabaseName. Core scripts were not modified."
} elseif ($exists -eq 'EXISTS') {
    # Re-run verifies only: never reset/reseed/replace procedures in a populated DB.
    Invoke-MobileScript (($rendered | Where-Object name -eq '06_smoke_tests.sql').renderedPath)
    Write-Host "Preserved existing $DatabaseName; schema and data were not redeployed."
} else { throw "Unexpected database existence response: $exists" }
Write-Host 'Next: run tools/start-mobile-api.ps1 with -SeedDemoAccounts once, then without it.'
