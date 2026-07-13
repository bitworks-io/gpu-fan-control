# Code-Signing Setup — Azure Artifact Signing

How to give this app an Authenticode signature so Windows stops flagging it as an
unknown publisher. The CI pipeline is already wired for it; this doc covers the
one-time Azure onboarding **you** must do (it can't be automated) and the repo
secrets that switch signing on.

> **Naming:** the service is now called **Azure Artifact Signing**. It is a straight
> rebrand (≈Jan 2026) of **Azure Trusted Signing**, which was itself the GA name for
> **Azure Code Signing**. Same service, same `Microsoft.CodeSigning` Azure resource
> provider, same certificates. In the portal you create an **"Artifact Signing
> account"** — that is the correct, current resource. Nothing to do with container/OCI
> image signing (Notary/cosign); this is Windows PE Authenticode signing.

---

## 0. Read this first — the eligibility gate

Before spending any time, confirm all of these. If any fails, signing via this
service is blocked and the rest of this doc is moot until it's resolved.

| Requirement | Detail |
|---|---|
| **Paid Azure subscription** | Free / trial / MSDN-sponsored subscriptions are **rejected** at account creation. Must be pay-as-you-go or EA. |
| **Country of domicile** (for a *public* cert) | **Public Trust** certs — the kind that work for public distribution — are currently available only to **organizations in USA / Canada / EU / UK**, and to **individual developers in USA / Canada**. |
| **Identity you can prove** | Either an **organization** (business registration/incorporation docs, matching domain) or an **individual developer** (government photo ID + liveness check via Microsoft Authenticator / Verified ID). |

**For Bitworks specifically:** the deciding input is Bitworks' legal country. If Bitworks is
US/Canada-based, the **individual-developer path** is the fastest route and sidesteps the
historical "organization must be 3+ years old" restriction (that age requirement is not
present in the current Microsoft docs, but treat any age-related rejection during validation
as authoritative). If Bitworks is outside USA/Canada/EU/UK, only **Private Trust** certs are
available — those do **not** chain to a public root and are useless for public distribution,
so signing via this service would not help end users.

**Cost:** Basic tier ≈ **$9.99 / month** (includes 5,000 signatures/mo; $0.005 each after);
Premium ≈ **$99.99 / month** (100,000/mo). Billing is **not** pro-rated — you pay the full
month regardless of when in the cycle you create the account. *(Verify current numbers on the
[pricing page](https://azure.microsoft.com/pricing/details/artifact-signing/) before committing.)*

**What signing does and doesn't buy you:** it removes the "Unknown Publisher" experience
and lets SmartScreen/AV chain the binary to a Microsoft-managed CA. It does **not** grant
instant SmartScreen trust — reputation still accrues per file hash over real download volume,
so the "Windows protected your PC" prompt can persist on a brand-new signed release until the
file has been downloaded enough. Artifact Signing does **not** issue EV certificates.

---

## 1. Manual steps (Azure portal — one time)

These are portal/Azure-side and cannot be scripted end-to-end (identity validation is
interactive by design). Do them once with an account that is an **Owner** (or has
User Access Administrator) on the subscription so you can also assign roles in step 6.

1. **Register the resource provider.** The provider is still named **`Microsoft.CodeSigning`** —
   the technical namespace was *not* rebranded to "Artifact Signing," so searching the provider
   list for "artifact" or "signing" finds nothing. Search **`codesigning`** instead.
   - *Portal:* **Subscriptions** → select your subscription → **Settings → Resource providers**
     → search box: **`codesigning`** → select **Microsoft.CodeSigning** (status shows
     *NotRegistered*) → **⋯ → Register**. (This blade is per-subscription — make sure the right
     subscription is selected.)
   - *Or skip it:* creating the Artifact Signing account (step 2) auto-registers the provider.
   - *Reliable CLI (authoritative — bypasses the portal search entirely):*
     ```bash
     az account set -s <subscription-id>
     az provider register --namespace Microsoft.CodeSigning
     az provider show --namespace Microsoft.CodeSigning --query registrationState -o tsv
     ```
     Wait until it prints `Registered` (it briefly shows `Registering`).

   If it's still missing: confirm the subscription is **paid** (free/trial/sponsored are blocked
   and can hide/deny the resource), and that your account has **Contributor or Owner** on it —
   registering a provider needs subscription-level write access.

2. **Create the Artifact Signing account.** Portal search → **"Artifact Signing accounts"**
   → *Create*. Pick a **region** from the supported list (e.g. East US, West US 2, West
   Europe, North Europe — the account's region sets your signing **endpoint** URL, see below)
   and a **pricing tier** (Basic is fine for this app's volume).

3. **Create an identity validation.** Inside the account → *Identity validations* → *New*.
   Choose **Organization** or **Individual**, and **Public** trust type. Complete the flow:
   - *Individual:* select an Individual-type billing account, then do the Microsoft
     Authenticator / Verified ID liveness check with a government photo ID (address must match
     a utility bill / bank statement if asked).
   - *Organization:* legal entity name, website, business docs; a named representative also
     completes an individual check. Processing takes **1–20 business days**.

   Wait until status is **Completed** before continuing.

4. **Create a certificate profile.** Inside the account → *Certificate profiles* → *Create*
   → type **Public Trust** → link the completed identity validation. Note the **profile name**.

5. **Create an app registration + federated credential (for GitHub Actions, passwordless).**
   The CI signs **only on release (`v*` tag) builds** and uses OpenID Connect, so **no secret
   is stored** in the repo. The federated credential must match the **tag** token subject —
   a `main`/branch credential will *not* authenticate the release build, and every release is
   a new tag, so you can't pre-register one exact tag name.
   - Microsoft Entra ID → *App registrations* → *New registration* (name e.g.
     `github-gpu-fan-control-signing`). Note its **Application (client) ID** and
     **Directory (tenant) ID**.
   - On that app → *Certificates & secrets* → *Federated credentials* → *Add credential*.
     Basic federated credentials are exact-match (no wildcards), so use **one** of:
     - **Flexible federated credential** *(recommended)* — choose the flexible/advanced option
       and set a subject **claims-matching expression** matching
       `repo:bitworks-io/gpu-fan-control:ref:refs/tags/*`. One credential covers every
       future `v*` release.
     - **GitHub Environment credential** — create a GitHub Actions Environment (e.g. `release`),
       add `environment: release` to the workflow's `build` job, and register an
       **Environment**-type credential named `release`. Subject
       `repo:bitworks-io/gpu-fan-control:environment:release` is one exact string good for
       all releases. (Requires the small workflow edit.)
     - **Per-tag credential** — entity type **Tag**, exact tag name. Works, but you must add a
       new credential before every release; only sane for infrequent releases.

   *(Fallback — client secret instead of OIDC: create a client secret on the app and wire it
   per the note in §2. A secret is ref-agnostic, so it **sidesteps this tag-subject matching
   entirely** — at the cost of a long-lived credential. GitHub encrypts repo secrets and never
   exposes them to forked-PR builds, so the public-repo exposure risk is mild. OIDC is still
   preferred, but the secret path is the lowest-friction way to get signing working if flexible
   FICs give you trouble.)*

6. **Grant the signing role.** On the **certificate profile** (most-scoped) →
   *Access control (IAM)* → *Add role assignment* → role
   **"Artifact Signing Certificate Profile Signer"** → assign to the app registration from
   step 5. CLI equivalent:
   ```bash
   az role assignment create \
     --assignee <app-client-id> \
     --role "Artifact Signing Certificate Profile Signer" \
     --scope "/subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.CodeSigning/codeSigningAccounts/<account-name>/certificateProfiles/<profile-name>"
   ```

7. **Add the GitHub repo secrets.** Repo → *Settings* → *Secrets and variables* → *Actions*
   → *New repository secret*, for each of:

   | Secret | Value |
   |---|---|
   | `AZURE_CLIENT_ID` | App registration's Application (client) ID — **also the on/off switch**: its presence enables the signing steps. |
   | `AZURE_TENANT_ID` | Directory (tenant) ID |
   | `AZURE_SUBSCRIPTION_ID` | The subscription the signing account lives in |
   | `AZURE_ENDPOINT` | Regional endpoint for your account's region, e.g. `https://eus.codesigning.azure.net/` (East US), `https://wus2.codesigning.azure.net/` (West US 2), `https://neu.codesigning.azure.net/` (North Europe) |
   | `AZURE_SIGNING_ACCOUNT` | Artifact Signing account name from step 2 |
   | `AZURE_SIGNING_CERTIFICATE` | Certificate profile name from step 4 |

   No `AZURE_CLIENT_SECRET` is needed with OIDC.

Once those six secrets exist, the next `main`/tag build signs the installer automatically.
Until they exist, every build stays **green and unsigned** — the signing steps are skipped.

---

## 2. What's automated (already in the repo)

[`.github/workflows/build-windows.yml`](../.github/workflows/build-windows.yml) `build` job:

- Job-level `permissions: id-token: write` lets `azure/login` mint the OIDC token.
- A gated **`azure/login@v2`** step authenticates via the federated credential (no secret).
- A gated **`azure/artifact-signing-action@v2`** step signs
  `output\LightweightAmdGpuFanControl-Setup.exe` (SHA-256, RFC-3161 timestamp at
  `http://timestamp.acs.microsoft.com`), pinning `DefaultAzureCredential` to the Azure-CLI
  session that `azure/login` established. The `release` job then attaches this **same signed
  artifact** to the GitHub Release.

Both steps carry `if: ${{ env.AZURE_CLIENT_ID != '' && startsWith(github.ref, 'refs/tags/v') }}`
— so they run **only on `v*` tag (release) builds**, and only once the secret is set. On PRs and
`main` pushes they're skipped (green, unsigned) and no signing quota is spent. This tag-only
scope is also why the OIDC federated credential only needs to match the tag subject (step 5).

**Using a client secret instead of OIDC (fallback):** delete the `azure/login` step and add
`azure-client-id`, `azure-tenant-id`, `azure-client-secret` inputs (from secrets) directly on
the signing step; drop the two `exclude-*-credential` lines. Simpler and avoids the federated-
credential subject matching, but stores a long-lived secret — prefer OIDC where practical.

---

## 3. Verify a signature (after the first signed build)

On a Windows machine with the Windows SDK:
```powershell
signtool verify /v /pa .\LightweightAmdGpuFanControl-Setup.exe
```
Or: right-click the `.exe` → *Properties* → *Digital Signatures* tab → the signer should be
your validated org/individual name, countersigned by the Microsoft timestamp authority.

---

## 4. Follow-up — sign the inner app `.exe` too (not yet wired)

Today only the **installer** `.exe` is signed. The inner
`LightweightAmdGpuFanControl.exe` (what actually runs after install) is unsigned, so a user
who runs it directly still sees an unknown-publisher prompt. Signing it requires signing
**before** Inno Setup packages it, which means splitting the build:

1. `.\build.ps1 -SkipInstaller`  → publish only (produces
   `LightweightAmdGpuFanControl\bin\Release\net48\publish\LightweightAmdGpuFanControl.exe`).
2. Gated `azure/login` + sign step over the **publish** folder
   (`files-folder-filter: exe`, restricted to the app exe — not the vendor ADLX DLLs).
3. Add an installer-only path to `build.ps1` (e.g. a new `-InstallerOnly` switch that skips
   the submodule/ADLX/publish phase and runs `iscc`) and invoke it here.
4. Gated sign step over the **output** folder (the installer), as today.

This is a build-pipeline refactor (ungated `build.ps1` runs on every build), so it must be
proven on a real CI run before trusting it — hence it's deferred rather than done blind.

---

## Sources

- [What is Artifact Signing? (Microsoft Learn)](https://learn.microsoft.com/en-us/azure/artifact-signing/overview)
- [Quickstart: Set up Artifact Signing](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart) — onboarding, regions, endpoints, eligibility
- [Artifact Signing FAQ](https://learn.microsoft.com/en-us/azure/artifact-signing/faq) — eligibility, SmartScreen, file types, billing
- [Tutorial: Assign roles in Artifact Signing](https://learn.microsoft.com/en-us/azure/artifact-signing/tutorial-assign-roles) — the Certificate Profile Signer role
- [Azure/artifact-signing-action](https://github.com/Azure/artifact-signing-action) — the GitHub Action (formerly `Azure/trusted-signing-action`)
- [Configure OIDC from GitHub Actions to Azure](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect) — federated credential setup
- [Pricing](https://azure.microsoft.com/pricing/details/artifact-signing/)
