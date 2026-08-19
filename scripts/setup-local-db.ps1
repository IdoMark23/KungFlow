[CmdletBinding()]
param(
    [string]$InstanceName = "MSSQLLocalDB"
)

$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. Install SQL Server LocalDB / sqlcmd and open a new PowerShell window."
    }
}

Require-Command "sqllocaldb"
Require-Command "sqlcmd"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$setupScript = Join-Path $repoRoot "server\db\setup.sql"

if (-not (Test-Path $setupScript)) {
    throw "Database setup script was not found at $setupScript."
}

$infoOutput = & sqllocaldb info $InstanceName 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "LocalDB instance '$InstanceName' was not found. Creating it..."
    $createOutput = & sqllocaldb create $InstanceName 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Could not create LocalDB instance '$InstanceName'. Output: $createOutput"
    }
} else {
    $infoOutput | ForEach-Object { Write-Verbose $_ }
}

$startOutput = & sqllocaldb start $InstanceName 2>&1

if ($LASTEXITCODE -ne 0 -and ($startOutput -notmatch "already started")) {
    throw "Could not start LocalDB instance '$InstanceName'. Output: $startOutput"
}

$serverName = "(localdb)\$InstanceName"
Write-Host "Applying database setup script to $serverName..."

& sqlcmd -S $serverName -E -i $setupScript -b

if ($LASTEXITCODE -ne 0) {
    throw "Database setup failed."
}

Write-Host "KungFlow local database is ready: $serverName / KungFlowDB"
