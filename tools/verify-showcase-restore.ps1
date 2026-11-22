[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^\d{14}$')][string]$BackupId)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dir=Join-Path $root "artifacts/showcase/backups/$BackupId"
$proof=Get-Content (Join-Path $dir 'verification.json') -Raw | ConvertFrom-Json
if($proof.restored -ne "PortQueueMobileRestore_$BackupId"){throw 'Unexpected restore target'}
if(@(Get-NetTCPConnection -LocalPort 5183 -State Listen -ErrorAction SilentlyContinue).Count){throw '5183 is occupied; no process will be stopped.'}
$names=@('ASPNETCORE_ENVIRONMENT','ASPNETCORE_URLS','ConnectionStrings__PortQueue','Mobile__Enabled','Mobile__PhotoDirectory','DemoAccounts__Enabled')
$before=@{}; foreach($n in $names){$before[$n]=[Environment]::GetEnvironmentVariable($n,'Process')}
$process=$null
try {
    $env:ASPNETCORE_ENVIRONMENT='Development'
    $env:ASPNETCORE_URLS='http://127.0.0.1:5183'
    $env:ConnectionStrings__PortQueue="Server=(localdb)\PortQueueDev;Database=$($proof.restored);Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5"
    $env:Mobile__Enabled='true'; $env:Mobile__PhotoDirectory=Join-Path $dir 'photos'; $env:DemoAccounts__Enabled='true'
    $process=Start-Process (Join-Path $root 'src/PortQueue.Api/bin/Release/net8.0/PortQueue.Api.exe') -WorkingDirectory (Join-Path $root 'src/PortQueue.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dir 'restore-api.stdout.log') -RedirectStandardError (Join-Path $dir 'restore-api.stderr.log')
    @{pid=$process.Id;port=5183;purpose='temporary isolated restore verification'}|ConvertTo-Json|Set-Content (Join-Path $dir 'restore-process.json')
    $base='http://127.0.0.1:5183'; $ready=$false
    foreach($attempt in 1..20){
        try {$options=Invoke-RestMethod "$base/api/session/options" -TimeoutSec 2; $ready=$true; break} catch {Start-Sleep -Milliseconds 500}
    }
    if(-not $ready){throw 'Restore API not ready'}
    $ws=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $s=Invoke-RestMethod "$base/api/session" -WebSession $ws
    $body=@{username='pq.recorder';password=$options.defaultPassword}|ConvertTo-Json
    $null=Invoke-RestMethod "$base/api/session/login" -Method Post -WebSession $ws -Headers @{'X-CSRF-TOKEN'=$s.csrfToken} -ContentType 'application/json' -Body $body
    $reports=Invoke-RestMethod "$base/api/mobile/reports?scope=mine&page=1&pageSize=50" -WebSession $ws
    # This verifier targets the controlled showcase fixture owned by pq.recorder.
    $expectedCounts = $proof.counts -split '\|'
    if ($reports.totalCount -ne [int]$expectedCounts[1]) {throw 'Restored report count does not match the showcase backup'}
    if ($reports.totalCount -gt 50) {throw 'Restore fixture exceeds this verifier page; add pagination before accepting'}
    $checked=0
    foreach($r in $reports.items | Where-Object reportType -eq 'PHOTO') {
        $detail=Invoke-RestMethod "$base/api/mobile/reports/$($r.reportId)" -WebSession $ws
        $output=Join-Path $dir "restored-photo-$($detail.photo.photoId).jpg"
        Invoke-WebRequest "$base/api/mobile/photos/$($detail.photo.photoId)" -WebSession $ws -OutFile $output
        if ((Get-FileHash $output).Hash -notin @($proof.photos.sha256)) {throw 'Restored photo bytes do not match the paired backup'}
        $checked++
    }
    if($checked -lt 1){throw 'No real photo in restore; cannot claim photo recovery'}
    if ($checked -ne [int]$expectedCounts[2]) {throw 'Restored photo count does not match the showcase backup'}
    @{restored=$proof.restored;reportCount=$reports.totalCount;photoCount=$checked;result='PASS authenticated restored report details, counts and photo bytes';checkedAt=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $dir 'api-verification.json')
    Write-Output "PASS restored authenticated reports and $checked photo(s)."
} finally {
    if($process -and -not $process.HasExited){Stop-Process -Id $process.Id}
    foreach($n in $names){[Environment]::SetEnvironmentVariable($n,$before[$n],'Process')}
}
