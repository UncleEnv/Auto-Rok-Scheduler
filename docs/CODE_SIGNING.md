# Code signing (Azure Trusted Signing)

The release exe is signed with **Azure Trusted Signing** in CI
(`.github/workflows/release.yml`). Signing proves the publisher's identity (no more
"Unknown Publisher" in the UAC prompt) and lets the app build **SmartScreen reputation**
over time. Cost is roughly **$10/month**.

> **Two different things.** Signing is not the same as SmartScreen trust. A freshly
> signed build can still show a SmartScreen warning until it earns reputation from
> download volume and signing history — Trusted Signing just gets you there much faster
> than an unsigned or self-signed build (self-signed does nothing for other machines).

---

## One-time Azure setup

1. **Azure subscription.** In the subscription, register the resource provider
   `Microsoft.CodeSigning` (Subscription → *Resource providers* → search → *Register*).
2. **Create a Trusted Signing account** (search "Trusted Signing" in the portal). Pick a
   region — note the account's **URI**, e.g. `https://wus2.codesigning.azure.net/`; that
   is your `TRUSTED_SIGNING_ENDPOINT`.
3. **Create a Certificate Profile** of type **Public Trust** on that account and complete
   **identity validation** (individual or organization). Validation can take a while and
   must finish before signing works. The profile's name is `TRUSTED_SIGNING_PROFILE`.
   > ⚠️ Check current eligibility — Trusted Signing has had identity-validation
   > constraints (e.g. an organization-age requirement); individual-developer signing has
   > been added but confirm it is available for your case/region.
4. **Create a service principal** (Microsoft Entra ID → *App registrations* → *New
   registration*). Note the **Directory (tenant) ID** and **Application (client) ID**,
   then create a **client secret** under *Certificates & secrets*.
5. **Grant the service principal signing rights**: on the Trusted Signing account, open
   *Access control (IAM)* → *Add role assignment* → role **Trusted Signing Certificate
   Profile Signer** → assign to the app registration from step 4.

---

## GitHub configuration

Repo → **Settings → Secrets and variables → Actions**.

**Secrets** (sensitive):

| Name | Value |
|------|-------|
| `AZURE_TENANT_ID` | Directory (tenant) ID |
| `AZURE_CLIENT_ID` | Application (client) ID |
| `AZURE_CLIENT_SECRET` | the client secret value |

**Variables** (not sensitive):

| Name | Value |
|------|-------|
| `TRUSTED_SIGNING_ENDPOINT` | account URI, e.g. `https://wus2.codesigning.azure.net/` |
| `TRUSTED_SIGNING_ACCOUNT` | Trusted Signing account name |
| `TRUSTED_SIGNING_PROFILE` | certificate profile name |

---

## Releasing

- **New version:** bump `<Version>` in `AutoRokScheduler/AutoRokScheduler.csproj`, then
  tag and push:
  ```powershell
  git tag v1.0.1
  git push origin v1.0.1
  ```
  The workflow builds the single-file exe, signs it, verifies the signature, and attaches
  `AutoRokScheduler.exe` to the `v1.0.1` release.

- **Sign an existing release** (e.g. the current `v1.0.0`, which was uploaded unsigned):
  Actions → **Release (build + sign + publish)** → *Run workflow* → enter `v1.0.0`. It
  rebuilds from that tag, signs, and replaces the release's `AutoRokScheduler.exe` with
  the signed one.

---

## Local signing (optional, without CI)

If you ever need to sign by hand, install the Windows SDK (`signtool.exe`) and the
Trusted Signing dlib, then:

```powershell
signtool sign /v /fd SHA256 /tr http://timestamp.acs.microsoft.com/ /td SHA256 `
  /dlib <path-to-Azure.CodeSigning.Dlib.dll> /dmdf <metadata.json> `
  publish\AutoRokScheduler.exe
```

where `metadata.json` holds your `Endpoint`, `CodeSigningAccountName`, and
`CertificateProfileName`. The CI action wraps all of this for you.
