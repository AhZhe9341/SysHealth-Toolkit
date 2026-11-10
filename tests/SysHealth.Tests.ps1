BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../src/SysHealth.psd1') -Force
}

Describe 'Get-SysHealthPrintAudit' {
    BeforeEach {
        Mock Get-Service -ModuleName SysHealth { [pscustomobject]@{ Status = 'Running' } }
        Mock Get-WinEvent -ModuleName SysHealth -ParameterFilter { $ListLog } { [pscustomobject]@{ IsEnabled = $true } }
        Mock Get-WinEvent -ModuleName SysHealth -ParameterFilter { $FilterHashtable } {
            [pscustomobject]@{
                ProviderName = 'Microsoft-Windows-PrintService'
                Id = 372
                TimeCreated = [datetime]'2026-01-01T00:00:00Z'
            }
        }
    }

    It 'reports a running Spooler and groups recent print events' {
        $report = Get-SysHealthPrintAudit

        $report.status | Should -Be 'complete'
        $report.spooler.state | Should -Be 'Running'
        $report.events.groups.Count | Should -Be 1
        $report.events.groups[0].eventId | Should -Be 372
        Should -Invoke Get-WinEvent -ModuleName SysHealth -Exactly -Times 1 -ParameterFilter {
            $FilterHashtable.LogName -eq 'Microsoft-Windows-PrintService/Admin' -and
            ($FilterHashtable.EndTime - $FilterHashtable.StartTime).TotalHours -eq 24 -and
            $MaxEvents -eq 201
        }
    }

    It 'reports a stopped Spooler as an observed service state' {
        Mock Get-Service -ModuleName SysHealth { [pscustomobject]@{ Status = 'Stopped' } }

        $report = Get-SysHealthPrintAudit

        $report.spooler.state | Should -Be 'Stopped'
        $report.status | Should -Be 'complete'
    }

    It 'treats no matching events as a successful collection' {
        Mock Get-WinEvent -ModuleName SysHealth -ParameterFilter { $FilterHashtable } {
            Write-Error 'No events match the query' -ErrorId 'NoMatchingEventsFound' -ErrorAction Stop
        }

        $report = Get-SysHealthPrintAudit

        $report.events.status | Should -Be 'noEvents'
        $report.events.groups.Count | Should -Be 0
        $report.events.errorCode | Should -BeNullOrEmpty
        $report.status | Should -Be 'complete'
    }

    It 'retains service state when the event query is denied' {
        Mock Get-WinEvent -ModuleName SysHealth -ParameterFilter { $FilterHashtable } {
            throw [System.UnauthorizedAccessException]::new('Access denied')
        }

        $report = Get-SysHealthPrintAudit

        $report.status | Should -Be 'partial'
        $report.spooler.state | Should -Be 'Running'
        $report.events.status | Should -Be 'unavailable'
        $report.events.errorCode | Should -Be 'accessDenied'
    }

    It 'reports a disabled event log without querying it' {
        Mock Get-WinEvent -ModuleName SysHealth -ParameterFilter { $ListLog } { [pscustomobject]@{ IsEnabled = $false } }

        $report = Get-SysHealthPrintAudit

        $report.events.errorCode | Should -Be 'logDisabled'
        Should -Invoke Get-WinEvent -ModuleName SysHealth -Exactly -Times 0 -ParameterFilter { $FilterHashtable }
    }

    It 'marks the report unavailable when neither source can be read' {
        Mock Get-Service -ModuleName SysHealth { throw 'Service query failed' }
        Mock Get-WinEvent -ModuleName SysHealth -ParameterFilter { $ListLog } { throw 'Log unavailable' }

        $report = Get-SysHealthPrintAudit

        $report.status | Should -Be 'unavailable'
        $report.spooler.errorCode | Should -Be 'serviceQueryFailed'
        $report.events.errorCode | Should -Be 'logUnavailable'
    }

    It 'counts only the requested sample when there are additional records' {
        Mock Get-WinEvent -ModuleName SysHealth -ParameterFilter { $FilterHashtable } {
            foreach ($hour in @(3, 2, 1)) {
                [pscustomobject]@{
                    ProviderName = 'Microsoft-Windows-PrintService'
                    Id = 372
                    TimeCreated = [datetime]::new(2026, 1, 1, $hour, 0, 0, [DateTimeKind]::Utc)
                }
            }
        }

        $report = Get-SysHealthPrintAudit -MaxEvents 2

        $report.events.truncated | Should -BeTrue
        $report.events.sampledEvents | Should -Be 2
        $report.events.groups[0].count | Should -Be 2
        $report.events.groups[0].lastSeenUtc | Should -Be '2026-01-01T03:00:00.0000000Z'
    }
}

Describe 'SysHealth CLI on Windows' {
    BeforeAll {
        $script:cli = Join-Path $PSScriptRoot '../syshealth.ps1'
        $script:powerShell = Join-Path $PSHOME 'pwsh.exe'
    }

    It 'writes machine-readable stdout with a matching exit code' {
        $json = & $script:powerShell -NoProfile -File $script:cli audit -Check Print -Format Json -MaxEvents 5
        $exitCode = $LASTEXITCODE
        $report = ($json -join "`n") | ConvertFrom-Json

        $report.schemaVersion | Should -Be 1
        $report.spooler.name | Should -Be 'Spooler'
        $report.events.maxEvents | Should -Be 5
        $exitCode | Should -Be (@{ complete = 0; partial = 2; unavailable = 1 }[$report.status])
    }

    It 'writes a JSON file and preserves it if the same destination is used again' {
        $path = Join-Path $TestDrive 'print report.json'
        $null = & $script:powerShell -NoProfile -File $script:cli audit -Format Json -OutputPath $path
        $original = Get-Content -LiteralPath $path -Raw
        ($original | ConvertFrom-Json).schemaVersion | Should -Be 1

        $null = & $script:powerShell -NoProfile -File $script:cli audit -Format Json -OutputPath $path 2>&1
        $LASTEXITCODE | Should -Be 1
        (Get-Content -LiteralPath $path -Raw) | Should -BeExactly $original
        @(Get-ChildItem -LiteralPath $TestDrive -Filter '.syshealth-*.tmp' -Force).Count | Should -Be 0
    }
}
