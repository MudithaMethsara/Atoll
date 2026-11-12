param(
    [string]$OutputDir = "$PSScriptRoot\..\dist\win-x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot\.."
$Project = Join-Path $RepoRoot "src\Atoll\Atoll.csproj"

if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}

Write-Host "Publishing Atoll (Self-Contained, Single-File, ReadyToRun, Partial Trim)..." -ForegroundColor Cyan
dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=true `
    -p:TrimMode=partial `
    -p:_SuppressWpfTrimError=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $OutputDir

$ExePath = Join-Path $OutputDir "Atoll.exe"
if (-not (Test-Path $ExePath)) {
    throw "Publish succeeded but Atoll.exe was not found at $ExePath"
}

$SizeMb = [Math]::Round((Get-Item $ExePath).Length / 1MB, 2)
Write-Host "Published: $ExePath ($SizeMb MB)" -ForegroundColor Green

$Iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($Iscc) {
    Write-Host "Compiling Inno Setup installer using $Iscc..." -ForegroundColor Cyan
    & $Iscc (Join-Path $RepoRoot "installer\Atoll.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC failed with exit code $LASTEXITCODE"
    }
}
else {
    Write-Host "Inno Setup 6 (ISCC.exe) not found; skipping installer .exe compile." -ForegroundColor Yellow
}

