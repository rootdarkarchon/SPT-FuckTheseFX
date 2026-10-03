"""Read the installed DoF shader. Local validation only; no game assets packaged."""
import argparse, ctypes, hashlib, json, struct, sys
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(repo / 'obj/SsrShaderAudit/deps'))
import UnityPy
from UnityPy.helpers import CompressionHelper

parser = argparse.ArgumentParser()
parser.add_argument('--game', default='D:/Tarkov-SPT-4.1')
args = parser.parse_args()
source = Path(args.game) / 'EscapeFromTarkov_Data/StreamingAssets/Windows/shaders'
out = repo / 'obj/InactiveEffectsAudit'
out.mkdir(parents=True, exist_ok=True)
env = UnityPy.load(str(source))
obj = next(o for o in env.objects if o.type.name == 'Shader' and o.read().m_ParsedForm.m_Name == 'Hidden/Dof/DepthOfFieldHdr')
shader = obj.read()
shader_hash = hashlib.sha256(obj.get_raw_data()).hexdigest().upper()
assert shader_hash == '96F78FC1D8E481A37EB059A0EDDA7BE15185E9E58665FA698F275B78E9ED043D', 'Unsupported native DoF shader'
template = UnityPy.load(str(repo / 'Assets/distantshadows'))
targets = [o for o in template.objects if o.type.name == 'Shader']
assert len(targets) == 1
targets[0].set_raw_data(obj.get_raw_data())
(out / 'native-dof-reference').write_bytes(next(iter(template.files.values())).save())
dll = ctypes.WinDLL('d3dcompiler_47.dll')
dll.D3DDisassemble.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint, ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
records = []
for platform, offsets, lengths, sizes in zip(shader.platforms, shader.offsets, shader.compressedLengths, shader.decompressedLengths):
    if int(platform) != 4:
        continue
    for offset, length, size in zip(offsets, lengths, sizes):
        data = CompressionHelper.decompress_lz4(bytes(shader.compressedBlob)[offset:offset+length], size)
        entries = [struct.unpack_from('<III', data, 4+i*12) for i in range(struct.unpack_from('<I', data)[0])]
        for index, (offset, length, segment) in enumerate(entries):
            record = data[offset:offset+length]
            start = record.find(b'DXBC')
            if start < 0:
                continue
            length = struct.unpack_from('<I', record, start+24)[0]
            bytecode = record[start:start+length]
            blob = ctypes.c_void_p()
            assert dll.D3DDisassemble(bytecode, len(bytecode), 0, None, ctypes.byref(blob)) == 0
            vt = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
            ptr = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(vt[3])(blob)
            text = ctypes.string_at(ptr).decode()
            ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(vt[2])(blob)
            (out / f'dof-program-{index}.txt').write_text(text)
            records.append(index)
passes = []
for index, p in enumerate(shader.m_ParsedForm.m_SubShaders[0].m_Passes):
    passes.append({'pass': index, 'name': p.m_State.m_Name,
                   'fragment_programs': [s.m_BlobIndex for g in p.progFragment.m_PlayerSubPrograms for s in g]})
(out / 'dof-shader-evidence.json').write_text(json.dumps({'name': shader.m_ParsedForm.m_Name,
    'shader_sha256': shader_hash, 'passes': passes, 'records': records}, indent=2))
print(json.dumps(passes))
