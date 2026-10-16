param(
    [string]$MsysRoot = $(if ($env:MSYS2_ROOT) { $env:MSYS2_ROOT } else { 'C:\msys64' }),
    [string]$BuildDirectory = $(Join-Path $env:TEMP 'AirGlass-native-build')
)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$nativeSource = Join-Path $PSScriptRoot 'AirGlassEngine'
$bash = Join-Path $MsysRoot 'usr\bin\bash.exe'
if (!(Test-Path -LiteralPath $bash)) { throw 'MSYS2 is required. See README.md for the native build dependencies.' }
if (!(Test-Path -LiteralPath $nativeSource)) {
    Expand-Archive -LiteralPath (Join-Path $PSScriptRoot 'AirGlassEngine-source.zip') -DestinationPath $PSScriptRoot
}
$buildScript = (Join-Path $PSScriptRoot 'build-native.sh').Replace('\', '/')
& $bash --noprofile --norc $buildScript $nativeSource $BuildDirectory $appRoot
if ($LASTEXITCODE -ne 0) { throw 'Native engine build failed.' }
