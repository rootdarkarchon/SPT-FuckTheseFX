# SSR source cleanup after NRD standardization

The pre-removal archive remains unchanged. Its `implementation.zip` already contains exact copies of all 24 retired root validation C# files; byte equality was checked before removing those loose files. A supplemental archive at `unity/StochasticSsrValidation/Baseline/BeforeNrdStandard/CleanupSupplement` preserves the retired profiler-test entry point, three obsolete fixture shaders, native diagnostic export and pre-cleanup helper/pipeline sources, with SHA-256 hashes. Nothing in these archives is shipped.

## Runtime changes

- First-rejection `TraceStop` is no longer part of ordinary allocations/readiness. `FTFX_REJECTION_DIAGNOSTICS` compiles its writes and binding only for explicit diagnostics or the RejectionLocation view. Captures retain the texture through asynchronous readbacks and resize; lost targets recreate. This saves one ARGBFloat trace-size allocation in ordinary rendering: 9 MiB at R768 or 64 MiB at R2048. Ray sequence, continuation classification and budgets do not depend on this target.
- `TraceInfo` remains mandatory because completed-sky continuation reads its counters/flags. Raw hit guides, hit position, classification, NRD guides/histories, receiver-colour fallback and both reconstruction options remain.
- The late raster buffer shrinks from 29 to 16 float4 rows. Previous-receiver transformation/inverse/jitter-history uploads and the unused SsrRasterHistory/SsrHistoryUV shader helpers are gone. NRD still receives its actual current/previous transforms and jitter through its native frame packet. Historical camera metadata is retained.
- Dead OctDecode and the unused ReceiverWeight overload are gone. The reflection uniform now carries only cutoff/debug view; unused custom-history components are removed. The redundant pre-trace support-cutoff upload is removed; native receiver-fallback support still uses its independent threshold.
- The native FTFX_NrdTexture export and managed DiagnosticTexture/import, used only by the old comparison fixture, are removed. The NRD algorithm and input adapter are unchanged; the native bridge is rebuilt.
- Redundant capture rearming and stale comments are cleaned up. The black-only EnvironmentFallback debug item is retired; saved name/number 17 resets to Normal, with surviving IDs and settings preserved.

## Validation tooling

The current root suite is NrdStandardValidation. The independent native material-response audit remains usable. The 24 old partial/comparison fixtures and three orphaned fixture shaders are retained in the historical archives. Test-SsrProfilerInterop.ps1 is retired because it copied old fixtures against removed runtime types; it is not silently replaced by a test claiming equivalent profiler coverage. Historical reports still document that earlier experiment. Native SSR profiling and the separate GPU timing/research companion remain available.

Current validation passed 1,559 checks with the actual D3D11/DXBC NRD backend. Raw trace/classification, current resolve, NRD output and composition match the archived NRD renderer exactly for synthetic sequences and three R768 captures. Both reconstruction options and temporal on/off are covered. New checks cover ordinary missing diagnostic targets, diagnostic/traversal variant parity, ray-phase preservation, capture rearm, first-rejection data preservation, lost targets and pending-readback resize retirement. Configuration and package contracts verify removed helpers/options and validated source/bundle/native-DLL hashes.

GPU timing was not collected while the user was gaming. The texture memory reduction is a format/extent calculation, not a measured frame-time gain. Live visual acceptance remains separate from fixture parity.

## Research-tree cleanup

The disconnected seven older baseline directories, five experiment files, historical SSR/water reports, raw validation exports and five obsolete analysis/replay scripts are preserved in `research-archive/ssr-water-research-20261003.zip`. This local archive is deliberately ignored by Git and excluded from packages. Keep or back it up separately when retaining investigation history; it is not a dependency for a fresh source checkout.

All 519 entries listed in `archive-manifest.json` were SHA-256 verified before removing 514 loose files. The other five entries preserve the pre-edit current guides and receiver-fallback source. Archive SHA-256: `283A71C2DB072C47FB4DA1CEF6543409E3EE50EEAD503F8DAC70DDFD15D07104`. Relative paths inside the archive match their original repository paths. Extract selected entries into a separate directory for inspection or restoration; historical defaults and harnesses must not be overlaid on the current runtime indiscriminately.

`BeforeNrdStandard`, its original `implementation.zip`/manifest and `CleanupSupplement` remain in the source tree for regression and restoration. The native material-response audit, water extraction/generation tools, current water fixtures and separate profiling tools remain. The obsolete DirectFinalMip A/B field is removed from production; its former default-true path is fixed without changing rendering or configuration.

Current documentation is [SSR behavior](StochasticSSR.md), [water surface](Water.md), [rendering contracts](SSR-reflection-contract.md), [build preparation](BuildValidation.md) and [declined implementations](SSR-declined-implementations-20261003.md). Original historical documents remain readable inside the local research archive, with their evidence and internal paths intact.

After fixing the receiver-fallback path and pruning the research tree, the fresh NRD regression passed all 1,559 checks. The Release build, all package contracts, 100 configuration checks and all 12 package-entry hashes passed. The rebuilt `FuckTheseFX-1.7.0-SPT4.1-ssr-cleanup.zip` has SHA-256 `08F2B69E55759E776BCC9FB630D17B89B80E591F037D39AB01A3B69849CD944A`. Sandbox Unity/SDK access failures were resolved by rerunning the same checks with normal access. No game installation or live GPU benchmark was performed.
