# Regression checks for fail-fast packaging. Run after a successful package build.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$builder = Join-Path $PSScriptRoot 'build-release.ps1'
$version = ([xml](Get-Content (Join-Path $root 'src/vKOROBKU.App/vKOROBKU.App.csproj') -Raw)).Project.PropertyGroup.Version
$archive = Join-Path $root "artifacts/vKOROBKU-v$version-win-x64.zip"
$originalHash = (Get-FileHash -LiteralPath $archive).Hash

function Assert-Failure([scriptblock]$Action, [string]$Expected) {
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.Message }
    if (-not $failure -or $failure -notlike "*$Expected*") {
        throw "Expected failure '$Expected', got '$failure'."
    }
    if ((Get-FileHash -LiteralPath $archive).Hash -ne $originalHash) {
        throw 'Failed build changed the last good archive.'
    }
}

Assert-Failure { & $builder -Version '999.0.0' } 'must match'
Assert-Failure { & $builder -Version '../escape' } 'Version'
Assert-Failure { & $builder -Runtime 'win-arm64' } 'Runtime'

# PowerShell functions take precedence over executables; no real publish is run.
function dotnet {
    $global:ReleaseFailureProbe.Calls++
    $global:LASTEXITCODE = if ($global:ReleaseFailureProbe.Calls -eq $global:ReleaseFailureProbe.FailAt) { 42 } else { 0 }
}
try {
    $global:ReleaseFailureProbe = @{ Calls = 0; FailAt = 1 }
    Assert-Failure { & $builder } 'App publish failed (42)'
    if ($global:ReleaseFailureProbe.Calls -ne 1) { throw 'Worker publish ran after App failure.' }
    $global:ReleaseFailureProbe = @{ Calls = 0; FailAt = 2 }
    Assert-Failure { & $builder } 'Worker publish failed (42)'
    if ($global:ReleaseFailureProbe.Calls -ne 2) { throw 'Unexpected publish count.' }
} finally {
    Remove-Item Function:\dotnet
    Remove-Variable ReleaseFailureProbe -Scope Global
    $global:LASTEXITCODE = 0
}
Write-Host 'Passed: invalid version/runtime, failed App, failed Worker, previous archive preserved.'
