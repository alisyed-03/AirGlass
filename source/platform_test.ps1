# No media runtime, Bonjour or administrator privileges required.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'Platform-Support.ps1')
$cases = @(
    @('AMD64', 10240, $true), @('AMD64', 19045, $true),
    @('AMD64', 22000, $true), @('AMD64', 9600, $false),
    @('ARM64', 19045, $false), @('ARM64', 21999, $false),
    @('ARM64', 22000, $true), @('ARM64', 26100, $true),
    @('arm64', 26100, $true), @('x86', 26100, $false),
    @('ARM', 26100, $false), @('unknown', 26100, $false),
    @('', 26100, $false)
)
foreach ($case in $cases) {
    $result = Get-AirGlassPlatformSupport -Architecture $case[0] -Build $case[1]
    if ($result.Supported -ne $case[2]) { throw "Unexpected support for $($case[0]) build $($case[1])" }
    if (!$result.Detail) { throw 'Missing platform guidance.' }
}
foreach ($process in @('x86', 'AMD64', 'ARM64')) {
    $architecture = Get-AirGlassHostArchitecture -MachineArchitecture ARM64 -WowArchitecture '' -ProcessArchitecture $process
    if ($architecture -ne 'ARM64') { throw 'Emulated process hid the ARM64 host.' }
    if (!(Get-AirGlassPlatformSupport -Architecture $architecture -Build 22000).Supported) { throw 'Windows 11 ARM rejected.' }
    if ((Get-AirGlassPlatformSupport -Architecture $architecture -Build 19045).Supported) { throw 'Windows 10 ARM accepted.' }
}
if ((Get-AirGlassHostArchitecture -MachineArchitecture '' -WowArchitecture ARM64 -ProcessArchitecture x86) -ne 'ARM64') { throw 'WOW fallback failed.' }
if ((Get-AirGlassHostArchitecture -MachineArchitecture '' -WowArchitecture '' -ProcessArchitecture AMD64) -ne 'AMD64') { throw 'Process fallback failed.' }
$installer = Get-Content -Raw (Join-Path $PSScriptRoot 'installer.iss')
foreach ($directive in @('ArchitecturesAllowed', 'ArchitecturesInstallIn64BitMode')) {
    if ($installer -notmatch "(?m)^$directive=x64compatible\r?$") { throw "$directive must allow Windows 11 x64 emulation." }
}
# Parse all PowerShell scripts, without executing setup or compiler operations.
Get-ChildItem $root -Filter '*.ps1' -Recurse | ForEach-Object {
    $tokens = $null; $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
}
Write-Output 'PASS: platform matrix, emulated host detection, installer selectors and PowerShell syntax.'
