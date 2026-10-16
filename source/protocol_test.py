import ctypes, itertools, os, pathlib, plistlib, select, socket, subprocess, sys, time

root = pathlib.Path(__file__).resolve().parents[1]
if len(sys.argv) != 2: raise SystemExit('Usage: python source/protocol_test.py <test-output-directory>')
data = pathlib.Path(sys.argv[1]).resolve()
data.mkdir(parents=True, exist_ok=True)
(data / 'empty.rc').write_text('# explicit CLI settings\n')
env = dict(os.environ)
env['PATH'] = str(root / 'engine' / 'bin') + ';' + env['PATH']
env['GST_PLUGIN_PATH'] = env['GST_PLUGIN_SYSTEM_PATH'] = str(root / 'engine' / 'lib' / 'gstreamer-1.0')
env['GST_REGISTRY'] = str(data / 'registry.bin')
env['GST_REGISTRY_FORK'] = 'no'
env.pop('UXPLAYRC', None)
dns = ctypes.WinDLL('dnssd.dll')
cbtype = ctypes.WINFUNCTYPE(None, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_int32,
                          ctypes.c_char_p, ctypes.c_char_p, ctypes.c_char_p, ctypes.c_void_p)
dns.DNSServiceBrowse.argtypes = [ctypes.POINTER(ctypes.c_void_p), ctypes.c_uint32, ctypes.c_uint32,
                               ctypes.c_char_p, ctypes.c_char_p, cbtype, ctypes.c_void_p]
dns.DNSServiceRefSockFD.argtypes = [ctypes.c_void_p]
dns.DNSServiceRefSockFD.restype = ctypes.c_int
dns.DNSServiceProcessResult.argtypes = [ctypes.c_void_p]
dns.DNSServiceRefDeallocate.argtypes = [ctypes.c_void_p]
results = []
for mode, (res, fps) in itertools.product(('Latency', 'Picture'), [('1920x1080', 15), ('1920x1080', 30), ('1920x1080', 60), ('1280x720', 30), ('2560x1440', 30)]):
    name = 'AirGlass Protocol QA ' + mode
    env['AIRGLASS_LOW_LATENCY'] = '1' if mode == 'Latency' else '0'
    env['AIRGLASS_GPU_RENDERER'] = '1'
    env['AIRGLASS_FRAME_PACING'] = '1' if mode == 'Latency' else '0'
    env['AIRGLASS_FPS'] = str(fps)
    sink = 'd3d11videosink processing-deadline=0 fullscreen-toggle-mode=alt-enter+property'
    if mode == 'Picture': sink += ' max-lateness=-1'
    args = [str(root / 'engine' / 'bin' / 'uxplay.exe'), '-n', name, '-nh', '-s', f'{res}@{fps}',
            '-fps', str(fps), '-vs', sink, '-as', 'wasapisink low-latency=true', '-p', '7100', '-rc', str(data / 'empty.rc'),
            '-key', str(data / 'key.pem')]
    if mode == 'Latency':
        args += ['-vsync', 'no']
    else: args += ['-vsync']
    if res == '2560x1440': args.insert(1, '-h265')
    with open(data / f'{mode}-{res}-{fps}.log', 'wb') as log:
        proc = subprocess.Popen(args, env=env, stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            deadline = time.monotonic() + 40
            while True:
                try: stream = socket.create_connection(('127.0.0.1', 7101), 1); break
                except OSError:
                    if proc.poll() is not None or time.monotonic() > deadline: raise RuntimeError('Receiver did not listen')
                    time.sleep(.2)
            with stream:
                stream.settimeout(5)
                stream.sendall(b'GET /info RTSP/1.0\r\nCSeq: 1\r\n\r\n')
                response = b''
                while b'\r\n\r\n' not in response:
                    chunk = stream.recv(65536)
                    if not chunk: raise RuntimeError('Receiver closed request without a response')
                    response += chunk
                header, body = response.split(b'\r\n\r\n', 1)
                assert b'200 OK' in header, header
                size = int(next(line.split(b':',1)[1] for line in header.split(b'\r\n') if line.lower().startswith(b'content-length:')))
                while len(body) < size:
                    chunk = stream.recv(65536)
                    if not chunk: raise RuntimeError('Truncated /info response')
                    body += chunk
                info = plistlib.loads(body[:size])
                display = info['displays'][0]
                expected_w, expected_h = map(int,res.split('x'))
                assert display['widthPixels'] == expected_w and display['heightPixels'] == expected_h, display
                assert display['maxFPS'] == fps, display
                results.append(f'PASS: {mode} mode RTSP /info advertises {res}, maxFPS={fps}')
            found = []
            @cbtype
            def callback(ref, flags, interface, error, service, regtype, domain, context):
                if error == 0 and service.decode() == name and flags & 2: found.append(True)
            ref = ctypes.c_void_p()
            assert dns.DNSServiceBrowse(ctypes.byref(ref),0,0,b'_airplay._tcp',None,callback,None) == 0
            fd = dns.DNSServiceRefSockFD(ref)
            wrapper = socket.socket(fileno=fd)
            try:
                deadline = time.monotonic() + 5
                while not found and time.monotonic() < deadline:
                    if select.select([wrapper], [], [], .25)[0]: dns.DNSServiceProcessResult(ref)
            finally:
                wrapper.detach()
                dns.DNSServiceRefDeallocate(ref)
            assert found, 'AirPlay mDNS service not found'
            results.append('PASS: Bonjour discovers receiver as an AirPlay service')
        finally:
            proc.terminate()
            proc.wait(timeout=5)
(data / 'results.txt').write_text('\n'.join(results))
print('\n'.join(results))

