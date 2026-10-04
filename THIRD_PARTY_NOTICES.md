# Third-party notices

The root [MIT License](LICENSE) applies to FTFX's own code. Third-party source, assets and binaries retain their respective copyright notices and license terms.

| Component | Use in FTFX | License and source |
| --- | --- | --- |
| Intel XeGTAO | Ambient occlusion estimator | MIT; [bundled license](unity/Assets/XeGTAO/Upstream/LICENSE) and [upstream project](https://github.com/GameTechDev/XeGTAO). |
| AMD FidelityFX SSSR | Adapted GGX sampling and blue-noise sequence | MIT; [bundled license](unity/StochasticSSR/AMD/SSSR-LICENSE.txt), [attribution](unity/StochasticSSR/AMD/NOTICE.md) and [pinned source metadata](unity/StochasticSSR/AMD/UPSTREAM.json). |
| NVIDIA NRD 4.18.0 | Specular and diffuse denoising through the FTFX native bridge | NVIDIA RTX SDKs license; [license at the pinned revision](https://github.com/NVIDIA-RTX/NRD/blob/c3d8999f8b30dca3b1c07d9aeab235b29d9b1a28/LICENSE.txt) and [source metadata](native/NRD/UPSTREAM.json). |
| NVIDIA MathLib | NRD build dependency | MIT; license copied from the pinned NRD build inputs. |
| NVIDIA ShaderMakeBlob | NRD shader build dependency | MIT; license copied from the pinned NRD build inputs. |

## Runtime license files

`scripts/Build-Package.ps1` includes the following under `BepInEx/plugins/FuckTheseFX`:

- `LICENSE` — FTFX
- `Licenses/XeGTAO.txt`
- `Licenses/StochasticSSR/SSSR-LICENSE.txt`
- `Licenses/NRD.txt`
- `Licenses/NRD-MathLib.txt`
- `Licenses/NRD-ShaderMakeBlob.txt`

NRD's source checkout and generated dependency directories are local build inputs rather than tracked repository content. See [build and validation](docs/BuildValidation.md) for setup. The root MIT license does not replace NRD's SDK terms.
