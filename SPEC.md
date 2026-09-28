# KeePassOneTimeSecret — Technical Specification

A KeePass 2.x plugin that adds **"Share with OneTimeSecret…"** to the entry context
menu, publishes selected entry data as a one-time secret via the OneTimeSecret API,
and returns the share link to the clipboard.

Status: no longer greenfield, corrected 2026-09-27. The original claim here ("no existing
plugin implements this, verified Sept 2026") was wrong — a comparable plugin,
[davidsiv/KeePass-OneTimeSecretShare](https://github.com/davidsiv/KeePass-OneTimeSecretShare),
has commits from August 2026, predating that claim. Found by an Opus subagent doing a
feature/security comparison against it, not by re-checking the original claim itself.
That comparison found ours ahead on features (v2 detection, burn, all four passphrase
modes, self-hosted TLS/path-prefix handling, test coverage) and more secure overall
(their plugin allows plain `http://` unconditionally, stores the API token in
`KeePass.config.xml`, runs network calls on the UI thread, and binds a global `Ctrl+Z`
shortcut that shares with no confirmation) — with one point in their favor worth taking
seriously: their in-memory secret handling never lets the composed secret become a
managed `string`, which ours does via `SprEngine.Compile`. See `ROADMAP.md`'s
`code-review-fixes` progress note for the full comparison.

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
    PluginConfig.cs            # CustomConfig read/write (global settings + fallback pointer)
    DatabaseScopedConfig.cs    # a database's own PwDatabase.CustomData read/write (M6)
    CredentialResolver.cs      # PwUuid -> (username, token), own entry then fallback
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
of the entry context menu and mirrors it into the main Entry menu automatically.
`GetMenuItem` hands back exactly **one** `ToolStripMenuItem` per `PluginMenuType` — there
is no second call for a second command. A plugin offering more than one entry-scoped
command nests them as `DropDownItems` of the one item it returns, rather than adding
further items to `entryMenu.Items` directly:

```csharp
public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
{
    if (t == PluginMenuType.Entry)
    {
        var root = new ToolStripMenuItem("OneTimeSecret");

        var share = new ToolStripMenuItem("Share…");
        share.Click += OnShareClick;
        root.DropDownItems.Add(share);

        var quickShare = new ToolStripMenuItem("Quick Share…");
        quickShare.Click += OnQuickShareClick;
        root.DropDownItems.Add(quickShare);

        var burn = new ToolStripMenuItem("Burn Last Share…");
        burn.Click += OnBurnClick;
        root.DropDownItems.Add(burn);

        return root;
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

> **M4 tried putting "Burn Last Share" under the Tools menu first** (a second `DropDownItems`
> entry next to Options), reasoning that it left the already-shipped one-click
> "Share with OneTimeSecret…" entry item untouched. The user found it while testing:
> "very confusing when it's hidden under tools" — a second, unrelated command tucked away
> in Tools was not discoverable from the entry it acts on. Moving both commands into one
> submenu on the entry context menu itself fixed that at the cost of one extra click on
> Share. A version of this that added a second flat item directly to `entryMenu.Items` was
> also tried and reverted — it would have worked, but breaks the rule above for no real
> gain over the submenu, and the user asked not to go around it once the tradeoff was clear.

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
Authorization: Basic <base64(user:token)>        (omit for anonymous)

{
  "secret": {
    "kind": "conceal",
    "secret": "<value>",
    "passphrase": "<optional>",
    "ttl": "86400",
    "recipient": "<optional>"
  }
}
```

> **Verified against `eu.onetimesecret.com`, September 2026 — the shape below replaces
> the earlier guess derived from the published OpenAPI spec, which was wrong in several
> load-bearing ways.** Anonymous requests are accepted (same as v1); the TTL cap is
> enforced by the same silent clamp to 604800 seconds seen on v1, not a rejection.
>
> Real response to a conceal call:
>
> ```json
> {
>   "success": true,
>   "record": {
>     "receipt":  { "key": "…", "identifier": "…", "secret_ttl": 300, "share_domain": null, "…lots more…": "…" },
>     "metadata": { "…same shape as receipt…": "…" },
>     "secret":   { "key": "…", "identifier": "…", "secret_ttl": 300, "has_passphrase": false },
>     "share_domain": null
>   },
>   "details": { "kind": "conceal", "recipient": [], "recipient_safe": [] }
> }
> ```
>
> Differences from the guessed shape, all confirmed by a live call:
> - The payload is wrapped in a top-level `success` flag and a `details` object neither
>   of which the earlier draft anticipated. The JSON reader ignores both — only `record`
>   matters for a share.
> - `record.receipt` and `record.secret` carry the two required `key`/`identifier` fields
>   the earlier draft expected, plus dozens more (`secret_ttl`, `metadata_ttl`,
>   `receipt_ttl`, `lifespan`, timestamps, `is_*` flags, …) that the reader correctly
>   ignores rather than failing on.
> - **Critically, `record` carries no `receipt_url` / `share_url` / `metadata_url` at
>   conceal time** — unlike v1, which hands those back ready-made. Both links have to be
>   constructed client-side from `record.secret.key` and `record.receipt.key`, using the
>   same `{host}/secret/{key}` and `{host}/receipt/{key}` pattern the v1 client already
>   uses, substituting `record.share_domain` for the host when it is set. Confirmed by
>   actually opening the constructed share link (`https://eu.onetimesecret.com/secret/{key}`
>   returned the reveal page, HTTP 200) rather than assuming the pattern holds.
> - The applied TTL lives at `record.secret.secret_ttl` (also duplicated onto `receipt`
>   and `metadata`) — read the same way the v1 client reads `secret_ttl`/`ttl`.
> - The server-provided full URL set (`share_url`, `receipt_url`, `burn_url`, plus the
>   bare `*_path` forms) only appears in the **burn** endpoint's response, never in the
>   conceal response. Not used by this client since it is only available after the fact,
>   but worth knowing if a future response shape changes again.

- Share link: `{https://share_domain or base}/secret/{record.secret.key}`
- Receipt link: `{https://share_domain or base}/receipt/{record.receipt.key}`
- Burn: `POST {base}/api/v2/private/{record.receipt.key}/burn`, JSON body `{"continue":"true"}`

> **The published API docs at `api.onetimesecret.com` — not the OpenAPI-derived guess this
> spec started from — turned up two corrections, found only after a real account's burn
> attempts kept silently failing.**
>
> 1. **All three burn endpoint variants (`v1/private`, `v2/private`, `v2/receipt`) require
>    a `continue` body field**, undocumented beyond its presence in the schema
>    (`{"type": "string"}`, no description, no enum, no example — checked the raw OpenAPI
>    JSON directly, not just the rendered docs). This client omitted it entirely and sent
>    `{}`. Every burn attempt — anonymous, and later a real authenticated account with a
>    confirmed non-null `owner_id` — came back HTTP 200 with `"success":false` until this
>    was found. `"true"` is what this client now sends; the API gives no way to confirm
>    that specific value is correct short of a working burn, which still needs the user's
>    own account to verify end-to-end.
> 2. **v1 has its own burn endpoint too** (`POST {base}/api/v1/private/{key}/burn`, form
>    encoded like the rest of v1, deprecated but present). An earlier draft of this spec
>    assumed burning was v2-only; that was never verified and was wrong. `OtsClientV1` now
>    implements it.
>
> Earlier testing (before the `continue` field was found) captured this: three anonymous
> burn attempts — a bare cross-session call, one replaying the creating session's cookie,
> and one additionally carrying that session's `X-CSRF-Token` — all returned HTTP 200 with
> `"success":false` and a `custid`/`owner_id` of `null` on the created record, which read
> at the time as "only an owning account can burn its own secret." A follow-up test with a
> **real authenticated account** (confirmed `owner_id` populated, so genuinely
> account-owned) got the identical `"success":false` shape anyway — disproving the
> ownership theory and pointing at the missing `continue` field instead. Treat
> `success:false` (v2) or a `state` that isn't `"burned"` (v1) as a normal, non-exceptional
> outcome the client reports back rather than throws on — the real cause of a decline
> once `continue` is present is not yet known and is deliberately left unguessed.

Error responses look like one of:

```json
{"error": "You did not provide anything to share", "error_type": "missing", "field": "secret", "request_id": "…"}
{"error": "Authentication Required", "message": "[CREDENTIALS_INVALID] Invalid credentials", "timestamp": 1790506696}
```

> **Verified, not assumed:** bad credentials return **401** on v2 (unlike v1's 404
> quirk — see §8). `request_id` is present on field-validation errors but absent on the
> auth error above; `message` is present on the auth error but absent on the
> field-validation one. Read both fields, prefer `message` when present (it was the more
> specific one in the one real case where both existed), and treat every field as
> optional — this API does not hand back a stable, complete shape either.

### TTL options to expose

5 minutes, 1 hour, 1 day (default), 3 days, 7 days, plus a custom value in seconds.

---

## 6. Configuration

Two tiers, added in M6 to support more than one open `.kdbx` at a time (a `PwUuid` is
only unique within the database it came from):

**Global, via `m_host.CustomConfig`** (`KeePass.config.xml`, local to the machine, never
written into any `.kdbx`):

| Key | Type | Default | Notes |
|---|---|---|---|
| `OneTimeSecret.BaseUrl` | string | `https://eu.onetimesecret.com` | Validated by `UrlPolicy` |
| `OneTimeSecret.ApiVersion` | string | `auto` | `auto` \| `v1` \| `v2` |
| `OneTimeSecret.DetectedVersion` | string | — | Cache; with `…DetectedForUrl`/`…DetectedAt` |
| `OneTimeSecret.DefaultTtlSeconds` | int | `86400` | |
| `OneTimeSecret.Template` | string | `{USERNAME}\n{PASSWORD}` | Run through `SprEngine`. Regenerated by `TemplateBuilder` whenever a field checkbox or the custom message changes; remains directly editable by hand between those changes |
| `OneTimeSecret.IncludeTitle` | bool | `false` | Field checkbox default |
| `OneTimeSecret.IncludeUserName` | bool | `true` | Field checkbox default |
| `OneTimeSecret.IncludePassword` | bool | `true` | Field checkbox default |
| `OneTimeSecret.IncludeUrl` | bool | `false` | Field checkbox default |
| `OneTimeSecret.IncludeNotes` | bool | `false` | Field checkbox default |
| `OneTimeSecret.PassphraseMode` | string | `generate` | `none` \| `generate` \| `manual` \| `entry`. Only the *mode* is stored — `manual` preselects "Enter one each time" with an empty box. A stored passphrase would be a stored secret |
| `OneTimeSecret.WriteBackReceipt` | bool | `false` | |
| `OneTimeSecret.AllowInsecureHttp` | bool | `false` | Loopback only even when true |
| `OneTimeSecret.CredentialFallback.*` | string ×3 | empty | `DatabaseUuid`/`EntryUuid` (hex `PwUuid`s) + `Display` (cached label). The entry to borrow credentials from for any database with none configured of its own — only usable while the database named by `DatabaseUuid` is open in another tab. See below |
| `OneTimeSecret.PassphraseFallback.*` | string ×3 | empty | Same shape, for the standing-passphrase entry (mode `entry`) |

**Per-database, via that database's own `PwDatabase.CustomData`** (lives inside the
`.kdbx` itself, travels with the file, works automatically on another PC with the plugin
installed — but a change here requires saving the database, like any other KeePass edit):

| Key | Type | Default | Notes |
|---|---|---|---|
| `OneTimeSecret.CredentialEntryUuid` | string | empty | Hex `PwUuid` of the entry (in *this* database) holding the OTS account e-mail and API token. Empty means "use the global fallback, if any" |
| `OneTimeSecret.PassphraseEntryUuid` | string | empty | Hex `PwUuid` of the entry (in *this* database) whose **Password** field supplies the passphrase when the mode is `entry`. Empty means "use the global fallback, if any" |
| `OneTimeSecret.Migrated` | bool | — | Set once, the first time Options is opened for this database post-M6, so the one-time copy of the pre-M6 global reference (below) never re-fires after an explicit later "Clear" |

**Resolution order** (`CredentialResolver`/`PassphraseResolver`, identical for both):
this database's own entry, if configured — a broken reference here (entry deleted) fails
loudly rather than silently falling through to the fallback, since the user configured
this database explicitly. If this database has none configured, the global fallback
entry, but only when the database it names is currently open in another tab; otherwise
the share proceeds anonymously with a warning naming the fallback database as not open,
distinct from "nothing configured at all."

**Why the split, not just a per-database reference for everything:** some databases are
shared with customers and must carry no trace of any other database's existence or
credentials. A database with its own configured entry needs nothing else; a database
with none (the common case for a customer-facing `.kdbx`) borrows the fallback purely as
a local, this-machine convenience — nothing about the fallback is ever written into that
database's file, so a copy handed to someone else carries no reference to it.

**Migration:** a pre-M6 install's global `OneTimeSecret.CredentialEntryUuid`/
`PassphraseEntryUuid` (un-namespaced, no per-database meaning) is copied into whichever
database Options is first opened against post-upgrade, and promoted to the global
fallback if none is configured yet — so an existing single-database setup keeps working,
and automatically becomes available to any second database opened later, without
re-selection.

> **The custom message is deliberately absent from this table.** An earlier revision
> persisted it as `OneTimeSecret.CustomMessage`, which was wrong twice over: the generated
> template containing the message was written back as well, corrupting the stored default
> template, and a note written for one recipient then prefilled the next share to someone
> else. A message is per-share by nature. `ShareForm` starts with an empty message every
> time, and neither the message nor the template it generates is written back — the options
> dialog is the only place the default template is set.
>
> Field checkboxes, passphrase mode and lifetime *are* durable preferences and are persisted.

> **On `entry` mode.** A passphrase read from a database entry is reused across every share,
> which is materially weaker than generating one per share: anyone who received an earlier
> share can open every later one, and it never rotates. It is only sensible with a single
> counterparty you agreed it with out of band. The UI labels the tradeoff rather than hiding
> it, and the mode is never the default. Selecting it without naming an entry is rejected at
> the options dialog, because the alternative is discovering it at the moment of sharing.

---

## 7. User flow

1. User right-clicks an entry → **OneTimeSecret → Share…**
2. **ShareForm** opens, pre-filled from config:
   - Field checkboxes — Title / UserName / Password / URL / Notes — each mapping to the
     matching `SprEngine` built-in placeholder (`{TITLE}`, `{USERNAME}`, `{PASSWORD}`,
     `{URL}`, `{NOTES}`). Checking/unchecking regenerates the template below via
     `TemplateBuilder`, joining checked placeholders with `\n` in that fixed order.
   - A custom message box. When non-empty, the template gains a `{MESSAGE}` token above
     the field block, separated by a blank line — **the message text itself never appears
     in the template.** Two reasons, both learned the hard way: showing the text in both
     boxes at once reads as duplication and confuses people about which one is authoritative,
     and making the message a *substituted value* rather than template text means
     `SecretComposer` never rescans it, so braces the user types in a message stay literal
     instead of silently expanding into another field's value.
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
   a **protected** custom string field `OTS Receipt` on the entry, enabling
   **OneTimeSecret → Burn Last Share…** from that entry's own context menu, right below
   Share. Only enabled when the field is present; burning removes it again on success.

### Quick Share

**OneTimeSecret → Quick Share…**, right below Share in the same submenu, shares
immediately using the configured defaults (field checkboxes, template, lifetime,
passphrase mode) without opening `ShareForm` — the dialog is right when composing a
share deliberately, but it is friction for "send this entry as configured". Three
invariants from elsewhere in this spec still apply and are not relaxed for this path:

- **Section 2's confirmation requirement still holds.** One `MessageBox` (entry title,
  the exact field list, the exact target host, OK/Cancel) stands in for `ShareForm`'s
  own confirmation — one dialog instead of zero, never zero.
- **`ResultForm` still opens.** A generated passphrase and the receipt link exist
  nowhere else; skipping it would destroy them.
- **The anonymous-fallback warning still fires** if the configured credential entry is
  missing or unreadable, exactly as it does before `ShareForm` opens.

`PassphraseMode.Manual` cannot be honoured — there is no dialog to type a passphrase
into — so Quick Share generates one instead, same protection, just not the exact
configured mode. No progress dialog exists either; the menu item disables itself for
the duration (`MainForm.SetStatusEx` shows progress in the status bar instead) and
there is no cancel — the request is short enough that one was not built.

---

## 8. Error handling

| Condition | Behaviour |
|---|---|
| 401 / 403 | "Check your OneTimeSecret credentials" + link to options |
| **404 on the v1 share endpoint** | **Rejected credentials *or* a base URL with no v1 API — name both.** See the note below |
| 404 on a v2 endpoint | Re-run capability detection once, then retry as v1 |
| 429 | Surface the rate-limit message; suggest configuring credentials if anonymous |
| 400 / 422 | Show `error`; if the field is `ttl`, note that anonymous TTL is capped |
| **Over-long TTL** | **Not an error at all — the server clamps silently. Read the applied lifetime back and tell the user.** See below |
| Timeout / DNS | Plain network error; do not retry automatically |
| TLS failure | Explicit message; never offer to disable validation |

> **Verified against `eu.onetimesecret.com`, September 2026.** Invalid credentials on the v1
> share endpoint return **404**, not 401 or 403 — so a 401/403 handler alone never fires for
> the hosted service and the user is left staring at a bare "HTTP 404". The response body is
> *inconsistent*: observed both empty and carrying `Not authorized`, so the message must be
> useful without it. Because a 404 there is equally consistent with a base URL that has no v1
> API, the wording names both causes and drops the credential explanation when the request was
> anonymous. The 401/403 row is retained for self-hosted instances that do use it.
>
> **The anonymous TTL cap is enforced by silent clamping, not by an error.** Requests for
> 30 days, 1 year and 10 years all returned HTTP 200 with `ttl` and `secret_ttl` set to
> **604800** (7 days). The `400/422 field == "ttl"` row above is therefore unreachable on the
> hosted service, and the real hazard is the opposite of an error: the user picks 30 days,
> sees a success dialog, and believes the link lives 30 days when it dies after 7. The client
> reads `secret_ttl` (falling back to `ttl`) back from the response and the result dialog
> states the lifetime actually applied, calling it out when it is shorter than requested. A
> response that omits the field is treated as *unknown*, never as shortened.

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
