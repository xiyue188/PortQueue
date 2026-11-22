[CmdletBinding()]
param([ValidateSet('PortQueueMobileDev','PortQueueMobileShowcase')][string]$DatabaseName='PortQueueMobileDev',[switch]$PlanOnly)
$ErrorActionPreference='Stop'
$scripts=@('05_eta.sql','04_smoke_tests.sql','06_eta_smoke_tests.sql')
if($PlanOnly){[pscustomobject]@{database=$DatabaseName;executed=$false;scripts=$scripts}|ConvertTo-Json;return}
$exe=(Get-Command sqlcmd -ErrorAction SilentlyContinue).Source
if(-not $exe){$exe='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'}
foreach($name in $scripts){
 & $exe -C -S '(localdb)\PortQueueDev' -E -b -I -f 65001 -d $DatabaseName -l 8 -t 120 -i (Join-Path $PSScriptRoot "mobile/$name")
 if($LASTEXITCODE -ne 0){throw "Lifecycle deployment stopped at $name; database preserved."}
}
