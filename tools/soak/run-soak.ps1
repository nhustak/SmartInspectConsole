[CmdletBinding()]
param(
    [int]$DurationSeconds = 7200,
    [int]$SampleIntervalSeconds = 60,
    [int]$DrainSeconds = 30,
    [int]$LocalQueueDrainThreshold = 50,
    [int]$RelayBufferDrainThreshold = 25,
    [string]$OutputRoot = "",
    [string]$RelayBaseUrl = "http://127.0.0.1:5109",
    [switch]$SkipTcp,
    [switch]$SkipPipe,
    [switch]$SkipWebSocket,
    [switch]$SkipRelay,
    [switch]$SkipMixed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\soak"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runName = "soak-$timestamp"
$runRoot = Join-Path $OutputRoot $runName
$runStartedAtUtc = [DateTime]::UtcNow.ToString("O")
$null = New-Item -ItemType Directory -Path $runRoot -Force

$localContextUri = "http://127.0.0.1:42331/api/local/v1/context/live"
$relayHealthPath = "/api/v1/health"
$relayStatusPath = "/api/v1/status"
$nodeDriverPath = Join-Path $repoRoot "tools\soak\traffic-driver.mjs"
$loadTesterProject = Join-Path $repoRoot "src\SmartInspectConsole.LoadTester\SmartInspectConsole.LoadTester.csproj"
$relayProject = Join-Path $repoRoot "src\SmartInspectConsole.Relay\SmartInspectConsole.Relay.csproj"

$global:SoakFailed = $false
$global:FailureReasons = [System.Collections.Generic.List[string]]::new()

function Write-EventLine {
    param(
        [string]$Leg,
        [string]$Phase,
        [hashtable]$Fields = @{}
    )

    $record = [ordered]@{
        timestampUtc = [DateTime]::UtcNow.ToString("O")
        leg = $Leg
        phase = $Phase
    }

    foreach ($entry in $Fields.GetEnumerator()) {
        $record[$entry.Key] = $entry.Value
    }

    $line = $record | ConvertTo-Json -Compress -Depth 8
    Add-Content -Path (Join-Path $runRoot "events.ndjson") -Value $line
    Write-Host ($line | ConvertFrom-Json | ConvertTo-Json -Compress)
}

function Save-JsonLine {
    param(
        [string]$Path,
        [object]$Value
    )

    Add-Content -Path $Path -Value ($Value | ConvertTo-Json -Compress -Depth 8)
}

function Invoke-JsonRequest {
    param(
        [string]$Uri
    )

    try {
        return Invoke-RestMethod -Uri $Uri -TimeoutSec 15
    }
    catch {
        return $null
    }
}

function Get-LocalSnapshot {
    param(
        [string]$Leg,
        [string]$Phase
    )

    $snapshot = Invoke-JsonRequest -Uri $localContextUri
    $record = [ordered]@{
        timestampUtc = [DateTime]::UtcNow.ToString("O")
        leg = $Leg
        phase = $Phase
        source = "local"
        success = $null -ne $snapshot
    }

    if ($snapshot) {
        $record.queueDepth = $snapshot.queueDepth
        $record.totalReceived = $snapshot.totalReceived
        $record.totalDroppedByRetention = $snapshot.totalDroppedByRetention
        $record.totalRetained = $snapshot.totalRetained
        $record.logEntryCount = $snapshot.logEntryCount
        $record.watchCount = $snapshot.watchCount
        $record.processFlowCount = $snapshot.processFlowCount
        $record.connectedApplicationCount = $snapshot.connectedApplicationCount
    }

    return [pscustomobject]$record
}

function Get-RelaySnapshot {
    param(
        [string]$Leg,
        [string]$Phase
    )

    $health = Invoke-JsonRequest -Uri ($RelayBaseUrl + $relayHealthPath)
    $status = Invoke-JsonRequest -Uri ($RelayBaseUrl + $relayStatusPath)
    $record = [ordered]@{
        timestampUtc = [DateTime]::UtcNow.ToString("O")
        leg = $Leg
        phase = $Phase
        source = "relay"
        healthSuccess = $null -ne $health
        statusSuccess = $null -ne $status
    }

    if ($health) {
        $record.healthStatus = $health.status
        $record.consoleConnected = $health.consoleConnected
    }

    if ($status) {
        $record.connected = $status.connected
        $record.messagesForwarded = $status.messagesForwarded
        $record.messagesBuffered = $status.messagesBuffered
        $record.lastForwardedAt = $status.lastForwardedAt
    }

    return [pscustomobject]$record
}

function Start-LoggedProcess {
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$StdOutPath,
        [string]$StdErrPath,
        [hashtable]$Environment = @{}
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    $psi.Arguments = (($Arguments | ForEach-Object { Convert-ToCommandLineArgument -Value $_ }) -join " ")

    $psi.WorkingDirectory = $WorkingDirectory
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    foreach ($entry in $Environment.GetEnumerator()) {
        $psi.Environment[$entry.Key] = [string]$entry.Value
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    [void]$process.Start()

    $stdoutWriter = [System.IO.StreamWriter]::new($StdOutPath, $false, [System.Text.Encoding]::UTF8)
    $stderrWriter = [System.IO.StreamWriter]::new($StdErrPath, $false, [System.Text.Encoding]::UTF8)

    $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutWriter.BaseStream)
    $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrWriter.BaseStream)

    return [pscustomobject]@{
        Name = $Name
        Process = $process
        StdOutTask = $stdoutTask
        StdErrTask = $stderrTask
        StdOutWriter = $stdoutWriter
        StdErrWriter = $stderrWriter
        StdOutPath = $StdOutPath
        StdErrPath = $StdErrPath
    }
}

function Convert-ToCommandLineArgument {
    param(
        [string]$Value
    )

    if ($null -eq $Value) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    $escaped = $Value -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

function Stop-LoggedProcess {
    param(
        [pscustomobject]$LoggedProcess
    )

    if (-not $LoggedProcess) {
        return
    }

    if (-not $LoggedProcess.Process.HasExited) {
        $LoggedProcess.Process.Kill()
        $LoggedProcess.Process.WaitForExit()
    }

    try {
        $LoggedProcess.StdOutTask.Wait()
        $LoggedProcess.StdErrTask.Wait()
    }
    finally {
        $LoggedProcess.StdOutWriter.Dispose()
        $LoggedProcess.StdErrWriter.Dispose()
        $LoggedProcess.Process.Dispose()
    }
}

function Wait-ForRelayReady {
    param(
        [pscustomobject]$RelayProcess,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($RelayProcess.Process.HasExited) {
            throw "Relay process exited before becoming ready."
        }

        $health = Invoke-JsonRequest -Uri ($RelayBaseUrl + $relayHealthPath)
        if ($health) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Relay did not become ready within ${TimeoutSeconds}s."
}

function New-LoadTesterSpec {
    param(
        [string]$Name,
        [string]$Transport,
        [int]$Clients,
        [int]$MessagesPerSecond,
        [int]$PayloadBytes,
        [int]$DurationSecondsValue,
        [string]$AppPrefix
    )

    $arguments = @(
        "run",
        "--project", $loadTesterProject,
        "--",
        "--transport", $Transport,
        "--clients", [string]$Clients,
        "--messages-per-second", [string]$MessagesPerSecond,
        "--payload-bytes", [string]$PayloadBytes,
        "--duration-seconds", [string]$DurationSecondsValue,
        "--connect-timeout-seconds", "15",
        "--operation-timeout-seconds", "15",
        "--watches-every", "100",
        "--flows-every", "200",
        "--app-prefix", $AppPrefix,
        "--session-prefix", $Name
    )

    if ($Transport -eq "pipe") {
        $arguments += @("--pipe", "smartinspect")
    }

    return [pscustomobject]@{
        Name = $Name
        FilePath = "dotnet"
        Arguments = $arguments
    }
}

function New-NodeDriverSpec {
    param(
        [string]$Name,
        [string]$Transport,
        [string]$Url,
        [int]$Sessions,
        [int]$MessagesPerSecond,
        [int]$PayloadBytes,
        [int]$DurationSecondsValue,
        [string]$AppPrefix
    )

    return [pscustomobject]@{
        Name = $Name
        FilePath = "node"
        Arguments = @(
            "--no-warnings=MODULE_TYPELESS_PACKAGE_JSON",
            $nodeDriverPath,
            "--transport", $Transport,
            "--url", $Url,
            "--sessions", [string]$Sessions,
            "--messages-per-second", [string]$MessagesPerSecond,
            "--payload-bytes", [string]$PayloadBytes,
            "--duration-seconds", [string]$DurationSecondsValue,
            "--timeout-seconds", "30",
            "--watches-every", "100",
            "--flows-every", "200",
            "--app-prefix", $AppPrefix,
            "--session-prefix", $Name
        )
    }
}

function Invoke-Leg {
    param(
        [string]$Name,
        [object[]]$ProcessSpecs,
        [switch]$RequiresRelay
    )

    $legRoot = Join-Path $runRoot $Name
    $null = New-Item -ItemType Directory -Path $legRoot -Force
    $snapshotPath = Join-Path $legRoot "snapshots.ndjson"
    $summaryPath = Join-Path $legRoot "summary.json"
    $loggedProcesses = [System.Collections.Generic.List[object]]::new()
    $relayProcess = $null
    $localFailures = 0
    $relayFailures = 0
    $relayDisconnectSamples = 0
    $legFailed = $false
    $failureReason = $null

    try {
        Write-EventLine -Leg $Name -Phase "start" -Fields @{
            durationSeconds = $DurationSeconds
            sampleIntervalSeconds = $SampleIntervalSeconds
            processCount = $ProcessSpecs.Count
            requiresRelay = $RequiresRelay.IsPresent
        }

        $preSnapshot = Get-LocalSnapshot -Leg $Name -Phase "pre"
        Save-JsonLine -Path $snapshotPath -Value $preSnapshot
        Write-EventLine -Leg $Name -Phase "pre" -Fields @{
            queueDepth = $preSnapshot.queueDepth
            totalReceived = $preSnapshot.totalReceived
            totalDroppedByRetention = $preSnapshot.totalDroppedByRetention
        }

        if ($RequiresRelay) {
            $relayProcess = Start-LoggedProcess -Name "relay" -FilePath "dotnet" -Arguments @("run", "--project", $relayProject, "--urls", $RelayBaseUrl) -WorkingDirectory $repoRoot -StdOutPath (Join-Path $legRoot "relay.stdout.log") -StdErrPath (Join-Path $legRoot "relay.stderr.log")
            Wait-ForRelayReady -RelayProcess $relayProcess
            $relayStartSnapshot = Get-RelaySnapshot -Leg $Name -Phase "relay-start"
            Save-JsonLine -Path $snapshotPath -Value $relayStartSnapshot
            Write-EventLine -Leg $Name -Phase "relay-start" -Fields @{
                connected = $relayStartSnapshot.connected
                messagesBuffered = $relayStartSnapshot.messagesBuffered
            }
        }

        foreach ($spec in $ProcessSpecs) {
            $stdoutPath = Join-Path $legRoot ($spec.Name + ".stdout.log")
            $stderrPath = Join-Path $legRoot ($spec.Name + ".stderr.log")
            $loggedProcess = Start-LoggedProcess -Name $spec.Name -FilePath $spec.FilePath -Arguments $spec.Arguments -WorkingDirectory $repoRoot -StdOutPath $stdoutPath -StdErrPath $stderrPath
            $loggedProcesses.Add($loggedProcess)

            Write-EventLine -Leg $Name -Phase "process-start" -Fields @{
                process = $spec.Name
                filePath = $spec.FilePath
                arguments = ($spec.Arguments -join " ")
            }
        }

        $deadline = (Get-Date).AddSeconds($DurationSeconds)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds $SampleIntervalSeconds

            $sample = Get-LocalSnapshot -Leg $Name -Phase "sample"
            Save-JsonLine -Path $snapshotPath -Value $sample
            if (-not $sample.success) {
                $localFailures += 1
                if ($localFailures -ge 3) {
                    throw "Local API stopped responding for three consecutive samples."
                }
            }
            else {
                $localFailures = 0
            }

            $eventFields = @{
                queueDepth = $sample.queueDepth
                totalReceived = $sample.totalReceived
                totalDroppedByRetention = $sample.totalDroppedByRetention
            }

            if ($RequiresRelay) {
                $relaySample = Get-RelaySnapshot -Leg $Name -Phase "sample"
                Save-JsonLine -Path $snapshotPath -Value $relaySample

                if (-not $relaySample.healthSuccess -or -not $relaySample.statusSuccess) {
                    $relayFailures += 1
                }
                else {
                    $relayFailures = 0
                }

                if ($relaySample.connected -ne $true) {
                    $relayDisconnectSamples += 1
                }
                else {
                    $relayDisconnectSamples = 0
                }

                $eventFields.relayConnected = $relaySample.connected
                $eventFields.relayBuffered = $relaySample.messagesBuffered
                $eventFields.relayForwarded = $relaySample.messagesForwarded

                if ($relayFailures -ge 3) {
                    throw "Relay health/status endpoints failed for three consecutive samples."
                }

                if ($relayDisconnectSamples -ge 3) {
                    throw "Relay stayed disconnected for three consecutive samples."
                }
            }

            foreach ($loggedProcess in $loggedProcesses) {
                if ($loggedProcess.Process.HasExited -and $loggedProcess.Process.ExitCode -ne 0) {
                    throw "$($loggedProcess.Name) exited with code $($loggedProcess.Process.ExitCode)."
                }
            }

            Write-EventLine -Leg $Name -Phase "sample" -Fields $eventFields
        }

        foreach ($loggedProcess in $loggedProcesses) {
            if (-not $loggedProcess.Process.WaitForExit(($DrainSeconds + 30) * 1000)) {
                throw "$($loggedProcess.Name) did not exit after the leg completed."
            }

            if ($loggedProcess.Process.ExitCode -ne 0) {
                throw "$($loggedProcess.Name) exited with code $($loggedProcess.Process.ExitCode)."
            }
        }

        $postSnapshot = Get-LocalSnapshot -Leg $Name -Phase "post"
        Save-JsonLine -Path $snapshotPath -Value $postSnapshot
        Write-EventLine -Leg $Name -Phase "post" -Fields @{
            queueDepth = $postSnapshot.queueDepth
            totalReceived = $postSnapshot.totalReceived
            totalDroppedByRetention = $postSnapshot.totalDroppedByRetention
        }

        Start-Sleep -Seconds $DrainSeconds
        $drainSnapshot = Get-LocalSnapshot -Leg $Name -Phase "drain"
        Save-JsonLine -Path $snapshotPath -Value $drainSnapshot

        if (-not $drainSnapshot.success) {
            throw "Local API did not respond during drain validation."
        }

        if ($drainSnapshot.queueDepth -gt $LocalQueueDrainThreshold) {
            throw "Queue depth remained above threshold after drain window. queueDepth=$($drainSnapshot.queueDepth), threshold=$LocalQueueDrainThreshold"
        }

        $summary = [ordered]@{
            leg = $Name
            success = $true
            durationSeconds = $DurationSeconds
            localQueueDrainThreshold = $LocalQueueDrainThreshold
            drainSeconds = $DrainSeconds
            finalQueueDepth = $drainSnapshot.queueDepth
            finalTotalReceived = $drainSnapshot.totalReceived
            finalTotalDroppedByRetention = $drainSnapshot.totalDroppedByRetention
        }

        if ($RequiresRelay) {
            $relayDrainSnapshot = Get-RelaySnapshot -Leg $Name -Phase "drain"
            Save-JsonLine -Path $snapshotPath -Value $relayDrainSnapshot

            if (-not $relayDrainSnapshot.statusSuccess) {
                throw "Relay status endpoint did not respond during drain validation."
            }

            if ($relayDrainSnapshot.messagesBuffered -gt $RelayBufferDrainThreshold) {
                throw "Relay buffer remained above threshold after drain window. messagesBuffered=$($relayDrainSnapshot.messagesBuffered), threshold=$RelayBufferDrainThreshold"
            }

            $summary.relayBufferDrainThreshold = $RelayBufferDrainThreshold
            $summary.finalRelayBuffered = $relayDrainSnapshot.messagesBuffered
            $summary.finalRelayConnected = $relayDrainSnapshot.connected
        }

        $summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath
        Write-EventLine -Leg $Name -Phase "complete" -Fields $summary
    }
    catch {
        $legFailed = $true
        $failureReason = $_.Exception.Message
        $global:SoakFailed = $true
        $global:FailureReasons.Add("${Name}: $failureReason")
        Write-EventLine -Leg $Name -Phase "failure" -Fields @{ reason = $failureReason }
    }
    finally {
        foreach ($loggedProcess in $loggedProcesses) {
            Stop-LoggedProcess -LoggedProcess $loggedProcess
        }

        if ($relayProcess) {
            Stop-LoggedProcess -LoggedProcess $relayProcess
        }
    }

    if ($legFailed) {
        throw $failureReason
    }
}

$legs = [System.Collections.Generic.List[object]]::new()
if (-not $SkipTcp) {
    $legs.Add([pscustomobject]@{
        Name = "leg1-tcp"
        RequiresRelay = $false
        Processes = @(
            (New-LoadTesterSpec -Name "tcp" -Transport "tcp" -Clients 8 -MessagesPerSecond 1000 -PayloadBytes 1024 -DurationSecondsValue $DurationSeconds -AppPrefix "SoakTcp")
        )
    })
}

if (-not $SkipPipe) {
    $legs.Add([pscustomobject]@{
        Name = "leg2-pipe"
        RequiresRelay = $false
        Processes = @(
            (New-LoadTesterSpec -Name "pipe" -Transport "pipe" -Clients 4 -MessagesPerSecond 750 -PayloadBytes 1024 -DurationSecondsValue $DurationSeconds -AppPrefix "SoakPipe")
        )
    })
}

if (-not $SkipWebSocket) {
    $legs.Add([pscustomobject]@{
        Name = "leg3-websocket"
        RequiresRelay = $false
        Processes = @(
            (New-NodeDriverSpec -Name "websocket" -Transport "websocket" -Url "ws://127.0.0.1:4229" -Sessions 4 -MessagesPerSecond 500 -PayloadBytes 1024 -DurationSecondsValue $DurationSeconds -AppPrefix "SoakWebSocket")
        )
    })
}

if (-not $SkipRelay) {
    $legs.Add([pscustomobject]@{
        Name = "leg4-relay"
        RequiresRelay = $true
        Processes = @(
            (New-NodeDriverSpec -Name "relay-http" -Transport "http" -Url ($RelayBaseUrl + "/api/v1") -Sessions 4 -MessagesPerSecond 500 -PayloadBytes 1024 -DurationSecondsValue $DurationSeconds -AppPrefix "SoakRelay")
        )
    })
}

if (-not $SkipMixed) {
    $legs.Add([pscustomobject]@{
        Name = "leg5-mixed"
        RequiresRelay = $true
        Processes = @(
            (New-LoadTesterSpec -Name "tcp-mixed" -Transport "tcp" -Clients 6 -MessagesPerSecond 500 -PayloadBytes 1024 -DurationSecondsValue $DurationSeconds -AppPrefix "SoakMixedTcp"),
            (New-LoadTesterSpec -Name "pipe-mixed" -Transport "pipe" -Clients 4 -MessagesPerSecond 350 -PayloadBytes 1024 -DurationSecondsValue $DurationSeconds -AppPrefix "SoakMixedPipe"),
            (New-NodeDriverSpec -Name "websocket-mixed" -Transport "websocket" -Url "ws://127.0.0.1:4229" -Sessions 4 -MessagesPerSecond 250 -PayloadBytes 1024 -DurationSecondsValue $DurationSeconds -AppPrefix "SoakMixedWebSocket"),
            (New-NodeDriverSpec -Name "relay-mixed" -Transport "http" -Url ($RelayBaseUrl + "/api/v1") -Sessions 4 -MessagesPerSecond 250 -PayloadBytes 1024 -DurationSecondsValue $DurationSeconds -AppPrefix "SoakMixedRelay")
        )
    })
}

foreach ($leg in $legs) {
    try {
        Invoke-Leg -Name $leg.Name -ProcessSpecs $leg.Processes -RequiresRelay:([bool]$leg.RequiresRelay)
    }
    catch {
        continue
    }
}

$runSummary = [ordered]@{
    runName = $runName
    startedAtUtc = $runStartedAtUtc
    outputRoot = $runRoot
    success = -not $global:SoakFailed
    failureReasons = $global:FailureReasons
    durationSecondsPerLeg = $DurationSeconds
    sampleIntervalSeconds = $SampleIntervalSeconds
    drainSeconds = $DrainSeconds
    legs = $legs.Name
}

$runSummary | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $runRoot "run-summary.json")
Write-Host ("Run output: " + $runRoot)

if ($global:SoakFailed) {
    Write-Error ("Soak run failed: " + ($global:FailureReasons -join "; "))
}
