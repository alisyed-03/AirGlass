# Windows on ARM

AirGlass can be installed on **Windows 11 ARM64 using x64 emulation**.
The interface, UxPlay engine, GStreamer plugins and other runtime DLLs remain x64.
This is a compatibility path, not a native ARM64 port. Windows 10 ARM and 32-bit
Windows are unsupported.

## Install and run

1. Download `AirGlass-Setup-2.7.2.exe` or extract the complete portable
   `AirGlass-Windows-portable.zip` from release v2.7.2 or later.
2. Install .NET Framework 4.8 or a compatible later version and Apple's Bonjour
   Service, including its **x64 client DLLs**. An ARM64 or x86-only Bonjour DLL
   cannot be loaded into the x64 receiver. Keep Apple's components from its
   official installer; do not download individual DLLs from third-party sites.
3. Run `powershell -NoProfile -File .\Check-Setup.ps1` from the release folder.
   It reports the host architecture even from an emulated PowerShell process.
4. Open AirGlass and connect the iPhone on the same private network. Start with
   Direct3D11. If the graphics driver cannot provide the required codec through
   emulation, select Software and try 720p/30. Software decoding still uses the
   Direct3D11 video sink, so a working display driver is required.

NVIDIA NVDEC is not required. GPU acceleration, power use and latency depend on
Windows' emulator, the graphics driver and available codecs.

## Build and package

Use the existing x64 build and complete matching x64 release runtime. Keep
`/platform:x64` in `source/build.ps1` and MINGW64 in `source/build-native.sh`:
changing only the UI or compiler architecture would mix incompatible native DLLs.
Build on an x64 Windows development host using the README instructions. Ship
`Platform-Support.ps1` with `Check-Setup.ps1`; the installer includes both.

The installer uses Inno Setup's `x64compatible` architecture selector, which
admits x64 Windows and ARM64 Windows 11 while rejecting ARM64 Windows 10.

## Validation

The platform regression test covers x64 Windows 10/11, the Windows 11 ARM64 build
boundary, Windows 10 ARM, x86/unknown hosts, and emulated process environments.
Run `powershell -NoProfile -File .\source\platform_test.ps1`; GitHub Actions runs
it on Windows as well.

**Physical ARM64 end-to-end validation is pending.** Before publishing an ARM
compatibility claim for a release, test on a Windows 11 ARM64 PC:

- Install and uninstall into a path containing spaces and non-ASCII characters.
- Run Check-Setup from native and emulated PowerShell; verify .NET and Bonjour.
- Run `AirGlass.exe --self-test C:\Temp\AirGlass-ARM-QA` with Bonjour running.
  The existing self-test includes NVIDIA-specific checks; record unavailable
  NVIDIA cases separately rather than claiming they passed on an ARM GPU.
- Connect an iPhone; verify discovery, audio, video, full screen, Stop/start and
  settings restarts in Direct3D11 and Software modes.
- Confirm no missing/wrong-architecture DLLs, and record actual decoder, Windows
  build, CPU/GPU, resolution, frame rate and observed responsiveness.

## References

- [Microsoft: x86/x64 emulation on ARM](https://learn.microsoft.com/en-us/windows/arm/apps-on-arm-x86-emulation)
- [Inno Setup: architecture identifiers](https://jrsoftware.org/ishelp/topic_archidentifiers.htm)
