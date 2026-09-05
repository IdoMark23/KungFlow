param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string]$ApiUrl,

    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "desktop\KungFlow.Desktop\KungFlow.Desktop.UI\KungFlow.Desktop.UI.csproj"
$publishDirectory = Join-Path $repoRoot "desktop\build-cloud-client"
$packageDirectory = Join-Path $publishDirectory "desktop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "server\public\landing\downloads\kungflow-client.zip"
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained false -o $packageDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE. The cloud client package was not created."
}

$settings = @{
    Api = @{
        BaseUrl = $ApiUrl.TrimEnd('/')
    }
} | ConvertTo-Json -Depth 3

Set-Content -LiteralPath (Join-Path $packageDirectory "appsettings.json") -Value $settings -Encoding utf8

$readme = @"
KungFlow cloud client
=====================

1. Install the .NET 10 Desktop Runtime for Windows x64:
   https://dotnet.microsoft.com/download/dotnet/10.0
2. Open desktop/KungFlow.Desktop.UI.exe.

This build connects to: $($ApiUrl.TrimEnd('/'))
"@

Set-Content -LiteralPath (Join-Path $publishDirectory "README.txt") -Value $readme -Encoding utf8
New-Item -ItemType Directory -Path (Split-Path $resolvedOutputPath) -Force | Out-Null
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $resolvedOutputPath -Force

Write-Host "Created cloud client package: $resolvedOutputPath"
