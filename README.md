# keepass-onetimesecret

A KeePass 2.x plugin that shares a password entry as a one-time link via the
[OneTimeSecret](https://onetimesecret.com) API. See `SPEC.md` for the technical spec and
`ROADMAP.md` for what is built so far.

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