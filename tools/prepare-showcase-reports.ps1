[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$manifest=Get-Content (Join-Path $root 'artifacts/showcase/manifest.json') -Raw | ConvertFrom-Json
if ($manifest.database -ne 'PortQueueMobileShowcase') { throw 'Unexpected showcase manifest.' }
$base='http://127.0.0.1:5182'
function Login([string]$username) {
    $ws=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $initial=Invoke-RestMethod "$base/api/session" -WebSession $ws
    $options=Invoke-RestMethod "$base/api/session/options" -WebSession $ws
    if (-not $options.defaultPassword) { throw 'Loopback Development demo account configuration required.' }
    $body=@{username=$username;password=$options.defaultPassword}|ConvertTo-Json
    $result=Invoke-RestMethod "$base/api/session/login" -Method Post -WebSession $ws -Headers @{'X-CSRF-TOKEN'=$initial.csrfToken} -ContentType 'application/json' -Body $body
    @{session=$ws;token=$result.csrfToken}
}
$field=Login 'pq.recorder'; $dispatcher=Login 'pq.dispatcher'
$list=Invoke-RestMethod "$base/api/appointments?keyword=PQ202611-1003&page=1&pageSize=20" -WebSession $field.session
$task=@($list.items | Where-Object appointmentNo -eq 'PQ202611-1003')
if ($task.Count -ne 1) { throw 'Main task is missing or ambiguous.' }
$id=$task[0].appointmentId
$overview=Invoke-RestMethod "$base/api/mobile/appointments/$id/overview" -WebSession $field.session
if ($overview.hasArrivalFact) { throw 'This legacy scenario targets an arrived visit. Do not generate ETA history here; prepare the final clean story after development freeze.' }
$eta=([datetimeoffset]::Parse($overview.publishedPlan.plannedBerthingAt)).AddHours(-1)
if ($eta -le [datetimeoffset]::UtcNow) { throw 'Frozen scenario date has expired; do not rewrite existing history.' }
$scenes=@(
    @{request='161cfc76-5a44-4ede-a030-bcf5ce3a0451';description='接应班组已核对抵港进度，预计提前一小时抵达候泊水域，请调度确认。';decision='ACCEPTED';comment='已接收预计到港时间，仍按当前发布的靠泊窗口执行；抵达后请补充现场反馈。'},
    @{request='161cfc76-5a44-4ede-a030-bcf5ce3a0452';description='船方初步反馈抵港时间可能变化，尚待代理确认具体时刻。';decision='REJECTED';comment='目前时间依据尚未确认，请与代理核实预计到港时刻后重新报送。'},
    @{request='161cfc76-5a44-4ede-a030-bcf5ce3a0453';description='接应人员已到达联络点，已再次核实预计到港时间，请核对后续接应安排。';decision=$null;comment=$null}
)
$records=foreach ($s in $scenes) {
    $found=$null
    try { $found=Invoke-RestMethod "$base/api/mobile/reports/by-request/$($s.request)" -WebSession $field.session }
    catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw } }
    if (-not $found) {
        $body=@{requestId=$s.request;appointmentId=[string]$id;proposedEta=$eta.ToString('yyyy-MM-ddTHH:mm:sszzz');description=$s.description}|ConvertTo-Json
        $found=Invoke-RestMethod "$base/api/mobile/reports/eta" -Method Post -WebSession $field.session -Headers @{'X-CSRF-TOKEN'=$field.token} -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    }
    if ($s.decision -and $found.statusCode -eq 'SUBMITTED') {
        $current=Invoke-RestMethod "$base/api/mobile/reports/$($found.reportId)" -WebSession $dispatcher.session
        $body=@{decision=$s.decision;comment=$s.comment;expectedVersion=$current.version}|ConvertTo-Json
        $null=Invoke-RestMethod "$base/api/mobile/reports/$($found.reportId)/decision" -Method Post -WebSession $dispatcher.session -Headers @{'X-CSRF-TOKEN'=$dispatcher.token} -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    }
    $read=Invoke-RestMethod "$base/api/mobile/reports/$($found.reportId)" -WebSession $field.session
    if ($read.statusCode -ne $(if($s.decision){$s.decision}else{'SUBMITTED'})) { throw 'Report status verification failed.' }
    [ordered]@{reportId=$read.reportId;appointmentNo=$read.appointmentNo;type=$read.reportType;status=$read.statusCode;description=$read.description;comment=$read.processComment}
}
$history=Invoke-RestMethod "$base/api/mobile/appointments/$id/changes?page=1&pageSize=20" -WebSession $field.session
if ($history.totalCount -ne 2) { throw 'Expected two published plans.' }
[ordered]@{checkedAt=(Get-Date).ToString('o');records=@($records);publishedVersions=$history.totalCount;photo='Pending real capture; do not substitute a placeholder'}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 (Join-Path $root 'artifacts/showcase/report-manifest.json')
$records | ForEach-Object { Write-Output "$($_.appointmentNo) / $($_.type) / $($_.status)" }
