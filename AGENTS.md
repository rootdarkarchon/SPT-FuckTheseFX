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

## Subagent orchestration — cost-aware

### Objective

Optimize for **successful, validated engineering work per unit of model usage**, not the number of agents, amount of parallelism, or apparent thoroughness.

Use the cheapest model that can reliably complete a bounded task. Preserve rendering correctness, production/research boundaries and the quality of the final implementation.

Subagents are tools for isolating work, not mandatory participants in every task.

### 1. Primary-agent responsibility

The primary agent owns:

- Task interpretation and scope.
- Architectural decisions and shared interfaces.
- Decomposition and model selection.
- Assignment of file ownership.
- Integration of changes and conflicting findings.
- Final builds, appropriate validation and delivery.
- Explicit reporting of what has and has not been verified.

Before delegating, inspect enough of the relevant code to determine whether the task actually benefits from delegation.

Do not automatically spawn agents because a task mentions rendering, shaders, performance or multiple languages.

For small, localized changes, work directly.

### 2. Model routing

Use models according to actual task difficulty, not job title.

**Haiku — default for bounded work**

Prefer Haiku for:

- Codebase exploration, symbol and dependency discovery.
- Extracting shader properties or formulas from a specific shader family.
- Generating manifests, scripts and simple validation tools.
- Log analysis, test-result summaries and comparisons.
- Straightforward C#, C++ or HLSL edits with defined contracts.
- Small bug fixes, mechanical refactors and focused tests.
- Read-only review of narrow, well-understood changes.

Haiku is allowed to implement code. Do not artificially restrict it to research-only work.

**Sonnet — moderately complex engineering**

Prefer Sonnet for:

- Changes spanning several files or languages.
- Bounded but nontrivial rendering or shader implementation.
- Debugging that requires interpreting multiple interacting systems.
- Integrating results from Haiku investigations.
- Tests involving moderately complex numerical or stateful behavior.
- Independent review of changes with meaningful integration risks.

**Opus — difficult reasoning and high-risk integration**

Reserve Opus for:

- Renderer architecture and major design tradeoffs.
- Ambiguous failures after cheaper investigation.
- CUDA/D3D11 interoperability and synchronization.
- OptiX acceleration structure and memory-lifetime correctness.
- NRD signal contracts, temporal reprojection and resource ownership.
- Nontrivial rendering mathematics with uncertain assumptions.
- Critical cross-system integration and resolving conflicting findings.

Opus should not routinely perform mechanical searches or repeat straightforward investigations already completed by cheaper agents.

**Do not spawn an Opus subagent without a specific reason.** The primary Opus agent can perform critical integration itself.

Model selection is a heuristic, not a claim that Haiku is incapable of difficult work. Escalate based on observed limitations.

### 3. When to delegate

Delegate when at least one condition applies:

- Independent exploration would produce substantial intermediate context unnecessary to the primary agent.
- A narrow implementation can be completed and tested independently.
- Several genuinely independent questions can be investigated concurrently.
- A high-risk change needs an independent perspective.
- The specialist's work has a clear completion condition.

Otherwise, work directly.

Do not spawn agents merely to generate plans, restate findings, summarize a few lines, or validate trivial changes.

Do not delegate the same question to multiple agents by default.

### 4. Task decomposition and context budget

Prefer **small, independently verifiable work packages** over long-running general-purpose agents.

A normal subagent assignment should have:

1. One clearly defined objective.
2. A narrow subsystem or small set of relevant files.
3. Known interfaces and constraints.
4. An explicit permitted edit scope.
5. A concrete deliverable.
6. A stopping condition.

As a guideline, try to keep Haiku assignments within one narrow implementation or investigation, commonly involving a few files and manageable context.

Avoid agents that accumulate hundreds of thousands of tokens while repeatedly investigating unrelated issues.

Aim to finish an ordinary cheap-agent task well below approximately 50k tokens of accumulated context when practical. This is a soft operating target, not an enforceable accuracy or context limit.

If a task is growing substantially:

- Stop at a coherent checkpoint.
- Return findings, changed files and remaining work.
- Have the primary agent divide the remainder into smaller tasks.
- Use a fresh agent when the previous history is no longer needed.

Resume an existing agent only when retaining its context is genuinely more efficient than a fresh, focused assignment.

Do not repeatedly resume the same agent until its context becomes enormous.

### 5. Delegation instructions

Every delegated task must specify:

**Objective:** What problem to solve.

**Scope:** Relevant files, subsystems, and files that must not change.

**Known context:** Only facts necessary for the task, including required rendering contracts.

**Deliverable:** Exact findings, implementation or test result expected.

**Validation:** What must be checked and what evidence is sufficient.

**Stop condition:** When to return control, including blockers and escalation criteria.

Do not paste entire conversation histories, large previous reports or unrelated source files into delegation prompts.

Give file paths and concise summaries instead.

A subagent must distinguish verified source findings from hypotheses.

### 6. Execution limits

Use these defaults unless the task clearly justifies an exception:

- Start with one subagent.
- Run at most two independent subagents concurrently.
- Do not allow subagents to spawn additional subagents.
- Do not run redundant investigations in parallel.
- Do not automatically launch a reviewer after every small edit.
- Do not let multiple implementation agents modify the same files or shared contracts concurrently.
- Do not let different agents run competing GPU workloads against the same device, capture set or outputs.

Prefer serial execution when tasks have dependencies.

Parallel work is useful only when independence is real and the combined work is cheaper or meaningfully faster.

Do not use agent teams or recursive orchestration unless explicitly requested or independently justified.

### 7. Escalation policy

A cheap agent should stop and return a blocker when:

- The task requires a significant architectural change not in scope.
- Critical assumptions cannot be verified.
- The implementation requires changing shared GPU/ABI contracts unexpectedly.
- Repeated attempts fail for reasons the agent cannot explain.
- Continuing requires extensive unrelated investigation.
- It discovers a material synchronization, numerical or lifetime risk.

The primary agent decides whether to:

- Clarify the task and retry with Haiku.
- Assign a more capable Sonnet agent.
- Resolve the issue directly with Opus.
- Defer the issue as unrelated.

Do not promote every uncertainty immediately to Opus.

Do not allow an agent to expand its assignment indefinitely to work around a blocker.

### 8. Implementation ownership

Implementation agents must respect explicit file ownership.

Prefer one agent implementing a shared feature end to end when several tightly coupled files are involved.

Parallelize independent extraction, test generation and read-only research instead of having several agents simultaneously modify common renderer code.

The primary agent owns shared-interface changes and final integration.

Subagents must not independently install, package or publish builds unless explicitly assigned.

Preserve existing working behavior and A/B controls.

### 9. Risk-proportionate validation

Validation depends on the consequence of an error.

**Low risk**

Examples: documentation, manifests, narrow data extraction, simple configuration changes and mechanical transformations.

Use direct checks, build validation or relevant tests. Independent agent review is optional.

**Moderate risk**

Examples: material decoding, texture-atlas logic, ordinary shaders, scene collection and bounded rendering features.

Use targeted tests and an independent Sonnet review when the change has meaningful correctness or integration risks.

**High risk**

Examples: CUDA/D3D11 synchronization, memory/resource lifetime, OptiX GAS/IAS ownership, NRD ABI and signal contracts, temporal history, asynchronous packet retirement and major shared shader contracts.

Require appropriate independent verification and direct primary-agent review. Escalate to Opus-level reasoning when necessary.

Reviewers must focus on plausible defects in the actual change, not perform another open-ended project audit.

A review should return actionable findings, not a lengthy restatement of the implementation.

A review that finds no significant issues is a valid result. Do not manufacture additional work to justify the review.

### 10. FTFX-specific invariants

All agents must respect:

- Unity 2022 / EFT D3D11 rendering capabilities and limitations.
- Existing XeGTAO, SSR, GI, NRD and CustomAmbient contracts.
- Correct camera ordering, feedback prevention and GPU synchronization.
- Explicit resource ownership, retirement and generation lifetimes.
- Correct depth, motion, normal, colour-space and coordinate conventions.
- Existing configuration defaults and opt-in experimental paths.
- Current known-good implementations and comparison modes.
- Production versus isolated research boundaries.
- Protected input files and existing research evidence.

Never assume a renderer buffer, API, live geometry source or shader capability exists without evidence.

Do not introduce substantial additional render passes, allocations, readbacks or dependencies without justification.

Do not weaken critical correctness or resource-lifetime safeguards in pursuit of lower inference cost.

### 11. GPU testing and performance

GPU tests, EFT captures and benchmarks must be coordinated by the primary agent.

Do not run competing GPU jobs simultaneously when they can interfere with measurement or shared state.

Prefer small targeted fixtures before long live captures.

Distinguish:

- Offline correctness.
- Native/Unity fixture success.
- Actual live EFT visual behavior.
- Isolated GPU timing.
- Complete incremental GPU envelope.
- Whole-game performance.

Do not present theoretical savings as measured results.

Avoid running exhaustive benchmarks without a specific question they are intended to answer.

### 12. Subagent reporting

Subagents should return concise, decision-useful results.

Default report:

- **Result:** Completed, partial or blocked.
- **Finding/change:** What was established or modified.
- **Evidence:** Key file paths, source locations and tests.
- **Risks:** Important unresolved problems only.
- **Next action:** One concrete recommendation, if required.

Prefer a few paragraphs or a short structured summary. Return lengthy documentation only when explicitly requested.

Do not return full logs, giant source dumps, repeated build output or entire investigation transcripts.

The primary agent synthesizes results instead of concatenating agent reports.

### 13. Stop discipline

Once the requested behavior is implemented and appropriately validated, stop.

Do not automatically begin:

- Adjacent rendering improvements.
- New architecture proposals.
- Additional exploratory audits.
- Broader refactoring.
- Performance optimizations unrelated to the task.
- Another work package.

Report worthwhile follow-up ideas without implementing them unless authorized.

**Core principle: One accountable primary agent, small bounded cheap agents, targeted verification and no unnecessary work. Optimize for delivered results, not agent activity.**