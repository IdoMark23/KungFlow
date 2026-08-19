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

if (-not (Test-Path (Join-Path $serverDir "node_modules"))) {
    $npmCommand = Get-Command "npm.cmd" -ErrorAction SilentlyContinue

    if (-not $npmCommand) {
        $npmCommand = Get-Command "npm" -ErrorAction SilentlyContinue
    }

    if (-not $npmCommand) {
        throw "node_modules is missing and npm was not found. Install Node.js LTS and open a new PowerShell window."
    }

    Write-Host "Installing server dependencies..."
    Push-Location $serverDir
    try {
        & $npmCommand.Source install
    } finally {
        Pop-Location
    }
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
