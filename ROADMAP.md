# Roadmap

Eight milestones. Each is a branch and a PR. Do not start the next before the previous
one's acceptance criteria are met and manually verified in a running KeePass.

---

## M0 — Skeleton that loads

**Branch:** `m0-skeleton`

- Repo structure per `SPEC.md` §3, `.gitignore`, MIT `LICENSE`, README stub
- `OneTimeSecret.csproj` targeting `net472`, referencing `KeePass.exe` with `CopyLocal=false`
  (path configurable via an MSBuild property or a `Directory.Build.props`, not hardcoded
  to one machine)
- `OneTimeSecretExt : Plugin` with `Initialize`, `Terminate`, `GetMenuItem`
- Entry context menu item **"Share with OneTimeSecret…"** that opens a message box
  showing the selected entry's title
- `build/post-build-copy.ps1`
- Empty test project that runs green

**Acceptance:**
- Plugin appears in KeePass under Tools → Plugins with correct name and version
- Menu item appears in the entry context menu, in the plugin block at the bottom
- Item is greyed out when zero or multiple entries are selected
- Message box shows the correct title for the selected entry
- KeePass shuts down cleanly with no hung threads

---

## M1 — v1 client, anonymous share

**Branch:** `m1-v1-client`

- HTTP abstraction (`IHttpTransport`) so clients are testable without network
- `OtsClientV1` implementing `POST /api/v1/share`, form-encoded, anonymous only
- `SecretComposer` with the default template `{USERNAME}\n{PASSWORD}`, via `SprEngine`
- `UrlPolicy` with https enforcement and the loopback exception
- Hardcoded base URL (`https://eu.onetimesecret.com`) and TTL (86400) for now
- Result link copied via `ClipboardUtil.Copy`; a message box confirms
- Request runs off the UI thread

**Acceptance:**
- Right-clicking a test entry produces a working onetimesecret.com link within a few seconds
- Opening the link reveals exactly the composed text, once; a second open shows it burned
- An entry whose password is a `{REF:P@I:…}` reference shares the **resolved** value
- The KeePass UI stays responsive during the request
- Unit tests cover `SecretComposer` and `UrlPolicy`; both pass offline
- Grep the codebase: no log statement anywhere can emit a secret value

---

## M2 — Configuration

**Branch:** `m2-config`

- `PluginConfig` over `CustomConfig` with all keys from `SPEC.md` §6
- `OptionsForm` reached from the main Tools menu: base URL, region preset dropdown,
  API version (`auto`/`v1`/`v2`), default TTL, template, passphrase mode, insecure-http toggle
- `CredentialResolver`: an entry picker storing a `PwUuid`; username and token read at
  call time from that entry
- Base URL validated on save, with a clear message on rejection
- Basic auth wired into `OtsClientV1`

**Acceptance:**
- Settings survive a KeePass restart
- The API token appears nowhere in `KeePass.config.xml` — verify by opening the file
- An authenticated share succeeds and shows up in the OTS account's recent list
- Entering `http://example.com` is rejected; `http://127.0.0.1:3000` is accepted only
  with the insecure toggle enabled
- Deleting the referenced credential entry degrades to anonymous with a clear warning,
  not a crash

> **Deliberate scope choices, so the dialog never looks more functional than it is.** The
> API-version dropdown offers `auto` and `v1` only — no v2 client exists until M4, and
> offering it would silently hand back v1. The passphrase checkbox persists
> `GeneratePassphrase` but is labelled "(takes effect in a later version)" because nothing
> consumes it until M3. The template stays a plain text box; the field checkboxes and
> custom-message box from `SPEC.md` §7 are M3's `TemplateBuilder`, though `PluginConfig`
> already persists their keys.

---

## M3 — Share dialog and error handling

**Branch:** `m3-share-dialog`

- `ShareForm` per `SPEC.md` §7: field checkboxes (Title/UserName/Password/URL/Notes) and
  a custom message box driving a generated, still hand-editable template via
  `Core/TemplateBuilder.cs`, with masked live preview, TTL dropdown, passphrase
  (none/generate/manual, generation via KeePass `PwGenerator`), optional recipient, and
  a visible line naming the target host
- `ResultForm` with separate copy buttons for link and passphrase, plus the receipt link
- Cancellable request with a progress state
- Full error mapping per `SPEC.md` §8
- TLS 1.2/1.3 forced at init; KeePass proxy settings respected

> **Partly landed early, in M1.** Recorded here so "M3 done" keeps its meaning and a future
> session is not misled about what was actually verified when:
>
> - **`ResultForm` exists** (`Ui/ResultForm.cs`) showing the share link and the receipt link
>   as selectable read-only boxes with a Copy button each. Built in M1 because the clipboard
>   auto-clear could otherwise lose the only copy of a link while the secret stayed live and
>   unburnable. **M3 still owes:** the passphrase row and its separate copy button, and
>   **making the clipboard-clear warning far more prominent** — it is currently a small grey
>   line under the receipt box that is easy to miss, and missing it is precisely how a user
>   loses the only copy of a live link.
> - **TLS 1.2/1.3 is already forced at init** via `HttpTransport.EnableModernTls()`.
> - **Most of the §8 error mapping is already in `OtsClientV1`** (401/403, 429, 400/422 with
>   the `ttl` hint, timeout, DNS, TLS). **M3 still owes:** surfacing these through the new
>   dialogs, and the cancellation path.
> - **Not done, despite the bullet above: KeePass proxy settings are not read.**
>   `HttpTransport` uses the system default proxy only.

**Acceptance:**
- Every error row in §8 is reachable and produces a comprehensible message —
  test at minimum: bad credentials, no network, and an over-long anonymous TTL
- Cancel actually aborts and leaves nothing on the clipboard
- A generated passphrase is required to open the link, and is *not* included in the link
- Copy link and copy passphrase are separate actions; neither overwrites the other silently
- Toggling a field checkbox updates the template and preview immediately; a custom
  message is prepended ahead of the field block, separated by a blank line
- Unit tests cover `TemplateBuilder`: every field-inclusion combination, empty message,
  a message containing literal `{`/`}` characters
- Locking the database mid-request does not throw

---

## M4 — v2 API and capability detection

**Branch:** `m4-v2-api`

- `OtsClientV2` for `POST /api/v2/secret/conceal`
- `OtsClientFactory` probing `GET /api/v2/status`, with the per-host cache
- Share URL construction verified against a **live** instance, including a
  `share_domain` case if one is available
- Burn support: `POST /api/v2/private/{receipt}/burn`
- Optional receipt write-back to a protected custom field `OTS Receipt`, plus a
  "Burn last share" entry menu item when that field is present

**Acceptance:**
- Against the hosted service: v2 is detected and used
- Against a self-hosted instance old enough to lack v2: v1 is used, with no error shown
- The share URL produced by the v2 path opens correctly — confirmed by actually opening it
- Burn marks the secret as burned; the link then shows the burned state
- Any divergence from the documented v2 response shape is written up in the PR description

---

## M5 — Self-hosted hardening

**Branch:** `m5-selfhosted`

- Docker-based local OTS instance documented in the README for testing
- Base URLs with a path prefix (`https://example.com/ots`) handled correctly
- Self-signed certificate case: clear failure message, no bypass offered
- Integration test suite gated behind the `OTS_TEST_*` environment variables

**Acceptance:**
- A full share/reveal/burn cycle works end to end against the local Docker instance
- A path-prefixed base URL works
- A self-signed cert produces the documented message and no option to proceed

---

## M6 — Multi-KeePass files

**Branch:** `m6-multi-db`

Scheduled 2026-09-27, ahead of packaging, after the code review (see the
`code-review-fixes` progress note) flagged that the credential and passphrase entry
references lived in KeePass's single global `CustomConfig`, with no notion of which
`.kdbx` a reference belongs to — a `PwUuid` is only unique within its own database.
Design went through two rejected drafts before landing on the real requirement (full
write-up in the progress note below); the shipped design:

- **Per-database primary reference**, in that database's own `PwDatabase.CustomData` —
  travels with the file, works automatically on another PC with the plugin installed.
  Requires saving the database (Ctrl+S) to persist, like any other KeePass edit.
- **Global fallback pointer** (`CredentialFallback`/`PassphraseFallback`), in
  `CustomConfig`, local to the machine only, never written into any `.kdbx` — the entry
  other open databases with none of their own configured can borrow, but only while the
  database holding it is open in another tab. Set via a "Use This Entry" button next to
  each database's own entry picker in Options.
- One-time migration of a pre-M6 install's global reference into the first database
  Options is opened against, promoted to the fallback automatically.

**Acceptance:**
- Configure credentials in `Main.kdbx`, mark as fallback. With `Main.kdbx` and
  `Customer.kdbx` (no credentials of its own) both open in separate tabs: sharing from
  an entry in `Customer.kdbx` uses `Main.kdbx`'s credentials.
- With only `Customer.kdbx` open: sharing from it degrades to anonymous with a warning
  naming the fallback database as not currently open — not the generic "nothing
  configured" message.
- Configuring `Customer.kdbx` with its own credential entry makes it use that one even
  while `Main.kdbx` is also open.
- Deleting the entry backing a database's own primary reference produces "no longer
  exists in this database" and shares anonymously — does not silently fall through to
  the fallback.
- `Customer.kdbx`'s raw file content carries no reference to `Main.kdbx`, its entries,
  or its credentials, at any point in the above.
- An existing single-database install still works after upgrading, and is automatically
  available as the fallback the first time a second database is opened.
- Clearing a database's own reference, then reopening Options for it later, stays
  cleared (not silently re-migrated from the legacy global value).
- Same coverage, mirrored for the passphrase-entry fallback.

---

## M7 — Packaging and release

**Branch:** `m7-release`

- `Plugin.UpdateUrl` pointing at `UPDATE.txt` in the repo
- README: install instructions, configuration walkthrough, screenshots (pending —
  placeholders in place, see the progress note), and an honest security section covering
  what the plugin transmits, to whom, and the limits of in-memory secret scrubbing in
  .NET
- CHANGELOG, v1.0.0 tag, GitHub release with the DLL
- `build/build-plgx.ps1` producing a working `.plgx` — **deferred past v1.0.0**, see the
  progress note below for why

**Acceptance:**
- KeePass's update check detects a newer version when the version file is bumped
- The README's install instructions work when followed literally by someone who has
  not seen the codebase
- ~~The `.plgx` loads cleanly in a fresh KeePass install on a machine that has never
  built the project~~ — deferred; no `.plgx` ships in v1.0.0

---

## Backlog — requested, not yet scheduled

Empty. Quick Share (requested 2026-09-24) was the only entry; built 2026-09-27 on branch
`backlog-quick-share` — see the progress notes below for how its three design tensions
were resolved.

---

## Progress notes

Append a dated line per milestone as it completes, including anything that turned out
differently from this plan. Future sessions read this file first.

<!-- M0: -->
**M0 — 2026-09-23.** Done, all acceptance criteria smoke-tested in KeePass 2.61.1.0 by the
user. Environment: .NET SDK 10.0.401 (not 8.x), portable KeePass at `C:\KeePass\`.
Surprises worth carrying forward:

- **`AssemblyProduct` must be exactly `"KeePass Plugin"`.** `PluginManager.LoadPlugins`
  compares the DLL's Win32 `ProductName` against `AppDefs.PluginProductName` and silently
  skips the file *before* hashing it or calling `Assembly.LoadFrom` — no error, no entry in
  `<PluginCompatibility>`, nothing in Tools → Plugins. This cost hours; now recorded as a
  non-negotiable in `CLAUDE.md`. `AssemblyTitle` → `FileDescription` is the friendly name.
- **`KeePassLib` ships inside `KeePass.exe`** — there is no separate `KeePassLib.dll` to
  reference (the `KeePassLibN.*.dll` files are native helpers). One `<Reference>` to
  `KeePass.exe` covers both `KeePass.*` and `KeePassLib.*`.
- **`<UseWindowsForms>true</UseWindowsForms>` is required** in an SDK-style `net472`
  project or `System.Windows.Forms` will not resolve.
- **`KeePass.exe.config` carries `<bindingRedirect oldVersion="2.0.9.0-2.61.0.127"`**, which
  is why plugins compiled against older KeePass versions still load. Out-of-process
  reflection harnesses lack that config and will throw `ReflectionTypeLoadException` —
  a harness artifact, not a plugin defect.
- **Keep `.ps1` files pure ASCII.** An em-dash in `build/post-build-copy.ps1` broke parsing
  under Windows PowerShell 5.1 (BOM-less files are read via the system codepage).
- `KeePassDir` is an overridable MSBuild property (defaults to `C:\KeePass`), so the build
  is not pinned to this machine.

<!-- M1: -->
**M1 — 2026-09-23.** Done, all acceptance criteria smoke-tested against the live
`eu.onetimesecret.com` by the user: share, reveal-once-then-burn, `{REF:P@I:…}` resolving
to the real password, umlauts surviving the round trip, and a responsive UI.

- **`ClipboardUtil.Copy` does not arm the auto-clear timer.** The countdown belongs to
  `MainForm.StartClipboardCountdown()`, which KeePass's own copy handlers call immediately
  after copying. Without it the share link sits on the clipboard forever. Found only by the
  manual smoke test — no compiler or unit test would have caught it. `SPEC.md` §4 said the
  opposite and has been corrected.
- **The anonymous TTL cap was not a problem.** We expected 86400 might be rejected for
  anonymous shares; the hosted service accepted it. The 400/422 `field == "ttl"` handling
  is in place regardless.
- **`SecretComposer` cannot call `SprEngine`** (non-negotiable #3). Split adopted: the
  adapter resolves each *field value* through `SprEngine`, then `Core/SecretComposer` does a
  single-pass `{FIELD}` substitution over those resolved values. Substituted content is
  never rescanned, so a password that literally reads `{TITLE}` survives verbatim — passing
  the whole template to `SprEngine` would have double-expanded it.
- **`SprCompileFlags.NonActive`, not `All`.** Still dereferences `{REF:…}`, but excludes
  `{CMD:}` execution, the character-picker prompt, and the state-changing `NewPassword` /
  `HmacOtp` placeholders. Composing a secret must not run anything or burn an OTP counter.
- **The test project link-compiles `Core/` and `Ots/`** instead of referencing the plugin
  assembly. The suite therefore builds and runs with no KeePass present, and a `using
  KeePass…` added to either folder breaks the test build immediately — the build-time check
  `SPEC.md` §3 calls welcome.
- `IOtsClient` is synchronous (`Share`, not `ShareAsync`) and runs on a thread-pool thread.
  Async plumbing waits for M3, where cancellation actually lands.

<!-- M2: -->
**M2 — 2026-09-24.** Config, options dialog and authenticated shares. All acceptance
criteria smoke-tested by the user except the authenticated share itself, which waits on a
real OTS account.

- **Rejected credentials return 404, not 401/403** on the hosted v1 API, and the body is
  *inconsistent* — observed both empty and carrying `Not authorized`. `SPEC.md` §8 had no
  404 row for v1 (its 404 row was about v2 detection), so the auth handler never fired and
  the user saw a bare "HTTP 404". §8 now records the verified behaviour; the message names
  both real causes (rejected credentials *or* a base URL with no v1 API) and drops the
  credential wording when the request was anonymous.
- **Verified directly in `KeePass.config.xml`:** the only credential-related value written
  is `CredentialEntryUuid`, a hex `PwUuid`. The entry's password and username, the database
  master password, and the base64 Basic-auth form are all absent, and no stray files are
  created.
- **`CustomConfig` preserves CRLF inside values** — the two-line template round-trips
  byte-for-byte as `{USERNAME}\r\n{PASSWORD}`.
- **Settings are written on clean exit only.** Killing KeePass loses them; the restart test
  must use a proper exit.
- **`KeePass.Forms.OptionsForm` already exists**, so `Ui.OptionsForm` must be qualified at
  its use site. Do not "simplify" that away.
- **`ListViewForm` was rejected as the entry picker.** It exists and looks native, but its
  `InitEx(..., List<object>, ...)` data contract is undocumented and could not be verified
  without driving the UI, so the picker is hand-rolled per `BOOTSTRAP.md`.
- Two UI defects found by reading screenshots rather than by testing: the multi-line
  template box opened scrolled past its first line (making a two-line template look like
  one), and the picker was parented to the main window instead of the options dialog.
- `AceCustomConfig` offers only `GetString`/`GetBool`/`GetLong`/`GetULong` and their
  setters — no typed dates, hence the ISO-8601 round-trip for `DetectedAt`.

<!-- M3: -->
**M3 — 2026-09-27.** `ShareForm`, four passphrase modes, cancellation, and the full §8
error mapping. All acceptance criteria smoke-tested by the user against the live
`eu.onetimesecret.com`, including cancel-leaves-nothing-on-clipboard and the passphrase/
copy-button independence. Two rows are accepted as verified by design rather than by
observation: locking the database mid-request is unreachable through the UI as built
(`ShareForm` is modal and registered with `GlobalWindowManager`, so KeePass's own menus,
Lock Workspace included, are unreachable while it's open), and KeePass proxy settings are
read from `AceIntegration` but have not been exercised against a real configured proxy.

- **The v1 anonymous TTL cap is enforced by silent clamping, not an error.** 30-day,
  1-year and 10-year requests all returned HTTP 200 with `ttl`/`secret_ttl` clamped to
  604800 seconds. `SPEC.md` §8's `400/422 field=="ttl"` row is unreachable on the hosted
  service — the real hazard is the opposite of an error: the user picks a long lifetime,
  sees success, and believes the link outlives the 7 days it actually gets. `ShareResult`
  now carries the server's actual applied TTL and `ResultForm` states it, calling out when
  it was shortened.
- **`CustomConfig` round-trips a bare LF, not CRLF.** The config is XML; XML parsing
  collapses CRLF to LF, and a WinForms multi-line `TextBox` only breaks lines on CRLF. A
  stored two-line template rendered as one run-on line until this was caught by
  inspection. Fixed by renormalizing to CRLF in `PluginConfig.Template`'s getter.
- **The custom message is a substituted value (`{MESSAGE}`), never inlined into the
  template text.** An earlier revision inlined it, which duplicated the same text in both
  the message box and the template box, and let `SecretComposer` rescan attacker-controlled
  text — a message containing `{PASSWORD}` would have leaked the password. Substituting it
  once, like any other field, closed both problems at once. Neither the message nor the
  regenerated template is ever persisted; only field selection, passphrase mode and
  lifetime are durable preferences.
- **Regression caught by the message feature, fixed at the root.** `SecretComposer`'s
  brace scanner searched forward for the *next* `}` from any `{`, so a stray unmatched
  brace earlier in the text could swallow a real placeholder and print its literal name
  instead of its value. Fixed: `{` only opens a placeholder if its `}` arrives before any
  further `{`.
- Passphrase is a mode, not a bool: None / Generate / Enter each time / a standing
  passphrase read from a chosen entry's Password field at share time. Only the mode and,
  for the last one, an entry reference are ever stored — never passphrase text.
- "Cancel" becomes "Close" once a request is dispatched — cancellation only stops us
  waiting, since the server may already have stored the secret, and the label says so
  rather than implying an undo that cannot happen.

<!-- M4: -->
**M4 — 2026-09-27.** `OtsClientV2`, capability detection with per-host caching, burn, and
receipt write-back. All acceptance criteria smoke-tested by the user against the hosted
service with a real account, except the self-hosted-v1-only fallback path, which has no
instance to test against and is verified by logic and unit test only. This milestone's
own spec (the v2 shape, guessed from the OpenAPI doc) turned out wrong in more places than
any prior milestone, and burn did not actually work at all until the third fix — recorded
in full here because the pattern (docs vs. reality) is worth carrying into M5.

- **The v2 conceal response bears no resemblance to the OpenAPI-derived guess.** Real
  shape verified with live calls: wrapped in `success`/`record`/`details`, `record.secret`
  and `record.receipt` carry dozens of undocumented fields, and — the load-bearing
  difference — **it hands back no `share_url`/`receipt_url` at all**, unlike v1. Both
  links are now constructed client-side from bare keys, confirmed correct by actually
  opening the constructed share URL. See `SPEC.md` §5.
- **Burning did not work at all, for anyone, until the API's published docs — not the
  OpenAPI guess — were checked directly.** All three burn endpoint variants
  (`v1/private`, `v2/private`, `v2/receipt`) require a `continue` body field, undocumented
  beyond its presence in the schema. This client sent `{}` and got HTTP 200 with
  `"success":false` unconditionally — first read as "anonymous shares can't be burned"
  (plausible: the created record's `custid`/`owner_id` were null), but a real authenticated
  account with a **confirmed populated `owner_id`** got the identical failure, which
  disproved that theory and pointed at the missing field instead. Sending
  `"continue":"true"` fixed it for both anonymous and authenticated shares, confirmed by
  the user. **`api.onetimesecret.com`'s interactive docs, not the OpenAPI JSON export
  alone, were what surfaced the actual endpoint paths and the `continue` requirement** —
  worth checking first next time a v2 assumption needs verifying.
- **v1 has a burn endpoint too** (deprecated but present, `POST /api/v1/private/{key}/burn`,
  form-encoded). An earlier version of this milestone assumed burn was v2-only and had
  `OtsClientV1.Burn` throw; that assumption was never verified and was wrong. Implemented
  properly once found.
- **A 401/403 on v2 does not always mean bad credentials.** A real account requesting a
  longer-than-default lifetime got 401 with `"...entitlement"` in the message — a plan
  limitation, not rejected credentials. The blanket "check your credentials" wording
  actively misled the user during testing. Both clients now recognize `"entitlement"` in
  the server message and give a distinct, accurate message instead.
- **Every `Label` in this codebase defaults to `AutoSize = true` and none of them had it
  turned off.** WinForms silently ignores the `Height` in a `Bounds` assignment on an
  auto-sizing `Label` and grows it to fit the text on one line instead of wrapping —
  meaning every multi-line hint/warning label's carefully computed `Bounds` was never
  actually being honoured, and text that didn't fit was simply cut off by whatever was
  drawn after it. Two real instances found by smoke test (`ShareForm`'s passphrase warning,
  `ResultForm`'s section header); a first fix attempt (raising `Height` alone) did nothing,
  because `AutoSize` was still `true`. Fixed at the root: all 19 `Label` instances across
  all four forms now set `AutoSize = false` explicitly. **Any future label with wrapped or
  variable-length text needs this set, or the bug recurs.**
- **The entry-context menu only gives a plugin one `GetMenuItem` slot per `PluginMenuType`
  — there is no way to add a second flat item without manipulating `entryMenu.Items`
  directly**, which `SPEC.md` §4 already ruled out as unsafe (KeePass owns and rebuilds
  that shared collection). "Burn Last Share" first went under the Tools menu to sidestep
  this; the user found that placement confusing during testing ("hidden under tools") and
  asked to move it. After confirming the Items-manipulation route really would need to
  break the documented rule, the fix was a submenu — one entry-context item ("OneTimeSecret")
  with "Share…" and "Burn Last Share…" nested as `DropDownItems` — which keeps both
  commands on the entry's own right-click menu without touching KeePass's menu collection.
  Costs one extra click on Share versus the milestone's original flat item; accepted as the
  right tradeoff once the alternative (Tools menu) was shown to be worse in practice.
- Capability detection (`OtsClientFactory.Detect`) writes its result to `CustomConfig`
  from a background thread during a share — the first time this codebase has written
  config off the UI thread. Not believed to be a real problem (nothing else touches config
  concurrently during a share) but flagged rather than silently introduced.
- A live API token was accidentally pasted into a throwaway diagnostic script during this
  milestone's burn investigation. Deleted immediately (it was never staged or committed),
  and the user was told to rotate it. Recorded here as a reason to keep the "credentials
  never leave the user's own database" rule absolute even for one-off debugging.

<!-- M5: -->
**M5 — 2026-09-27.** Docker-based local instance, path-prefixed base URLs, and the
integration test suite. All three acceptance criteria verified — the automated
integration tests hit the local instance directly over the real network, and the user
additionally ran a full authenticated share → reveal → burn cycle through the actual
plugin UI against it, closing the one gap the automated suite couldn't reach (see below).
A step up from every prior milestone's manual-only verification, made possible because
this milestone added no new UI (CLAUDE.md's manual-smoke-test rule is specifically for
UI/KeePass-API-touching changes; this one only proves existing client code against new
infrastructure) — the manual check happened anyway and paid off.

- **Path-prefixed base URLs already worked, since M1.** `UrlPolicy` and both clients
  already handled a path prefix correctly and already had unit coverage for it
  (`Share_HonoursAPathPrefixedBaseUrl` in both `OtsClientV1Tests` and
  `OtsClientV2Tests`). M5's contribution was proving it against a real reverse proxy
  that actually strips the prefix (`docker/Caddyfile`'s `handle_path /ots/*`) rather
  than a unit test's synthetic URL — genuinely new evidence, not new code.
- **The self-signed-certificate case needed no code either** — non-negotiable #6 means
  there was never a bypass to remove. `SelfSignedCertificate_IsRejectedWithNoOptionToProceed`
  exercises the real TLS handshake against Caddy's `tls internal` cert and confirms the
  rejection, which is the difference between "the code has no bypass" and "the code has
  no bypass and a real self-signed cert actually gets refused."
- **Path-prefix and self-signed-cert had to be tested on separate ports.** Putting the
  prefixed route behind the same self-signed HTTPS listener would have made the
  prefix test also exercise (and be blocked by) certificate rejection, conflating two
  independent things to prove. Split across `:8443` (cert only) and `:8080/ots`
  (prefix only, plain HTTP) in `docker/Caddyfile`.
- **`burn` was not re-verified by the automated integration suite** — the self-hosted OTS
  image has no API endpoint for account creation (confirmed against the same published
  docs that found the `continue` field in M4 — only a web-form signup exists), so the
  integration tests above cover anonymous share-and-reveal against the local instance
  only. **Covered instead by the user's own manual smoke test**: signed up for an
  account through the local instance's web UI, then ran a full authenticated
  share → reveal → burn cycle through the actual plugin against
  `http://localhost:3000` — confirmed working.
- New `tests/OneTimeSecret.IntegrationTests` project, deliberately separate from the
  unit suite (never touched by `dotnet test tests/OneTimeSecret.Tests`) and additionally
  gated per-test on its own `OTS_TEST_*` environment variable, so it is harmless even if
  run by accident with no instance up — confirmed both by running it with no variables
  set (4/4 pass trivially, sub-millisecond) and with them set against the live
  containers (5/5 pass with real network timing, 60ms-1s per test).

<!-- backlog-quick-share: -->
**Quick Share — 2026-09-27.** Cleared the backlog before starting M6, per the user's
request. Resolved the backlog note's own open questions:

- **SPEC.md section 2's confirmation requirement**: the backlog note's own wording
  ("silently dropping a stated security invariant is not an option") already pointed at
  the resolution — one `MessageBox` (entry title, exact field list, exact host,
  OK/Cancel) standing in for `ShareForm`'s confirmation. Built as described, not
  re-litigated.
- **`PassphraseMode.Manual`**, the one real gap the backlog note did not address (Manual
  needs a per-share prompt Quick Share has no dialog for): falls back to generating a
  passphrase instead of asking, decided by the user rather than assumed.
- **Menu placement** was no longer an open question by the time this was built — M4's
  "OneTimeSecret" entry-context submenu already existed, so Quick Share is a third
  `DropDownItems` entry there (Share… / Quick Share… / Burn Last Share…), not a new
  design problem.

Smoke-tested by the user against `eu.onetimesecret.com`; found one real bug and one
cosmetic issue in the process, both fixed:

- **The "any fields configured?" gate read the wrong source.** It checked
  `config.IncludeTitle`/`IncludeUserName`/etc — the last-used checkbox state written back
  by `ShareForm` — not the actual template text, which stays independently hand-editable
  in Options. Clearing the template in Options left those flags untouched, so Quick Share
  would have silently sent an **empty secret** while its own confirmation dialog still
  claimed "user name, password" — the confirmation would have been describing fields
  that were not actually in what got sent, directly undermining SPEC.md section 2's
  "exact fields being transmitted" requirement. Fixed by reading the field list from
  `TemplateBuilder.FieldsPresentIn(config.Template)` (new — scans the literal template
  text for known placeholders) instead of the checkbox flags, for both the confirmation
  text and the refusal gate. Confirmed fixed: an emptied template now refuses cleanly.
- Confirmation phrasing: "Share X's user name, password with host?" read awkwardly
  (comma-joined list, no "and"; the possessive breaks on a title that already has an
  apostrophe in it). Changed to "Share user name and password from "X" with host?" with
  natural-language list joining, plus a line break before "with {host}" per the user's
  suggestion.
- One error seen during testing (an account-plan-limitation message on a too-long
  default TTL) was expected behaviour confirming M4's error-message fix still works
  through this new code path, not a new bug.

<!-- code-review-fixes: -->
**Code review and competitor comparison — 2026-09-27.** Before starting M6, the user
asked for two Opus subagents: a full code review of the unmerged M4/M5/Quick Share work,
and a feature/security comparison against a similar existing plugin,
[davidsiv/KeePass-OneTimeSecretShare](https://github.com/davidsiv/KeePass-OneTimeSecretShare).
Both findings are recorded here in full since between them they corrected a factual claim
in `SPEC.md`, found two real bugs this project's own testing never would have caught
(neither had a live account or a v1 burn to test against), and confirmed the security
posture is sound.

**Competitor comparison.** Verified their earliest commit predates `SPEC.md`'s "no
existing plugin implements this, verified Sept 2026" line — that claim was wrong and is
now corrected there. Full comparison:

- *Their advantages*: `.plgx` packaging + `UpdateUrl` versioning (our unfinished M6),
  region presets, saved clipboard notes, an auto-close countdown on the result window, a
  "Test connection" button in Options, and materially better in-memory secret handling —
  they read the password via `ProtectedString.ReadUtf8()` into a `byte[]` and zero every
  buffer, never letting the secret become a managed `string`. Ours goes through
  `SprEngine.Compile`, which returns an immutable `string` we cannot scrub. That is a
  real, deliberate tradeoff (we need `SprEngine` for correct `{REF:...}` resolution,
  which they skip entirely) rather than an oversight - and it is exactly what M6's
  planned "limits of in-memory secret scrubbing" README section exists to state honestly
  rather than paper over.
- *Our advantages*: v2 detection by probing the server (theirs is a URL-substring check
  that defaults to deprecated v1), anonymous sharing, all four passphrase modes, field
  selection/custom message/live preview/recipient, per-share TTL, burn + receipt,
  self-hosted path-prefix and TLS handling, KeePass proxy support, a confirmation step
  before anything is sent, and a test suite (they have none).
- *Security, measured against our own non-negotiables*: neither project weakens TLS. Their
  real gaps: plain `http://` allowed to any host with no restriction (breaks our #7); the
  API token stored in `KeePass.config.xml` (DPAPI-encrypted, but still decryptable by
  anything running as the same Windows user, and readable while the database is locked -
  breaks the spirit of our #5); all network calls run synchronously on the UI thread
  (breaks our #9); a global `Ctrl+Z` shortcut shares immediately with no confirmation and
  no host shown (breaks our SPEC §2 rule); no `SprEngine` resolution, so a `{REF:...}`
  field sends the literal placeholder; and an unmanaged `Clipboard.SetText` with no
  auto-clear. Verdict: we are more secure overall, and the gap is not close.

**Code review**, against the diff from `main` through `backlog-quick-share`:

- **Confirmed, fixed: `OtsClientV1.Burn` read the wrong JSON level.** Verified against
  `api.onetimesecret.com`'s docs: the v1 burn response nests everything under a top-level
  `state` object, with the actual status at `state.state` - not a bare top-level `state`
  string as the client read it. Every real v1 burn would have reported "not confirmed"
  even on genuine success, and nothing in this project's own testing could have caught
  it - M5 explicitly noted the v1 burn integration test never ran, and the user's manual
  burn went through auto-detection to v2. Fixed, with a regression test asserting the old
  (wrong) flat shape is treated as unconfirmed rather than silently accepted.
- **Confirmed, fixed: Options save didn't clear the stale-cache escape hatch it
  advertises.** `OtsClientV2`'s own 404 message tells the user to "re-open the options
  and click OK to refresh it," but nothing wired that up - the cache lasted its full
  7 days regardless. Fixed: saving Options now clears `DetectedVersion`, so the next
  share re-probes.
- **Likely, fixed defensively: a 404 from Burn was permanently downgrading the host to
  v1.** A 404 on Burn is at least as plausible a sign of "this receipt is gone" (expired,
  already burned, wrong host) as "wrong API version," and caching v1 off that guess would
  silently downgrade every later Share on the host for up to 7 days - the "never silently
  fall back" invariant applied to Share, not to a burn's stale-receipt guess. Whether OTS
  actually returns 404 for a missing receipt was not confirmed live; the fix (Burn still
  retries once as v1, but never writes the cache) is safe either way and was applied
  regardless of that confirmation.
- **Confirmed, fixed: Quick Share could send an empty or literal-only secret.**
  `TemplateFields.Any` counts the `{MESSAGE}` token, which Quick Share always forces
  empty - a template of just `{MESSAGE}`, or a `{URL}` on an entry with no URL, passed
  the "anything to share?" gate and composed to nothing. `ShareForm` already guards this;
  Quick Share now does too.
- **Confirmed, fixed: Quick Share and Burn reintroduced the lock/tab-switch risk M3 ruled
  out for Share.** M3's "unreachable by design" reasoning was specific to `ShareForm`,
  which is modal - Quick Share and Burn have no modal dialog open while their request is
  in flight, so KeePass's own Lock Workspace or a tab switch is fully reachable during
  that window. Two consequences, both fixed: the receipt write-back (and burn's field
  removal) now capture the source `PwDatabase` at click time instead of re-reading
  whichever database is active when the response arrives, so a tab switch mid-flight
  cannot touch the wrong database's entry; and Quick Share now checks whether that
  captured database is still open before showing the result (link + passphrase) or
  copying to the clipboard, showing a safe generic notice instead of exposing them over a
  locked workspace.
- **Confirmed, fixed: Burn had no in-flight guard.** Quick Share already disables its own
  menu item while a request is running (`m_quickShareInFlight`); Burn had no equivalent,
  so double-clicking it could dispatch two concurrent burns. Added `m_burnInFlight`,
  mirroring the existing pattern.
- **Confirmed, fixed: an unguarded `Invoke` from the thread pool could crash the process.**
  If KeePass is closing when a Quick Share or Burn response arrives, calling `Invoke` on
  a disposed window throws on the thread-pool thread - an unhandled exception under .NET
  Framework, which takes the whole process down. `ShareForm` already guards its own
  callback this way (`IsDisposed`/`IsHandleCreated` plus try/catch); the same `SafeInvoke`
  pattern now covers Quick Share and Burn too.
- **Confirmed, fixed: the version cache was keyed by host only.** Two path-prefixed
  instances on one host, or the same host on two ports - the exact shape of M5's own
  Docker setup (`localhost:3000` vs `localhost:8080/ots`) - would have shared one cache
  entry. Now keyed on the full normalized base URL (`OtsClientFactory.CacheKey`);
  `PluginConfig.DetectedForHost` renamed to `DetectedForUrl` to match what it actually
  stores.
- **Fixed, low cost: the "entry no longer exists" warnings were misleading across
  multiple databases.** A `PwUuid` configured while database A was open reads as
  "deleted" when database B is active, even though the entry is intact in A. Reworded in
  `CredentialResolver`/`PassphraseResolver` to say the entry was not found *in this
  database* and suggest opening the right one - the honest, cheap fix. The user's
  broader concern (global `CustomConfig` doesn't know which database a credential
  reference belongs to) is real but architectural - storing the reference in the
  database's own `PwDatabase.CustomData` instead would fix it properly. **Deliberately
  not attempted in this pass**: it changes where credentials are stored for every
  existing config value, and deserves its own dedicated design discussion rather than
  being folded into a review-fix batch. Left as a backlog candidate.
- **Considered, not fixed: `CustomConfig` is written from a background thread during
  capability detection.** `AceCustomConfig` is a plain dictionary with no documented
  thread-safety guarantee, and a share's detection probe can race a simultaneous Options
  save. The reviewer's opinion was to fix it (defer the write into the `Invoke`
  callback); this project's judgement is that the practical risk is small enough (this
  is the only background config write in the codebase, and the race window is narrow)
  that restructuring the client-factory/cache interface to defer every write to the UI
  thread is not worth doing under the same time pressure as the rest of this batch.
  Recorded here as a deliberately accepted risk, not a silently dropped finding.
- **Not confirmed either way**: whether `Uri.AllowAutoRedirect = false` fully prevents an
  `Authorization` header from following a redirect on .NET Framework was flagged by the
  reviewer as unverified from memory. `AllowAutoRedirect` is already `false` on every
  request this plugin makes, so there is no redirect to follow in the first place -
  recorded as a non-issue for this codebase specifically, not because the underlying
  question was resolved.
- Minor wording fix: the anonymous-fallback confirmation always said "Share anonymously
  anyway?", including when triggered from Burn. `ConfirmAnonymousFallback` now takes the
  verb, so Burn says "Burn anonymously anyway?" instead.
- Minor layout fix: the new `m_writeBackReceipt` checkbox (79 characters) was sized for
  one line and would have clipped its second line - the same class of bug as M4's
  `AutoSize` findings, just on a `CheckBox` rather than a `Label` this time.

<!-- M6: -->
**M6 — 2026-09-28.** Multi-database credential/passphrase resolution. Design went
through two rejected drafts before landing on the shipped one - recorded in full since a
future session should not re-propose either:

1. *Store both references in `PwDatabase.CustomData`, nothing else changes.* Rejected:
   some databases are shared with customers, and most databases have no OTS credentials
   of their own to store in the first place - a bare per-database reference doesn't
   address that.
2. *Keep everything in global `CustomConfig`, namespaced per database by
   `PwDatabase.RootGroup.Uuid`.* Rejected: the user is fine with storing a reference
   inside the `.kdbx` itself (it travels to another PC with the plugin installed for
   free) - the actual gap is that most databases need to *borrow* the credential entry
   configured in the user's main database, not get their own.
3. **Shipped**: a database's own reference lives in its own `PwDatabase.CustomData`; a
   separate, global, local-only fallback pointer (`PluginConfig.CredentialFallback`/
   `PassphraseFallback`) names a database (`PwDatabase.RootGroup.Uuid`) and an entry
   inside it, resolved only when that database happens to be open in another tab
   (`MainForm.DocumentManager.Documents`). See `SPEC.md` section 6 for the full
   resolution order and the reasoning for the split.

Implementation notes:

- `DatabaseScopedConfig` (new) mirrors `PluginConfig`'s existing hex `PwUuid`
  read/write logic against `PwDatabase.CustomData.Get/Set/Remove` instead of
  `AceCustomConfig` - same encode/decode, different backing store.
- A broken *own* reference (entry deleted from the database that configured it) fails
  loudly rather than falling through to the fallback - the user configured this
  database explicitly and should hear that it broke, not silently get a different
  credential they never asked this database to use. Only an *unconfigured* database
  (nothing of its own either way) tries the fallback.
- Migration is automatic and one-time per database, guarded by a
  `CustomData["OneTimeSecret.Migrated"]` marker set unconditionally the first time
  Options opens for that database post-upgrade - without it, an explicit later "Clear"
  (which just removes the key, indistinguishable from "never configured") would have
  been silently repopulated from the stale pre-M6 global value on the next Options open.
- Writing the primary reference marks the database modified (`mainWindow.UpdateUI`, the
  same call `WriteBackReceipt` already uses) only when it actually changed, so an
  Options save that touches nothing but a global setting does not spuriously prompt
  "save changes?" on exit. The fallback pointer, being global `CustomConfig`, still
  saves instantly with no such flag - a real, deliberate asymmetry between the two
  tiers, not an inconsistency.
Smoke-tested by the user against two real, simultaneously open `.kdbx` files (a
`test.kdbx` with credentials configured, and a throwaway `Customer.kdbx` with none of
its own) - `KeePassAdapters/` has never been unit-testable (per M1's notes), so this
milestone's acceptance criteria were manual-only. Found and fixed three real bugs in the
process, none caught by the build or the 218 (now 225) passing unit tests:

- **Confirmed, fixed: migration copied the legacy global reference into every database,
  not just the one it actually belonged to.** `MigrateLegacyReferences` ran the first
  time Options opened for *any* database post-upgrade and unconditionally copied the
  pre-M6 global `CredentialEntryUuid` into that database's own `CustomData` - including
  `Customer.kdbx`, which never had that entry, producing a false "no longer exists in
  this database" the moment its Options was first opened. The legacy value has no idea
  which database it belongs to either (the exact problem M6 exists to fix), so blindly
  trusting it for every database reintroduced the same confusion one level down. Fixed:
  migration now only applies to a database if `database.RootGroup.FindEntry` actually
  finds the referenced entry there; otherwise it just marks that database migrated (so
  the check is not needlessly repeated) and leaves its primary reference untouched.
- **Fixed, cosmetic: the fallback status text showed the entry's title and username, not
  just the database name.** `"Use This Entry"` built its cached `Display` string as
  `"{database} - {entry title} - {username}"`; shown back in the "fallback database not
  open" warning, this read as unnecessarily specific (and, for a database shared with a
  customer, would have named an entry in a database the customer has no business knowing
  about). Fixed to store just the database name.
- **Confirmed, fixed: burning a passphrase-protected secret was rejected by the server,
  for every passphrase mode.** Found by the user testing burn against a real account:
  the error named the passphrase specifically. First fixed narrowly for
  `PassphraseMode.Entry` only (its passphrase is re-derivable from current config, so
  `OnBurnClick` could just re-resolve it) - the user's own follow-up test then showed the
  identical rejection for `Generate` and `Manual` shares too, which an earlier, less
  rigorous test round had wrongly reported as working. Re-resolving is structurally
  impossible for those two (the passphrase is shown once in `ResultForm` and was never
  persisted anywhere), so the real fix is different: **the user asked for the passphrase
  to be written back alongside the receipt** (a second protected custom string, `OTS
  Passphrase`, cleared together with `OTS Receipt` on burn or removal) rather than
  accepting the gap as permanent - rejecting an earlier, more conservative instinct to
  just document it as an unfixable limitation. This is not the `KeePass.config.xml`
  problem non-negotiable #5 rules out: it never leaves the entry, and is protected the
  same way that entry's own password already is. `IOtsClient.Burn` gained a `passphrase`
  parameter, sent the same way Share already sends it (`passphrase=...` form field on
  v1, `"passphrase":"..."` JSON field on v2); `OnBurnClick` now reads the receipt's own
  stored passphrase first (correct for this specific share regardless of mode, and
  immune to the configured `Entry` entry having changed since), falling back to a live
  `PassphraseResolver` re-resolution only for a receipt written before this field
  existed.

<!-- M7: -->
**M7 — 2026-09-30.** `Plugin.UpdateUrl`, `UPDATE.txt`, the v1.0.0 version bump, README
install/config/security sections, and `CHANGELOG.md`. The `.plgx` build was investigated
in depth and deliberately deferred rather than shipped broken or half-verified — recorded
here in full since it cost real time and a future session should not re-attempt the same
things without reading this first.

- **`.plgx` creation and load-time compilation are broken, or at least inconsistent, for
  this project on this machine, against KeePass 2.61.1.0.** Confirmed step by step, not
  assumed:
  - `KeePass.exe --plgx-create <dir>` against this project's real, SDK-style `.csproj`
    *does* correctly bundle all 30 source files (verified by extracting the resulting
    container and diffing the file list) - contrary to the concern that implicit file
    globbing (no explicit `<Compile Include>` items) would confuse KeePass's plgx
    creator. That part works.
  - Loading that `.plgx` in an isolated, throwaway KeePass profile (a second portable
    copy, config and plugin cache both cleared) triggers a real load-time compile - but
    the result is consistently an empty ~3KB stub assembly with none of this plugin's
    types in it, regardless of what the `.csproj`'s reference list said. Tried three
    variants (the real project's `Microsoft.NETFramework.ReferenceAssemblies.net472`
    `PackageReference` intact; removed; replaced with explicit
    `<Reference Include="System.Windows.Forms" />` etc.) - identical 3072-byte result
    every time.
  - Isolated the actual source: extracted the exact bundled `.cs` files from KeePass's
    own plgx-compile cache directory and compiled them directly with `csc.exe`, giving
    it only `System`, `System.Core`, `System.Drawing`, `System.Windows.Forms`, and
    `KeePass.exe` as references - a clean, complete, correctly-sized (108KB) assembly
    with zero errors. **The source and the reference list are both fine; whatever KeePass's
    own plgx compiler does internally with the `.csproj`'s reference info is not
    working.**
  - Tried the classic, explicitly-namespaced MSBuild `.csproj` format the older official
    plgx docs describe (explicit `<Compile Include>` and `<Reference>` items, no SDK
    attribute) as an alternative - `--plgx-create` failed to produce any output at all
    against it, including against a maximally minimal one-file test plugin built the
    same way. Classic-style is not a working fallback on this KeePass version, whatever
    the docs say.
  - **The failure is not even reliably reproducible.** The exact same directory and
    command that had produced a working container three times in a row (still failing
    the load-time compile each time, per above) was re-run with literally nothing
    changed and failed to produce any output on the fourth attempt. This point is why
    the investigation stopped: a content-dependent bug has a fix; this behaved like
    stateful flakiness (found a `PluginCache` directory under `%LocalAppData%\KeePass`
    that plausibly explains some of it, but clearing it did not restore reliable
    success either).
- **Decision, given to the user directly rather than assumed**: ship v1.0.0 as
  DLL-only, which `SPEC.md` §10 already explicitly permitted as an acceptable first
  step. `.plgx` stays a real backlog item, not a silently dropped one - worth revisiting
  on a second machine (to separate "this project's `.csproj`" from "this machine's
  KeePass/plgx-cache state") before sinking more time into it blind.
- Screenshots for the README are placeholders, not yet captured - this plugin has no
  UI-automation path (established throughout this project), so they need the user's own
  captures. Not a blocker for the rest of this milestone, per the user's own choice when
  asked.
