# PortQueue LocalDB bootstrap script
#
# Prepares (localdb)\PortQueueDev and restores PortQueue only when it is missing.
# Existing databases keep their schema/data; only missing reference rows are inserted.
# This is environment preparation, not an automatic business-schema upgrade.
# Run from the repository root:
#   powershell -ExecutionPolicy Bypass -File .\database\bootstrap-localdb.ps1

[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_-]{0,49}$')]
    [string]$InstanceName = 'PortQueueDev',
    [ValidateSet('PortQueue')]
    [string]$DatabaseName = 'PortQueue'
)

$ErrorActionPreference = 'Stop'
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

function Invoke-Checked([string]$executable, [string[]]$arguments, [string]$description) {
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$description failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Query([string]$query, [string]$database = 'master') {
    $output = & $sqlCmd -C -S "(localdb)\$InstanceName" -E -b -d $database -h -1 -W -Q $query 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "SQL query failed in database ${database}:`n$output"
    }

    return $output.Trim()
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
if ($LASTEXITCODE -ne 0) {
    throw "Unable to list LocalDB instances."
}
if (-not ($instanceList -contains $InstanceName)) {
    Write-Host "LocalDB instance $InstanceName is missing; creating SQL Server 2019 LocalDB instance."
    Invoke-Checked $localDb @('create', $InstanceName, '15.0') "Creating LocalDB instance $InstanceName"
}

$instanceInfo = (& $localDb info $InstanceName) -join "`n"
if ($instanceInfo -notmatch 'State:\s+Running') {
    Write-Host "Starting LocalDB instance $InstanceName."
    Invoke-Checked $localDb @('start', $InstanceName) "Starting LocalDB instance $InstanceName"
}

$databaseQuery = "SET NOCOUNT ON; IF DB_ID(N'$DatabaseName') IS NULL SELECT 'MISSING' AS database_state ELSE SELECT 'EXISTS' AS database_state;"
$databaseStateOutput = Invoke-Query $databaseQuery
$databaseState = (($databaseStateOutput -split "`r?`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -eq 'MISSING' -or $_ -eq 'EXISTS' } | Select-Object -Last 1)

if ([string]::IsNullOrWhiteSpace($databaseState)) {
    throw "Cannot determine database state for $DatabaseName. Query output: $databaseStateOutput"
}

if ($databaseState -eq 'MISSING') {
    $dataFile = Join-Path $env:USERPROFILE "$DatabaseName.mdf"
    $logFile = Join-Path $env:USERPROFILE "${DatabaseName}_log.ldf"
    $hasDataFile = Test-Path -LiteralPath $dataFile
    $hasLogFile = Test-Path -LiteralPath $logFile

    if ($hasDataFile -or $hasLogFile) {
        if (-not ($hasDataFile -and $hasLogFile)) {
            throw "Database $DatabaseName is not registered, and only one database file exists. Review $dataFile and $logFile before retrying. No file was changed."
        }

        Write-Host "Database $DatabaseName is missing from the catalog, but existing MDF/LDF files were found. Attempting a safe attach."
        $dataLiteral = $dataFile.Replace("'", "''")
        $logLiteral = $logFile.Replace("'", "''")
        $attachQuery = "CREATE DATABASE [$DatabaseName] ON (FILENAME = N'$dataLiteral'), (FILENAME = N'$logLiteral') FOR ATTACH;"
        Invoke-Checked $sqlCmd @('-C', '-S', "(localdb)\$InstanceName", '-E', '-d', 'master', '-b', '-Q', $attachQuery) "Attaching existing database files"
    } else {
        Write-Host "Database $DatabaseName is missing and no old files were found; restoring scripts 00 through 05 and 09."
        $scripts = @(
            '00_create_database.sql',
            '01_schema.sql',
            '02_views.sql',
            '03_triggers.sql',
            '04_procedures.sql',
            '05_seed_reference.sql',
            '09_permissions.sql'
        )

        foreach ($script in $scripts) {
            $scriptPath = Join-Path $PSScriptRoot $script
            Write-Host "Running $script"
            Invoke-Checked $sqlCmd @('-C', '-S', "(localdb)\$InstanceName", '-E', '-d', 'master', '-b', '-f', '65001', '-i', $scriptPath) "Running $script"
        }
    }
} elseif ($databaseState -eq 'EXISTS') {
    Write-Host "Database $DatabaseName already exists; preserving data and skipping schema restore."
}

# Insert missing reference rows after a new restore, safe attach or existing-database check.
# Never overwrite stored procedures or load demo facts as a side effect of starting the environment.
$seedPath = Join-Path $PSScriptRoot '05_seed_reference.sql'
Invoke-Checked $sqlCmd @('-C', '-S', "(localdb)\$InstanceName", '-E', '-d', 'master', '-b', '-f', '65001', '-i', $seedPath) "Running 05_seed_reference.sql"

$verificationQuery = @"
SET NOCOUNT ON;
DECLARE @table_count int = (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND is_ms_shipped = 0);
DECLARE @view_count int = (SELECT COUNT(*) FROM sys.views WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN (N'v_port_event', N'v_anchorage_queue', N'v_current_berth_plan'));
DECLARE @procedure_count int = (SELECT COUNT(*) FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'dbo') AND is_ms_shipped = 0);
DECLARE @trigger_count int = (SELECT COUNT(*) FROM sys.triggers WHERE parent_class = 1 AND is_ms_shipped = 0);
DECLARE @index_count int = (SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id WHERE t.schema_id=SCHEMA_ID(N'dbo') AND t.is_ms_shipped=0 AND i.index_id>0);
IF @table_count <> 21 OR @view_count <> 3 OR @procedure_count <> 7 OR @trigger_count <> 8
    THROW 53001, 'PortQueue is not at stage 1. Apply 03, 04 and 09 explicitly; see database/README.md.', 1;
SELECT DB_NAME() AS current_database, @table_count AS table_count, @view_count AS view_count, @procedure_count AS procedure_count, @trigger_count AS trigger_count, @index_count AS index_count;
SELECT 'role' AS reference_table, COUNT(*) AS row_count FROM dbo.role
UNION ALL SELECT 'dictionary_item', COUNT(*) FROM dbo.dictionary_item
UNION ALL SELECT 'port', COUNT(*) FROM dbo.port
UNION ALL SELECT 'terminal', COUNT(*) FROM dbo.terminal
UNION ALL SELECT 'anchorage', COUNT(*) FROM dbo.anchorage
UNION ALL SELECT 'berth', COUNT(*) FROM dbo.berth
UNION ALL SELECT 'berth_ship_type', COUNT(*) FROM dbo.berth_ship_type;
"@

Write-Host "Verifying database objects:"
Invoke-Checked $sqlCmd @('-C', '-S', "(localdb)\$InstanceName", '-E', '-d', $DatabaseName, '-b', '-Q', $verificationQuery) "Verifying database objects"
Write-Host "Bootstrap complete. Connect in VS Code: (localdb)\$InstanceName / $DatabaseName"
