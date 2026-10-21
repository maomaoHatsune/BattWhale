import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

PATH = r"C:\Windows\System32\drivers\RzDev_0094.sys"
data = open(PATH, "rb").read()

# --- minimal PE parse ---
pe_off = struct.unpack_from("<I", data, 0x3C)[0]
assert data[pe_off:pe_off+4] == b"PE\0\0"
coff = pe_off + 4
num_sections = struct.unpack_from("<H", data, coff + 2)[0]
opt_size = struct.unpack_from("<H", data, coff + 16)[0]
opt_off = coff + 20
image_base = struct.unpack_from("<Q", data, opt_off + 24)[0]
section_off = opt_off + opt_size

sections = []
for i in range(num_sections):
    o = section_off + 40 * i
    name = data[o:o+8].rstrip(b"\0").decode(errors="replace")
    vsize, vaddr, rawsize, rawptr = struct.unpack_from("<IIII", data, o + 8)
    sections.append((name, vaddr, vsize, rawptr, rawsize))
print("image base 0x%X" % image_base)
for s in sections:
    print("  %-8s va=0x%X vsize=0x%X raw=0x%X size=0x%X" % s)

def off_to_va(off):
    for name, vaddr, vsize, rawptr, rawsize in sections:
        if rawptr <= off < rawptr + rawsize:
            return image_base + vaddr + (off - rawptr)
    return None

# --- find IOCTL-ish constants ---
targets = [0x88883010, 0x88883014, 0x88883140, 0x88883020]
print("\nconstant occurrences:")
hits = []
for t in targets:
    needle = struct.pack("<I", t)
    start = 0
    while True:
        i = data.find(needle, start)
        if i < 0:
            break
        va = off_to_va(i)
        print("  0x%08X at file 0x%X va %s" % (t, i, ("0x%X" % va) if va else "?"))
        hits.append((t, i))
        start = i + 1

md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = False

def dump_around(file_off, before=96, after=96):
    start = max(0, file_off - before)
    chunk = data[start:file_off + after]
    va = off_to_va(start)
    print("\n--- disasm around file 0x%X (va %s) ---" % (file_off, ("0x%X" % va) if va else "?"))
    for insn in md.disasm(chunk, va if va else start):
        mark = "  <== " if insn.address == (off_to_va(file_off) if va else file_off) else ""
        print("0x%X: %-24s %s %s%s" % (insn.address, insn.bytes.hex(), insn.mnemonic, insn.op_str, mark))

for t, off in hits:
    dump_around(off)
