[CmdletBinding()]
param(
    [string]$InstanceName = "MSSQLLocalDB",
    [string]$Driver = "ODBC Driver 17 for SQL Server",
    [int]$Port = 3000,
    [switch]$SkipDbSetup
)

$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. Install it and open a new PowerShell window."
    }
}

Require-Command "node"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$serverDir = Join-Path $repoRoot "server"

if (-not $SkipDbSetup) {
    & (Join-Path $PSScriptRoot "setup-local-db.ps1") -InstanceName $InstanceName
}

$nodeModulesDir = Join-Path $serverDir "node_modules"
$localDbDriverDir = Join-Path $nodeModulesDir "msnodesqlv8"

if (-not (Test-Path $nodeModulesDir) -or -not (Test-Path $localDbDriverDir)) {
    $npmCommand = Get-Command "npm.cmd" -ErrorAction SilentlyContinue

    if (-not $npmCommand) {
        $npmCommand = Get-Command "npm" -ErrorAction SilentlyContinue
    }

    if (-not $npmCommand) {
        throw "node_modules is missing and npm was not found. Install Node.js LTS and open a new PowerShell window."
    }

    Write-Host "Installing server dependencies, including the LocalDB driver..."
    Push-Location $serverDir
    try {
        & $npmCommand.Source install --include=optional

        if ($LASTEXITCODE -ne 0) {
            throw "Server dependency installation failed."
        }
    } finally {
        Pop-Location
    }
}

if (-not (Test-Path $localDbDriverDir)) {
    throw "The LocalDB driver 'msnodesqlv8' is still missing after npm install."
}

$env:KUNGFLOW_DB_MODE = "local"
$env:SQLSERVER_SERVER = "(localdb)\$InstanceName"
$env:SQLSERVER_DRIVER = $Driver
$env:PORT = [string]$Port

Write-Host "Starting KungFlow server on http://127.0.0.1:$Port"
Write-Host "Using local DB: $($env:SQLSERVER_SERVER)"

Push-Location $serverDir
try {
    node src/index.js
} finally {
    Pop-Location
}
