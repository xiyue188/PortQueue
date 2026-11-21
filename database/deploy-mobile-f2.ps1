[CmdletBinding()]
param([ValidateSet('PortQueueMobileDev')][string]$DatabaseName='PortQueueMobileDev',[switch]$PlanOnly)
$ErrorActionPreference='Stop'
if($PlanOnly){[pscustomobject]@{database=$DatabaseName;executed=$false;scripts=@('07_plan_history.sql')}|ConvertTo-Json;return}
$historySqlCmd=(Get-Command sqlcmd -ErrorAction SilentlyContinue).Source
if(-not $historySqlCmd){$historySqlCmd='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'}
& $historySqlCmd -C -S '(localdb)\PortQueueDev' -E -b -I -f 65001 -d $DatabaseName -l 8 -t 120 -i (Join-Path $PSScriptRoot 'mobile/07_plan_history.sql')
if($LASTEXITCODE -ne 0){throw 'F2 deployment failed; no data is reset.'}
