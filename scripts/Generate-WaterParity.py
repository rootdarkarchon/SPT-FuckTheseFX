"""Regenerate the deliberately literal phase-one arithmetic from the pinned local audit.

Run Extract-WaterReference.py first. No native bytecode/assets enter the package.
Keep the baseline literal until live parity is accepted; later shading changes can
then be measured against it. Unsupported opcodes or unbound constants fail closed.
"""
import json,re,struct
from pathlib import Path
root=Path(__file__).resolve().parents[1];src=root/'obj/WaterValidation';out=root/'unity/Water'
out.mkdir(exist_ok=True,parents=True)
shader=json.loads((src/'shader.json').read_text());decls={};textures={}
lanes='xyzw'
def params(record):
 data=(src/f'record-{record}.bin').read_bytes();pos=0
 def integer():
  nonlocal pos
  v=struct.unpack_from('<i',data,pos)[0];pos+=4;return v
 def string():
  nonlocal pos
  n=integer();v=data[pos:pos+n].decode();pos=(pos+n+3)&~3;return v
 assert integer()==202012090
 count=integer()-1;assert [integer() for _ in range(4)]==[0]*4
 buffers={}
 for _ in range(count):
  name=string();size=integer();n=integer();v=[]
  for _ in range(n):
   key=string();typ,rows,cols,arr,unused,offset=[integer() for _ in range(6)]
   assert typ==arr==unused==0 and rows==1
   v.append((key,offset,cols,False))
  assert integer()==0
  buffers[name]=v
 tex={}
 for _ in range(integer()):
  name=string();unused,index,sampler,dim=[integer() for _ in range(4)]
  assert unused==0 and dim in [4,8]
  tex[index]=(name,sampler,dim);textures[name]=dim
 assert pos==len(data),(pos,len(data))
 return buffers,tex
def bindings(passid,stage,record):
 p=shader['m_SubShaders'][0]['m_Passes'][passid];names={i:n for n,i in p['m_NameIndices']}
 common=p[stage]['m_CommonParameters'];buffers,tex=params(record)
 for cb in common['m_ConstantBuffers']:
  v=buffers[names[cb['m_NameIndex']]]
  for key,mat in [('m_VectorParams',False),('m_MatrixParams',True)]:
   for item in cb[key]:v.append((names[item['m_NameIndex']],item['m_Index'],item.get('m_Dim',4),mat))
 mapped={}
 for cb in common['m_ConstantBufferBindings']:
  for name,offset,dim,matrix in buffers[names[cb['m_NameIndex']]]:
   decls[name]='float4x4' if matrix else ('float' if dim==1 else f'float{dim}')
   if matrix:
    for col in range(4):
     for row in range(4):mapped[(cb['m_Index'],offset//4+col*4+row)]=f'{name}[{row}][{col}]'
   else:
    for i in range(dim):mapped[(cb['m_Index'],offset//4+i)]=name+('' if dim==1 else '.'+lanes[i])
 return mapped,tex
def translate(program,passid,stage,record):
 mapped,tex=bindings(passid,stage,record);code=[]
 def operand(s,mask):
  neg=s.startswith('-');s=s.removeprefix('-');absolute=s.startswith('|');s=s.strip('|')
  if s.startswith('l('):
   values=s[2:-1].split(', ');v=[values[0] if len(values)==1 else values[lanes.index(c)] for c in mask]
  else:
   m=re.fullmatch(r'(r\d+|v\d+|cb(\d+)\[(\d+)\])\.([xyzw]+)',s);assert m,s
   reg,cb,index,swizzle=m.groups()
   selected=[swizzle[0] if len(swizzle)==1 else swizzle[lanes.index(c)] for c in mask]
   if cb is not None:v=[mapped[(int(cb),int(index)*4+lanes.index(c))] for c in selected]
   else:v=[reg+'.'+c for c in selected]
  expr=v[0] if len(v)==1 else f'float{len(v)}('+', '.join(v)+')'
  if absolute:expr=f'abs({expr})'
  if neg:expr='-('+expr+')'
  return expr
 for raw in (src/f'program-{program}.txt').read_text().splitlines():
  raw=raw.strip()
  if not raw or raw.startswith('//') or raw in ['vs_4_0','ps_4_0','ret'] or raw.startswith('dcl_'):continue
  op,args=raw.split(' ',1);a=re.split(r', (?![^()]*\))',args);dst=a.pop(0);mask=dst.split('.')[1]
  sat=op.endswith('_sat');op=op.removesuffix('_sat')
  v=lambda i,m=mask:operand(a[i],m)
  if op in ['sample','sample_b','sample_l']:
   tm=re.fullmatch(r't(\d+)\.([xyzw]+)',a[1]);name,sampler,dim=tex[int(tm[1])];assert a[2]==f's{sampler}'
   method={'sample':'Sample','sample_b':'SampleBias','sample_l':'SampleLevel'}[op]
   extra='' if op=='sample' else ', '+v(3,'x')
   expr=f'{name}.{method}(sampler{name}, {v(0,"xyz" if dim==8 else "xy")}{extra}).'+''.join(tm[2][lanes.index(c)] for c in mask)
  elif op in ['dp2','dp3','dp4']:expr=f'dot({v(0,lanes[:int(op[-1])])}, {v(1,lanes[:int(op[-1])])})'
  elif op in ['add','mul','div']:expr=f'{v(0)} '+{'add':'+','mul':'*','div':'/'}[op]+f' {v(1)}'
  elif op=='mad':expr=f'mad({v(0)}, {v(1)}, {v(2)})'
  elif op=='mov':expr=v(0)
  elif op=='movc':
   terms=[f'({v(0,c)} != 0 ? {v(1,c)} : {v(2,c)})' for c in mask];expr=terms[0] if len(terms)==1 else f'float{len(terms)}('+', '.join(terms)+')'
  elif op in ['lt','ge']:expr=f'({v(0)} '+{'lt':'<','ge':'>='}[op]+f' {v(1)})'
  elif op in ['min','max']:expr=f'{op}({v(0)}, {v(1)})'
  elif op in ['sqrt','rsq','log','exp','frc']:expr=f'{dict(rsq="rsqrt",log="log2",exp="exp2",frc="frac").get(op,op)}({v(0)})'
  else:raise ValueError(raw)
  code.append('    '+dst+' = '+('saturate('+expr+')' if sat else expr)+';')
 return '\n'.join(code)
vs=translate(1,0,'progVertex',0);dry=translate(5,0,'progFragment',2);rain=translate(6,0,'progFragment',3)
# Snow removes rain; symbolic bindings must make all otherwise identical variants exact.
assert translate(7,0,'progFragment',4)==dry
assert translate(8,0,'progFragment',4)==dry
for p,base,param in [(9,dry,2),(10,rain,3),(11,dry,4),(12,dry,4)]:
 hdr=translate(p,0,'progFragment',param)
 assert hdr==base.replace('r0.xyz = float3(r1.w, r1.w, r1.w) * float3(r0.x, r0.y, r0.z);','o3.xyz = float3(r1.w, r1.w, r1.w) * float3(r0.x, r0.y, r0.z);').replace('    o3.xyz = exp2(-(float3(r0.x, r0.y, r0.z)));\n',''),p
def hdr_switch(s):return s.replace('    o3.xyz = exp2(-(float3(r0.x, r0.y, r0.z)));', '#if defined(UNITY_HDR_ON)\n    o3.xyz = r0.xyz;\n#else\n    o3.xyz = exp2(-r0.xyz);\n#endif')
normal_boundary='    r0.w = _ProjectionParams.z / v6.z;'
assert dry.split(normal_boundary)[0]==rain.split(normal_boundary)[0]
def normal_switch(s):
 prefix,rest=s.split(normal_boundary)
 return '#if defined(FTFX_WATER_MULTISCALE)\n    r0.xyz = WaterMultiscaleNormal(input.world);\n#else\n'+prefix+'#endif\n'+normal_boundary+rest
builtins={'_Time','_WorldSpaceCameraPos','_ProjectionParams','_ZBufferParams','unity_CameraToWorld','unity_ObjectToWorld','unity_MatrixV','unity_MatrixVP'}
header='// Phase-one EFT 40743 parity arithmetic. Regenerated by scripts/Generate-WaterParity.py.\n// Symbolic native bindings; original assets/bytecode are local validation inputs only.\n#include "UnityCG.cginc"\n'
header+='\n'.join(f'{typ} {name};' for name,typ in sorted(decls.items()) if name not in builtins)+'\n'
header+='\n'.join(f'Texture{"Cube" if dim==8 else "2D"}<float4> {name}; SamplerState sampler{name};' for name,dim in sorted(textures.items()))+'\n'
header+='#if defined(FTFX_WATER_MULTISCALE)\n#include "WaterNormals.cginc"\n#endif\n'
header+='''struct WaterVaryings {
    float4 position : SV_POSITION;
    float4 normals : TEXCOORD0;
    float4 detailRipple : TEXCOORD1;
    float4 foam : TEXCOORD2;
    float4 screen : TEXCOORD3;
    float3 world : TEXCOORD4;
    float3 view : TEXCOORD5;
};
struct WaterTargets {
    float4 diffuse : SV_Target0;
    float4 specular : SV_Target1;
    float4 normal : SV_Target2;
    float4 emission : SV_Target3;
    float snow : SV_Target4;
};
WaterVaryings WaterVertex(float4 v0 : POSITION) {
    float4 r0=0,r1=0,r2=0,r3=0;
    float4 o0=0,o1=0,o2=0,o3=0,o4=0,o5=0,o6=0;
'''+vs+'''
    WaterVaryings result;
    result.position=o0; result.normals=o1; result.detailRipple=o2;
    result.foam=o3; result.screen=o4; result.world=o5.xyz; result.view=o6.xyz;
    return result;
}
WaterTargets WaterFragment(WaterVaryings input) {
    float4 v0=input.position, v1=input.normals, v2=input.detailRipple,
        v3=input.foam, v4=input.screen, v5=float4(input.world,0), v6=float4(input.view,0);
    float4 r0=0,r1=0,r2=0,r3=0,r4=0,r5=0,r6=0,r7=0,r8=0,r9=0,r10=0,r11=0;
    float4 o0=0,o1=0,o2=0,o3=0,o4=0;
#if defined(Rain) && !defined(WINTER_SNOW)
'''+normal_switch(hdr_switch(rain))+'''
#else
'''+normal_switch(hdr_switch(dry))+'''
#endif
    WaterTargets result;
    result.diffuse=o0; result.specular=o1; result.normal=o2;
    result.emission=o3; result.snow=o4.x;
    return result;
}
'''
(out/'WaterParity.cginc').write_text(header)
print('Generated symbolic parity shader; all eight native fragment variants reconciled.')
