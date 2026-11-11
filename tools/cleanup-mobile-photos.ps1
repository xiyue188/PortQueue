# Dry run by default. Files newer than 24 hours and all referenced files are preserved.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PhotoDirectory,[ValidateRange(24,8760)][int]$MinimumAgeHours=24,[switch]$Apply)
$ErrorActionPreference='Stop'
$photoRoot=[IO.Path]::GetFullPath($PhotoDirectory)
$expectedRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/mobile-private-photos'))
if(-not [string]::Equals($photoRoot.TrimEnd('\','/'),$expectedRoot.TrimEnd('\','/'),[StringComparison]::OrdinalIgnoreCase)){throw 'Photo directory must match the isolated mobile API artifacts/mobile-private-photos directory.'}
if(-not(Test-Path -LiteralPath $photoRoot -PathType Container)){throw 'Photo directory does not exist.'}
$mobileSqlCmd=(Get-Command sqlcmd -ErrorAction SilentlyContinue).Source
if(-not $mobileSqlCmd){$mobileSqlCmd='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'}
$keys=& $mobileSqlCmd -C -S '(localdb)\PortQueueDev' -E -b -I -d PortQueueMobileDev -h -1 -W -l 8 -t 30 -Q 'SET NOCOUNT ON; SELECT storage_key FROM dbo.report_photo;'
if($LASTEXITCODE -ne 0){throw 'Database verification failed; no file deleted.'}
$known=@{};foreach($key in $keys){$known[$key.Trim()]=$true}
$cutoff=[DateTime]::UtcNow.AddHours(-$MinimumAgeHours)
foreach($file in Get-ChildItem -LiteralPath $photoRoot -File){
 if($file.Name -notmatch '^[0-9a-f]{32}\.(jpg|tmp)$' -or $file.LastWriteTimeUtc -ge $cutoff -or $known.ContainsKey($file.Name)){continue}
 if($Apply){Remove-Item -LiteralPath $file.FullName}
 [pscustomobject]@{file=$file.Name;deleted=[bool]$Apply}
}
