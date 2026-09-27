# CHECKLIST — for Klaus, not for the agent

What you do by hand, in order. Everything else lives in `BOOTSTRAP.md`, `SPEC.md`,
`CLAUDE.md` and `ROADMAP.md`, which are written for Claude Code.

Machine: **PC-26-Klaus**. Repo at `C:\Code\ML-Smartfile\keepass-onetimesecret\`,
KeePass at `C:\KeePass\`.

The agent has no admin rights, so every installation below is yours. It can verify,
build, and write code — it cannot install.

---

## Step 1 — install, once (you, with admin)

- [ ] **Git**, if `git --version` fails — [git-scm.com](https://git-scm.com/download/win)
- [ ] **.NET SDK 8.0 or newer**, if `dotnet --info` shows none —
      [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
- [ ] **KeePass 2.x portable ZIP** from [keepass.info](https://keepass.info/download.html),
      unpacked to `C:\KeePass\` (portable, not the installer — the installed copy under
      `Program Files` would need elevation on every single build)
- [ ] Create `C:\KeePass\Plugins\` if the ZIP didn't bring one
- [ ] **Grant your own account Modify on `C:\KeePass\`** — right-click the folder →
      Properties → Security → Edit → your user → tick Modify → OK

That last one is easy to skip and annoying to diagnose. Folders created directly under
`C:\` are usually read-only for normal users, and without Modify the agent can neither
create the test database nor copy the plugin DLL into `Plugins\` after a build. You'd
see a stream of access-denied errors that look like bugs in the plugin.

- [ ] Create `C:\Code\ML-Smartfile\keepass-onetimesecret\`
- [ ] Copy the five `.md` files into it

## Step 2 — verification run

- [ ] Open a **normal** terminal in the repo folder and start Claude Code
- [ ] Paste prompt 1 (below)
- [ ] Read the two tables it reports. If the "missing" table has rows, install those,
      then re-run the same prompt.
- [ ] Green means: `dotnet --info` works · `C:\KeePass\KeePass.exe` found · write test in
      `Plugins\` succeeded · scratch build succeeded · `test.kdbx` has four entries

## Step 3 — each milestone, same rhythm

- [ ] Paste prompt 2, with the milestone number changed
- [ ] Answer its two opening questions
- [ ] Let it work; it stops and hands you a checklist
- [ ] **Restart KeePass before testing.** It caches plugins. If a change seems to have
      done nothing, this is why — it is the most common way to lose an hour here.
- [ ] Run its checklist (for M0, the one below)
- [ ] Paste your results back, including anything that merely looked off
- [ ] When happy: tell it to commit, then start the next milestone

## M0 smoke test — the part only you can do

- [ ] Start `C:\KeePass\KeePass.exe`, open `test.kdbx`, password `TestOnly!2026`
- [ ] Tools → Plugins: "OneTimeSecret" listed, with a sensible version?
- [ ] Right-click `Test Simple`: does **Share with OneTimeSecret…** appear near the
      bottom, in the same block as "Start Remote Connection"?
- [ ] Click it: correct entry title in the message box?
- [ ] Select two entries, right-click: is the item greyed out?
- [ ] Close KeePass: clean exit, no hang?

## Before M2 — not now

- [ ] Create a OneTimeSecret account and generate an API token
- [ ] Put it into an entry in `test.kdbx`: username = your OTS email, password = the token

M0 and M1 run anonymously and need none of this.

---

## Prompt 1 — verification

```
Read BOOTSTRAP.md and run the checks it describes.

You have no administrator rights on this machine and must not install
anything — no winget, no installers, no unpacking into C:\. If something
is missing, report it; do not work around it.

Run every check, not just up to the first failure, and report both
tables as the file describes. Do not start M0.
```

## Prompt 2 — a milestone

Change the milestone number each time.

```
Read SPEC.md, CLAUDE.md and ROADMAP.md in full.

Implement M0 only. Stop at the M0 acceptance criteria.

You cannot operate the KeePass UI. When a milestone needs a manual
smoke test, give me a numbered checklist and wait for my results.

Before you begin, tell me which M0 acceptance criteria you can verify
yourself and which need me, plus anything in the spec you think is
wrong or underspecified.
```

---

## If something goes sideways

- **Access denied writing to `C:\KeePass\`** → the Modify permission in step 1 is missing.
- **Menu item doesn't appear at all** → assembly name and class name must match exactly
  (`OneTimeSecret.dll` ⇄ `OneTimeSecretExt`). KeePass fails silently otherwise. Tell the
  agent to check this first.
- **Change seems to have no effect** → KeePass wasn't restarted.
- **Agent asks for admin, or wants Visual Studio** → it has drifted from `BOOTSTRAP.md`.
  Point it back there rather than approving.
- **nuget.org unreachable** → the no-Visual-Studio build path won't work, and the plan
  needs revisiting. Tell me rather than letting the agent improvise.
- **Agent says a milestone is done without giving you a checklist** → ask how it verified
  the UI criteria. It can't, so it shouldn't claim them.
