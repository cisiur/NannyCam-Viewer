[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return ([System.BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\publish\win-x64'
}
else {
    $OutputDirectory
}
$projectPath = Join-Path $repositoryRoot 'src\NannyCam.Viewer.Windows\NannyCam.Viewer.Windows.csproj'
$noticePath = Join-Path $repositoryRoot 'licenses\FFMPEG-REDISTRIBUTION-NOTICE.txt'
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$allowedOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\publish'))
$allowedOutputPrefix = $allowedOutputRoot + [System.IO.Path]::DirectorySeparatorChar

if (-not $outputPath.StartsWith($allowedOutputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be under '$allowedOutputRoot'."
}

if (-not (Test-Path -LiteralPath $noticePath)) {
    throw "Required FFmpeg redistribution notice was not found at '$noticePath'."
}

$ffmpegReleaseTag = '9.0.2'
$ffmpegAssetName = 'ffmpeg-9.0.2-full_build-shared.7z'
$ffmpegSha256 = '4d2060a8b34a940aa47d785142055bb92a63053781e55f2ace4546edd519a8f5'
$ffmpegAssetUri = "https://github.com/GyanD/codexffmpeg/releases/download/$ffmpegReleaseTag/$ffmpegAssetName"
$cacheRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'NannyCam-Viewer\ffmpeg-cache'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "NannyCam-Viewer\ffmpeg-stage-$ffmpegReleaseTag"
$archivePath = Join-Path $cacheRoot $ffmpegAssetName
$extractPath = Join-Path $temporaryRoot 'extracted'

try {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
    $useCachedArchive = $false
    if (Test-Path -LiteralPath $archivePath) {
        $cachedSha256 = Get-Sha256 $archivePath
        $useCachedArchive = $cachedSha256 -eq $ffmpegSha256
    }

    if (-not $useCachedArchive) {
        Write-Host "Downloading pinned FFmpeg runtime: $ffmpegAssetName"
        Invoke-WebRequest -Uri $ffmpegAssetUri -OutFile $archivePath
    }
    else {
        Write-Host "Using hash-verified cached FFmpeg runtime: $ffmpegAssetName"
    }

    $actualSha256 = Get-Sha256 $archivePath
    if ($actualSha256 -ne $ffmpegSha256) {
        throw "Downloaded FFmpeg archive SHA-256 did not match the pinned value. Expected '$ffmpegSha256', got '$actualSha256'."
    }

    $tarCommand = Get-Command tar.exe -ErrorAction SilentlyContinue
    if ($null -eq $tarCommand) {
        throw "The pinned Gyan .7z archive requires Windows tar.exe (libarchive), but tar.exe was not found."
    }

    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null
    & $tarCommand.Source -xf $archivePath -C $extractPath
    if ($LASTEXITCODE -ne 0) {
        throw "Windows tar.exe could not extract the verified Gyan FFmpeg archive."
    }

    $ffmpegRoot = Get-ChildItem -LiteralPath $extractPath -Directory | Select-Object -First 1
    if ($null -eq $ffmpegRoot) {
        throw "The FFmpeg archive did not contain its expected top-level directory."
    }

    $ffmpegBin = Join-Path $ffmpegRoot.FullName 'bin'
    foreach ($requiredDll in 'avcodec-63.dll', 'avformat-63.dll', 'avutil-61.dll', 'swscale-10.dll') {
        if (-not (Test-Path -LiteralPath (Join-Path $ffmpegBin $requiredDll))) {
            throw "Pinned FFmpeg runtime is incomplete: '$requiredDll' was not found in '$ffmpegBin'."
        }
    }

    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Recurse -Force
    }

    dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained true --property:PublishSingleFile=false --property:PublishTrimmed=false --output $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $distributionFfmpegDirectory = Join-Path $outputPath 'ffmpeg'
    New-Item -ItemType Directory -Path $distributionFfmpegDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $ffmpegBin '*.dll') -Destination $distributionFfmpegDirectory -Force

    $distributionLicensesDirectory = Join-Path $outputPath 'licenses\ffmpeg'
    New-Item -ItemType Directory -Path $distributionLicensesDirectory -Force | Out-Null
    Copy-Item -LiteralPath $noticePath -Destination (Join-Path $distributionLicensesDirectory 'NannyCam-FFmpeg-Redistribution-Notice.txt') -Force

    $licenseFiles = Get-ChildItem -LiteralPath $ffmpegRoot.FullName -File | Where-Object { $_.Name -match '^(LICENSE|COPYING)' }
    if ($licenseFiles.Count -eq 0) {
        throw "Pinned FFmpeg archive did not include a top-level license file."
    }

    foreach ($licenseFile in $licenseFiles) {
        Copy-Item -LiteralPath $licenseFile.FullName -Destination $distributionLicensesDirectory -Force
    }

    $executablePath = Join-Path $outputPath 'NannyCam.Viewer.Windows.exe'
    if (-not (Test-Path -LiteralPath $executablePath)) {
        throw "Publish output did not contain '$executablePath'."
    }

    Write-Host "Ready-to-run distribution created at: $outputPath"
    Write-Host "Launch by double-clicking: $executablePath"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
