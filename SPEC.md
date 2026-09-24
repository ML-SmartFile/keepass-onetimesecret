# KeePassOneTimeSecret — Technical Specification

A KeePass 2.x plugin that adds **"Share with OneTimeSecret…"** to the entry context
menu, publishes selected entry data as a one-time secret via the OneTimeSecret API,
and returns the share link to the clipboard.

Status: greenfield. No existing plugin implements this (verified Sept 2026).

---

## 0. Prerequisites before writing code

**Build environment.** KeePass 2.x plugins are .NET Framework / WinForms assemblies.

This project is developed on **PC-26-Klaus**, a Windows machine. The repo lives at
`C:\Code\ML-Smartfile\keepass-onetimesecret\`; KeePass sits outside the repo tree at
`C:\KeePass\`. `BOOTSTRAP.md` describes the setup; the short version:

- .NET SDK (per-user install) plus the `Microsoft.NETFramework.ReferenceAssemblies.net472`
  package, so `net472` builds without Visual Studio and without administrator rights
- Portable KeePass at `C:\KeePass\`, whose `KeePass.exe` is the reference assembly
  and whose `Plugins\` folder receives the build output
- A throwaway test database `C:\KeePass\test.kdbx`. **Never use a real database
  during development.**

There is no WinForms designer in this setup. Write form code by hand.

**A successful compile is not a completed milestone.** Every milestone that touches the
UI or the KeePass API must be smoke-tested in a running KeePass before it is marked
done, and the agent cannot operate that UI — the user does it and reports back.

Optional, from M5 onwards: a self-hosted OneTimeSecret instance in Docker for
integration testing.

---

## 1. Hard constraints

These are non-negotiable and derive from the KeePass plugin loader:

1. **Assembly name and class name must match.** Assembly `OneTimeSecret.dll` requires a
   public class named exactly `OneTimeSecretExt` deriving from `KeePass.Plugins.Plugin`.
   If these diverge, KeePass silently refuses to load the plugin with no error.
2. **Reference `KeePass.exe` with `CopyLocal=false`.** Never ship a copy of KeePass.exe
   or KeePassLib alongside the plugin.
3. **Zero third-party runtime dependencies.** JSON is handled with
   `System.Runtime.Serialization.Json.DataContractJsonSerializer` or a small hand-rolled
   reader. No Newtonsoft, no RestSharp. This keeps the plugin a single DLL and keeps the
   `.plgx` packaging path open.
4. **Target `net472`.** Present on every supported Windows version and compatible with
   current KeePass builds.
5. **All UI work on the KeePass UI thread; all network work off it.** Marshal back with
   `MainWindow.Invoke`.

---

## 2. Security invariants

Treat these as acceptance criteria, not suggestions. This plugin transmits plaintext
credentials to a remote server by design, so the blast radius of a sloppy detail is real.

- **The API token is never written to `KeePass.config.xml`.** Credentials are stored by
  reference: the options dialog stores a `PwUuid` pointing at an entry in the user's own
  database. Username and token are read from that entry at call time. This is a password
  manager; use it.
- **Never log, trace, or `Debug.WriteLine` a composed secret value or an API token.**
  No exception message may embed the secret. Scrub before rethrowing.
- **Never persist the composed secret** to a temp file, a log, or a settings value.
- **Refuse `http://`** unless the host resolves to loopback *and* the user has explicitly
  enabled `AllowInsecureHttp`. A silent plaintext POST of a password to someone's homelab
  is the failure mode to design against.
- **No network calls to any host other than the configured base URL.** No telemetry,
  no update pings beyond KeePass's own `UpdateUrl` mechanism, no favicon fetching.
- **The confirmation dialog must display the exact target host** and the exact fields
  being transmitted before any request is made.
- **Keep secrets in `ProtectedString` / `byte[]` as long as possible.** Convert to `string`
  only at the HTTP serialization boundary; zero intermediate buffers in a `finally`.
  .NET string immutability makes scrubbing imperfect — document this honestly in the
  README rather than overclaiming.
- **On any failure, do not leave the secret or a partial link on the clipboard.**

---

## 3. Architecture

Keep everything that touches KeePass in thin adapters so the interesting logic is
unit-testable without a KeePass reference.

```
src/OneTimeSecret/
  OneTimeSecret.csproj
  OneTimeSecretExt.cs          # Plugin entry point, menu wiring, orchestration
  Properties/AssemblyInfo.cs

  Ots/                         # NO KeePass references in this folder
    IOtsClient.cs              # ShareAsync(ShareRequest) -> ShareResult; BurnAsync(receiptKey)
    OtsClientV1.cs
    OtsClientV2.cs
    OtsClientFactory.cs        # capability detection + caching
    ShareRequest.cs
    ShareResult.cs
    OtsException.cs
    Json.cs                    # minimal serializer/reader

  Core/                        # NO KeePass references in this folder
    SecretComposer.cs          # template -> composed secret text
    TemplateBuilder.cs         # field checkboxes + custom message -> template string
    UrlPolicy.cs               # base URL validation, https enforcement, loopback rule
    TtlOptions.cs

  KeePassAdapters/
    PluginConfig.cs            # CustomConfig read/write
    CredentialResolver.cs      # PwUuid -> (username, token)
    EntryFieldReader.cs        # SprEngine.Compile wrapper
    ReceiptWriter.cs           # write-back of receipt key to the entry

  Ui/
    ShareForm.cs               # per-share dialog
    ResultForm.cs              # link + passphrase, copy buttons
    OptionsForm.cs             # global settings

tests/OneTimeSecret.Tests/     # xUnit or NUnit, targets Core/ and Ots/ only
build/
  build-plgx.ps1
  post-build-copy.ps1
CLAUDE.md
README.md
LICENSE                        # MIT
```

The `Ots/` and `Core/` folders must compile without a KeePass reference. Enforce this
by keeping them free of `using KeePass*` — a build-time check is welcome but a code
review rule is sufficient.

---

## 4. KeePass integration details

### Menu item

Do not manipulate `MainWindow.EntryContextMenu.Items` directly. Use the supported hook
(available since KeePass 2.42), which places the item in the plugin block at the bottom
of the entry context menu and mirrors it into the main Entry menu automatically:

```csharp
public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
{
    if (t == PluginMenuType.Entry)
    {
        var tsmi = new ToolStripMenuItem("Share with OneTimeSecret…");
        tsmi.Click += OnShareClick;
        return tsmi;
    }
    if (t == PluginMenuType.Main)
    {
        var tsmi = new ToolStripMenuItem("OneTimeSecret Options…");
        tsmi.Click += OnOptionsClick;
        return tsmi;
    }
    return null;
}
```

Hook the owning menu's `Opening` event to enable the item only when
`host.MainWindow.GetSelectedEntriesCount() == 1`. Multi-select is out of scope for v1 —
disable rather than silently sharing only the first entry.

### Reading entry data

```csharp
PwEntry pe = m_host.MainWindow.GetSelectedEntry(true);
```

All field values must pass through `SprEngine.Compile` with a proper `SprContext` so
that `{REF:P@I:…}` references, `{S:CustomField}` and environment placeholders resolve.
A raw `Strings.ReadSafe` alone will hand OneTimeSecret an unresolved reference string.

### Clipboard

Use `ClipboardUtil.Copy(text, bSprCompile, bIsEntryInfo, pe, db, hOwner)` rather than
`Clipboard.SetText`.

Pass `bSprCompile: false` for a value that is already final — a share URL must not be run
through `SprEngine` again.

**`Copy` does not arm the auto-clear timer.** The countdown belongs to `MainForm`, and
KeePass's own copy handlers call it immediately after copying. Verified against 2.61.1.0:

```csharp
if (ClipboardUtil.Copy(url, false, true, pe, db, hOwner))
    m_host.MainWindow.StartClipboardCountdown();
```

Omit the second line and the value stays on the clipboard indefinitely — found by manual
smoke test in M1, not by any compiler or unit test.

### Custom forms

Wrap every custom form with `GlobalWindowManager.AddWindow` / `RemoveWindow` in a
`try/finally`, or KeePass's auto-lock and secure-desktop behaviour will misfire.

### Database locking mid-flight

The database can lock while an HTTP request is in flight. Materialize every value you
need **before** dispatching the request. Never touch `PwDatabase` or `PwEntry` from the
completion callback without re-checking `m_host.Database.IsOpen`.

---

## 5. OneTimeSecret API

Three API generations exist. The plugin supports v1 and v2; v3 is preview and out of scope.

Hosted service base URIs are regional: `https://REGION.onetimesecret.com/api` where
REGION is one of `ca`, `eu`, `nz`, `uk`, `us`. **Default to `https://eu.onetimesecret.com`.**
Self-hosted instances take an arbitrary base URL.

Authentication is HTTP Basic (`username:apitoken`) or anonymous. Anonymous works but
with reduced rate limits and a shorter maximum TTL — handle the resulting 4xx gracefully
rather than treating it as a bug.

### Capability detection

```
GET {base}/api/v2/status
  200  -> use v2
  404  -> use v1
  else -> surface the error, do not silently fall back
```

Cache the result per host in `CustomConfig` with a timestamp; re-probe if older than
7 days or when the user hits Save in the options dialog.

### v1 — legacy, supported by every self-hosted instance ever deployed

```
POST {base}/api/v1/share
Content-Type: application/x-www-form-urlencoded
Authorization: Basic <base64(user:token)>        (omit for anonymous)

secret=<urlencoded>&ttl=86400&passphrase=<optional>&recipient=<optional>
```

Response (200, JSON) includes `secret_key` and `metadata_key`.

- Share link: `{base}/secret/{secret_key}`
- Receipt page: **use the `receipt_url` (or `metadata_url`) field the response returns.**

> **Verified against `eu.onetimesecret.com`, September 2026.** The hosted service serves
> the receipt from `/receipt/{metadata_key}`, *not* the `/private/{metadata_key}` this spec
> previously documented — constructing that path yields a 404. The response carries both
> `receipt_url` and `metadata_url` fully formed, so build nothing: the server also knows the
> correct host once `share_domain` is set, which no client-side construction can. Fall back
> to `{base}/private/{metadata_key}` only when neither field is present, for self-hosted
> instances old enough to predate them.
>
> A full captured response additionally carries `custid`, `ttl`, `metadata_ttl`, `secret_ttl`,
> `receipt_ttl`, `state`, `created`, `updated`, `identifier`, `secret_identifier`,
> `share_domain`, `has_passphrase`, `passphrase_required`, `recipient` and `recipients`.
> Note there is **no** `secret_url` field — the share link still has to be constructed.

### v2 — current stable

```
POST {base}/api/v2/secret/conceal
Content-Type: application/json
Authorization: Basic <base64(user:token)>

{
  "secret": {
    "kind": "conceal",
    "secret": "<value>",
    "passphrase": "<optional>",
    "ttl": "86400",
    "recipient": "<optional>",
    "share_domain": "<optional>"
  }
}
```

Response (200) shape, abbreviated:

```json
{
  "record": {
    "receipt": { "key": "…", "identifier": "…", "shortid": "…", "state": "new" },
    "secret":  { "key": "…", "identifier": "…", "shortid": "…", "state": "new" },
    "share_domain": null
  }
}
```

- Share link: `{share_domain or base}/secret/{record.secret.key}`
- Burn: `POST {base}/api/v2/private/{record.receipt.key}/burn`

Error responses carry `error`, `error_type`, `message`, `field` and `request_id`.
Branch on `error_type`; show `error` to the user; include `request_id` in any detail
view so the user can quote it in a bug report.

> **Verify before trusting.** The v2 response shape above is derived from the published
> OpenAPI spec, not from a live call. The exact construction of the user-facing share
> URL — especially with a custom `share_domain` — must be confirmed against a real
> instance during M4. Write the JSON reader to **ignore unknown fields** and to fail
> loudly on missing expected ones.

### TTL options to expose

5 minutes, 1 hour, 1 day (default), 3 days, 7 days, plus a custom value in seconds.

---

## 6. Configuration

Stored via `m_host.CustomConfig`, except credentials.

| Key | Type | Default | Notes |
|---|---|---|---|
| `OneTimeSecret.BaseUrl` | string | `https://eu.onetimesecret.com` | Validated by `UrlPolicy` |
| `OneTimeSecret.ApiVersion` | string | `auto` | `auto` \| `v1` \| `v2` |
| `OneTimeSecret.DetectedVersion` | string | — | Cache; with `…DetectedAt` timestamp |
| `OneTimeSecret.CredentialEntryUuid` | string | empty | Hex `PwUuid`; empty = anonymous |
| `OneTimeSecret.DefaultTtlSeconds` | int | `86400` | |
| `OneTimeSecret.Template` | string | `{USERNAME}\n{PASSWORD}` | Run through `SprEngine`. Regenerated by `TemplateBuilder` whenever a field checkbox or the custom message changes; remains directly editable by hand between those changes |
| `OneTimeSecret.IncludeTitle` | bool | `false` | Field checkbox default |
| `OneTimeSecret.IncludeUserName` | bool | `true` | Field checkbox default |
| `OneTimeSecret.IncludePassword` | bool | `true` | Field checkbox default |
| `OneTimeSecret.IncludeUrl` | bool | `false` | Field checkbox default |
| `OneTimeSecret.IncludeNotes` | bool | `false` | Field checkbox default |
| `OneTimeSecret.CustomMessage` | string | `""` | Literal text prepended to the field block, separated by a blank line when non-empty. Spliced into the template *before* `SprEngine.Compile` runs, so `{...}`-shaped text the user types here is interpreted as a placeholder like any other template text — call this out in the UI |
| `OneTimeSecret.GeneratePassphrase` | bool | `true` | Uses KeePass `PwGenerator` |
| `OneTimeSecret.WriteBackReceipt` | bool | `false` | |
| `OneTimeSecret.AllowInsecureHttp` | bool | `false` | Loopback only even when true |

**Credentials** live in the entry referenced by `CredentialEntryUuid`: `UserName` is the
OTS account email, `Password` is the API token. The options dialog offers an entry picker.

---

## 7. User flow

1. User right-clicks an entry → **Share with OneTimeSecret…**
2. **ShareForm** opens, pre-filled from config:
   - Field checkboxes — Title / UserName / Password / URL / Notes — each mapping to the
     matching `SprEngine` built-in placeholder (`{TITLE}`, `{USERNAME}`, `{PASSWORD}`,
     `{URL}`, `{NOTES}`). Checking/unchecking regenerates the template below via
     `TemplateBuilder`, joining checked placeholders with `\n` in that fixed order.
   - A custom message box. When non-empty, its literal text is prepended to the
     placeholder block, separated by a blank line.
   - Content template, with a live preview of the exact text to be transmitted
     (masked by default, with a "show" toggle). Directly editable; the next checkbox
     or message change regenerates and overwrites manual edits, so the UI must make
     that overwrite-on-regenerate behaviour obvious rather than silently discarding work
   - TTL dropdown
   - Passphrase: none / generate / manual
   - Recipient email (optional; triggers OTS's own email delivery)
   - A read-only line: *"Will be sent to `eu.onetimesecret.com` over HTTPS"*
3. User confirms. Request runs on a background thread; the form shows a progress state
   and a working Cancel button.
4. **ResultForm** shows the share link and, if generated, the passphrase — as two
   **separate** copy buttons, so the user naturally sends them over different channels.
   Also offers the receipt/burn link.
5. Optionally (if `WriteBackReceipt`) the receipt key and a UTC timestamp are written to
   a **protected** custom string field `OTS Receipt` on the entry, enabling a later
   "Burn last share" action.

---

## 8. Error handling

| Condition | Behaviour |
|---|---|
| 401 / 403 | "Check your OneTimeSecret credentials" + link to options |
| **404 on the v1 share endpoint** | **Rejected credentials *or* a base URL with no v1 API — name both.** See the note below |
| 404 on a v2 endpoint | Re-run capability detection once, then retry as v1 |
| 429 | Surface the rate-limit message; suggest configuring credentials if anonymous |
| 400 / 422 | Show `error`; if the field is `ttl`, note that anonymous TTL is capped |
| Timeout / DNS | Plain network error; do not retry automatically |
| TLS failure | Explicit message; never offer to disable validation |

> **Verified against `eu.onetimesecret.com`, September 2026.** Invalid credentials on the v1
> share endpoint return **404**, not 401 or 403 — so a 401/403 handler alone never fires for
> the hosted service and the user is left staring at a bare "HTTP 404". The response body is
> *inconsistent*: observed both empty and carrying `Not authorized`, so the message must be
> useful without it. Because a 404 there is equally consistent with a base URL that has no v1
> API, the wording names both causes and drops the credential explanation when the request was
> anonymous. The 401/403 row is retained for self-hosted instances that do use it.

Default HTTP timeout: 20 seconds. Set `ServicePointManager.SecurityProtocol` to include
TLS 1.2 and 1.3 explicitly at plugin init — older KeePass/.NET combinations still
default to something embarrassing.

Respect KeePass's configured proxy settings.

---

## 9. Testing

**Unit tests** (`tests/OneTimeSecret.Tests`) cover `Core/` and `Ots/` only:
- `SecretComposer`: template expansion, empty fields, multiline notes, CRLF handling
- `UrlPolicy`: https enforcement, loopback exception, trailing slashes, paths in base URL,
  IDN hosts, rejection of `file://` and similar
- JSON reader: v1 and v2 success payloads, error payloads, unknown fields, truncated body
- `OtsClientFactory`: detection logic against a stubbed HTTP layer

Abstract HTTP behind a small interface so clients can be tested without network access.

**Integration tests** hit a real instance, gated behind an environment variable
(`OTS_TEST_BASE_URL`, `OTS_TEST_USER`, `OTS_TEST_TOKEN`) so they never run in a default
`dotnet test`. Prefer a local Docker instance over the public service.

**Manual smoke test** per milestone, on Windows, against a throwaway database:
entry with references → share → link opens → secret matches → clipboard clears on
timeout → KeePass closes cleanly without a hung thread.

---

## 10. Packaging and release

Two distribution formats; ship the plain DLL first.

- **DLL**: faster load. Requires a KeePass restart after every rebuild — KeePass caches
  loaded plugins. Set up `build/post-build-copy.ps1` early to copy into the KeePass
  `Plugins` folder, or hours will be lost to stale binaries.
- **`.plgx`**: ships source that KeePass compiles at load. More resilient across
  KeePass/.NET versions and works under Mono, but requires explicit reference directives
  for any assembly beyond the defaults and forbids NuGet packages entirely — which is
  why constraint 3 above matters.

Set `Plugin.UpdateUrl` to a version file in the repo so KeePass's update check works.
Include the plugin version in `AssemblyInfo.cs` and keep it in sync with git tags.

License: MIT. Repo: `ML-Smartfile/keepass-onetimesecret`.

---

## 11. Out of scope for v1

Multi-entry sharing, attachment sharing, OTS v3 API, custom domain management,
a share history browser, localisation beyond English strings. Design so these are not
actively blocked, but do not build them.
