[CmdletBinding()]
param([ValidateSet('PortQueueMobileDev')][string]$DatabaseName='PortQueueMobileDev',[switch]$PlanOnly)
$ErrorActionPreference='Stop'
$scripts=@('05_eta.sql','04_smoke_tests.sql','06_eta_smoke_tests.sql')
if($PlanOnly){[pscustomobject]@{database=$DatabaseName;executed=$false;scripts=$scripts}|ConvertTo-Json;return}
$m3SqlCmd=(Get-Command sqlcmd -ErrorAction SilentlyContinue).Source
if(-not $m3SqlCmd){$m3SqlCmd='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'}
foreach($name in $scripts){
 & $m3SqlCmd -C -S '(localdb)\PortQueueDev' -E -b -I -f 65001 -d $DatabaseName -l 8 -t 120 -i (Join-Path $PSScriptRoot "mobile/$name")
 if($LASTEXITCODE -ne 0){throw "M3 deployment stopped at $name; existing database is preserved."}
}
