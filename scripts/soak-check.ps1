param(
    [int]$DurationSeconds = 15,
    [string]$ExePath = "$PSScriptRoot\..\dist\win-x64\Atoll.exe"
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $ExePath)) {
    throw "Atoll.exe not found at $ExePath. Run scripts\publish.ps1 first."
}

$proc = Start-Process -FilePath $ExePath -PassThru
try {
    Start-Sleep -Seconds 14
    $samples = @()
    $iterations = [Math]::Max(2, [int]($DurationSeconds / 2))

    for ($i = 0; $i -lt $iterations; $i++) {
        $proc.Refresh()
        if ($proc.HasExited) {
            throw "Atoll.exe exited unexpectedly with code $($proc.ExitCode)"
        }

        $wsMb = [Math]::Round($proc.WorkingSet64 / 1MB, 2)
        $pmMb = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 2)
        $cpuSec = [Math]::Round($proc.TotalProcessorTime.TotalSeconds, 3)
        $samples += [PSCustomObject]@{
            Sample = $i + 1
            WorkingSetMB = $wsMb
            PrivateMB = $pmMb
            TotalCpuSec = $cpuSec
        }
        Start-Sleep -Seconds 2
    }

    $samples | Format-Table -AutoSize
    $firstCpu = $samples[0].TotalCpuSec
    $lastCpu = $samples[-1].TotalCpuSec
    $elapsedWall = ($samples.Count - 1) * 2.0
    $avgCpuPct = if ($elapsedWall -gt 0) {
        [Math]::Round((($lastCpu - $firstCpu) / ($elapsedWall * $env:NUMBER_OF_PROCESSORS)) * 100, 3)
    } else { 0 }

    $finalWs = $samples[-1].WorkingSetMB
    Write-Host "Summary -> Avg Idle CPU: $avgCpuPct% | Final Working Set: $finalWs MB" -ForegroundColor Cyan
}
finally {
    if (-not $proc.HasExited) {
        $proc.Kill()
        $proc.WaitForExit(3000) | Out-Null
    }
}

