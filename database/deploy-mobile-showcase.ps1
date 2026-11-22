[CmdletBinding()]
param([datetime]$SceneDate = '2026-11-22', [switch]$PlanOnly,
    [ValidateSet('mobile/02_permissions.sql','mobile/08_showcase_scenarios.sql','mobile/04_smoke_tests.sql')][string]$ResumeFrom)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$db = 'PortQueueMobileShowcase'
$out = Join-Path $root 'artifacts/showcase/sql'
$manifest = Join-Path $root 'artifacts/showcase/manifest.json'
New-Item -ItemType Directory -Force $out | Out-Null
$day = $SceneDate.Date
$journal=Join-Path $root 'artifacts/showcase/initializing.json'
if ($day -gt (Get-Date).Date) { throw 'Actual events must not be initialized in the future.' }
$shift = ($day - [datetime]'2026-11-08').Days
$scripts = @('00_create_database.sql','01_schema.sql','02_views.sql','03_triggers.sql','04_procedures.sql','14_appointment_registration.sql','05_seed_reference.sql','15_yangshan_reference.sql','09_permissions.sql','06_smoke_tests.sql','13_seed_port_scenarios.sql','mobile/01_schema.sql','mobile/02_permissions.sql','mobile/05_eta.sql','mobile/07_plan_history.sql','mobile/08_showcase_scenarios.sql','mobile/04_smoke_tests.sql','mobile/06_eta_smoke_tests.sql')
$paths = foreach ($name in $scripts) {
    $sql = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $name)).Replace('[PortQueue]',"[$db]").Replace("N'PortQueue'","N'$db'")
    if ($name -eq '13_seed_port_scenarios.sql') {
        # Shift only literal source timestamps in a NEW instance. IDs/MMSIs stay stable for the map.
        $sql = [regex]::Replace($sql,"'2026-\d{2}-\d{2}(?:T\d{2}:\d{2}:\d{2})?'", {
            param($m)
            $v = [datetime]::Parse($m.Value.Trim("'"),[Globalization.CultureInfo]::InvariantCulture)
            "'" + $v.AddDays($shift).ToString('yyyy-MM-ddTHH:mm:ss') + "'"
        })
    }
    foreach ($m in [regex]::Matches($sql,'(?im)^\s*USE\s+\[([^\]]+)\]')) {
        if ($m.Groups[1].Value -notin @('master',$db)) { throw "Unexpected context in $name" }
    }
    $file = Join-Path $out ($name.Replace('/','-'))
    [IO.File]::WriteAllText($file,$sql,[Text.UTF8Encoding]::new($false))
    $file
}
if ($PlanOnly) { Write-Output "Rendered $($paths.Count) scripts for $db, Shanghai scene date $($day.ToString('yyyy-MM-dd')); no SQL executed."; return }
if ($ResumeFrom) {
    if (-not (Test-Path $journal)) { throw 'Resume requires the original initialization journal.' }
    $original=Get-Content $journal -Raw|ConvertFrom-Json
    if($original.sceneDate -ne $day.ToString('yyyy-MM-dd')) { throw 'Do not shift dates while resuming an existing instance.' }
}
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
$exists = (& $sqlcmd -S '(localdb)\PortQueueDev' -E -C -b -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name=N'$db'") -join ''
if ($LASTEXITCODE -ne 0) { throw 'Database preflight failed' }
if ($exists.Trim() -ne '0' -and -not $ResumeFrom) { throw 'Showcase database already exists. Refusing to reset, reseed or shift existing history. Verify its manifest instead.' }
if ($exists.Trim() -eq '0') {
    @{sceneDate=$day.ToString('yyyy-MM-dd');database=$db;startedAt=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content -Encoding UTF8 $journal
}
if ($ResumeFrom) {
    if ($exists.Trim() -ne '1' -or (Test-Path $manifest)) { throw 'Resume is only for an inspected incomplete initialization, never a delivered scene.' }
    $count = (& $sqlcmd -S '(localdb)\PortQueueDev' -E -C -b -d $db -h -1 -W -Q 'SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.mobile_report') -join ''
    if ($LASTEXITCODE -ne 0 -or $count.Trim() -ne '0') { throw 'Cannot resume once report input exists.' }
    $index=[array]::IndexOf($scripts,$ResumeFrom)
    $paths=$paths[$index..($paths.Count-1)]
}
foreach ($path in $paths) {
    $target = if ([IO.Path]::GetFileName($path) -eq '00_create_database.sql') { 'master' } else { $db }
    & $sqlcmd -S '(localdb)\PortQueueDev' -E -C -I -b -f 65001 -d $target -i $path -t 120 *> "$path.log"
    if ($LASTEXITCODE -ne 0) { throw "Stopped at $path; preserve the partial instance and inspect the log. Never auto-drop." }
    Write-Output "PASS $([IO.Path]::GetFileName($path))"
}
[ordered]@{database=$db;sceneDate=$day.ToString('yyyy-MM-dd');timezone='Asia/Shanghai';apiPort=5182;photoDirectory='artifacts/showcase/photos';sourceCommit=(& git -C $root rev-parse HEAD);createdAt=(Get-Date).ToString('o');scenarios=@{waiting='PQ202611-1009';assigned='PQ202611-1003';direct='PQ202611-1002';history='PQ202611-1006';cancelled='PQ202611-1005'};reports='Awaiting API input';synthetic=$true} | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $manifest
