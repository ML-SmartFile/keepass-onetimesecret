# CLAUDE.md

Working agreement for this repository. Read `SPEC.md` before writing code and
`ROADMAP.md` before starting a milestone.

## What this is

A KeePass 2.x plugin (C#, .NET Framework 4.7.2, WinForms) that shares a password
entry as a one-time link via the OneTimeSecret API. It handles plaintext credentials
and sends them to a remote server. Code quality here is a security property, not a
style preference.

## Build and run

```powershell
# Build
msbuild src/OneTimeSecret/OneTimeSecret.csproj /p:Configuration=Debug

# Deploy to local KeePass (edit the path in the script once)
./build/post-build-copy.ps1

# Unit tests — must pass with no KeePass installed
dotnet test tests/OneTimeSecret.Tests

# Integration tests — real network, gated behind OTS_TEST_*, never run by the above.
# See README.md for the local Docker instance this is meant to run against.
dotnet test tests/OneTimeSecret.IntegrationTests
```

KeePass caches loaded plugins. **Restart KeePass after every rebuild.** If a change
seems to have no effect, that is almost always the reason — check before debugging.

To debug: start KeePass, then attach the debugger to `KeePass.exe`.

## Non-negotiables

1. **`OneTimeSecret.dll` ⇄ class `OneTimeSecretExt`.** If the assembly name and the
   `<AssemblyName>Ext` class name ever diverge, KeePass silently refuses to load the
   plugin and gives no error. Do not rename either in isolation.
1a. **`AssemblyInfo.cs`'s `AssemblyProduct` must be exactly `"KeePass Plugin"`.**
   KeePass's `PluginManager.LoadPlugins` reads each DLL's Win32 version resource and
   `continue`s past it silently if `FileVersionInfo.ProductName` isn't literally
   `AppDefs.PluginProductName` (`"KeePass Plugin"`) — *before* hashing the file or
   calling `Assembly.LoadFrom`. No error, no compatibility-cache entry, nothing in the
   Plugins dialog. `AssemblyTitle` is safe to customize (`"OneTimeSecret"`) — it maps to
   `FileDescription`, the friendly name shown in the dialog. Cost real debugging time
   once already; don't let `AssemblyProduct` drift from the exact string.
2. **No new runtime dependencies.** Nothing that ships inside the plugin. JSON via
   `DataContractJsonSerializer` or the hand-rolled reader in `Ots/Json.cs`. If you
   think a runtime dependency is genuinely required, stop and ask rather than adding it.
   *Build-time-only* packages are the one exception and are already in use:
   `Microsoft.NETFramework.ReferenceAssemblies.net472` provides the `net472` reference
   assemblies so the project builds without Visual Studio. Such packages must carry
   `PrivateAssets="all"` and must never appear in the shipped output. Test-project
   packages (the xUnit stack) are likewise fine.
3. **`Core/` and `Ots/` must not reference KeePass.** They are the unit-testable core.
   Anything needing `KeePass.*` or `KeePassLib.*` goes in `KeePassAdapters/` or `Ui/`.
4. **Never log a secret or a token.** No `Debug.WriteLine`, no `Console.WriteLine`, no
   exception message that embeds a secret value, no secret in a test fixture that gets
   printed on failure. Scrub before rethrowing.
5. **Never write the API token to `CustomConfig`.** Credentials are read at call time
   from a user-chosen database entry, referenced by `PwUuid`.
6. **Never weaken TLS.** No `ServerCertificateValidationCallback` returning true, not
   even behind a debug flag, not even temporarily while testing.
7. **Plain `http://` is refused** unless the host is loopback *and* `AllowInsecureHttp`
   is set. There is no third case.
8. **No network call to any host other than the configured base URL.** No telemetry.
9. **All HTTP off the UI thread; all UI on the UI thread.** Marshal with
   `MainWindow.Invoke`. A blocked KeePass UI is a bug, not a cosmetic issue.

## Conventions

- Pass entry field values through `SprEngine.Compile` with a proper `SprContext`.
  A raw `Strings.ReadSafe` will hand OneTimeSecret an unresolved `{REF:…}` string.
- Wrap custom forms in `GlobalWindowManager.AddWindow` / `RemoveWindow` in `try/finally`.
- Use `ClipboardUtil.Copy`, never `Clipboard.SetText` — and follow it with
  `MainWindow.StartClipboardCountdown()`, or the value never auto-clears. See `SPEC.md` §4.
- Materialize every value needed for a request **before** dispatching it — the database
  can lock mid-flight.
- JSON readers ignore unknown fields and fail loudly on missing expected ones.
- Prefer explicit over clever. Someone auditing this needs to follow it quickly.

## Testing expectations

- Every milestone in `ROADMAP.md` has acceptance criteria. Do not mark one done until
  each is demonstrably met.
- Unit tests must pass without KeePass installed and without network access.
- Integration tests against a real OneTimeSecret instance are gated behind
  `OTS_TEST_BASE_URL` / `OTS_TEST_USER` / `OTS_TEST_TOKEN` and must not run by default.
- **A successful compile is not a completed milestone.** Anything touching the UI or
  the KeePass API needs a manual smoke test in a running KeePass against a throwaway
  `.kdbx`. If that test has not been run yet, say so plainly instead of implying it has.
- Never test against a real password database.

## Things that are genuinely uncertain

The OneTimeSecret v2 response shape in `SPEC.md` comes from the published OpenAPI
spec, not from a live call. The construction of the user-facing share URL — especially
when `share_domain` is set — is unverified. Confirm it against a real instance before
building on top of it, and flag any divergence rather than silently adapting the spec.

## Commits

Conventional commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`). One milestone per
branch, named `m1-v1-client` and so on. Keep commits small enough to review.
