import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

PATH = r"C:\Windows\System32\drivers\RzDev_0094.sys"
data = open(PATH, "rb").read()
pe_off = struct.unpack_from("<I", data, 0x3C)[0]
coff = pe_off + 4
num_sections = struct.unpack_from("<H", data, coff + 2)[0]
opt_size = struct.unpack_from("<H", data, coff + 16)[0]
opt_off = coff + 20
image_base = struct.unpack_from("<Q", data, opt_off + 24)[0]
section_off = opt_off + opt_size
sections = []
for i in range(num_sections):
    o = section_off + 40 * i
    vsize, vaddr, rawsize, rawptr = struct.unpack_from("<IIII", data, o + 8)
    sections.append((vaddr, vsize, rawptr, rawsize))

def va_to_off(va):
    rva = va - image_base
    for vaddr, vsize, rawptr, rawsize in sections:
        if vaddr <= rva < vaddr + rawsize:
            return rawptr + (rva - vaddr)
    return None

md = Cs(CS_ARCH_X86, CS_MODE_64)

def dump(va, length=320):
    off = va_to_off(va)
    if off is None:
        print("bad va 0x%X" % va)
        return
    chunk = data[off:off + length]
    print("\n=== 0x%X ===" % va)
    for insn in md.disasm(chunk, va):
        print("0x%X: %-22s %s %s" % (insn.address, insn.bytes.hex(), insn.mnemonic, insn.op_str))

for va in (0x14000376C, 0x14000234C, 0x140001E44):
    dump(va)
