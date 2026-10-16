"""Windows/Bonjour regression: observe the same service across settings restarts."""
import ctypes, os, pathlib, plistlib, select, socket, subprocess, sys, time
app=pathlib.Path(__file__).resolve().parents[1]
out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
env=dict(os.environ)
env['PATH']=str(app/'engine/bin')+';'+os.environ['SystemRoot']+'\\System32;'+os.environ['SystemRoot']
env['GST_PLUGIN_PATH']=env['GST_PLUGIN_SYSTEM_PATH']=str(app/'engine/lib/gstreamer-1.0')
env['GST_REGISTRY']=str(out/'registry.bin');env['GST_REGISTRY_FORK']='no'
env.update(AIRGLASS_DECODER='Auto',AIRGLASS_GPU_RENDERER='1')
(out/'empty.rc').write_text('# explicit settings\n')
dns=ctypes.WinDLL('dnssd.dll');win=ctypes.WinDLL('kernel32',use_last_error=True)
ref=ctypes.c_void_p;uint=ctypes.c_uint32
Browse=ctypes.WINFUNCTYPE(None,ref,uint,uint,ctypes.c_int32,ctypes.c_char_p,ctypes.c_char_p,ctypes.c_char_p,ref)
Resolve=ctypes.WINFUNCTYPE(None,ref,uint,uint,ctypes.c_int32,ctypes.c_char_p,ctypes.c_char_p,ctypes.c_uint16,ctypes.c_uint16,ref,ref)
dns.DNSServiceBrowse.argtypes=[ctypes.POINTER(ref),uint,uint,ctypes.c_char_p,ctypes.c_char_p,Browse,ref]
dns.DNSServiceResolve.argtypes=[ctypes.POINTER(ref),uint,uint,ctypes.c_char_p,ctypes.c_char_p,ctypes.c_char_p,Resolve,ref]
dns.DNSServiceRefSockFD.argtypes=[ref];dns.DNSServiceRefSockFD.restype=ctypes.c_int
dns.DNSServiceProcessResult.argtypes=[ref];dns.DNSServiceRefDeallocate.argtypes=[ref]
win.OpenEventW.argtypes=[uint,ctypes.c_int,ctypes.c_wchar_p];win.OpenEventW.restype=ref
win.SetEvent.argtypes=[ref];win.CloseHandle.argtypes=[ref]
name='AirGlass Restart QA '+str(int(time.time()))
events=[];results=[]
@Browse
def browse_cb(r,flags,interface,error,service,kind,domain,context):
    if not error and service.decode()==name:events.append((bool(flags&2),interface,domain))
def pump(r,until,seconds=8):
    sock=socket.socket(fileno=dns.DNSServiceRefSockFD(r))
    try:
        deadline=time.monotonic()+seconds
        while not until() and time.monotonic()<deadline:
            if select.select([sock],[],[],.1)[0]:assert dns.DNSServiceProcessResult(r)==0
        assert until(),'Bonjour event timed out'
    finally:sock.detach()
def stop(p):
    if p.poll() is not None:return
    event=win.OpenEventW(2,False,'Local\\AirGlass.Stop.'+str(p.pid))
    assert event,'Graceful control event unavailable'
    try:assert win.SetEvent(event)
    finally:win.CloseHandle(event)
    p.wait(timeout=6)
browser=ref();assert dns.DNSServiceBrowse(ctypes.byref(browser),0,0,b'_airplay._tcp',None,browse_cb,None)==0
try:
    for i in range(8):
        fps=(15,30,60)[i%3];width,height=((1920,1080),(1280,720))[i%2]
        env.update(AIRGLASS_LOW_LATENCY=str(i%2),AIRGLASS_FRAME_PACING=str(i%2),AIRGLASS_FPS=str(fps))
        logfile=out/f'cycle-{i}.log'
        args=[str(app/'engine/bin/uxplay.exe'),'-n',name,'-nh','-s',f'{width}x{height}@{fps}','-fps',str(fps),'-p','7100','-rc',str(out/'empty.rc'),'-key',str(out/'key.pem'),'-vs','d3d11videosink processing-deadline=0','-as','0','-vsync','no']
        before=len(events)
        with logfile.open('wb') as log:
            p=subprocess.Popen(args,env=env,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                pump(browser,lambda:any(e[0] for e in events[before:]))
                answers=[]
                @Resolve
                def resolve_cb(r,flags,interface,error,fullname,host,port,length,txt,context):
                    if not error:answers.append(socket.ntohs(port))
                resolver=ref()
                assert dns.DNSServiceResolve(ctypes.byref(resolver),0,0,name.encode(),b'_airplay._tcp',b'local.',resolve_cb,None)==0
                try:pump(resolver,lambda:bool(answers));assert answers[0]==7101,answers
                finally:dns.DNSServiceRefDeallocate(resolver)
                with socket.create_connection(('127.0.0.1',answers[0]),3) as s:
                    s.settimeout(3);s.sendall(b'GET /info RTSP/1.0\r\nCSeq: 1\r\n\r\n')
                    data=b''
                    while b'\r\n\r\n' not in data:data+=s.recv(65536)
                    head,body=data.split(b'\r\n\r\n',1)
                    length=int(next(x.split(b':',1)[1] for x in head.split(b'\r\n') if x.lower().startswith(b'content-length:')))
                    while len(body)<length:body+=s.recv(65536)
                    d=plistlib.loads(body)['displays'][0]
                    assert (d['widthPixels'],d['heightPixels'],d['maxFPS'])==(width,height,fps),d
                removed=len(events);stop(p)
                pump(browser,lambda:any(not e[0] for e in events[removed:]))
                text=logfile.read_text(errors='replace')
                assert 'AirGlass discovery: registered' in text and 'AirGlass discovery: services withdrawn' in text
                assert p.returncode==0,p.returncode
                results.append(f'PASS: cycle {i+1}, same receiver name; add/resolve/current {width}x{height}@{fps}/graceful stop/remove')
                print(results[-1],flush=True)
            finally:
                if p.poll() is None:
                    try:stop(p)
                    except Exception:p.kill();p.wait()
finally:dns.DNSServiceRefDeallocate(browser)
(out/'results.txt').write_text('\n'.join(results)+'\n',encoding='utf-8')
