[CmdletBinding()]
param(
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$Version = '1.0.0-rc.9',

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactDirectory = Join-Path $repositoryRoot 'artifacts'
$releaseName = "GameDraw-$Version-$Runtime"
$releaseDirectory = Join-Path $artifactDirectory $releaseName
$publishDirectory = Join-Path $releaseDirectory 'app'
$archivePath = Join-Path $artifactDirectory "$releaseName.zip"
$checksumPath = "$archivePath.sha256"

if ((Test-Path -LiteralPath $releaseDirectory -PathType Container) -or
    (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
    (Test-Path -LiteralPath $checksumPath -PathType Leaf))
{
    throw "Release '$releaseName' already exists under '$artifactDirectory'. Use a new version or remove that exact release first."
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Push-Location $repositoryRoot
try
{
    dotnet restore GameDraw.sln
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    if (-not $SkipTests)
    {
        dotnet test GameDraw.sln -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
    }

    dotnet publish src/GameDraw.App/GameDraw.App.csproj `
        -c Release `
        -r $Runtime `
        --self-contained true `
        --no-restore `
        -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    $executablePath = Join-Path $publishDirectory 'GameDraw.App.exe'
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf))
    {
        throw "Published executable was not found at '$executablePath'."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $publishDirectory,
        $archivePath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $releaseName.zip" | Set-Content -LiteralPath $checksumPath -Encoding ascii

    [pscustomobject]@{
        Version = $Version
        Runtime = $Runtime
        Archive = $archivePath
        Sha256 = $hash
        Bytes = (Get-Item -LiteralPath $archivePath).Length
    } | Format-List
}
finally
{
    Pop-Location
}
