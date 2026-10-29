# PortQueue LocalDB bootstrap script
#
# Prepares (localdb)\PortQueueDev and restores PortQueue only when it is missing.
# Existing databases are checked only; this script never deletes or rebuilds them.
# Run from the repository root:
#   powershell -ExecutionPolicy Bypass -File .\database\bootstrap-localdb.ps1

[CmdletBinding()]
param(
    [string]$InstanceName = 'PortQueueDev',
    [string]$DatabaseName = 'PortQueue'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Resolve-Executable([string]$name, [string[]]$fallbacks) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($fallback in $fallbacks) {
        if (Test-Path $fallback) {
            return $fallback
        }
    }

    throw "Executable not found: $name. Install SQL Server LocalDB and command-line tools."
}

$localDb = Resolve-Executable 'SqlLocalDB' @(
    'C:\Program Files\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe',
    'C:\Program Files\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe'
)
$sqlCmd = Resolve-Executable 'sqlcmd' @(
    'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE',
    'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE'
)

Write-Host "LocalDB tool: $localDb"
Write-Host "sqlcmd tool: $sqlCmd"

$instanceList = @(& $localDb info)
if (-not ($instanceList -contains $InstanceName)) {
    Write-Host "LocalDB instance $InstanceName is missing; creating SQL Server 2019 LocalDB instance."
    & $localDb create $InstanceName '15.0'
}

$instanceInfo = (& $localDb info $InstanceName) -join "`n"
if ($instanceInfo -notmatch 'State:\s+Running') {
    Write-Host "Starting LocalDB instance $InstanceName."
    & $localDb start $InstanceName
}

$databaseQuery = "SET NOCOUNT ON; IF DB_ID(N'$DatabaseName') IS NULL SELECT 'MISSING' AS database_state ELSE SELECT 'EXISTS' AS database_state;"
$databaseState = (& $sqlCmd -C -S "(localdb)\$InstanceName" -E -d master -h -1 -W -Q $databaseQuery -b | Out-String).Trim()

if ($databaseState -eq 'MISSING') {
    Write-Host "Database $DatabaseName is missing; restoring scripts 00 through 05."
    $scripts = @(
        '00_create_database.sql',
        '01_schema.sql',
        '02_views.sql',
        '03_triggers.sql',
        '04_procedures.sql',
        '05_seed_reference.sql'
    )

    foreach ($script in $scripts) {
        $scriptPath = Join-Path $PSScriptRoot $script
        Write-Host "Running $script"
        & $sqlCmd -C -S "(localdb)\$InstanceName" -E -d master -b -i $scriptPath
    }
} elseif ($databaseState -eq 'EXISTS') {
    Write-Host "Database $DatabaseName already exists; preserving data and skipping schema restore."
} else {
    throw "Cannot determine database state for $DatabaseName. sqlcmd returned: $databaseState"
}

$verificationQuery = "SELECT DB_NAME() AS current_database, (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo')) AS table_count, (SELECT COUNT(*) FROM sys.views WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN (N'v_port_event', N'v_anchorage_queue', N'v_current_berth_plan')) AS view_count;"

Write-Host "Verifying database objects:"
& $sqlCmd -C -S "(localdb)\$InstanceName" -E -d $DatabaseName -b -Q $verificationQuery
Write-Host "Bootstrap complete. Connect in VS Code: (localdb)\$InstanceName / $DatabaseName"
