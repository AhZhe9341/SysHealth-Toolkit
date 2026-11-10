#Requires -Version 7.4
<#
.SYNOPSIS
Runs a read-only Windows print audit.
.EXAMPLE
./syshealth.ps1 audit -Check Print -Hours 24 -Format Json -OutputPath ./print-audit.json
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('Audit')]
    [string]$Command = 'Audit',

    [ValidateSet('Print')]
    [string]$Check = 'Print',

    [ValidateRange(1, 168)]
    [int]$Hours = 24,

    [ValidateRange(1, 1000)]
    [int]$MaxEvents = 200,

    [ValidateSet('Text', 'Json')]
    [string]$Format = 'Text',

    [ValidateNotNullOrEmpty()]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

try {
    Import-Module (Join-Path $PSScriptRoot 'src/SysHealth.psd1') -ErrorAction Stop
    switch ("$Command/$Check") {
        'Audit/Print' {
            $report = Get-SysHealthPrintAudit -Hours $Hours -MaxEvents $MaxEvents
        }
    }

    if ($Format -eq 'Json') {
        $content = $report | ConvertTo-Json -Depth 6
    }
    else {
        $content = @(
            "SysHealth print audit: $($report.status)"
            "Window: $($report.sinceUtc) to $($report.collectedAtUtc)"
            "Spooler: $($report.spooler.state ?? $report.spooler.errorCode)"
            "PrintService/Admin: $($report.events.status), $($report.events.sampledEvents) sampled event(s)"
            if ($report.events.errorCode) { "Log error: $($report.events.errorCode)" }
            if ($report.events.truncated) { 'Event counts reflect a capped sample.' }
            foreach ($group in $report.events.groups) {
                "  $($group.provider) #$($group.eventId): $($group.count); last seen $($group.lastSeenUtc)"
            }
        ) -join [Environment]::NewLine
    }

    if ($PSBoundParameters.ContainsKey('OutputPath')) {
        $provider = $null
        $drive = $null
        $target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
            $OutputPath, [ref]$provider, [ref]$drive
        )
        if ($provider.Name -ne 'FileSystem') {
            throw 'OutputPath must be a file-system path.'
        }

        # Publish the completed report in one move and preserve any existing file.
        $temporary = Join-Path ([IO.Path]::GetDirectoryName($target)) ('.syshealth-' + [guid]::NewGuid() + '.tmp')
        try {
            [IO.File]::WriteAllText($temporary, $content + [Environment]::NewLine)
            [IO.File]::Move($temporary, $target)
        }
        finally {
            if ([IO.File]::Exists($temporary)) {
                [IO.File]::Delete($temporary)
            }
        }
    }
    else {
        $content
    }

    switch ($report.status) {
        'complete' { exit 0 }
        'partial' { exit 2 }
        default { exit 1 }
    }
}
catch {
    [Console]::Error.WriteLine("SysHealth: $($_.Exception.Message)")
    exit 1
}
