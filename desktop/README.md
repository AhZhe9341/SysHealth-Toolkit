# SysHealth Desktop v0.2.1

SysHealth checks the local Windows print service and the last 24 hours of PrintService Admin events. It uses the current user's access and does not change the service or printer settings.

## Quick start

1. On Windows x64, extract the entire ZIP.
2. Double-click **SysHealth.exe**. The same window shows progress and then the result.
3. Read the headline, **Next step**, and **What was checked**. Select **Check again** to refresh or **Copy summary** to share the result.

The executable includes the .NET runtime. The app opens directly without a separate installer, PowerShell command, or report import.

| Result | Meaning |
| --- | --- |
| The print service is running | The service was running when checked. Review the suggested connection and queue checks if printing still fails. |
| The print service is stopped | Open Windows Services and check Print Spooler. |
| No matching events | The log was read successfully and returned no events in the 24-hour window. |
| Access denied, logging off, or unavailable | Read the individual findings. A partial result still includes the source that could be checked. |
| The check could not finish | Select **Check again**. The failed attempt clears the prior report. |

Expand **Technical details** to view event groups by provider and event ID. The count and most recent occurrence appear together. If the report reached the 200-event sample limit, its counts describe the sample. Select **Export JSON** to save the existing SysHealth v1 report format. The app confirms replacement through the Windows Save dialog when the file already exists.

Use Tab or Shift+Tab to move between actions and Space to activate a focused button or expand details. Windows contrast themes and display scaling are covered by the manual checks in the verification notes.

## Build and verify

Install the .NET 10 SDK for development. Open a terminal in the directory containing `SysHealth.csproj`. In the distribution ZIP this is the `source` folder.

```powershell
dotnet run --project tests/SysHealth.Tests.csproj -c Release
dotnet publish SysHealth.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o ./publish
```

The first command runs the portable regression checks. The second produces `publish/SysHealth.exe`. The distribution ZIP includes `review.patch` against the v0.2.0 archive source and the Windows verification checklist.
