[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    throw "Required command 'dotnet' was not found. Install the .NET SDK and open a new PowerShell window."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$desktopProject = Join-Path $repoRoot "desktop\KungFlow.Desktop\KungFlow.Desktop.UI\KungFlow.Desktop.UI.csproj"

if (-not (Test-Path $desktopProject)) {
    throw "Desktop UI project was not found at $desktopProject."
}

dotnet run --project $desktopProject
