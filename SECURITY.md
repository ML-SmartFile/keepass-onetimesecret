# Security Policy

This project handles plaintext credentials (it reads a KeePass entry's password and
sends it to the OneTimeSecret API). Code quality here is a security property, not a
style preference — see `CLAUDE.md`'s non-negotiables for the specific invariants every
change must preserve (no logging secrets, no weakened TLS, no network destination other
than the configured base URL, credentials never written to `KeePass.config.xml`, etc.).

## Supported versions

Only the latest release is supported. There is no long-term support branch; fixes land
on the next release.

## Reporting a vulnerability

**Please do not open a public GitHub issue for a security vulnerability.**

Use GitHub's private vulnerability reporting instead:

1. Go to the [Security tab](https://github.com/ML-SmartFile/keepass-onetimesecret/security)
   of this repository.
2. Click **Report a vulnerability**.

This opens a private advisory visible only to the maintainer and you, so the issue can be
discussed and fixed before any public disclosure.

If you'd rather not use GitHub for the initial report, you can instead open a regular
issue asking to be contacted privately, with no technical details included — a
maintainer will follow up.

## What's in scope

- The plugin itself (`src/OneTimeSecret/`, `Core/`, `Ots/`, `KeePassAdapters/`, `Ui/`).
- Anything that could leak a password, API token, or passphrase — to logs, to disk
  outside the user's own KeePass database, or to a network destination other than the
  single OneTimeSecret base URL the user configured.
- TLS/certificate handling.

## What's out of scope

- `docker/` — a disposable local test instance for self-hosted-mode testing. It is never
  part of the shipped plugin and is not meant to be reused or exposed anywhere.
- The OneTimeSecret service itself (onetimesecret.com or a self-hosted instance) — report
  those upstream.
- The in-memory secret-scrubbing limitation already documented in `README.md`'s Security
  section — this is a known, accepted tradeoff (see that section for why), not something
  that needs a new report.
