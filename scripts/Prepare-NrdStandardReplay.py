"""Lossless linear inputs for the NRD-only removal regression; no GPU profiling."""
import argparse, struct, sys
from pathlib import Path
local_deps = Path(__file__).resolve().parents[1] / 'obj/ssr-analysis-deps'
if local_deps.exists(): sys.path.insert(0, str(local_deps))
import numpy as np
import OpenEXR
parser=argparse.ArgumentParser()
parser.add_argument('--captures',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
for capture in ('20261002-231147-581','20261003-032313-165','20261002-215552-280'):
    target=args.output/capture; target.mkdir(parents=True,exist_ok=True)
    for name in ('scene-source','gbuffer0','gbuffer1','gbuffer2','trace-depth','water'):
        pixels=next(iter(OpenEXR.File(str(args.captures/capture/(name+'.exr'))).channels().values())).pixels
        if pixels.ndim!=3 or pixels.shape[2]!=4 or not np.isfinite(pixels).all(): raise ValueError(f'Invalid capture: {capture}/{name}')
        height,width,_=pixels.shape
        (target/(name+'.rgba32f')).write_bytes(struct.pack('<ii',width,height)+np.asarray(pixels[::-1],dtype='<f4').tobytes())
    print('Prepared NRD regression:',capture)
