"""GameSir vendor-channel probe (pure ctypes, no build step).

Enumerates HID interfaces for VID 3537, dumps report descriptors, and lets us
write/read raw reports on the vendor-defined (usage page 0xFFA0) collection.

Usage:
  python gsprobe.py list
  python gsprobe.py desc
  python gsprobe.py listen [seconds]
  python gsprobe.py send <hex bytes> [listen_ms]
  python gsprobe.py sweep [max_id]
  python gsprobe.py sweepcmd <prefix_hex> [settle_ms]
  python gsprobe.py watch [seconds]      # read every interface, tag by path
"""
import ctypes
import ctypes.wintypes as wt
import queue
import sys
import threading
import time

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
setupapi = ctypes.WinDLL("setupapi", use_last_error=True)
hid = ctypes.WinDLL("hid", use_last_error=True)

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x1
FILE_SHARE_WRITE = 0x2
OPEN_EXISTING = 3
INVALID_HANDLE_VALUE = wt.HANDLE(-1).value
DIGCF_PRESENT = 0x2
DIGCF_DEVICEINTERFACE = 0x10
HIDP_STATUS_SUCCESS = 0x00110000


class GUID(ctypes.Structure):
    _fields_ = [("Data1", wt.DWORD), ("Data2", wt.WORD), ("Data3", wt.WORD),
                ("Data4", ctypes.c_ubyte * 8)]


class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [("cbSize", wt.DWORD), ("InterfaceClassGuid", GUID),
                ("Flags", wt.DWORD), ("Reserved", ctypes.c_void_p)]


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", wt.ULONG), ("VendorID", wt.USHORT),
                ("ProductID", wt.USHORT), ("VersionNumber", wt.USHORT)]


class HIDP_CAPS(ctypes.Structure):
    _fields_ = [("Usage", wt.USHORT), ("UsagePage", wt.USHORT),
                ("InputReportByteLength", wt.USHORT),
                ("OutputReportByteLength", wt.USHORT),
                ("FeatureReportByteLength", wt.USHORT),
                ("Reserved", wt.USHORT * 17),
                ("NumberLinkCollectionNodes", wt.USHORT),
                ("NumberInputButtonCaps", wt.USHORT),
                ("NumberInputValueCaps", wt.USHORT),
                ("NumberInputDataIndices", wt.USHORT),
                ("NumberOutputButtonCaps", wt.USHORT),
                ("NumberOutputValueCaps", wt.USHORT),
                ("NumberOutputDataIndices", wt.USHORT),
                ("NumberFeatureButtonCaps", wt.USHORT),
                ("NumberFeatureValueCaps", wt.USHORT),
                ("NumberFeatureDataIndices", wt.USHORT)]


setupapi.SetupDiGetClassDevsW.restype = wt.HANDLE
setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), wt.LPCWSTR, wt.HWND, wt.DWORD]
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [wt.HANDLE, ctypes.c_void_p,
                                                ctypes.POINTER(GUID), wt.DWORD,
                                                ctypes.POINTER(SP_DEVICE_INTERFACE_DATA)]
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [wt.HANDLE,
                                                      ctypes.POINTER(SP_DEVICE_INTERFACE_DATA),
                                                      ctypes.c_void_p, wt.DWORD,
                                                      ctypes.POINTER(wt.DWORD), ctypes.c_void_p]
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [wt.HANDLE]

kernel32.CreateFileW.restype = wt.HANDLE
kernel32.CreateFileW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, ctypes.c_void_p,
                                 wt.DWORD, wt.DWORD, wt.HANDLE]
kernel32.ReadFile.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.DWORD,
                              ctypes.POINTER(wt.DWORD), ctypes.c_void_p]
kernel32.WriteFile.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.DWORD,
                               ctypes.POINTER(wt.DWORD), ctypes.c_void_p]
kernel32.CloseHandle.argtypes = [wt.HANDLE]
kernel32.DeviceIoControl.argtypes = [wt.HANDLE, wt.DWORD, ctypes.c_void_p, wt.DWORD,
                                     ctypes.c_void_p, wt.DWORD,
                                     ctypes.POINTER(wt.DWORD), ctypes.c_void_p]
hid.HidD_GetAttributes.argtypes = [wt.HANDLE, ctypes.POINTER(HIDD_ATTRIBUTES)]
hid.HidD_GetPreparsedData.argtypes = [wt.HANDLE, ctypes.POINTER(ctypes.c_void_p)]
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)]
hid.HidD_GetProductString.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.ULONG]
hid.HidD_GetManufacturerString.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.ULONG]
hid.HidD_GetSerialNumberString.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.ULONG]
hid.HidD_GetFeature.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.ULONG]
hid.HidD_SetFeature.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.ULONG]

HID_GUID = GUID(0x4D1E55B2, 0xF16F, 0x11CF,
                (ctypes.c_ubyte * 8)(0x88, 0xCB, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30))
IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0000


def device_paths():
    paths = []
    hdev = setupapi.SetupDiGetClassDevsW(ctypes.byref(HID_GUID), None, None,
                                         DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    if hdev in (None, INVALID_HANDLE_VALUE):
        return paths
    try:
        i = 0
        while True:
            did = SP_DEVICE_INTERFACE_DATA()
            did.cbSize = ctypes.sizeof(did)
            if not setupapi.SetupDiEnumDeviceInterfaces(hdev, None, ctypes.byref(HID_GUID), i, ctypes.byref(did)):
                break
            i += 1
            need = wt.DWORD(0)
            setupapi.SetupDiGetDeviceInterfaceDetailW(hdev, ctypes.byref(did), None, 0, ctypes.byref(need), None)
            if need.value <= 0:
                continue
            buf = ctypes.create_string_buffer(need.value)
            # cbSize is 8 on x64 for the W variant
            ctypes.cast(buf, ctypes.POINTER(wt.DWORD))[0] = 8 if ctypes.sizeof(ctypes.c_void_p) == 8 else 6
            if not setupapi.SetupDiGetDeviceInterfaceDetailW(hdev, ctypes.byref(did), buf, need.value,
                                                             ctypes.byref(need), None):
                continue
            paths.append(ctypes.wstring_at(ctypes.addressof(buf) + 4))
    finally:
        setupapi.SetupDiDestroyDeviceInfoList(hdev)
    return paths


def open_path(path, access=GENERIC_READ | GENERIC_WRITE):
    return kernel32.CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                None, OPEN_EXISTING, 0, None)


def info(path):
    d = {"path": path, "vid": 0, "pid": 0, "product": "", "manufacturer": "",
         "serial": "", "usage_page": 0, "usage": 0, "in_len": 0, "out_len": 0,
         "feat_len": 0, "readable": False}
    h = open_path(path)
    if h == INVALID_HANDLE_VALUE:
        h = open_path(path, 0)
        if h == INVALID_HANDLE_VALUE:
            return None
    else:
        d["readable"] = True
    try:
        attr = HIDD_ATTRIBUTES()
        attr.Size = ctypes.sizeof(attr)
        if not hid.HidD_GetAttributes(h, ctypes.byref(attr)):
            return None
        d["vid"], d["pid"] = attr.VendorID, attr.ProductID
        sb = ctypes.create_unicode_buffer(260)
        if hid.HidD_GetProductString(h, sb, ctypes.sizeof(sb)):
            d["product"] = sb.value
        sb = ctypes.create_unicode_buffer(260)
        if hid.HidD_GetManufacturerString(h, sb, ctypes.sizeof(sb)):
            d["manufacturer"] = sb.value
        sb = ctypes.create_unicode_buffer(260)
        if hid.HidD_GetSerialNumberString(h, sb, ctypes.sizeof(sb)):
            d["serial"] = sb.value
        pre = ctypes.c_void_p()
        if hid.HidD_GetPreparsedData(h, ctypes.byref(pre)):
            try:
                caps = HIDP_CAPS()
                if hid.HidP_GetCaps(pre, ctypes.byref(caps)) == HIDP_STATUS_SUCCESS:
                    d["usage_page"] = caps.UsagePage
                    d["usage"] = caps.Usage
                    d["in_len"] = caps.InputReportByteLength
                    d["out_len"] = caps.OutputReportByteLength
                    d["feat_len"] = caps.FeatureReportByteLength
            finally:
                hid.HidD_FreePreparsedData(pre)
    finally:
        kernel32.CloseHandle(h)
    return d


def descriptor(path):
    for acc in (GENERIC_READ, GENERIC_READ | GENERIC_WRITE):
        h = open_path(path, acc)
        if h == INVALID_HANDLE_VALUE:
            continue
        try:
            buf = ctypes.create_string_buffer(4096)
            got = wt.DWORD(0)
            if kernel32.DeviceIoControl(h, IOCTL_HID_GET_REPORT_DESCRIPTOR, None, 0,
                                        buf, 4096, ctypes.byref(got), None) and got.value > 0:
                return buf.raw[:got.value]
        finally:
            kernel32.CloseHandle(h)
    return None


def gs_devices():
    out = []
    for p in device_paths():
        d = info(p)
        if d and d["vid"] == 0x3537:
            out.append(d)
    return out


def vendor_channel(devs):
    for d in devs:
        if d["usage_page"] == 0xFFA0:
            return d
    return None


def hexs(b):
    return " ".join("%02X" % x for x in b)


def parse_hex(s):
    parts = s.replace(",", " ").replace(":", " ").replace("-", " ").split()
    return bytes(int(x, 16) for x in parts)


class Reader(threading.Thread):
    def __init__(self, handle, length):
        super().__init__(daemon=True)
        self.handle = handle
        self.length = length
        self.q = queue.Queue()
        self.stop = False

    def run(self):
        buf = ctypes.create_string_buffer(self.length)
        got = wt.DWORD(0)
        while not self.stop:
            ok = kernel32.ReadFile(self.handle, buf, self.length, ctypes.byref(got), None)
            if not ok or got.value == 0:
                time.sleep(0.01)
                continue
            self.q.put(buf.raw[:got.value])


def cmd_list():
    devs = gs_devices()
    print("GameSir interfaces: %d" % len(devs))
    for d in devs:
        print("  up=0x%04X u=0x%04X in=%d out=%d feat=%d %s" % (
            d["usage_page"], d["usage"], d["in_len"], d["out_len"], d["feat_len"],
            "rw" if d["readable"] else "query-only"))
        print("    product=%r mfr=%r serial=%r" % (d["product"], d["manufacturer"], d["serial"]))
        print("    %s" % d["path"])
    return devs


def cmd_desc():
    devs = cmd_list()
    print()
    for d in devs:
        desc = descriptor(d["path"])
        if not desc:
            print("up=0x%04X  <no descriptor>" % d["usage_page"])
            continue
        print("up=0x%04X descriptor %d bytes" % (d["usage_page"], len(desc)))
        for i in range(0, len(desc), 16):
            print("  %03X  %s" % (i, hexs(desc[i:i + 16])))
        ids = []
        i = 0
        while i < len(desc) - 1:
            if desc[i] == 0x85:
                ids.append("%02X" % desc[i + 1])
                i += 2
                continue
            if desc[i] == 0x86:
                i += 2
                continue
            i += 1
        print("  report ids: %s" % (",".join(ids) if ids else "none"))
        print()


def cmd_listen(seconds):
    devs = gs_devices()
    ch = vendor_channel(devs)
    if not ch:
        print("no 0xFFA0 channel")
        return
    print("listening %.0fs on %s (in=%d)" % (seconds, ch["path"], ch["in_len"]))
    h = open_path(ch["path"])
    if h == INVALID_HANDLE_VALUE:
        print("open failed %d" % ctypes.get_last_error())
        return
    try:
        r = Reader(h, ch["in_len"] or 64)
        r.start()
        end = time.time() + seconds
        seen = {}
        n = 0
        while time.time() < end:
            try:
                rep = r.q.get(timeout=0.2)
            except queue.Empty:
                continue
            n += 1
            seen[hexs(rep)] = seen.get(hexs(rep), 0) + 1
            if n <= 40:
                print("  rx " + hexs(rep))
        r.stop = True
        print("reports=%d unique=%d" % (n, len(seen)))
        if n > 40:
            for k, v in sorted(seen.items(), key=lambda kv: -kv[1])[:20]:
                print("  x%-4d %s" % (v, k))
    finally:
        kernel32.CloseHandle(h)


def cmd_send(hexstr, listen_ms):
    devs = gs_devices()
    ch = vendor_channel(devs)
    if not ch:
        print("no 0xFFA0 channel")
        return
    payload = parse_hex(hexstr)
    out_len = ch["out_len"] or 32
    buf = bytearray(out_len)
    buf[:min(len(payload), out_len)] = payload[:out_len]
    h = open_path(ch["path"])
    if h == INVALID_HANDLE_VALUE:
        print("open failed %d" % ctypes.get_last_error())
        return
    try:
        r = Reader(h, ch["in_len"] or 64)
        r.start()
        written = wt.DWORD(0)
        ok = kernel32.WriteFile(h, bytes(buf), out_len, ctypes.byref(written), None)
        print("tx %s %s" % (hexs(buf), "ok" if ok else "FAIL %d" % ctypes.get_last_error()))
        end = time.time() + listen_ms / 1000.0
        seen = {}
        while time.time() < end:
            try:
                rep = r.q.get(timeout=0.05)
            except queue.Empty:
                continue
            k = hexs(rep)
            seen[k] = seen.get(k, 0) + 1
        r.stop = True
        if not seen:
            print("  (no reply)")
        for k, v in sorted(seen.items(), key=lambda kv: -kv[1]):
            print("  x%-3d %s" % (v, k))
    finally:
        kernel32.CloseHandle(h)


def cmd_sweep(max_id):
    devs = gs_devices()
    ch = vendor_channel(devs)
    if not ch:
        print("no 0xFFA0 channel")
        return
    out_len = ch["out_len"] or 32
    h = open_path(ch["path"])
    if h == INVALID_HANDLE_VALUE:
        print("open failed %d" % ctypes.get_last_error())
        return
    try:
        r = Reader(h, ch["in_len"] or 64)
        r.start()
        for rid in range(1, max_id + 1):
            buf = bytearray(out_len)
            buf[0] = rid
            written = wt.DWORD(0)
            ok = kernel32.WriteFile(h, bytes(buf), out_len, ctypes.byref(written), None)
            replies = []
            end = time.time() + 0.22
            while time.time() < end:
                try:
                    replies.append(r.q.get(timeout=0.04))
                except queue.Empty:
                    pass
            if not ok:
                print("id %02X write FAIL %d" % (rid, ctypes.get_last_error()))
                continue
            first = hexs(replies[0]) if replies else "-"
            print("id %02X n=%d %s" % (rid, len(replies), first))
        r.stop = True
    finally:
        kernel32.CloseHandle(h)


def cmd_sweepcmd(prefix, settle_ms):
    devs = gs_devices()
    ch = vendor_channel(devs)
    if not ch:
        print("no 0xFFA0 channel")
        return
    out_len = ch["out_len"] or 32
    h = open_path(ch["path"])
    if h == INVALID_HANDLE_VALUE:
        print("open failed %d" % ctypes.get_last_error())
        return
    try:
        r = Reader(h, ch["in_len"] or 64)
        r.start()
        def drain():
            while True:
                try:
                    r.q.get_nowait()
                except queue.Empty:
                    return

        for cmd in range(0x100):
            buf = bytearray(out_len)
            buf[0] = 0x07
            buf[1] = cmd
            if prefix:
                buf[2] = prefix
            drain()
            written = wt.DWORD(0)
            if not kernel32.WriteFile(h, bytes(buf), out_len, ctypes.byref(written), None):
                continue
            replies = []
            end = time.time() + settle_ms / 1000.0
            while time.time() < end:
                try:
                    rep = r.q.get(timeout=0.02)
                except queue.Empty:
                    continue
                if any(rep[1:]):
                    replies.append(rep)
            if not replies:
                continue
            uniq = {}
            for rep in replies:
                uniq[hexs(rep)] = uniq.get(hexs(rep), 0) + 1
            for s, n in uniq.items():
                print("cmd %02X n=%-2d %s" % (cmd, n, s))
        r.stop = True
    finally:
        kernel32.CloseHandle(h)


def cmd_watch(seconds):
    """Open every interface at once and tag incoming reports by usage page."""
    devs = gs_devices()
    handles, readers = [], []
    for d in devs:
        h = open_path(d["path"])
        if h == INVALID_HANDLE_VALUE:
            continue
        r = Reader(h, d["in_len"] or 64)
        r.start()
        handles.append(h)
        readers.append((d, r))
    print("watching %d interfaces for %.0fs" % (len(readers), seconds))
    end = time.time() + seconds
    counts = {}
    while time.time() < end:
        for d, r in readers:
            try:
                rep = r.q.get(timeout=0.02)
            except queue.Empty:
                continue
            key = (d["usage_page"], hexs(rep))
            counts[key] = counts.get(key, 0) + 1
    for (up, s), n in sorted(counts.items(), key=lambda kv: -kv[1])[:40]:
        print("  up=0x%04X x%-4d %s" % (up, n, s))
    for d, r in readers:
        r.stop = True
    for h in handles:
        kernel32.CloseHandle(h)


def cmd_probe(usage_page, seconds):
    """Listen one interface and report, per byte offset, every value seen."""
    devs = gs_devices()
    target = None
    for d in devs:
        if d["usage_page"] == usage_page:
            target = d
            break
    if not target:
        print("no interface with usage page 0x%04X" % usage_page)
        return
    h = open_path(target["path"])
    if h == INVALID_HANDLE_VALUE:
        print("open failed %d" % ctypes.get_last_error())
        return
    try:
        r = Reader(h, target["in_len"] or 64)
        r.start()
        end = time.time() + seconds
        values = {}
        total = 0
        uniq = {}
        while time.time() < end:
            try:
                rep = r.q.get(timeout=0.05)
            except queue.Empty:
                continue
            total += 1
            uniq[hexs(rep)] = uniq.get(hexs(rep), 0) + 1
            for i, b in enumerate(rep):
                values.setdefault(i, set()).add(b)
        r.stop = True
        print("usage page 0x%04X  reports=%d unique=%d len=%d" % (
            usage_page, total, len(uniq), target["in_len"]))
        for i in sorted(values):
            vs = sorted(values[i])
            if len(vs) == 1 and vs[0] == 0:
                continue
            shown = " ".join("%02X" % v for v in vs[:12])
            if len(vs) > 12:
                shown += " ...(%d values)" % len(vs)
            print("  byte[%02d] = %s" % (i, shown))
        print("  -- distinct reports --")
        for s, n in sorted(uniq.items(), key=lambda kv: -kv[1])[:10]:
            print("    x%-4d %s" % (n, s))
    finally:
        kernel32.CloseHandle(h)


def cmd_scan(template, settle_ms):
    """Sweep one byte of a command template. '??' marks the byte to sweep."""
    devs = gs_devices()
    ch = vendor_channel(devs)
    if not ch:
        print("no 0xFFA0 channel")
        return
    out_len = ch["out_len"] or 32
    tokens = template.split()
    pos = tokens.index("??")
    base = bytearray(out_len)
    for i, t in enumerate(tokens):
        if t != "??":
            base[i] = int(t, 16)
    h = open_path(ch["path"])
    if h == INVALID_HANDLE_VALUE:
        print("open failed %d" % ctypes.get_last_error())
        return
    try:
        r = Reader(h, ch["in_len"] or 64)
        r.start()

        def drain():
            while True:
                try:
                    r.q.get_nowait()
                except queue.Empty:
                    return

        hits = 0
        for v in range(0x100):
            buf = bytearray(base)
            buf[pos] = v
            drain()
            written = wt.DWORD(0)
            if not kernel32.WriteFile(h, bytes(buf), out_len, ctypes.byref(written), None):
                continue
            uniq = {}
            end = time.time() + settle_ms / 1000.0
            while time.time() < end:
                try:
                    rep = r.q.get(timeout=0.02)
                except queue.Empty:
                    continue
                if any(rep[1:]):
                    k = hexs(rep)
                    uniq[k] = uniq.get(k, 0) + 1
            for s, n in uniq.items():
                # an all-zero payload after the ack byte is just the ack
                payload = s.split()[2:]
                if not any(p != "00" for p in payload):
                    continue
                hits += 1
                print("byte=%02X n=%-2d %s" % (v, n, s))
        print("data-bearing replies: %d" % hits)
        r.stop = True
    finally:
        kernel32.CloseHandle(h)


def cmd_dump(first, last, rounds):
    """Read config blocks and flag bytes that change between rounds."""
    devs = gs_devices()
    ch = vendor_channel(devs)
    if not ch:
        print("no 0xFFA0 channel")
        return
    out_len = ch["out_len"] or 32
    h = open_path(ch["path"])
    if h == INVALID_HANDLE_VALUE:
        print("open failed %d" % ctypes.get_last_error())
        return
    try:
        r = Reader(h, ch["in_len"] or 64)
        r.start()

        def ask(block):
            buf = bytearray(out_len)
            buf[0], buf[1], buf[2], buf[3], buf[4] = 0x07, 0x00, 0x02, 0x02, block
            while True:
                try:
                    r.q.get_nowait()
                except queue.Empty:
                    break
            written = wt.DWORD(0)
            if not kernel32.WriteFile(h, bytes(buf), out_len, ctypes.byref(written), None):
                return None
            end = time.time() + 0.12
            while time.time() < end:
                try:
                    rep = r.q.get(timeout=0.02)
                except queue.Empty:
                    continue
                if rep[0] == 0x06 and len(rep) > 5 and rep[3] == 0x02 and rep[4] == block:
                    return rep
            return None

        for block in range(first, last + 1):
            seen = []
            for _ in range(rounds):
                rep = ask(block)
                if rep is None:
                    seen.append(None)
                else:
                    seen.append(rep[6:30])
                time.sleep(0.02)
            if any(s is None for s in seen):
                print("block %02X  <no reply>" % block)
                continue
            if all(s == seen[0] for s in seen):
                if not any(seen[0]):
                    continue
                print("block %02X  stable  %s" % (block, hexs(seen[0])))
            else:
                print("block %02X  CHANGES" % block)
                for i, s in enumerate(seen):
                    print("   #%d %s" % (i, hexs(s)))
        r.stop = True
    finally:
        kernel32.CloseHandle(h)


def main():
    mode = sys.argv[1].lower() if len(sys.argv) > 1 else "list"
    if mode == "list":
        cmd_list()
    elif mode == "desc":
        cmd_desc()
    elif mode == "listen":
        cmd_listen(float(sys.argv[2]) if len(sys.argv) > 2 else 5)
    elif mode == "send":
        cmd_send(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 800)
    elif mode == "sweep":
        cmd_sweep(int(sys.argv[2]) if len(sys.argv) > 2 else 40)
    elif mode == "sweepcmd":
        cmd_sweepcmd(int(sys.argv[2], 16) if len(sys.argv) > 2 else 0,
                     int(sys.argv[3]) if len(sys.argv) > 3 else 60)
    elif mode == "scan":
        cmd_scan(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 45)
    elif mode == "dump":
        cmd_dump(int(sys.argv[2], 16) if len(sys.argv) > 2 else 0x00,
                 int(sys.argv[3], 16) if len(sys.argv) > 3 else 0x30,
                 int(sys.argv[4]) if len(sys.argv) > 4 else 3)
    elif mode == "watch":
        cmd_watch(float(sys.argv[2]) if len(sys.argv) > 2 else 5)
    elif mode == "probe":
        cmd_probe(int(sys.argv[2], 16) if len(sys.argv) > 2 else 0xFFA0,
                  float(sys.argv[3]) if len(sys.argv) > 3 else 5)
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
