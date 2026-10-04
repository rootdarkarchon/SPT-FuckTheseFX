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
