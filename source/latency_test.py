"""Synthetic decoded-frame backlog test. This does not measure iPhone latency."""
import ctypes, os, pathlib, sys, time

root = pathlib.Path(__file__).resolve().parents[1]
if len(sys.argv) != 2:
    raise SystemExit('Usage: python source/latency_test.py <test-output-directory>')
data = pathlib.Path(sys.argv[1]).resolve()
data.mkdir(parents=True, exist_ok=True)
bin_path = root / 'engine' / 'bin'
os.environ['PATH'] = str(bin_path) + ';' + os.environ['PATH']
os.environ['GST_PLUGIN_PATH'] = os.environ['GST_PLUGIN_SYSTEM_PATH'] = str(root / 'engine' / 'lib' / 'gstreamer-1.0')
os.environ['GST_REGISTRY'] = str(data / 'test-registry.bin')
os.environ['GST_REGISTRY_FORK'] = 'no'
directory = os.add_dll_directory(str(bin_path))
gst = ctypes.CDLL(str(bin_path / 'libgstreamer-1.0-0.dll'))
gobject = ctypes.CDLL(str(bin_path / 'libgobject-2.0-0.dll'))
gst.gst_init.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
gst.gst_parse_launch.argtypes = [ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
gst.gst_parse_launch.restype = ctypes.c_void_p
gst.gst_bin_get_by_name.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
gst.gst_bin_get_by_name.restype = ctypes.c_void_p
gst.gst_element_set_state.argtypes = [ctypes.c_void_p, ctypes.c_int]
gst.gst_element_get_bus.argtypes = [ctypes.c_void_p]
gst.gst_element_get_bus.restype = ctypes.c_void_p
gst.gst_bus_timed_pop_filtered.argtypes = [ctypes.c_void_p, ctypes.c_uint64, ctypes.c_uint32]
gst.gst_bus_timed_pop_filtered.restype = ctypes.c_void_p
gst.gst_message_unref.argtypes = [ctypes.c_void_p]
gst.gst_object_unref.argtypes = [ctypes.c_void_p]
callback_type = ctypes.CFUNCTYPE(None, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
gobject.g_signal_connect_data.argtypes = [ctypes.c_void_p, ctypes.c_char_p, callback_type, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int]
gobject.g_signal_connect_data.restype = ctypes.c_ulonglong
gst.gst_init(None, None)

def run(latency_mode):
    queue = 'queue max-size-buffers=1 max-size-bytes=0 max-size-time=0 leaky=downstream silent=true' if latency_mode else 'queue'
    # Live 30-fps decoded frames, followed by a deliberately slow (~16-fps) renderer.
    pipeline_text = ('videotestsrc is-live=true num-buffers=60 ! '
                     'video/x-raw,framerate=30/1,width=320,height=180 ! ' + queue +
                     ' ! identity name=slow sleep-time=60000 signal-handoffs=true ! '
                     'fakesink sync=false processing-deadline=0')
    error = ctypes.c_void_p()
    pipeline = gst.gst_parse_launch(pipeline_text.encode(), ctypes.byref(error))
    assert pipeline and not error.value, 'Synthetic pipeline could not be parsed'
    slow = gst.gst_bin_get_by_name(pipeline, b'slow')
    count = [0]
    @callback_type
    def handoff(element, buffer, context): count[0] += 1
    gobject.g_signal_connect_data(slow, b'handoff', handoff, None, None, 0)
    bus = gst.gst_element_get_bus(pipeline)
    try:
        begin = time.monotonic()
        assert gst.gst_element_set_state(pipeline, 4) != 0, 'Cannot play synthetic pipeline'
        message = gst.gst_bus_timed_pop_filtered(bus, 10_000_000_000, 1)  # EOS only; timeout catches failures.
        assert message, 'Synthetic frame stream did not finish'
        elapsed = time.monotonic() - begin
        gst.gst_message_unref(message)
        return count[0], elapsed
    finally:
        gst.gst_element_set_state(pipeline, 1)
        gst.gst_object_unref(slow)
        gst.gst_object_unref(bus)
        gst.gst_object_unref(pipeline)

normal_count, normal_time = run(False)
fast_count, fast_time = run(True)
assert normal_count == 60, 'Baseline must preserve all decoded frames'
assert fast_count < 45, 'Latency queue should skip old frames under load'
assert fast_time < normal_time * .8, 'Latency queue should finish sooner under load'
result = (f'PASS: Synthetic slow-renderer baseline preserves {normal_count} frames; drains in {normal_time:.2f}s.\n'
          f'PASS: Best-latency queue displays {fast_count} current frames; drains in {fast_time:.2f}s.\n'
          'This verifies decoded-frame backlog removal under simulated load, not end-to-end iPhone latency.\n')
(data / 'results.txt').write_text(result)
print(result)
