Set-StrictMode -Version Latest

function Get-CollectionErrorCode {
    param(
        [System.Management.Automation.ErrorRecord]$Record,
        [string]$Fallback
    )

    if ($Record.CategoryInfo.Category -eq 'PermissionDenied' -or
        $Record.Exception -is [System.UnauthorizedAccessException]) {
        return 'accessDenied'
    }

    return $Fallback
}

function Get-SpoolerStatus {
    try {
        $service = Get-Service -Name 'Spooler' -ErrorAction Stop
        return [ordered]@{
            status = 'ok'
            name = 'Spooler'
            state = $service.Status.ToString()
            errorCode = $null
        }
    }
    catch {
        $code = Get-CollectionErrorCode -Record $_ -Fallback 'serviceQueryFailed'
        if ($_.FullyQualifiedErrorId -like 'NoServiceFoundForGivenName,*') {
            $code = 'serviceNotFound'
        }

        return [ordered]@{
            status = 'unavailable'
            name = 'Spooler'
            state = $null
            errorCode = $code
        }
    }
}

function Get-PrintEventSummary {
    param(
        [datetime]$StartTime,
        [datetime]$EndTime,
        [int]$MaxEvents
    )

    $logName = 'Microsoft-Windows-PrintService/Admin'
    $result = [ordered]@{
        status = 'ok'
        log = $logName
        sampledEvents = 0
        maxEvents = $MaxEvents
        truncated = $false
        groups = @()
        errorCode = $null
    }

    try {
        $log = Get-WinEvent -ListLog $logName -ErrorAction Stop
    }
    catch {
        $result.status = 'unavailable'
        $result.errorCode = Get-CollectionErrorCode -Record $_ -Fallback 'logUnavailable'
        return $result
    }

    if (-not $log.IsEnabled) {
        $result.status = 'unavailable'
        $result.errorCode = 'logDisabled'
        return $result
    }

    try {
        $filter = @{
            LogName = $logName
            StartTime = $StartTime
            EndTime = $EndTime
        }
        # Read one extra record to detect truncation without scanning the full log.
        $events = @(Get-WinEvent -FilterHashtable $filter -MaxEvents ($MaxEvents + 1) -ErrorAction Stop)
    }
    catch {
        if (($_.FullyQualifiedErrorId -split ',', 2)[0] -eq 'NoMatchingEventsFound') {
            $result.status = 'noEvents'
        }
        else {
            $result.status = 'unavailable'
            $result.errorCode = Get-CollectionErrorCode -Record $_ -Fallback 'eventQueryFailed'
        }
        return $result
    }

    if ($events.Count -eq 0) {
        $result.status = 'noEvents'
        return $result
    }

    $result.truncated = $events.Count -gt $MaxEvents
    $sample = @($events | Select-Object -First $MaxEvents)
    $result.sampledEvents = $sample.Count
    $result.groups = @(
        foreach ($group in ($sample | Group-Object -Property ProviderName, Id)) {
            # Get-WinEvent returns newest records first.
            $latest = $group.Group[0]
            $latestTime = $null
            if ($null -ne $latest.TimeCreated) {
                $latestTime = $latest.TimeCreated.ToUniversalTime().ToString('o')
            }

            [pscustomobject][ordered]@{
                provider = $latest.ProviderName
                eventId = $latest.Id
                count = $group.Count
                lastSeenUtc = $latestTime
            }
        }
    )
    return $result
}

function Get-SysHealthPrintAudit {
    <#
    .SYNOPSIS
    Collects the local Spooler state and summarizes the Windows PrintService Admin log.
    #>
    [CmdletBinding()]
    param(
        [ValidateRange(1, 168)]
        [int]$Hours = 24,

        [ValidateRange(1, 1000)]
        [int]$MaxEvents = 200
    )

    if (-not $IsWindows) {
        throw 'SysHealth Print audit requires Windows.'
    }

    $collectedAt = [datetime]::UtcNow
    $since = $collectedAt.AddHours(-$Hours)
    $spooler = Get-SpoolerStatus
    $events = Get-PrintEventSummary -StartTime $since -EndTime $collectedAt -MaxEvents $MaxEvents
    $unavailableCount = @(@($spooler, $events) | Where-Object { $_.status -eq 'unavailable' }).Count
    $status = @('complete', 'partial', 'unavailable')[$unavailableCount]

    [pscustomobject][ordered]@{
        schemaVersion = 1
        collectedAtUtc = $collectedAt.ToString('o')
        sinceUtc = $since.ToString('o')
        check = 'print'
        status = $status
        spooler = $spooler
        events = $events
    }
}

Export-ModuleMember -Function Get-SysHealthPrintAudit
