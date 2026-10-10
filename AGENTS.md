# Repository instructions

## Build artifacts

Every FuckTheseFX runtime ZIP, including release and testing builds, must be named exactly:

`FuckTheseFX-<semver>-SPT4.1-<working-tree hash>.zip`

- Use the verified built plugin's semantic version for `<semver>`.
- Use a deterministic 12-character lowercase SHA-256 digest of the current working tree for `<working-tree hash>`, including tracked edits and non-ignored untracked files. Enumerate with `git ls-files -z --cached --others --exclude-standard`, sort paths ordinally, and hash the UTF-8 manifest of NUL-delimited paths and uppercase per-file SHA-256 hashes. Omit deleted files and exclude ignored outputs such as `bin/` and `obj/`.
- Build packages with `scripts/Build-Package.ps1`; retain its generated filename when copying or delivering the artifact.
- Do not add descriptive suffixes, timestamps, configuration labels, dirty markers, or alternative names. Do not substitute the Git HEAD commit hash for the working-tree hash.
- If the working tree cannot be hashed or changes during the build, fail packaging instead of inventing a fallback name.

This rule applies to FuckTheseFX runtime packages; separate profiling and research companion plugins retain their own identities.

## Subagent orchestration

Use Codex subagents proactively for substantial FTFX development, investigation, auditing, and validation work. Prefer parallel independent investigation over having the primary agent sequentially inspect unrelated subsystems.

The goal is to improve technical depth, catch mistakes independently, and shorten complex investigations—not to generate unnecessary agents or duplicate work.

### When to delegate

Consider subagents whenever a task involves:

- Multiple independent rendering subsystems or source areas.
- Complex GPU algorithms, numerical correctness, or performance analysis.
- Investigating bugs with several plausible causes.
- Changes spanning C#, Unity/HLSL, native D3D11/NRD, and validation infrastructure.
- Architectural decisions benefiting from independent critical review.
- Substantial implementation work with separable components.
- Reviewing a completed implementation for regressions, unsupported assumptions, or missing tests.

Small, localized, straightforward tasks do not require delegation.

### Suggested specialization

Select relevant specialist roles according to the task rather than automatically spawning every role:

**Rendering / shader investigation**
- Examine HLSL, compute kernels, Hi-Z traversal, GGX sampling, AO, SSR, GI, denoising inputs, and lighting mathematics.
- Investigate correctness, coordinate spaces, depth conventions, sampling distributions, numerical stability, and visual artifacts.
- Distinguish algorithmic limitations from implementation defects.

**Unity / C# integration**
- Examine camera lifecycle, command buffers, render ordering, resource management, shader bindings, configuration, and Unity/D3D11 interoperability.
- Verify allocation lifetime, resets, synchronization, and compatibility with the actual Tarkov rendering pipeline.
- Identify integration problems that isolated shader tests may miss.

**Native / NRD integration**
- Examine native bridge code, ABI contracts, resource formats, motion vectors, radiance packing, hit-distance normalization, and denoiser configuration.
- Verify D3D11 compatibility and consistency between managed, shader, and native implementations.

**Validation / GPU performance**
- Independently inspect test coverage, fixture realism, benchmark methodology, and GPU captures.
- Look for false-positive tests, incomplete comparisons, measurement contamination, and undocumented assumptions.
- Distinguish complete frame/GI cost from individual GPU pass timings.
- Never extrapolate fixture performance into live EFT performance without supporting evidence.

**Independent adversarial review**
- Challenge the proposed design or implementation.
- Search specifically for correctness failures, regressions, concurrency problems, light leaks, feedback loops, history invalidation issues, and misleading diagnostics.
- Do not merely confirm the primary agent's conclusions.

### Delegation rules

1. Give every subagent a specific objective, relevant files, constraints, and expected deliverables.
2. Prefer independent, non-overlapping investigations that can run concurrently.
3. Assign explicit file ownership before delegating implementation. Do not allow concurrent agents to modify the same files or shared contracts without coordination.
4. Use read-only investigation/review agents when parallel edits cannot be safely isolated.
5. Do not delegate trivial subtasks merely to increase parallelism.
6. Avoid recursive or uncontrolled agent spawning.
7. Require evidence from actual source code, tests, captures, or documented APIs. Hypotheses must be labeled as hypotheses.
8. Subagents must report relevant file locations, findings, changes, validation performed, unresolved questions, and risks.
9. The primary agent must review and reconcile subagent findings before integrating changes.
10. Disagreement between agents is a signal to investigate, not something to resolve by majority vote.

### FTFX-specific constraints

Subagents must respect the actual renderer architecture:

- Tarkov's Unity rendering pipeline and D3D11 capabilities.
- Existing XeGTAO, SSR, Contact GI, BroadSSGI, world-cache, and NRD integration contracts.
- Camera ordering, source capture timing, and feedback prevention.
- Shader resource formats, dimensions, coordinate spaces, and lifetime.
- Existing configuration defaults, presets, and opt-in experimental behavior.
- Existing GPU performance requirements and visual-quality expectations.

Never assume a native renderer feature, buffer, API, or geometry representation exists merely because it would simplify an implementation.

Do not introduce additional render passes, persistent resources, denoisers, CPU/GPU transfers, or external dependencies without a concrete justification.

Preserve working rendering paths and maintain comparable A/B controls.

### Independent verification

For substantial changes, prefer assigning at least one independent reviewer that did not implement the change.

Reviewers should actively look for:

- Incorrect mathematical or physical assumptions.
- Hidden coupling between rendering passes.
- Shader/C# or shader/native contract mismatches.
- Invalid depth, motion, normal, or coordinate-space handling.
- GPU races and resource-state/lifetime errors.
- Temporal artifacts and inappropriate history reuse.
- Unexpected allocation or bandwidth overhead.
- Tests that validate the implementation against itself rather than an independent reference.
- Regressions in existing rendering modes.
- Claimed performance improvements unsupported by controlled measurements.

A successful build or passing fixture is not proof of visual correctness in EFT.

### Validation and integration ownership

The primary agent remains responsible for:

- Overall architecture and integration decisions.
- Reconciling conflicting findings.
- Maintaining unchanged behavior outside the requested scope.
- Running appropriate final builds and regression tests.
- Ensuring benchmark and capture comparisons are meaningful.
- Reporting what has and has not been validated.
- Producing the final testing package when requested.

Do not run competing GPU benchmarks or validation jobs against shared fixture outputs simultaneously. Coordinate those runs or isolate their environments and output paths.

Subagents must not independently package, install, or deliver runtime builds unless explicitly assigned. All runtime packaging must obey the existing AGENTS.md artifact-naming rules and use the repository's prescribed packaging workflow.

### Reporting

The final response should synthesize the results rather than concatenate subagent reports.

Summarize:

- What was investigated and implemented.
- Significant findings and architectural decisions.
- Important issues raised by independent review.
- Tests and measurements actually completed.
- Remaining limitations and uncertainty.
- Files changed and any requested testing artifacts.

Be concise but technically specific.

Never invent validation, claim tests passed without running them, or present theoretical performance improvements as measured results.

**Core principle:** Use subagents to investigate independently and challenge assumptions. Keep one accountable primary agent responsible for the correctness, coherence, and performance of the final FTFX implementation.