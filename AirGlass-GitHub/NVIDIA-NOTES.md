# Decoder selection in 2.4

Automatic evaluates H.264 and HEVC independently: Direct3D11 hardware, NVIDIA NVDEC, then avdec CPU. Factory loading refreshes per-device codec capabilities; a READY-state probe rejects initialization failures. Missing NVIDIA runtime support disables that candidate. Decodebin still checks stream caps and retains alternatives. GPU upload handles software frames when CPU fallback is needed.

Direct3D11 uses NVIDIA's video decoder on supported NVIDIA GPUs and avoids the CUDA/I420 conversion of this bundled direct NVDEC path. Earlier explicit NVDEC phone feedback reported more delay or jitter, so Automatic favors Direct3D11. Manual NVIDIA mode is retained. No claim of universal minimum latency is made.

The shipped GStreamer nvcodec runtime is 1.26.2. Required NVIDIA NVRTC 12.4.127 and builtins are private application dependencies, not a CUDA Toolkit or driver installation. Driver nvcuda/nvcuvid DLLs are external. NVRTC provenance/hashes and the separate NVIDIA license are in `licenses`.

Sources: [GStreamer codec capabilities and per-device factory registration](https://github.com/GStreamer/gstreamer/blob/1.26.2/subprojects/gst-plugins-bad/sys/nvcodec/plugin.c), [NVIDIA decoder properties](https://gstreamer.freedesktop.org/documentation/nvcodec/nvh264dec.html).
