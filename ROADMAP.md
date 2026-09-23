# Roadmap

Seven milestones. Each is a branch and a PR. Do not start the next before the previous
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
>   unburnable. **M3 still owes:** the passphrase row and its separate copy button.
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

## M6 — Packaging and release

**Branch:** `m6-release`

- `build/build-plgx.ps1` producing a working `.plgx`
- `Plugin.UpdateUrl` pointing at a version file in the repo
- README: install instructions, configuration walkthrough, screenshots, and an honest
  security section covering what the plugin transmits, to whom, and the limits of
  in-memory secret scrubbing in .NET
- CHANGELOG, v1.0.0 tag, GitHub release with both DLL and `.plgx`

**Acceptance:**
- The `.plgx` loads cleanly in a fresh KeePass install on a machine that has never
  built the project
- KeePass's update check detects a newer version when the version file is bumped
- The README's install instructions work when followed literally by someone who has
  not seen the codebase

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
