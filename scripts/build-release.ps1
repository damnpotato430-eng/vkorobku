param(
    [string]$Version = "0.1.2",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$appPublish = Join-Path $artifacts "publish-app"
$workerPublish = Join-Path $artifacts "publish-worker"
$packageName = "vKOROBKU-v$Version-$Runtime"
$package = Join-Path $artifacts $packageName
$zip = Join-Path $artifacts "$packageName.zip"
$checksum = Join-Path $artifacts "$packageName.sha256"
$checksumNotes = Join-Path $artifacts "$packageName-checksum.md"

Remove-Item $appPublish, $workerPublish, $package, $zip, $checksum, $checksumNotes -Recurse -Force -ErrorAction SilentlyContinue
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

& dotnet publish (Join-Path $root "src/vKOROBKU.App/vKOROBKU.App.csproj") @properties --output $appPublish
& dotnet publish (Join-Path $root "src/vKOROBKU.Worker/vKOROBKU.Worker.csproj") @properties --output $workerPublish

Copy-Item (Join-Path $appPublish "vKOROBKU.exe") $package
Copy-Item (Join-Path $workerPublish "vKOROBKU.Worker.exe") $package
Copy-Item (Join-Path $root "LICENSE") $package
Copy-Item (Join-Path $root "README.md") $package
@"
vKOROBKU v$Version ($Runtime)

1. Распакуйте весь архив в отдельную папку.
2. Запустите vKOROBKU.exe.
3. Не отделяйте vKOROBKU.Worker.exe от основного приложения.
4. UAC запрашивается только перед сжатием или распаковкой.

Системные требования: Windows 10/11 x64, NTFS для XPRESS/LZX.
"@ | Set-Content (Join-Path $package "START.txt") -Encoding UTF8

Compress-Archive -Path (Join-Path $package "*") -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $packageName.zip" | Set-Content $checksum -Encoding ASCII
@"
## SHA-256

``````text
$hash  $packageName.zip
``````
"@ | Set-Content $checksumNotes -Encoding UTF8

Write-Host "Package:  $zip"
Write-Host "SHA256:   $hash"
Write-Host "Notes:    $checksumNotes"
