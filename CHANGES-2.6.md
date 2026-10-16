# AirGlass 2.6

The user reported recurring discovery failures and lag/jitter in Direct3D11 after the NVIDIA update. This release addresses specific app-side causes; physical iPhone comparison is pending.

## Restart behavior

The old standalone test waited for Bonjour removal before starting a replacement, but the app did not. The app now owns a local Bonjour browser, waits for removal after graceful shutdown and leaves a one-second settling interval before replacement. It retains its last port block when available. Readiness requires local discoverability plus the native callback and listener. An unconfirmed startup gets one retry, not an endless restart loop. Saving identical settings retains the current receiver. Eight same-object Receiver tests now exercise these exact production paths.

## Direct3D11 and timing

Direct3D11 is the default, with CPU fallback. CUDA/NVDEC plugin scanning is opt-in for NVIDIA/Automatic modes. The installer removes the obsolete normally-scanned plugin. A fresh portable extraction is recommended. The normal D3D11 path does not preload NVDEC or create its contexts.

The sink accepts decoder NV12/P010 textures directly; the extra forced upload/conversion chain is removed. The low-latency sink releases last-sample references and does not send QoS feedback upstream. The post-decode queue still keeps only the latest waiting frame; compressed reference packets are retained.

Native tests previously requested precise timers while the receiver did not. The production renderer now balances timeBeginPeriod(1)/timeEndPeriod(1). The renderer integration test no longer supplies a separate timer request. Smoothing reduces its temporary cushion at 4 ms/sec instead of 1 ms/sec, without abrupt schedule jumps. Tests verify recovery within six clean seconds and reduced controlled jitter at 15/30/60 fps. H.264 1080p30/60 tests pass with the NVIDIA runtime absent; HEVC 1080p60 passes too.

These short tests measure receiver behavior and synthetic timing, not phone capture, network transit, GPU completion or monitor scanout. They do not prove a universal latency improvement. The CPU option remains available for streams whose hardware decoder adds reordering delay.

References: [GStreamer Direct3D11 decoder direct-to-sink example](https://github.com/GStreamer/gstreamer/blob/1.26.2/subprojects/gst-plugins-bad/sys/d3d11/gstd3d11h264dec.cpp), [Direct3D11 sink](https://gstreamer.freedesktop.org/documentation/d3d11/d3d11videosink.html).
