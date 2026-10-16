param([string]$Compiler = 'ISCC.exe')
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
if (!(Test-Path -LiteralPath (Join-Path $appRoot 'engine\bin\uxplay.exe'))) { throw 'Extract the complete matching release beside this source before building the installer.' }
if (!(Get-Command $Compiler -ErrorAction SilentlyContinue)) { throw 'Install Inno Setup 6.7 or later and pass -Compiler with the path to ISCC.exe.' }
& $Compiler (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
