# Security model

*AutoWork — Gravicode Studios, led by Kang Fadhil*

AutoWork runs an AI agent with access to your filesystem. That is useful precisely because it is
powerful, which means the boundaries need to be explicit, testable, and honestly described.

This document states what is guaranteed, what is merely gated, and what is not protected at all.

---

## The claim

**AutoWork can only reach the folders you grant it.**

Everything below is in service of making that claim true and verifiable.

## Deny by default

A fresh install has no folder grants beyond one: `Documents\AutoWork`. Every file operation
outside it is refused until you add a folder in **Settings › Permissions**.

This is deliberately inconvenient. An assistant that could read your whole home directory the
moment you installed it would be a worse trade than one that makes you think for ten seconds.

![Settings › Permissions: the granted folders and the capability switches](../images/settings-permissions.png)

## One chokepoint: `PathGuard`

Every filesystem tool routes through `PathGuard` before touching anything. The checks run in
order, and all must pass:

**1. Canonicalise, resolving symlinks.**
The path is expanded, made absolute, and then walked component by component with every symbolic
link resolved to its final target. Without this step, a link inside a granted folder pointing at
`~/.ssh` would be a free pass to anywhere on disk. Broken or circular links are refused.

**2. Reject protected locations.**
OS internals (`C:\Windows`, `/etc`, `/usr/bin`, `/System`, …) and AutoWork's own data folder are
never reachable, even if you explicitly grant them. AutoWork's folder holds the secret store, so
this closes the obvious loop where the agent reads its own keys.

**3. Require containment in a granted root.**
The canonical path must sit inside a folder you granted, compared with a **segment boundary** —
so a grant on `~/Documents` does not accidentally cover `~/Documents-backup`. The most specific
matching grant wins, which lets a read-only sub-folder narrow a read/write parent.

**4. Apply denied patterns.**
Glob patterns refused even inside a granted folder. Defaults:

```
**/.ssh/**    **/.aws/**    **/.gnupg/**    **/.git/config
**/*.pem      **/*.key      **/*.pfx        **/id_rsa*
**/.env       **/.env.*     **/secrets.json
**/AppData/Local/Microsoft/Credentials/**   **/Library/Keychains/**
```

Add your own in Settings.

### Tested, not asserted

`SandboxTests` is written as a series of attempted escapes:

- relative traversal (`granted/../private/secrets.txt`)
- absolute paths outside every root
- a sibling folder sharing a name prefix with a granted one
- a **symlink inside a granted folder pointing outside it**
- writing to a read-only grant
- reading a denied pattern inside a granted folder
- reaching AutoWork's own data folder after explicitly granting it
- operating with no grants at all

All are expected to fail with a specific reason code. If you change `PathGuard`, these are the
tests that tell you whether you broke the product's central promise.

## Capabilities are opt-in

| Capability | Default | Notes |
|---|---|---|
| Read granted folders | On, once granted | — |
| Write granted folders | Per-grant | Each folder is read-only or read/write |
| Delete | **Off** | Separate switch from writing |
| Soft delete | On | Deleted files move to a recycle folder inside AutoWork's protected directory, so the agent cannot read them back. An index records where each came from, and the Recycle page puts them back — but never into AutoWork's own folder |
| Shell commands | **Off** | Optional executable allow-list; approval-gated |
| MCP servers | **Off** | Starts external programs with your rights. See below |
| Skill scripts | **Off** | Runs code from a repository; approval-gated. See below |
| Screen capture | On | Approval-gated |
| Mouse and keyboard control | **Off** | Approval-gated |
| Network | On | Optional host allow-list |

A capability that is off is not merely refused — the tools that use it are **never described to
the model at all**. It cannot try what it does not know exists.

## Consent

Actions that write, delete, run commands or drive input raise an approval request. It appears
**inline in the Work Tape**, not as a modal dialog, showing the actual command line or file list
rather than a summary.

Modal dialogs train people to click through them. Keeping the request attached to its context is
the only way consent means anything.

"Allow for this run" exists for repeatable, reversible kinds — writes, network, screen capture —
so a 200-file batch asks once. It is deliberately **not offered for deletes or shell commands**:
a standing grant is the wrong affordance for an irreversible act.

A write over an existing file also shows **what would change** — a line diff, capped so the card
stays readable. Creating a new file shows no diff, on purpose: rendering a new file as a wall of
additions teaches people to skim past the case where it matters.

## Standing rules

Rules in Settings › Permissions answer a class of prompt once instead of every time: *always
allow writes under ~/Projects*, *never allow deletes*.

This is the first thing in AutoWork that lets a decision outlive the moment it was made, so it is
bounded by four rules of its own.

**Deny always wins.** Every matching rule is considered, not the first one found, and a deny beats
an allow whatever order they were written in. A standing deny also beats an "allow for this run"
clicked earlier in the same run — it is the more deliberate statement.

**A rule changes what you are asked, never what is permitted.** An allowed action still goes
through `PathGuard` when it runs. Point an allow rule at a folder you have not granted and the
action is refused anyway. The worst a careless allow rule can do is stop you being asked about
something the sandbox was always going to permit.

**An allow must name a folder.** "Always allow writes" with no path would approve writing
anywhere, which is the one shape that would quietly undo the consent model. It is refused when
you create it, and ignored by the engine if one reaches it another way. An allow also has to say
what it allows — "allow anything, here" is not expressible.

**Only writing and deleting can be allowed by a rule.** Running a command, driving the keyboard,
capturing the screen and reaching the network cannot be scoped to a folder, so a rule permitting
them would be a blanket surrender of the capability. Those already have visible switches in
Permissions, and they stay per-action. Denies, by contrast, may cover any kind and may be blanket.

Every decision a rule makes is written to the action log with the rule quoted. An automatic
approval that leaves no trace is the bad version of this feature.

## A signed-in browser is the biggest capability here

`PathGuard` bounds what AutoWork can reach on disk. It has nothing to say about a browser that is
signed in as you: on those sites the agent is you, and no folder grant limits that.

So it is treated accordingly.

- **Its own switch**, off by default, separate from network access. Fetching a public page and
  acting as the logged-in user are not the same permission, and both must be on before the tools
  exist at all.
- **Its own profile**, not your real one. You sign in to it deliberately, once, and it is
  therefore signed in to only what you put there — rather than inheriting every session in your
  everyday browser.
- **Visible by default.** Something acting as you should be watchable.
- **The same outbound allow-list** as the web tools, matched on a label boundary, so a rule for
  `example.com` does not cover `example.com.evil.net`.
- **Every navigation and click asks.** Reading a page you are already on is `Safe`; opening one is
  `System` with a network prompt; clicking and typing are `System` with an input prompt, because
  that is what they are.

What this does **not** protect against: a page that convinces the model to click something
harmful. Prompt injection from web content is real, and the mitigation here is that the actions
are individually approved, not that the content is trusted. Treat a browser run the way you would
treat handing someone your laptop while you are logged in.

## Recordings

Transcription is off until configured, and runs a speech model **on your machine** by default.
The remote option uploads the recording and is a separate, deliberate choice with its own consent
prompt.

This is stated plainly because a meeting recording is unusual among the things AutoWork touches:
it contains other people, who did not agree to anything.

## MCP servers are outside the sandbox

An MCP server is a program AutoWork starts — usually `npx something` — and it runs with your full
user rights. `PathGuard` governs *AutoWork's* file tools; it cannot reach inside another process.
A filesystem MCP server can read whatever the operating system lets it read, whatever you granted
in Settings.

This is the same class of power as the shell tool, so it gets the same treatment:

- **A capability switch.** `Settings › Permissions › Allow MCP servers`, off by default. With it
  off, no server is started and no MCP tool is described to the model, even if servers are
  configured.
- **A per-server switch.** Adding a server from the gallery writes a command line into
  `config.json`. It does not start anything. Enabling is a second, deliberate act.
- **The command line is always shown**, so what will actually be launched is never a mystery.
- **Their tools are marked as writing**, not as safe — AutoWork cannot see what an external tool
  does, and claiming otherwise would be a guarantee it cannot make.

Keys an MCP server needs go to the encrypted secret store like any other, and `config.json` keeps
only the reference.

The built-in catalogue lists servers whose packages were checked against their registry and are
not deprecated. It is a starting point, not an endorsement: you are running someone else's code.

## Skills: instructions, and sometimes code

A skill installs as a folder: `SKILL.md` plus whatever shipped with it. For most skills that is
reference documents and templates. For some — `anthropics/skills/pdf` and `docx` among them — it
also includes Python scripts.

**The instructions are inert.** Markdown the model reads cannot grant a capability or call a tool
the permission policy forbids. The worst a badly written skill can do is give the model bad
advice, bounded by the same sandbox as everything else.

**The scripts are not.** Running one runs code from someone else's repository with your full
rights, and `PathGuard` cannot see inside another process — the same limitation as the shell tool
and MCP servers. So it is gated the same way:

- **`Settings › Permissions › Allow running scripts bundled with skills`**, off by default. With
  it off, `skill_run` is not described to the model at all.
- **Its own switch**, deliberately not the shell's. Agreeing to run your own commands is not the
  same as agreeing to run a stranger's script.
- **Approval before each run**, on by default, showing the interpreter, the resolved script path
  and every argument — the real command, not a summary of it.
- **Only known interpreters.** `.py`, `.js`, `.mjs`, `.sh`, `.ps1`. Anything else is refused by
  name rather than handed to a shell to work out.
- **Arguments are passed as a list**, never through a shell, so a filename with a space or a
  quote in it is an argument and not an injection. That matters because the arguments come from
  the model.

**Dependencies are installed, and that is its own risk.** Most published skills do not declare
what they need — two of the eighteen in `anthropics/skills` ship a `requirements.txt`, and the
rest name their libraries only in prose and in the scripts' import lines. So AutoWork reads both:

- **`requirements.txt` is installed as written.**
- **Imports are resolved through a fixed table** — `fitz` is PyMuPDF, `PIL` is Pillow, `cv2` is
  opencv-python. An import that is not in the table is **reported, never guessed at**. Inventing a
  plausible package name is exactly how typosquatting gets its foothold, so the table is the only
  source of names and a missing library is preferred to a wrong one.
- **The package list is shown in the consent card** before anything is fetched. That list is the
  part worth reading: `pip install` runs the package's own `setup.py`, so installing is itself
  code execution, and it happens *before* the script you approved.
- **Everything lands in a virtual environment inside the skill's folder.** Your own Python is
  never modified, and removing the skill removes its libraries with it.

Be clear about what this does not solve: a `requirements.txt` in a skill's repository is written
by whoever wrote the skill. Reviewing that list when the card appears is the control, and it is a
real one — but it is the only one. Some packages also need a program pip cannot install
(`pdf2image` needs poppler, `pytesseract` needs tesseract); those are named up front rather than
surfacing later as a confusing traceback.

**Everything stays inside the skill's folder.** Both `skill_file` and `skill_run` resolve the path
you give them and confirm it landed inside that skill before doing anything — a bundled file
named `../../../etc/passwd` is dropped at install time, and a script path that climbs out is
refused at call time.

Skills live in AutoWork's own data directory, which `PathGuard` protects, so the agent cannot
rewrite its own instructions or drop a new script there with the file tools. Installing and
removing stay deliberate acts in the Skills gallery.

The repository each skill came from is shown next to it, because whose code you are about to run
is the part worth knowing.

## The action log

Every action lands in an append-only JSON Lines file at `logs/actions.jsonl`, with:

```
timestamp · run id · subsystem · tool · one-line summary · outcome · paths · duration · error
```

It is append-only so a crash mid-write cannot corrupt earlier entries, and it rotates at 8 MB.
The Activity view streams it live while a run is in progress.

Outcomes include `Denied`, so refused attempts are recorded too. If the agent tried something it
was not allowed to do, you will see it.

## Secrets

API keys are stored separately from configuration and encrypted with **AES-GCM**. The encryption
key is held in a sibling file.

- **Windows** — the AES key is wrapped with **DPAPI** (current-user scope). The store cannot be
  read by another account or moved to another machine.
- **Linux and macOS** — there is no DPAPI equivalent used here. The key file is created with
  owner-only permissions (`0600`) before any bytes are written. Protection is therefore
  equivalent to an SSH private key: anyone who can read your home directory as you can read your
  keys.

That is a real difference and worth stating plainly rather than dressing up. If you need a
stronger guarantee, reference keys as `env:VARIABLE_NAME` and let a proper secret manager inject
them — AutoWork then never persists them at all.

`config.json` contains only references. It is safe to copy or version.

## What is *not* protected

Being straightforward about this is more useful than a longer list of features.

**Synthetic input cannot be sandboxed.** Once a keystroke or click is synthesised it goes to
whichever window has focus. No permission model inside this process can constrain that. The
capability is off by default and approval-gated; those are consent controls, not containment.

**Shell commands run as you.** The working directory is pinned inside a granted folder, and an
optional allow-list restricts which executables may run — but the command itself has your full
user privileges. If you enable shell access, you are trusting the model's judgement about
commands. Keep it off unless you need it, and use the allow-list when you do.

**Network egress is coarse.** The host allow-list applies to AutoWork's own web tools. A shell
command can reach the network regardless.

**Prompt injection is a live risk.** A file or web page the agent reads can contain text trying
to redirect it. The sandbox is the mitigation — instructions cannot grant new permissions, and
anything outside your granted folders stays out of reach whatever the model is persuaded to
attempt. Approval gates on destructive actions are the second line. Neither makes injection
impossible; both bound the damage.

**The model provider sees what you send.** File contents, screenshots and extracted data go to
whichever provider you configured. For work that must not leave the machine, use Ollama or
LM Studio and AutoWork operates entirely offline.

## Recommended setup

**Cautious** — the default. Grant one project folder as read/write. Leave delete, shell and
input control off.

**Everyday** — grant your working folders. Enable delete with soft delete on. Leave shell and
input off.

**Power user** — enable shell with an executable allow-list (`git`, `python`, `ffmpeg`). Keep
"ask before every command" on. Enable input control only for the session you need it.

**Air-gapped** — Ollama as the provider, network off, no integrations. Nothing leaves the
machine.

## Reporting a vulnerability

Please report security issues privately to Gravicode Studios rather than opening a public issue.
