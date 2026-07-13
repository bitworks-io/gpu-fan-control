# Release Runbook — v1.0.0

How to cut a release. The mechanism is **one action**: push a `v*` tag and CI does
the rest. Everything below is the discipline around that one action.

> **Status: v1.0.0 RELEASED (2026-07-13).** The Phase 5 hardware gate passed (Radeon RX
> 7900 XTX bench sign-off) and the signed installer shipped. This runbook remains the
> checklist for future releases (`v1.0.1`, etc.).

---

## What happens when you push a `v*` tag

`.github/workflows/build-windows.yml` triggers on `push` of a tag matching `v*`:

1. **`build`** (windows-latest): runs unit tests → `build.ps1` (publish + Inno Setup
   installer) → verifies `ADLXCSharpBind.dll` / `ADLXWrapper.dll` are present →
   uploads `LightweightAmdGpuFanControl-Setup.exe`. Signing steps are a gated no-op
   (skipped while the `AZURE_CLIENT_ID` secret is unset — see
   [signing-setup.md](signing-setup.md)).
2. **`release`** (ubuntu-latest, `if: startsWith(github.ref, 'refs/tags/v')`):
   downloads the installer artifact and creates a **GitHub Release** via
   `softprops/action-gh-release@v2`, attaching `Setup.exe`, with
   `generate_release_notes: true`.

The `build` job builds the installer **fresh from the tagged commit** — the release
binary is not any previously-downloaded artifact.

---

## Preconditions (ALL must be true before tagging)

- [ ] **Phase 5 hardware sign-off complete** — all 7 checks in
      [agent-handoff.md](agent-handoff.md) → "Remaining Work" pass on a real AMD
      Windows PC (fan ramp/hold at 65 °C, emergency 85%, exit→auto incl. two-GPU
      both-restore, no-admin fan-set, feedback links, ADL fallback if available).
      Record results in [verification-evidence.md](verification-evidence.md).
- [ ] **v1.0.0 product code is on the release line.** Decide the tag target below.
- [ ] **CI is green on the *exact commit the tag will point at*** — not on an older
      SHA. If you merge the feature branch to `main`, the merge commit is a new SHA;
      its push-to-`main` run must be green before you tag it. (The prior evidence —
      run `28714260647` on `8247ca0` — goes stale the moment a merge commit exists.)
- [ ] **Version is consistent at `1.0.0`** across all sources:
      ```sh
      grep -rn -E "1\.0\.0" VERSION \
        LightweightAmdGpuFanControl/LightweightAmdGpuFanControl.csproj \
        installer/LightweightAmdGpuFanControl.iss \
        LightweightAmdGpuFanControl/Forms/AboutForm.cs
      ```
      Expect: `VERSION` = `1.0.0`, csproj `<Version>1.0.0`, iss `#define MyAppVersion "1.0.0"`,
      AboutForm fallback `"1.0.0"`. (Single source of truth is `VERSION`; `build.ps1`
      feeds it to `dotnet publish -p:Version` and to `iscc /DMyAppVersion`.)
- [ ] **`CHANGELOG.md` exists on the commit being tagged** and its `[1.0.0]` heading
      is updated from "Unreleased (pending Phase 5)" to the release date.
      ⚠️ CHANGELOG.md is currently staged on the `wip/…` checkpoint branch; make sure
      it rides onto whatever commit you tag (see "Doc placement" below).
- [ ] **Working tree clean**, you are on the release commit, and the active git
      identity is **bitworks-io** (`origin` = `git@github.com-bitworks:bitworks-io/...`;
      `gh auth status` active account `bitworks-io`).

---

## Tag target — DECISION REQUIRED

**Option A (recommended): merge to `main`, tag `main`.**
`origin/main` (`4ab96d5`) does not yet contain the v1.0 product code. Merge
`feature/v1.0-release-readiness` (PR #3) → `main`, wait for the `main` CI run to go
green, then tag that merge commit. Releases come off the mainline — the conventional,
auditable path.

**Option B (alternative): tag the feature commit directly.**
Tag `8247ca0` (`feature/v1.0-release-readiness`, already CI-green) without merging
first. Faster, but releases from a non-`main` commit and leaves `main` behind the
release. Only do this deliberately.

> Do **not** tag the current `wip/handoff-20260706-1105` branch: it carries
> environment/methodology commits (`aae6ffc`, `44f4101`) that are not part of the
> shipped product.

---

## The one action

Once every precondition is met and the tag target is decided:

```sh
# From the chosen release commit (main merge commit, or 8247ca0):
git tag -a v1.0.0 -m "v1.0.0 — first public release"
git push origin v1.0.0
```

That push is the entire release trigger. Watch it:

```sh
gh run list --limit 3
gh run watch <run-id>
```

## Optional +1 step — curated release notes

The `release` job uses `generate_release_notes: true`. Because there is **no prior
tag**, GitHub walks the *entire* commit history — including WIP/checkpoint commits —
so the auto-generated notes will be noisy. To ship clean notes:

- After CI creates the release, edit the GitHub Release body and paste the `[1.0.0]`
  section from [CHANGELOG.md](../CHANGELOG.md) above (or in place of) the auto notes.
- **Why not automate it now?** The `release` job has no `actions/checkout`, so a
  `body_path:` pointed at a repo file would fail (the file isn't in that job's
  workspace). Wiring curated notes into CI means adding a checkout + section-extract
  step, and that change can only be proven by an actual tag push. If wanted, do it as
  a follow-up validated with a throwaway `v0.0.1-test` pre-release first — not as part
  of the v1.0.0 cut.

---

## Post-release verification

- [ ] Both CI jobs (`build`, `release`) conclude **success** for the tag run.
- [ ] The GitHub Release page for `v1.0.0` exists and has
      `LightweightAmdGpuFanControl-Setup.exe` attached.
- [ ] Download the released asset and confirm it is a real installer:
      ```sh
      gh release download v1.0.0 -p "*.exe" -D /tmp/rel-v100
      file /tmp/rel-v100/LightweightAmdGpuFanControl-Setup.exe   # → PE32 executable (GUI) ... MS Windows
      ```
- [ ] (Ideally) install the released Setup.exe on the AMD PC and re-confirm it
      launches and controls fans — closes the loop that the *released* binary, not
      just a CI artifact, works.

---

## Rollback

If the release is wrong, delete it and the tag, fix, and re-tag:

```sh
gh release delete v1.0.0 --yes
git push --delete origin v1.0.0
git tag -d v1.0.0
```

Re-pushing the same tag re-triggers the full build + release. (A tag is cheap to
retract *before* users download it; prefer getting preconditions right over relying
on rollback.)

---

## Doc placement (one-time, for this release)

`CHANGELOG.md` and this runbook were authored on `wip/handoff-20260706-1105`. As a
product artifact, `CHANGELOG.md` should live on the commit that gets tagged. Because
they are uncommitted working-tree files, they carry across `git checkout`, so the
simplest path is: check out the release branch (e.g. `feature/v1.0-release-readiness`
or `main`), then `git add CHANGELOG.md docs/release-runbook.md` there and commit
before tagging. The methodology docs (this runbook, handoff, evidence) may live on
either line, but CHANGELOG.md must be on the tagged commit.

## Signing caveat

The build is **unsigned by default** — Windows SmartScreen warns on first run. The
workflow has real, gated **Azure Artifact Signing** steps (`azure/login` +
`azure/artifact-signing-action`) that stay skipped until six `AZURE_*` repo secrets
are set; see [signing-setup.md](signing-setup.md) for the one-time Azure onboarding.
Provisioning the account is a separate follow-up; it does not block the v1.0.0 mechanics.
Note: even once signed, SmartScreen reputation still accrues per file over download volume.
