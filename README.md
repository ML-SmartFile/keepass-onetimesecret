# keepass-onetimesecret

A KeePass 2.x plugin that shares a password entry as a one-time link via the
[OneTimeSecret](https://onetimesecret.com) API — right from the entry's own context
menu, with no copy-pasting a password into a browser. Supports the hosted service and
self-hosted instances, anonymous or authenticated sharing, an optional passphrase, and
burning a share early. See `SPEC.md` for the technical spec and `ROADMAP.md` for what is
built so far.

## Install

1. Download `OneTimeSecret.dll` from the
   [latest release](https://github.com/ML-SmartFile/keepass-onetimesecret/releases/latest).
2. Copy it into KeePass's `Plugins` folder (Tools → Plugins → the folder-open icon, or
   `<KeePass install directory>\Plugins` directly).
3. Restart KeePass. It should now show up under Tools → Plugins as "OneTimeSecret."

Only the DLL is distributed for now — see the note in `ROADMAP.md`'s M7 progress note if
you're wondering about a `.plgx` (the source-compiled format some other plugins ship):
it's deferred, not forgotten.

## Configuring it

Tools → **OneTimeSecret Options…**

- **Server**: pick a region preset or enter a custom base URL (for a self-hosted
  instance). `auto` API-version detection works for both; force `v1`/`v2` only if you
  need to override it.
- **Credentials**: optional. Pick a KeePass entry whose **user name** is your OneTimeSecret
  account email and whose **password** is your API token — the token itself is never
  written to `KeePass.config.xml`, only a reference to the entry (see *Security* below).
  Leave it unset to share anonymously. If you use more than one KeePass database, each
  can have its own credential entry, or borrow one from another database you keep open
  alongside it — see `SPEC.md` §6 for the full fallback mechanics.
- **Share defaults**: default lifetime, the content template (which entry fields get
  included and in what order — see the placeholder list in the dialog), and the default
  passphrase mode (none / generate one / enter one each time / read one from a standing
  KeePass entry).
- **Write the receipt back to the entry**: stores a reference on the entry itself so you
  can burn the share early later, from that entry's own right-click menu.

To share an entry: right-click it → **OneTimeSecret → Share…** (opens a dialog to
confirm exactly what's being sent and adjust it per-share) or **Quick Share…** (shares
immediately using your configured defaults, after one confirmation). **Burn Last
Share…** appears once an entry has an active share to revoke.

<!--
  Screenshots to add here once captured (see ROADMAP.md's M7 progress note):
  - The "OneTimeSecret" entry context submenu (Share… / Quick Share… / Burn Last Share…)
  - The Share dialog (ShareForm)
  - The result dialog after a successful share (ResultForm)
  - The Options dialog
-->

## Security

- **What gets sent, and to whom**: only the composed secret text (the entry fields you've
  configured, resolved through KeePass's own `{REF:...}` engine) and, if configured, your
  OneTimeSecret credentials — sent to the single base URL you configured, over HTTPS, and
  nowhere else. There is no telemetry and no other network destination this plugin will
  ever contact.
- **TLS is never weakened.** Plain `http://` is refused unless the host is loopback and
  you've explicitly enabled the insecure-http toggle for local testing; there is no
  certificate-bypass option, ever, for any host.
- **Credentials and passphrases are references, not copies, almost everywhere.** The
  options dialog stores a `PwUuid` pointing at the KeePass entry holding your API token
  or standing passphrase — never the token or passphrase text itself — and reads the real
  value from that entry only at the moment of sharing. The one deliberate exception: if
  you enable "write the receipt back to the entry" for burn support, the exact passphrase
  a given share used is also written to a second protected field on that same entry (so
  it can burn later even for a one-time generated passphrase), cleared together with the
  receipt once burned. This stays inside your own encrypted database — never
  `KeePass.config.xml`, never a shared file.
- **The honest limit of in-memory secret scrubbing in .NET**: the composed secret and any
  passphrase pass through immutable `System.String`s (via KeePass's own `SprEngine` and
  this plugin's JSON/form-body construction) that cannot be reliably zeroed afterward.
  This is a deliberate tradeoff, not an oversight — it's what correct `{REF:...}`
  resolution requires — and it means a memory dump taken while sharing could, in
  principle, recover the secret text. A plugin using raw byte buffers throughout could
  close this gap but would have to give up `SprEngine` resolution to do it.

## Local testing against a self-hosted instance

`docker/` runs a disposable OneTimeSecret instance for testing this plugin's self-hosted
handling — path-prefixed base URLs, self-signed certificates, and the manual/anonymous
API flows — without touching the public hosted service. It is not part of the shipped
plugin and never holds anything real; see the comments in `docker/docker-compose.yml`
before reusing any part of it elsewhere.

```powershell
cd docker
docker compose up -d
```

Three entry points come up:

- `http://localhost:3000` — plain HTTP, no certificate involved.
- `https://localhost:8443` — Caddy in front, terminating TLS with a self-signed
  certificate (Caddy's `tls internal`). Configure the plugin's base URL to this and
  expect the TLS-failure message from `SPEC.md` section 8 — this plugin never offers to
  bypass certificate validation, on this instance or any other.
- `http://localhost:8080/ots` — plain HTTP again, but path-prefixed, on its own port so
  it can be tested without first trusting the self-signed certificate above.

All three serve v2 (bundled with the pinned image), so there is no local way to exercise
the v1-only fallback path — that one is covered by unit tests only; see `ROADMAP.md`'s
M4 notes.

Stop it with `docker compose down` from the same directory; add `-v` to also drop the
Redis volume and start clean next time.

### Integration tests

`tests/OneTimeSecret.IntegrationTests` hits whichever instance you point it at over the
real network. It is a separate project from the unit suite and is **never** run by
`dotnet test tests/OneTimeSecret.Tests` or any other default command — every test inside
it also checks its own environment variable and passes trivially if unset, so running it
by accident is harmless either way.

```powershell
$env:OTS_TEST_BASE_URL = "http://localhost:3000"
$env:OTS_TEST_SELFSIGNED_URL = "https://localhost:8443"
$env:OTS_TEST_PATHPREFIX_URL = "http://localhost:8080/ots"
dotnet test tests/OneTimeSecret.IntegrationTests
```

`OTS_TEST_USER` / `OTS_TEST_TOKEN` are optional — set both to also exercise the
authenticated and burn paths against an account you create on the local instance; leave
them unset to cover the anonymous flows only, which need no signup.