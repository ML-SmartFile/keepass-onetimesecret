# Changelog

All notable changes to this project are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). See `ROADMAP.md`'s progress
notes for the full milestone-by-milestone development history.

## [1.0.0]

Initial release.

### Added

- Share a KeePass entry as a one-time link via OneTimeSecret, from the entry's own
  right-click menu — anonymous or authenticated, with an optional passphrase (generated,
  entered per-share, or read from a standing KeePass entry).
- **Quick Share**: send an entry using your configured defaults with a single
  confirmation, for when the full dialog is more than you need.
- **Burn Last Share**: revoke an active share early, right from the entry it came from.
- Automatic v1/v2 API detection, so both the hosted service and older self-hosted
  instances work without manual configuration; self-hosted support also covers
  path-prefixed base URLs and correctly rejects self-signed certificates rather than
  offering to bypass them.
- Configurable per-share lifetime, a customizable content template (which fields get
  included and in what order), and an optional custom message.
- Credentials and standing passphrases are referenced by KeePass entry, never copied
  into `KeePass.config.xml` — read from the entry only at the moment of sharing.
- Works across multiple simultaneously open KeePass databases: each database can have
  its own credential/passphrase entry, or fall back to one from another database you
  keep open alongside it, without anything about that fallback ever being written into
  a database that doesn't have credentials of its own.

### Known limitations

- Distributed as a DLL only for now; `.plgx` (KeePass's source-compiled plugin format)
  is deferred — see `ROADMAP.md`'s M7 progress note for why.
- In-memory secret scrubbing has real limits under .NET — see the README's *Security*
  section for the honest version.
