## Summary

What this changes and why. One logical change per pull request.

Closes #<!-- the issue a core contributor approved; pull requests without one are closed unreviewed -->

## Spec

Link the spec-driven artifacts if this is non-trivial work (skip for small fixes):

- Spec: `specs/<slug>/spec.md`
- Tasks: `specs/<slug>/tasks.md` — all boxes checked, `/speckit-converge` reported Converged

## Checklist

- [ ] Linked issue carries the `approved` label (or `good first issue` / `help wanted`), and this PR stays within its scope
- [ ] Base branch is `develop` (not `main`)
- [ ] `scripts/build.sh` (or `scripts/build-linux.sh` / `scripts/build-win.ps1`) passes — it runs the smoke test
- [ ] Built and tested on: [ ] macOS [ ] Windows [ ] Linux
- [ ] New source files carry the AGPL-3.0-only SPDX header
- [ ] New dependency, if any: license checked and listed in `LICENSES/third-party.md`
- [ ] User-visible change: `CHANGELOG.md` `[Unreleased]` entry added
- [ ] User-visible capability change: `FEATURES.md` and the user docs updated (nota-features / nota-docs skills)
- [ ] Engine behaviour change: smoke test extended to cover it
