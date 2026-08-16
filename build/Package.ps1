[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'Jellyfin.Plugin.AttachmentOptimizer.slnx'
$projectPath = Join-Path $repositoryRoot 'src\Jellyfin.Plugin.AttachmentOptimizer\Jellyfin.Plugin.AttachmentOptimizer.csproj'
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$publishPath = Join-Path $artifactRoot 'plugin'

dotnet test $solutionPath -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Tests failed.'
}

if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

New-Item -ItemType Directory -Path $publishPath | Out-Null
dotnet publish $projectPath -c $Configuration --no-restore -o $publishPath
if ($LASTEXITCODE -ne 0) {
    throw 'Publish failed.'
}

$version = (Select-Xml -Path (Join-Path $repositoryRoot 'Directory.Build.props') -XPath '/Project/PropertyGroup/Version').Node.InnerText
$packagePath = Join-Path $artifactRoot "AttachmentOptimizer_$version.zip"
if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

$packageFiles = @(
    (Join-Path $publishPath 'Jellyfin.Plugin.AttachmentOptimizer.dll'),
    (Join-Path $repositoryRoot 'meta.json')
)
Compress-Archive -LiteralPath $packageFiles -DestinationPath $packagePath -CompressionLevel Optimal
Write-Host "Created $packagePath"
