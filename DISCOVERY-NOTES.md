# Receiver restart and discovery changes in 2.5

Previously normal Save/Stop killed the receiver process. It skipped explicit Bonjour service withdrawal and normal receiver cleanup. The UI also considered a listening TCP socket sufficient to report Ready, before asynchronous service registration was confirmed. These are restart weaknesses consistent with intermittent discovery; physical iPhone behavior can also depend on Wi-Fi and discovery caching.

Normal shutdown now signals a per-process Windows event. The receiver's GLib loop handles it, withdraws both AirPlay and RAOP services, then stops its server. The UI waits for process exit before replacement and retains forced termination for an unresponsive process. The stop event is held alive across early startup races. The existing kill-on-close job remains crash protection.

Bonjour registration callbacks are processed on the same native loop that owns the registration references. This avoids concurrent deallocation and reports confirmed registration or asynchronous errors. The UI waits for the AirPlay confirmation and listener before saying Ready. Queued output from an old process is ignored after a restart.

Regression testing observes a persistent Bonjour browser over eight same-name settings restarts, verifying addition, resolution to the current port, current resolution/FPS over RTSP, graceful exit and service removal after each cycle. This local network test does not establish that every physical iPhone/network discovery issue is resolved.

API references: [Apple DNSServiceRegister](https://developer.apple.com/documentation/dnssd/1804733-dnsserviceregister), [Apple DNSServiceRefDeallocate](https://developer.apple.com/documentation/dnssd/dnsservicerefdeallocate).
