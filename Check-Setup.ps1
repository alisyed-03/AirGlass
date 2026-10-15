# Read-only setup checks. Does not install software or alter firewall settings.
$ErrorActionPreference = 'Stop'
$appRoot = $PSScriptRoot
$checks = @()
$checks += [pscustomobject]@{ Check='64-bit Windows'; Ready=[Environment]::Is64BitOperatingSystem; Detail='Windows 10/11 x64 is required.' }
$release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue).Release
$checks += [pscustomobject]@{ Check='.NET Framework 4.8'; Ready=($release -ge 528040); Detail='https://dotnet.microsoft.com/download/dotnet-framework/net48' }
$bonjour = Get-Service -Name 'Bonjour Service' -ErrorAction SilentlyContinue
$checks += [pscustomobject]@{ Check='Bonjour Service'; Ready=($null -ne $bonjour -and $bonjour.Status -eq 'Running'); Detail='https://developer.apple.com/bonjour/ (Bonjour Print Services for Windows)' }
foreach ($relative in @('AirGlass.exe','engine\bin\uxplay.exe','engine\lib\gstreamer-1.0\libgstd3d11.dll','engine\lib\gstreamer-1.0\libgstlibav.dll')) {
    $checks += [pscustomobject]@{ Check=$relative; Ready=(Test-Path -LiteralPath (Join-Path $appRoot $relative)); Detail='Extract the complete release ZIP, not only the EXE.' }
}
$checks | Format-Table -AutoSize -Wrap
Write-Output 'Keep Video decoder set to Automatic. NVIDIA drivers are optional; compatible hardware is detected per codec. No CUDA Toolkit, Python or MSYS2 installation is needed to run the release.'
Write-Output 'Allow the receiver on your trusted private network when Windows Firewall asks. Network isolation, VPNs and Wi-Fi congestion can still prevent or disrupt mirroring.'
