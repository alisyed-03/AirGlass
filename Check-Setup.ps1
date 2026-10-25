# Read-only setup checks. Does not install software or alter firewall settings.
$ErrorActionPreference = 'Stop'
$appRoot = $PSScriptRoot
$checks = @()
. (Join-Path $appRoot 'Platform-Support.ps1')
$platform = Get-AirGlassPlatformSupport
$checks += [pscustomobject]@{ Check='Windows platform'; Ready=$platform.Supported; Detail=$platform.Detail }
# Match the installer's 64-bit registry view even from 32-bit PowerShell.
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
try {
    $frameworkKey = $registry.OpenSubKey('SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full')
    try { $release = if ($frameworkKey) { $frameworkKey.GetValue('Release', 0) } else { 0 } }
    finally { if ($frameworkKey) { $frameworkKey.Dispose() } }
} finally { $registry.Dispose() }
$checks += [pscustomobject]@{ Check='.NET Framework 4.8'; Ready=($release -ge 528040); Detail='https://dotnet.microsoft.com/download/dotnet-framework/net48' }
$bonjour = Get-Service -Name 'Bonjour Service' -ErrorAction SilentlyContinue
$checks += [pscustomobject]@{ Check='Bonjour Service'; Ready=($null -ne $bonjour -and $bonjour.Status -eq 'Running'); Detail='https://developer.apple.com/bonjour/ (Bonjour Print Services for Windows)' }
foreach ($relative in @('AirGlass.exe','engine\bin\uxplay.exe','engine\lib\gstreamer-1.0\libgstd3d11.dll','engine\lib\gstreamer-1.0\libgstlibav.dll')) {
    $checks += [pscustomobject]@{ Check=$relative; Ready=(Test-Path -LiteralPath (Join-Path $appRoot $relative)); Detail='Extract the complete release ZIP, not only the EXE.' }
}
$checks | Format-Table -AutoSize -Wrap
Write-Output 'Direct3D11 is the default video decoder. NVIDIA drivers are optional; compatible hardware is detected per codec. No CUDA Toolkit, Python or MSYS2 installation is needed to run the release.'
Write-Output 'Allow the receiver on your trusted private network when Windows Firewall asks. Network isolation, VPNs and Wi-Fi congestion can still prevent or disrupt mirroring.'

if ($platform.Architecture -eq 'ARM64') {
    Write-Output 'Keep the complete x64 runtime together and install the x64 Bonjour components. Try Video decoder: Software at 720p/30 if Direct3D11 is unavailable or slow under emulation. ARM64 hardware validation is still required; see WINDOWS-ARM.md.'
}
