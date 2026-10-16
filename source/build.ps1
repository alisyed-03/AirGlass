$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$appRoot = Split-Path -Parent $sourceRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', ('/out:' + (Join-Path $appRoot 'AirGlass.exe')), ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')), ('/resource:' + (Join-Path $sourceRoot 'MainWindow.xaml') + ',MainWindow.xaml'), ('/r:' + (Join-Path $wpf 'PresentationFramework.dll')), ('/r:' + (Join-Path $wpf 'PresentationCore.dll')), ('/r:' + (Join-Path $wpf 'WindowsBase.dll')), '/r:System.Xaml.dll', '/r:System.Runtime.Serialization.dll', '/r:System.ServiceProcess.dll', (Join-Path $sourceRoot 'AirGlass.cs'))
$arguments += @(('/win32icon:' + (Join-Path $appRoot 'assets\AirGlass.ico')), ('/resource:' + (Join-Path $appRoot 'assets\airglass-logo.png') + ',AirGlassLogo.png'))
$arguments += (Join-Path $sourceRoot 'BonjourPresence.cs')
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'AirGlass compilation failed.' }
Write-Host ('Built ' + (Join-Path $appRoot 'AirGlass.exe'))
