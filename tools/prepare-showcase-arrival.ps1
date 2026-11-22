[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$manifest=Get-Content (Join-Path $root 'artifacts/showcase/manifest.json') -Raw | ConvertFrom-Json
if ($manifest.database -ne 'PortQueueMobileShowcase') { throw 'Showcase manifest required.' }
$base='http://127.0.0.1:5182'
$ws=New-Object Microsoft.PowerShell.Commands.WebRequestSession
$s=Invoke-RestMethod "$base/api/session" -WebSession $ws
$options=Invoke-RestMethod "$base/api/session/options" -WebSession $ws
if (-not $options.defaultPassword) {throw 'Loopback Development accounts required.'}
$body=@{username='pq.recorder';password=$options.defaultPassword}|ConvertTo-Json
$auth=Invoke-RestMethod "$base/api/session/login" -Method Post -WebSession $ws -Headers @{'X-CSRF-TOKEN'=$s.csrfToken} -ContentType application/json -Body $body
$list=Invoke-RestMethod "$base/api/appointments?keyword=PQ202611-1002&page=1&pageSize=20" -WebSession $ws
$matches=@($list.items|Where-Object appointmentNo -eq 'PQ202611-1002')
if($matches.Count -ne 1){throw 'Arrival task missing or ambiguous.'}
$id=$matches[0].appointmentId
$before=Invoke-RestMethod "$base/api/mobile/appointments/$id/overview" -WebSession $ws
if($before.queue -or -not $before.publishedPlan -or -not $before.canSubmitReport){throw 'Expected approved direct-arrival task without anchorage event.'}
$request='161cfc76-5a44-4ede-a030-bcf5ce3a0461'
$record=$null
try {$record=Invoke-RestMethod "$base/api/mobile/reports/by-request/$request" -WebSession $ws}
catch {if ([int]$_.Exception.Response.StatusCode -ne 404){throw}}
if(-not $record){
    $eta=([datetimeoffset]::Parse($before.publishedPlan.plannedBerthingAt)).AddHours(-1)
    if($eta -le [datetimeoffset]::UtcNow){throw 'Scenario has expired; do not rewrite existing history.'}
    $body=@{requestId=$request;appointmentId=[string]$id;proposedEta=$eta.ToString('o');description='港外航段受风浪影响，经联络核实预计到港时间已调整，请调度核对与当前靠泊窗口的衔接。'}|ConvertTo-Json
    $record=Invoke-RestMethod "$base/api/mobile/reports/eta" -Method Post -WebSession $ws -Headers @{'X-CSRF-TOKEN'=$auth.csrfToken} -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
}
$after=Invoke-RestMethod "$base/api/mobile/appointments/$id/overview" -WebSession $ws
if($record.appointmentNo -ne 'PQ202611-1002' -or $record.reportType -ne 'ETA'){throw 'Unexpected saved report identity.'}
if(($before.publishedPlan|ConvertTo-Json -Compress) -ne ($after.publishedPlan|ConvertTo-Json -Compress) -or $after.queue){throw 'ETA must not change formal berth plan or actual anchorage facts.'}
[ordered]@{scene='Direct arrival ETA feedback';appointmentNo=$record.appointmentNo;shipName=$record.shipName;reportId=$record.reportId;status=$record.statusCode;eta=$record.proposedEta;berthing=$after.publishedPlan.plannedBerthingAt;checkedAt=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/showcase/arrival-scene.json') -Encoding utf8
Write-Output "$($record.appointmentNo): $($record.shipName), ETA report $($record.reportId), $($record.statusCode); published plan unchanged."
