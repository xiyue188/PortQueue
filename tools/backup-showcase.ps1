[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (@(Get-NetTCPConnection -LocalPort 5182 -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Stop only the identified showcase API before taking a paired database/photo backup.' }
$stamp=Get-Date -Format 'yyyyMMddHHmmss'
$dir=Join-Path $root "artifacts/showcase/backups/$stamp"
New-Item -ItemType Directory $dir -ErrorAction Stop | Out-Null
$backup=Join-Path $dir 'database.bak'
$sqlcmd='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
function Query([string]$sql,[string]$db='master') {
    $output=& $sqlcmd -S '(localdb)\PortQueueDev' -E -C -b -I -d $db -h -1 -W -s '|' -Q $sql -t 120
    if ($LASTEXITCODE -ne 0) { throw 'Backup/restore SQL failed. Preserve the evidence directory.' }
    return $output
}
$null=Query "BACKUP DATABASE [PortQueueMobileShowcase] TO DISK=N'$($backup.Replace("'","''"))' WITH COPY_ONLY,CHECKSUM;"
$null=Query "RESTORE VERIFYONLY FROM DISK=N'$($backup.Replace("'","''"))' WITH CHECKSUM;"
$photoSource=Join-Path $root 'artifacts/showcase/photos'
$photoBackup=Join-Path $dir 'photos'
New-Item -ItemType Directory $photoBackup | Out-Null
if(Test-Path $photoSource) { Get-ChildItem $photoSource -File -Recurse | ForEach-Object {
    $relative=$_.FullName.Substring($photoSource.Length).TrimStart('\','/')
    $destination=Join-Path $photoBackup $relative
    New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination
} }
$files=@(Query 'SET NOCOUNT ON; SELECT name,type_desc FROM sys.database_files;' 'PortQueueMobileShowcase')
$restore="PortQueueMobileRestore_$stamp"
$moves=@(); $index=0
foreach($line in $files) {
    if(-not $line.Trim()) {continue}
    $parts=$line.Trim().Split('|'); if($parts.Count -ne 2){throw 'Unexpected file metadata'}
    $index++
    $path=Join-Path $dir "restored-$index.$(if($parts[1] -eq 'LOG'){'ldf'}else{'mdf'})"
    $moves+="MOVE N'$($parts[0].Replace("'","''"))' TO N'$($path.Replace("'","''"))'"
}
$null=Query "IF DB_ID(N'$restore') IS NOT NULL THROW 52590,'Refuse overwrite',1; RESTORE DATABASE [$restore] FROM DISK=N'$($backup.Replace("'","''"))' WITH CHECKSUM,$($moves -join ',');"
$counts='SET NOCOUNT ON; SELECT (SELECT COUNT(*) FROM dbo.arrival_appointment),(SELECT COUNT(*) FROM dbo.mobile_report),(SELECT COUNT(*) FROM dbo.report_photo),(SELECT COUNT(*) FROM dbo.berth_plan_revision);'
$before=(Query $counts 'PortQueueMobileShowcase') -join ''
$after=(Query $counts $restore) -join ''
if($before -ne $after){throw 'Restored row counts differ'}
$violations=Query 'DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS, NO_INFOMSGS;' $restore
if($violations){throw 'Restored constraints failed'}
$photos=@(Get-ChildItem $photoBackup -File -Recurse | ForEach-Object { @{name=$_.FullName.Substring($photoBackup.Length);sha256=(Get-FileHash $_.FullName).Hash} })
[ordered]@{source='PortQueueMobileShowcase';restored=$restore;backupSha256=(Get-FileHash $backup).Hash;counts=$after.Trim();photos=$photos;checkedAt=(Get-Date).ToString('o');result='Database restored and constraints/counts checked. Photo API recovery requires a real photo if photos is empty.'}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $dir 'verification.json')
Write-Output "Backup and isolated restore verified: $dir / $restore"
