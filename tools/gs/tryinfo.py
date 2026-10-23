import os, sys, time, queue
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gsprobe as g
import ctypes.wintypes as wt
from ctypes import byref

devs = g.gs_devices()
ch = g.vendor_channel(devs)
if not ch:
    print('no channel')
    sys.exit(1)

h = g.open_path(ch['path'])
out_len = ch['out_len'] or 32
r = g.Reader(h, ch['in_len'] or 64)
r.start()


def drain():
    while True:
        try:
            r.q.get_nowait()
        except queue.Empty:
            return


for attempt in range(8):
    buf = bytearray(out_len)
    buf[0], buf[1], buf[2], buf[3] = 0x07, 0x00, 0x01, 0x01
    drain()
    written = wt.DWORD(0)
    ok = g.kernel32.WriteFile(h, bytes(buf), out_len, byref(written), None)
    got = []
    end = time.time() + 0.35
    while time.time() < end:
        try:
            rep = r.q.get(timeout=0.05)
        except queue.Empty:
            continue
        if any(rep[1:]):
            got.append(rep)
    print('try %d write=%s' % (attempt + 1, ok))
    for rep in got:
        print('   ', g.hexs(rep[:32]))
    time.sleep(0.2)

r.stop = True
g.kernel32.CloseHandle(h)
