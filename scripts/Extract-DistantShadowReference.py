"""Create a LOCAL TEST ONLY native shader bundle. Requires UnityPy; never package it."""
import argparse
import hashlib
from pathlib import Path
import UnityPy

parser = argparse.ArgumentParser()
parser.add_argument('--game', default='D:/Tarkov-SPT-4.1')
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
resource = Path(args.game) / 'EscapeFromTarkov_Data/resources.assets'
with resource.open('rb') as stream:
    digest = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
if digest != '78F26AFBB461338ADC1DBE13F9E5006EE5D35ED5AE55E4BEE624EF62AF4E5FE0':
    raise RuntimeError('Unsupported installed shader assets')
source = UnityPy.load(str(resource))
shader = next(obj for obj in source.objects if obj.type.name == 'Shader'
              and obj.read().m_ParsedForm.m_Name == 'EFT/DistantShadowComputeMask')
template = UnityPy.load(str(repo / 'Assets/distantshadows'))
targets = [obj for obj in template.objects if obj.type.name == 'Shader']
if len(targets) != 1:
    raise RuntimeError('Expected one shader in the existing bundle template')
targets[0].set_raw_data(shader.get_raw_data())
output = repo / 'obj/DistantShadowValidation/native-reference'
output.parent.mkdir(parents=True, exist_ok=True)
output.write_bytes(next(iter(template.files.values())).save())
print(output)
