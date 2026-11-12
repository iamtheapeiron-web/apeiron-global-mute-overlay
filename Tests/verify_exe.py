"""Inspect the delivered x64 PE and Windows resources, without executing Windows."""
from pathlib import Path
import hashlib
import struct
import sys
import xml.etree.ElementTree as ET

def verify(path):
    path = Path(path)
    b = path.read_bytes()
    pe = struct.unpack_from('<I', b, 0x3c)[0]
    assert b[:2] == b'MZ' and b[pe:pe+4] == b'PE\0\0'
    machine, count = struct.unpack_from('<HH', b, pe+4)
    assert machine == 0x8664, 'Must be x64'
    op = pe+24
    assert struct.unpack_from('<H', b, op)[0] == 0x20b
    assert struct.unpack_from('<H', b, op+68)[0] == 2, 'GUI subsystem'
    flags = struct.unpack_from('<H', b, op+70)[0]
    assert flags & 0x100 and flags & 0x40, 'NX and ASLR'
    optional_size = struct.unpack_from('<H', b, pe+20)[0]
    sections = []
    for i in range(count):
        offset = op + optional_size + i*40
        size, rva, raw_size, raw = struct.unpack_from('<IIII', b, offset+8)
        sections.append((rva, max(size, raw_size), raw))
    def offset(rva):
        for start, size, raw in sections:
            if start <= rva < start + size:
                return raw+rva-start
        raise AssertionError('RVA outside sections')
    clr_rva, clr_size = struct.unpack_from('<II', b, op+112+14*8)
    assert clr_size >= 72
    clr = offset(clr_rva)
    assert struct.unpack_from('<I', b, clr+16)[0] & 1, 'Managed IL assembly'
    assert struct.unpack_from('<I', b, clr+20)[0], 'Managed entry point'
    meta_rva = struct.unpack_from('<I', b, clr+8)[0]
    assert b[offset(meta_rva):offset(meta_rva)+4] == b'BSJB', 'CLR metadata'
    resource_rva, resource_size = struct.unpack_from('<II', b, op+112+2*8)
    assert resource_rva and resource_size
    root = offset(resource_rva)
    resources = {}
    def walk(relative, chain=()):
        named, ids = struct.unpack_from('<HH', b, root+relative+12)
        for i in range(named+ids):
            name, item = struct.unpack_from('<II', b, root+relative+16+i*8)
            if item & 0x80000000:
                walk(item & 0x7fffffff, chain+(name,))
            else:
                rva, size = struct.unpack_from('<II', b, root+item)
                start = offset(rva)
                resources[chain+(name,)] = b[start:start+size]
    walk(0)
    assert {3, 14, 16, 24} <= {chain[0] for chain in resources}, 'Icon, group icon, version, manifest'
    icons = [v for k,v in resources.items() if k[0] == 3]
    group = next(v for k,v in resources.items() if k[0] == 14)
    assert len(icons) == 7 and struct.unpack_from('<HHH', group) == (0, 1, 7)
    version = next(v for k,v in resources.items() if k[0] == 16)
    for value in ['APEIRON Global Mute Overlay', '0.1.0.12', '0.1.0-beta.5']:
        assert value.encode('utf-16le') in version, value
    fixed = version.find(struct.pack('<I', 0xfeef04bd))
    assert fixed >= 0 and struct.unpack_from('<IIII', version, fixed+8) == (1, 12, 1, 12)
    manifest = ET.fromstring(next(v for k,v in resources.items() if k[0] == 24))
    identity = next(n for n in manifest if n.tag.endswith('assemblyIdentity'))
    assert identity.attrib['version'] == '0.1.0.12', 'Manifest/build revision mismatch'
    request = next(n for n in manifest.iter() if n.tag.endswith('requestedExecutionLevel'))
    assert request.attrib == {'level': 'asInvoker', 'uiAccess': 'false'}
    assert any(n.tag.endswith('dpiAware') and n.text == 'true' for n in manifest.iter())
    print('PASS: x64 managed Windows GUI PE; NX/ASLR; valid CLR entry point; seven icon sizes; product 0.1.0-beta.5 / file 0.1.0.12; asInvoker and DPI-aware manifest.')
    print('EXE SHA256:', hashlib.sha256(b).hexdigest())

if __name__ == '__main__':
    verify(sys.argv[1])
