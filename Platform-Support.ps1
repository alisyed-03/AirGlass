# Shared read-only platform checks; compatible with Windows PowerShell 5.1.
function Get-AirGlassHostArchitecture {
    param(
        [string]$MachineArchitecture = [Environment]::GetEnvironmentVariable('PROCESSOR_ARCHITECTURE', 'Machine'),
        [string]$WowArchitecture = $env:PROCESSOR_ARCHITEW6432,
        [string]$ProcessArchitecture = $env:PROCESSOR_ARCHITECTURE
    )
    # The machine environment is not rewritten by x86/x64 process emulation.
    if ($MachineArchitecture) { return $MachineArchitecture }
    if ($WowArchitecture) { return $WowArchitecture }
    return $ProcessArchitecture
}

function Get-AirGlassPlatformSupport {
    param(
        [string]$Architecture = (Get-AirGlassHostArchitecture),
        [int]$Build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name CurrentBuildNumber).CurrentBuildNumber
    )
    $arm = $Architecture -eq 'ARM64'
    $supported = ($Architecture -eq 'AMD64' -and $Build -ge 10240) -or ($arm -and $Build -ge 22000)
    $detail = if ($arm -and $supported) {
        'Windows 11 ARM64: AirGlass and its media runtime run through x64 emulation.'
    } elseif ($arm) {
        'Windows 11 is required on ARM64; Windows 10 ARM cannot run the x64 receiver.'
    } elseif ($supported) {
        'Windows 10/11 x64.'
    } else {
        'Windows 10/11 x64 or Windows 11 ARM64 is required.'
    }
    [pscustomobject]@{ Supported=$supported; Architecture=$Architecture; Build=$Build; Detail=$detail }
}
