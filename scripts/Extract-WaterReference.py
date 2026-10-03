"""Extract the pinned shader for LOCAL validation only; never package game assets."""
import argparse,hashlib,json,struct,ctypes,dataclasses
from pathlib import Path
import UnityPy
from UnityPy.helpers import CompressionHelper
parser=argparse.ArgumentParser();parser.add_argument('--game',default='D:/Tarkov-SPT-4.1');args=parser.parse_args()
repo=Path(__file__).resolve().parents[1];out=repo/'obj/WaterValidation';out.mkdir(parents=True,exist_ok=True)
source=Path(args.game)/'EscapeFromTarkov_Data/sharedassets15.assets'
assert hashlib.sha256(source.read_bytes()).hexdigest().upper()=='266A9AD217731AC3953E6603BDD5D8648F143DCEE976C76A4783C74891876550','Unsupported water assets'
env=UnityPy.load(str(source));obj=next(o for o in env.objects if o.type.name=='Shader' and o.read().m_ParsedForm.m_Name=='Hidden/WaterForSSRv2');s=obj.read()
template=UnityPy.load(str(repo/'Assets/distantshadows'));targets=[o for o in template.objects if o.type.name=='Shader'];assert len(targets)==1
targets[0].set_raw_data(obj.get_raw_data());(out/'native-reference').write_bytes(next(iter(template.files.values())).save())
# Pin each oracle to one unchanged native program, independently of the editor's
# camera keyword state (which can override HDR when rendering to a float target).
# Only selection metadata/name changes; all compiled instructions remain native.
native_raw=obj.get_raw_data()
for variant in range(8):
 obj.set_raw_data(native_raw);fixture=obj.read()
 fixture.m_ParsedForm.m_Name=f'Hidden/WaterForSSRv2-ParityFixture-{variant}'
 frag=fixture.m_ParsedForm.m_SubShaders[0].m_Passes[0].progFragment
 for index,group in enumerate(frag.m_PlayerSubPrograms):
  if group:
   selected=group[variant];selected.m_KeywordIndices=[];frag.m_PlayerSubPrograms[index]=[selected]
   frag.m_ParameterBlobIndices[index]=[frag.m_ParameterBlobIndices[index][variant]]
 fixture.save();targets[0].set_raw_data(obj.data)
 (out/f'native-reference-{variant}').write_bytes(next(iter(template.files.values())).save())
obj.set_raw_data(native_raw)
pi=next(i for i,p in enumerate(s.platforms) if int(p)==4);off=s.offsets[pi][0];cl=s.compressedLengths[pi][0];dl=s.decompressedLengths[pi][0]
data=CompressionHelper.decompress_lz4(bytes(s.compressedBlob)[off:off+cl],dl);(out/'programs.bin').write_bytes(data)
count=struct.unpack_from('<I',data)[0];entries=[struct.unpack_from('<III',data,4+i*12) for i in range(count)]
for i,(off,size,seg) in enumerate(entries):
 (out/f'record-{i}.bin').write_bytes(data[off:off+size])
def plain(v):
 if dataclasses.is_dataclass(v): return {f.name:plain(getattr(v,f.name)) for f in dataclasses.fields(v)}
 if hasattr(v,'__attrs_attrs__'): return {f.name:plain(getattr(v,f.name)) for f in v.__attrs_attrs__}
 if isinstance(v,(list,tuple)): return [plain(x) for x in v]
 if isinstance(v,dict): return {k:plain(x) for k,x in v.items()}
 return v
(out/'shader.json').write_text(json.dumps(plain(s.m_ParsedForm),indent=2))
# Native bytecode stays under ignored obj/ and is never distributed.
dll=ctypes.WinDLL('d3dcompiler_47.dll');dll.D3DDisassemble.argtypes=[ctypes.c_void_p,ctypes.c_size_t,ctypes.c_uint,ctypes.c_char_p,ctypes.POINTER(ctypes.c_void_p)]
for i,(off,size,seg) in enumerate(entries):
 record=data[off:off+size];start=record.find(b'DXBC')
 if start<0: continue
 length=struct.unpack_from('<I',record,start+24)[0];bytecode=record[start:start+length];blob=ctypes.c_void_p()
 assert dll.D3DDisassemble(bytecode,len(bytecode),0,None,ctypes.byref(blob))==0
 vt=ctypes.cast(blob,ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
 ptr=ctypes.WINFUNCTYPE(ctypes.c_void_p,ctypes.c_void_p)(vt[3])(blob)
 text=ctypes.string_at(ptr).decode();(out/f'program-{i}.txt').write_text(text)
 ctypes.WINFUNCTYPE(ctypes.c_ulong,ctypes.c_void_p)(vt[2])(blob)
print('Records',entries)
for i,p in enumerate(s.m_ParsedForm.m_SubShaders[0].m_Passes):
 for stage in ['progVertex','progFragment']:
  prog=getattr(p,stage);print(i,stage,'parameterIndices',prog.m_ParameterBlobIndices)
  for group in prog.m_PlayerSubPrograms:
   for v in group:print('variant',v.m_BlobIndex,[s.m_ParsedForm.m_KeywordNames[k] for k in v.m_KeywordIndices])
