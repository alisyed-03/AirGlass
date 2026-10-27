# Receiver restart and discovery changes in 2.5

Previously normal Save/Stop killed the receiver process. It skipped explicit Bonjour service withdrawal and normal receiver cleanup. The UI also considered a listening TCP socket sufficient to report Ready, before asynchronous service registration was confirmed. These are restart weaknesses consistent with intermittent discovery; physical iPhone behavior can also depend on Wi-Fi and discovery caching.

Normal shutdown now signals a per-process Windows event. The receiver's GLib loop handles it, withdraws both AirPlay and RAOP services, then stops its server. The UI waits for process exit before replacement and retains forced termination for an unresponsive process. The stop event is held alive across early startup races. The existing kill-on-close job remains crash protection.

Bonjour registration callbacks are processed on the same native loop that owns the registration references. This avoids concurrent deallocation and reports confirmed registration or asynchronous errors. The UI waits for the AirPlay confirmation and listener before saying Ready. Queued output from an old process is ignored after a restart.

Regression testing observes a persistent Bonjour browser over eight same-name settings restarts, verifying addition, resolution to the current port, current resolution/FPS over RTSP, graceful exit and service removal after each cycle. This local network test does not establish that every physical iPhone/network discovery issue is resolved.

API references: [Apple DNSServiceRegister](https://developer.apple.com/documentation/dnssd/1804733-dnsserviceregister), [Apple DNSServiceRefDeallocate](https://developer.apple.com/documentation/dnssd/dnsservicerefdeallocate).

## Startup recovery fix

Recovery previously used a 12-second timer while the startup warning used 45
seconds. This could replace a still-initializing cold receiver. Recovery was also
conditioned only on a missing local browse entry: a cached/present entry suppressed
recovery when registration confirmation or the listener was missing.

Recovery now uses the full 45-second startup window and checks the same three
signals as readiness: registration confirmation, local browse presence and the
process listener. The existing single graceful retry remains bounded; established
sessions are left running. A failed replacement retains its error, and an exhausted
startup shows explicit troubleshooting guidance. Manual Stop/Start resets the retry
budget as before.

The UI self-test exercises all eight readiness combinations before/at the deadline,
with a connected session and with an exhausted retry budget. Windows execution and
physical iPhone discovery validation remain required; local Bonjour visibility does
not prove multicast delivery to a phone. This fixes the startup recovery logic, not
a confirmed diagnosis of the reported intermittent iPhone visibility issue.
