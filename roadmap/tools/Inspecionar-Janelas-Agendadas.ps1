#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Root = 'C:\alexbonemberger\on-my-own\chat-automation\projects\chat-automation-test_id_0000',
    [ValidateRange(1, 120)][int]$Minutes = 20
)
$ErrorActionPreference = 'Stop'
$since = (Get-Date).AddMinutes(-$Minutes)
$folder = Join-Path $Root 'evidencias\diagnostico'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$file = Join-Path $folder ('janelas-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')

$taskRows = @()
foreach ($task in @(Get-ScheduledTask -ErrorAction SilentlyContinue)) {
    try {
        $details = Get-ScheduledTaskInfo -TaskName $task.TaskName -TaskPath $task.TaskPath -ErrorAction Stop
        $actions = @($task.Actions | ForEach-Object { $_.Execute })
        $relevant = ($task.TaskName + ' ' + $task.TaskPath + ' ' + ($actions -join ' ')) -match
            '(?i)mainalto|chess|chat.?automation|gitps7|uia|qwen|autonomous|pwsh|powershell|cmd\.exe|dotnet\.exe'
        $recent = ($details.LastRunTime -gt $since -and
                   $details.LastRunTime -le (Get-Date).AddMinutes(1))
        if (-not ($relevant -or $recent)) { continue }
        $taskRows += [pscustomobject]@{
            TaskPath = $task.TaskPath
            TaskName = $task.TaskName
            State = [string]$task.State
            Executables = $actions
            LastRunTime = $details.LastRunTime
            NextRunTime = $details.NextRunTime
            LastTaskResult = $details.LastTaskResult
        }
    } catch {
        # Unavailable task metadata is not a reason to stop read-only audit.
    }
}

$processes = @()
try {
    $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop |
        Where-Object { $_.Name -in @('cmd.exe','pwsh.exe','powershell.exe','dotnet.exe','conhost.exe','WindowsTerminal.exe') } |
        Select-Object Name,ProcessId,ParentProcessId,CreationDate,ExecutablePath)
} catch {}

$events = @()
try {
    $rawEvents = @(Get-WinEvent -FilterHashtable @{
        LogName = 'Microsoft-Windows-TaskScheduler/Operational'
        StartTime = $since
        Id = 100,102,200,201
    } -ErrorAction Stop | Select-Object -First 300)
    foreach ($event in $rawEvents) {
        $taskName = ''
        try {
            [xml]$xml = $event.ToXml()
            $named = @($xml.Event.EventData.Data | Where-Object { $_.Name -eq 'TaskName' })
            if ($named.Count -gt 0) { $taskName = [string]$named[0].'#text' }
        } catch {}
        if ($taskName -eq '' -or $taskName -match
            '(?i)mainalto|chess|chat.?automation|gitps7|uia|qwen|autonomous') {
            $events += [pscustomobject]@{
                TimeCreated = $event.TimeCreated
                EventId = $event.Id
                TaskName = $taskName
            }
        }
    }
} catch {}

$report = [pscustomobject]@{
    Schema = 'chat-automation.window-launch-audit/1'
    CapturedAt = [DateTimeOffset]::Now.ToString('O')
    LookbackMinutes = $Minutes
    NoChangesMade = $true
    Tasks = @($taskRows)
    Processes = @($processes)
    SchedulerEvents = @($events)
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file -Encoding utf8
Write-Host "AUDITORIA_SOMENTE_LEITURA=PASS"
Write-Host "ARQUIVO=$file"
Write-Host "TAREFAS=$($taskRows.Count) PROCESSOS=$($processes.Count) EVENTOS=$($events.Count)"
$taskRows | Sort-Object LastRunTime -Descending | Select-Object -First 12 |
    Format-Table TaskName,State,LastRunTime,NextRunTime,LastTaskResult -AutoSize
