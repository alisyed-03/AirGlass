# AirGlass

An AirPlay screen-mirroring receiver for Windows. Show your iPhone screen on your PC, with adjustable resolution, frame rate and latency settings.

<img src="assets/airglass-logo.png" width="120" alt="AirGlass logo">

## Download and run

1. Open this repository's **Releases** page and download **AirGlass-Windows.zip**. GitHub's automatic “Source code” downloads are for building the project and do not include the runnable engine/runtime.
2. Extract the **entire ZIP** to a folder you can write to. Keep `AirGlass.exe`, `engine`, `assets` and the license files together. Do not run the EXE from inside the ZIP or copy only the EXE.
3. Install **.NET Framework 4.8** and **Apple Bonjour** if they are missing; see Requirements below.
4. Open **AirGlass.exe**. The receiver starts automatically.
5. Put your iPhone and PC on the same local network. On iPhone, open **Control Center → Screen Mirroring → AirGlass**.
6. If Windows Firewall asks, allow the receiver on your trusted private/home network.

Video appears in a separate window. **Alt + Enter** toggles full screen. Closing AirGlass stops the receiver. After changing settings, select **Save & restart receiver** and reconnect from your iPhone.

## Features

- Default **1080p, up to 30 fps**.
- Resolution requests: 480p, 720p, 900p, 1080p, 1440p and 4K.
- Frame-rate limits: **15, 30 and 60 fps**.
- **Best latency** keeps short queues and displays the newest waiting decoded frame. **Smooth frame pacing** adapts a small waiting cushion to recent jitter.
- **Best picture** prioritizes preserving frames and audio/video timing.
- Automatic hardware detection, manual decoder selection, audio, full screen and receiver-name settings.

The requested resolution and FPS are not guaranteed output. The iPhone, content, aspect ratio, codec and network determine the delivered stream. Portrait video keeps its aspect ratio. Higher resolutions need a compatible HEVC sender. Some protected video cannot be mirrored.

## Settings

The main panel contains Receiver name, Resolution, Maximum frame rate and Performance preference. **Play iPhone audio** is an on/off switch directly below the performance preference. Select **Settings** underneath it to show or hide Video decoder, Smooth frame pacing and Open video in full screen. Settings stay saved when the additional controls are hidden.

Use **Save & restart receiver** after making changes, then reconnect from the iPhone. Normal Stop/restart now withdraws the Bonjour services before closing the receiver; forced termination is reserved for an unresponsive process. “Ready” waits for Bonjour to confirm the AirPlay registration, not just an open TCP port.

## Automatic GPU decoding

Leave **Video decoder → Automatic · detect hardware** selected. AirGlass checks H.264 and HEVC independently on each receiver start, using the installed driver and codec factories rather than a graphics-card name alone.

| Priority | Decoder | When it is used |
| --- | --- | --- |
| 1 | Direct3D11 hardware | Compatible NVIDIA, Intel or AMD Windows video decoder |
| 2 | NVIDIA NVDEC | Compatible NVIDIA codec/driver/runtime when Direct3D11 is unavailable |
| 3 | Software | CPU decoding when hardware support is unavailable |

**Direct3D11 on an NVIDIA card uses NVIDIA's hardware video decoder.** Direct NVDEC is an alternative API path, not a requirement for NVIDIA acceleration. This GStreamer version's direct NVDEC path needs an extra CUDA format conversion; local tests and phone feedback favored the native Direct3D11 path. Automatic therefore prefers Direct3D11. This is a tested default, not a promise that one mode is fastest on every PC.

Only compatible decoders are promoted. GStreamer retains alternatives for unsupported stream profiles and initialization failures. The log records available codecs and the actual decoder used when video arrives. Manual NVIDIA NVDEC, Direct3D11 and Software choices remain available for comparison. Hardware decoding can reduce CPU usage; network delay and frame pacing still affect responsiveness.

NVIDIA support does **not** require the full CUDA Toolkit. Required application runtime DLLs are bundled privately; an appropriate installed NVIDIA driver is needed only for NVIDIA acceleration. PCs without NVIDIA can use Direct3D11 or software.

## Requirements

- **Windows 10/11, x64**, with a working Direct3D11 display/driver. This release is not a macOS, Linux or Windows ARM-native build.
- **[.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)** or later compatible .NET Framework. The modern .NET runtime alone does not replace .NET Framework.
- **Apple Bonjour Service**, installed and running. Apple's [Bonjour developer page](https://developer.apple.com/bonjour/) links Bonjour Print Services for Windows. Install Bonjour from Apple, then reopen AirGlass. The app displays setup guidance if Bonjour is absent; it does not silently install system software.
- An iPhone capable of AirPlay Screen Mirroring, on a local network that permits device discovery.

You do not need Python, MSYS2, GStreamer, a compiler, the CUDA Toolkit or the developer's folders to run the complete release. No receiver keys, saved PC-specific settings, IP addresses or plugin caches are shipped. Each user gets their own settings and keys under `%LOCALAPPDATA%\AirGlass`; the install folder does not store runtime preferences.

The unsigned EXE may show a Windows reputation prompt. Verify the release SHA-256 and download from a trusted release. Routine use does not require running AirGlass as administrator. Installing prerequisites or approving firewall access may require Windows permissions.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| Receiver missing on iPhone | Bonjour Service is running; same local network; firewall allows `engine\bin\uxplay.exe`; no guest Wi-Fi/client isolation or conflicting VPN route. Close and reopen Screen Mirroring. |
| App asks for setup | Use **Connection help**. You can also run the included `Check-Setup.ps1` for read-only prerequisite checks. |
| Missing engine or DLL | Extract the complete release ZIP again. Keep the folder structure intact. Do not download only the EXE or GitHub's source archive. |
| Delay or jitter | Start with Best latency + Smooth frame pacing + Automatic. Compare Software. Try 720p/30 and connect the PC to the router by Ethernet if possible. |
| NVIDIA mode unavailable | Automatic uses the next compatible decoder. For direct NVIDIA support, check the installed driver and keep bundled runtime DLLs intact. |
| Settings need resetting | Close AirGlass and rename `%LOCALAPPDATA%\AirGlass\settings.xml`, then reopen. This resets preferences only. |

AirGlass uses Bonjour/mDNS discovery and available receiver ports in the 7100–7201 range. Open **Connection help** for the selected ports and receiver log. Best latency may trade audio synchronization for responsiveness. The live timing footer measures receiver processing up to presentation submission; it excludes phone capture/encoding, Wi-Fi transit, GPU completion and display scanout.

## Build from source

Interface source and build scripts are in `source`; complete modified native receiver source is `source/AirGlassEngine-source.zip`. The native engine is based on UxPlay v1.72, pinned to commit `7fc5f4d9b969dbb703ca59eb47697ff51b8cb7a5`.

For a full runnable developer checkout, extract a matching release into your build folder so `engine` is present, then overlay the repository source files. Rebuild the UI with the .NET Framework 4.8 compiler:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1
```

To rebuild the native receiver, install MSYS2 MINGW64 with GCC, CMake, Ninja, pkgconf, GStreamer/base development libraries, OpenSSL and libplist, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\source\build-native.ps1 -MsysRoot C:\msys64
```

The native source builds for generic x64 with `NO_MARCH_NATIVE=ON`. The release used GStreamer SDK 1.28.8 and the bundled 1.26.2 runtime. Replacing SDK/runtime versions requires retesting. Native integration tests are available with CMake `-DAIRGLASS_BUILD_TESTS=ON`. UI self-tests: `AirGlass.exe --self-test C:\Temp\AirGlass-QA`; protocol tests require Python and are in `source/protocol_test.py`. Close the normal app before tests using its ports.

## Validation and limitations

Version **2.5** was tested on Windows with an RTX 3060 Ti. Tests cover actual 1080p/60 H.264 and HEVC decoding/rendering, simulated missing codec capabilities, CPU fallback, settings persistence, receiver startup, protocol advertisement and Bonjour discovery. The release was also tested from a relocated folder containing spaces and a non-ASCII character, with development tools removed from PATH. Bundled PE imports were checked for missing runtime dependencies. See `TEST-RESULTS.txt`.

Fallback capability tests are simulations on the same PC; they do not replace physical Intel/AMD/older NVIDIA or clean Windows installations. End-to-end iPhone latency and universal hardware compatibility are not guaranteed. Network isolation, unsupported drivers and missing prerequisites can still prevent operation.

## License and credits

AirGlass project code and artwork are provided under **GPL-3.0-or-later**; see `LICENSE.txt`. The modified UxPlay source is included. Third-party components keep their own licenses. In particular, NVIDIA NVRTC DLLs in the runnable release are proprietary NVIDIA redistributable components with separate terms; they are not relicensed under GPL. See `THIRD-PARTY.md` and `licenses` for provenance and notices.

AirPlay and Bonjour are Apple trademarks. AirGlass is an independent project and is not affiliated with Apple or NVIDIA.
