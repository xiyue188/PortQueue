[CmdletBinding()]
param([ValidateSet('PortQueueMobileDev')][string]$DatabaseName='PortQueueMobileDev',[switch]$IncludeScenarios,[switch]$PlanOnly)
$ErrorActionPreference='Stop'
$m2SqlCmd=(Get-Command sqlcmd -ErrorAction SilentlyContinue).Source
if(-not $m2SqlCmd){$m2SqlCmd='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'}
$scripts=@('01_schema.sql','02_permissions.sql')
if($IncludeScenarios){$scripts+='03_seed_mobile_scenarios.sql'}
$scripts+='04_smoke_tests.sql'
if($PlanOnly){[pscustomobject]@{database=$DatabaseName;executed=$false;scripts=$scripts}|ConvertTo-Json;return}
foreach($name in $scripts){
 & $m2SqlCmd -C -S '(localdb)\PortQueueDev' -E -b -I -f 65001 -d $DatabaseName -l 8 -t 120 -i (Join-Path $PSScriptRoot "mobile/$name")
 if($LASTEXITCODE -ne 0){throw "M2 deployment stopped at $name; existing database is preserved."}
}
