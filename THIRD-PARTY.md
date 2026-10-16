# Third-party components and provenance

AirGlass contains an independently modified native receiver, a new WPF interface and original generated icon artwork. No LetsView or LonelyScreen code/binaries are redistributed. Commercial receiver binaries are not included in this project.

## Native engine

- AirGlass native engine 2.7, based on [UxPlay v1.72](https://github.com/FDH2/UxPlay/tree/v1.72), GPL-3.0-or-later.
- Pinned base commit: `7fc5f4d9b969dbb703ca59eb47697ff51b8cb7a5`.
- Complete patched source: `source/AirGlassEngine-source.zip`. This corresponds to the distributed modified executable.
- Modifications include bounded queues, GPU conversion, software-thread tuning, adaptive timing after decoding, public DXGI device queue limits, exact stage diagnostics, Winsock timeout/ACK handling, explicit configuration-file selection and dynamic Bonjour build support. Native integration checks are included.
- Public Apple DNS-SD API header is included under its BSD license in the source archive. Its notice is reproduced in `licenses/Apple-dns_sd-header.txt`. The header does not include Apple's Bonjour service/DLL, which remains an external prerequisite.

## Original runtime package

The GStreamer plugins and runtime DLLs remain from the [uxplay-windows 1.72.1-3 distribution](https://github.com/leapbtw/uxplay-windows/releases/tag/1.72.1-3). Its old receiver executable was replaced by the patched build. No Python tray app or Qt interface is used.

- Original installer SHA-256: `cb45de36d960b27d60dfe16a2a45489bd287d4335d37967f109d8cc4b5372986`.
- Original published engine archive: `source/UxPlay-v1.72.zip`.
- Original Windows packaging archive: `source/uxplay-windows-1.72.1-3.zip`.
- The original package did not record the exact default-branch commit used to build its executable. The new AirGlass native build uses the explicit base commit above. Build details/hashes are in `licenses/native-build.txt`.

## Principal runtime dependencies

- [GStreamer 1.26.2](https://gitlab.freedesktop.org/gstreamer/gstreamer), LGPL-2.1-or-later.
- [FFmpeg 7.1.1](https://ffmpeg.org/legal.html), LGPL/GPL as configured upstream.
- [libplist](https://github.com/libimobiledevice/libplist), LGPL-2.1-or-later.
- [OpenSSL 3](https://github.com/openssl/openssl), Apache-2.0.
- [GLib](https://gitlab.gnome.org/GNOME/glib), LGPL-2.1-or-later.
- [MinGW-w64](https://www.mingw-w64.org/) and [GCC C++ runtime](https://gcc.gnu.org/onlinedocs/libstdc++/manual/license.html).
- [x264](https://code.videolan.org/videolan/x264), GPL-2.0-or-later, and [x265](https://bitbucket.org/multicoreware/x265_git), GPL-2.0-or-later.
- Other codec, graphics, text and compression libraries are listed in `licenses/runtime-files.txt`. Upstream licenses remain applicable; MSYS2 package recipes/sources are at [MINGW-packages](https://github.com/msys2/MINGW-packages).

Apple Bonjour is an external prerequisite. AirPlay is Apple's trademark. AirGlass is an independent app. Original component copyright notices and license files are retained.

## NVIDIA optional acceleration runtime

NVIDIA NVRTC 12.4.127 and builtins are proprietary NVIDIA redistributable runtime components, distributed under the separate retained `licenses/NVIDIA-NVRTC-LICENSE.txt`, not the engine GPL. Package provenance and DLL hashes: `licenses/NVIDIA-NVRTC-provenance.json`. GStreamer nvcodec and matching libgstcuda/libgstd3d12/libgstgl use the GStreamer license noted above. Installed NVIDIA driver DLLs are external prerequisites and are not bundled.
