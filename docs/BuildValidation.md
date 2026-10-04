# Build and validation

Run commands from the repository root in PowerShell. Use the matched SPT 4.1 / EFT 40743 installation (default `D:\Tarkov-SPT-4.1`), Unity 2022.3.43f1 with a working editor license, D3D11 and the Windows C++ build tools. NRD requires CMake 3.30 or later and FXC; `native/NRD/CMakeLists.txt` also searches Windows SDK 10.0.26100.0. The managed project uses .NET SDK and exact local game references.

## NRD source and native build

The upstream checkout is intentionally ignored. For a fresh checkout, populate it at the revision recorded in `native/NRD/UPSTREAM.json`:

```powershell
git clone --no-checkout https://github.com/NVIDIA-RTX/NRD.git native/NRD/vendor/NRD
git -C native/NRD/vendor/NRD checkout c3d8999f8b30dca3b1c07d9aeab235b29d9b1a28
./scripts/Build-Nrd.ps1
```

The clone commands are for first-time setup; reuse an existing pinned checkout. Keep upstream unmodified. The build verifies its revision and produces `obj/NRD/bridge/Release/FTFXNrd.dll`, including upstream DXBC and local pack/unpack adapters. CMake needs network access for its upstream dependencies on first configuration. NRD, MathLib and ShaderMakeBlob licenses are taken from these build inputs for packaging.

## SSR regression and shader bundles

Use Python 3.12 with numpy and OpenEXR. The replay helper also searches `obj/ssr-analysis-deps`; native Python extensions must match the interpreter. Pass the interpreter explicitly if `python` resolves to another application:

```powershell
./scripts/Test-StochasticSSR.ps1 -ReplayPython 'C:\path\to\python.exe'
```

The current suite uses `BeforeNrdStandard` as its only historical rendering baseline. It executes the actual NRD native backend, compares synthetic output, checks routing/configuration/resource lifetime and rebuilds `Assets/stochasticssr` and `Assets/nrdssr`. It also requires three private local capture folders under the selected installation's `BepInEx/FTFX-SSR-Captures`:

- `20261002-231147-581`
- `20261003-032313-165`
- `20261002-215552-280`

Each needs `settings.txt` and `scene-source.exr`, `gbuffer0.exr`, `gbuffer1.exr`, `gbuffer2.exr`, `trace-depth.exr` and `water.exr`. These inputs are not shipped or stored in Git. A fresh checkout without them cannot run the complete capture parity suite; do not substitute unrelated captures and claim the same regression. The fixture reads projection and camera transforms from their settings.

## Water reference and validation

Use Python with UnityPy for reference extraction. The extraction verifies the installed water asset hash and writes local native shader oracles under `obj/WaterValidation`:

```powershell
& 'C:\path\to\python.exe' ./scripts/Extract-WaterReference.py --game 'D:\Tarkov-SPT-4.1'
./scripts/Test-Water.ps1
```

`Generate-WaterParity.py` regenerates the checked-in arithmetic from the extracted audit; run it only when intentionally updating that source, then rerun water validation. Ordinary bundle builds use the checked-in shader sources.

## Managed build and package

After SSR and water validation, build the runtime package:

```powershell
./scripts/Build-Package.ps1
```

Packaging builds the managed DLL, runs the FX/configuration/ABI contracts, verifies validated source/bundle identities and checks every ZIP entry's hash. NRD's native DLL and adapter bundle are mandatory package inputs. Runtime packages contain only DLLs, assets and required licenses. The script does not install into the game or publish a release.

Every FuckTheseFX runtime package is written to `bin/Release/FuckTheseFX-<semver>-SPT4.1-<working-tree hash>.zip`, using the verified built plugin version and a deterministic 12-character SHA-256 digest of current tracked and non-ignored untracked file paths and contents. The hash includes uncommitted edits, renames and deletions, excludes ignored build outputs, and is independent of staging state. This exact filename is required for release and testing builds and must be preserved when copying or delivering the ZIP. Custom suffixes and alternative names are prohibited; packaging fails if the working tree cannot be hashed or changes during the build. See [repository instructions](../AGENTS.md).

Validation projects, native oracles and receipts live under ignored `obj/`; packages live under ignored `bin/`. Source changes require fresh relevant validation before packaging. GPU fixtures establish rendering/resource contracts, not live raid appearance or frame-time improvement. Native SSR profiling and the separate research companion remain available for targeted investigations.
