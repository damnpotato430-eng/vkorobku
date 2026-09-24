param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [ValidateSet('win-x64')]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root "src/vKOROBKU.App/vKOROBKU.App.csproj"
$workerProject = Join-Path $root "src/vKOROBKU.Worker/vKOROBKU.Worker.csproj"
$appVersion = ([xml](Get-Content -LiteralPath $appProject -Raw)).Project.PropertyGroup.Version
$workerVersion = ([xml](Get-Content -LiteralPath $workerProject -Raw)).Project.PropertyGroup.Version
if (-not $Version) { $Version = $appVersion }
if ($Version -ne $appVersion -or $Version -ne $workerVersion) {
    throw "Package version $Version must match App ($appVersion) and Worker ($workerVersion)."
}
$artifacts = Join-Path $root "artifacts"
$staging = Join-Path $artifacts ("release-staging-" + [guid]::NewGuid().ToString('N'))
$appPublish = Join-Path $staging "publish-app"
$workerPublish = Join-Path $staging "publish-worker"
$packageName = "vKOROBKU-v$Version-$Runtime"
$package = Join-Path $staging $packageName
$zip = Join-Path $staging "$packageName.zip"
$checksum = Join-Path $staging "$packageName.sha256"
$checksumNotes = Join-Path $staging "$packageName-checksum.md"

New-Item $appPublish, $workerPublish, $package -ItemType Directory -Force | Out-Null

$properties = @(
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

& dotnet publish $appProject @properties --output $appPublish
if ($LASTEXITCODE -ne 0) { throw "App publish failed ($LASTEXITCODE). Previous package preserved; diagnostics: $staging" }
& dotnet publish $workerProject @properties --output $workerPublish
if ($LASTEXITCODE -ne 0) { throw "Worker publish failed ($LASTEXITCODE). Previous package preserved; diagnostics: $staging" }

# Preserve all publish assets, including native libraries and satellite resources.
Copy-Item (Join-Path $appPublish '*') $package -Recurse
foreach ($file in Get-ChildItem -LiteralPath $workerPublish -File -Recurse) {
    $relative = $file.FullName.Substring($workerPublish.Length + 1)
    $target = Join-Path $package $relative
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
            throw "Conflicting publish asset: $relative"
        }
    } else {
        New-Item (Split-Path -Parent $target) -ItemType Directory -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
foreach ($exe in @('vKOROBKU.exe', 'vKOROBKU.Worker.exe')) {
    $binary = Get-Item -LiteralPath (Join-Path $package $exe)
    if ($binary.Length -lt 1MB -or $binary.VersionInfo.FileVersion -ne "$Version.0") {
        throw "Invalid published binary or version: $exe"
    }
}
Copy-Item (Join-Path $root "LICENSE") $package
Copy-Item (Join-Path $root "README.md") $package
Copy-Item (Join-Path $root "README.ru.md") $package
@"
vKOROBKU v$Version ($Runtime)

1. Распакуйте весь архив в отдельную папку.
2. Запустите vKOROBKU.exe.
3. Не отделяйте vKOROBKU.Worker.exe от основного приложения.
4. UAC запрашивается только перед сжатием или распаковкой.

Системные требования: Windows 10/11 x64, NTFS для XPRESS/LZX.
"@ | Set-Content (Join-Path $package "START.txt") -Encoding UTF8

Compress-Archive -Path (Join-Path $package "*") -DestinationPath $zip -CompressionLevel Optimal
# Verify the archive contents byte-for-byte before replacing any previous output.
$expanded = Join-Path $staging 'archive-check'
Expand-Archive -LiteralPath $zip -DestinationPath $expanded
$files = @(Get-ChildItem -LiteralPath $package -File -Recurse)
if ($files.Count -ne @(Get-ChildItem -LiteralPath $expanded -File -Recurse).Count) {
    throw 'Archive file count does not match package.'
}
foreach ($file in $files) {
    $relative = $file.FullName.Substring($package.Length + 1)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $expanded $relative)).Hash) {
        throw "Archive verification failed: $relative"
    }
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $packageName.zip" | Set-Content $checksum -Encoding ASCII
@"
## SHA-256

``````text
$hash  $packageName.zip
``````
"@ | Set-Content $checksumNotes -Encoding UTF8

# Only validated output reaches the stable paths. Never recursively remove an
# unchecked path, and never delete the last good package before publishing succeeds.
foreach ($output in @($package, $zip, $checksum, $checksumNotes)) {
    $destination = [IO.Path]::GetFullPath((Join-Path $artifacts (Split-Path -Leaf $output)))
    $boundary = [IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar
    if (-not $destination.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output escaped artifacts directory: $destination"
    }
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    Move-Item -LiteralPath $output -Destination $destination
}
Write-Host "Package:  $(Join-Path $artifacts "$packageName.zip")"
Write-Host "SHA256:   $hash"
Write-Host "Verified: $($files.Count) files; staging retained at $staging"
