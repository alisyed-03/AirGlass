# AirGlass 2.7 — lower H.264 display delay

The actual iPhone's 2.6 trace showed about 180–200 ms between receiver input and decoded output; this was much larger than the smoothing cushion. Best latency now selects GStreamer's flexible, low-delay H.264 picture-output policy for Direct3D11. It keeps necessary reference pictures but avoids conservative output buffering for interactive I/P mirroring streams. Best picture retains the standard decoder policy; use it for content that needs conservative B-picture reordering. This is a latency/correctness tradeoff provided by the decoder, not a change to encrypted AirPlay packets or a removal of reference packets.

The user tested the changed mode and reported **“More responsive and smooth.”** In separate sessions on the same PC/iPhone at a 1080p request and 30-fps ceiling:

| Receiver metric | 2.6 | 2.7 |
| --- | --- | --- |
| Median input-to-decoded-output | 192.34 ms | 0.34 ms |
| Median input-to-presentation submission | 197.82 ms | 22.63 ms |
| Late drops in sampled reports | 21 | 0 |

There were 8 earlier reports and 19 later reports, with different content/session lengths. This is not a controlled identical-content product benchmark. These times exclude phone capture/encoding, network transit before receiver input, GPU completion and display scanout. They are not end-to-end latency. Sanitized measurements are in `PHONE-TIMING-2.7.json`; raw phone logs, keys and addresses are not shipped.

Closing a mirroring window also exposed a race: renderer storage could be freed before the network producer finished its last appsrc push. The receiver now flushes blocked pushes and joins mirroring producers before freeing the renderer, and clears stale renderer pointers. Five concurrent close-under-load cycles passed with GStreamer critical errors treated as fatal. The existing discovery observer, quiet restart interval, Direct3D11 default and opt-in NVDEC plugin are retained.

Synthetic H.264 High-profile I/P access units with optional VUI reorder hints omitted decode successfully in both the prior and updated renderers. That encoder fixture did not reproduce the phone's old stall; the real-phone trace and user comparison provide the relevant performance evidence. Automated UI/startup/settings/graceful-stop checks also pass.

Source references: [GStreamer H.264 decoder compliance policy](https://github.com/GStreamer/gstreamer/blob/1.26.2/subprojects/gst-plugins-bad/gst-libs/gst/codecs/gsth264decoder.c), [GStreamer decoded-picture bumping modes](https://github.com/GStreamer/gstreamer/blob/1.26.2/subprojects/gst-plugins-bad/gst-libs/gst/codecs/gsth264picture.c).
