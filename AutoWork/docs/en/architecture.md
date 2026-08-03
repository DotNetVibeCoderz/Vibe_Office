# Architecture

*AutoWork — Gravicode Studios, led by Kang Fadhil*

## The three subsystems

AutoWork is organised as Brain, Eyes and Hands. This is not framing for a README — it is how the
projects are split, how tools are tagged, and how the interface reports what is happening. Every
step and every log line is attributed to one of them, which is what lets the organ rail in the
header answer "what is it doing right now" honestly.

```
                        ┌─────────────────────────────┐
                        │      AutoWork.Desktop       │
                        │   Avalonia UI · Work Tape   │
                        └──────────────┬──────────────┘
                                       │ RunEvent stream
                        ┌──────────────▼──────────────┐
                        │      AutoWork.Agents        │   ← BRAIN
                        │  planner · orchestrator     │
                        │  sub-agents · compaction    │
                        │  verification · vision      │
                        └───┬───────────────────┬─────┘
                            │                   │
          ┌─────────────────▼──────┐   ┌────────▼─────────────────┐
          │   AutoWork.Providers   │   │      AutoWork.Tools      │  ← HANDS + EYES
          │  OpenAI-compatible     │   │  files · shell · docs    │
          │  Anthropic · Ollama    │   │  data · images · screen  │
          │  embeddings            │   │  input · web             │
          └────────────┬───────────┘   └────────┬─────────────────┘
                       │                        │
                       │      ┌─────────────────▼──────────┐
                       │      │  AutoWork.Integrations     │
                       │      │  GitHub · Google · Notion  │
                       │      │  Asana · PayPal            │
                       │      └─────────────────┬──────────┘
                       │                        │
                    ┌──▼────────────────────────▼──┐
                    │        AutoWork.Core         │
                    │  config · secrets · PathGuard │
                    │  action log · knowledge      │
                    └──────────────────────────────┘
```

Core depends on nothing but the framework. Everything depends on Core. The UI depends on all of
it and is depended on by none of it.

## How a run works

```
  goal
   │
   ├─ 1. Build the tool set          policy decides what exists; forbidden tools are
   │                                 never described to the model at all
   │
   ├─ 2. Recall knowledge            search auto-attach knowledge bases for context
   │
   ├─ 3. Plan                        planner model returns JSON: ordered steps, each with
   │                                 an organ, dependencies and success criteria
   │
   ├─ 4. Group into waves            steps whose dependencies are satisfied and that were
   │                                 explicitly marked independent may run together
   │
   ├─ 5. For each wave
   │      ├─ pause gate              hold here if the user asked to pause; put any
   │      │                          correction to the model before the step runs
   │      ├─ compact if needed       summarise older turns before the step, not after,
   │      │                          so the step has room to work
   │      ├─ run the step            executor model + auto-invoked tools
   │      └─ record the outcome      a step fails only if every tool call in it failed
   │
   ├─ 6. Verify                      check the transcript against the success criteria
   │
   ├─ 7. Report                      summary, elapsed, steps, compaction stats
   │
   └─ 8. Save the transcript         written whatever the outcome, including cancellation
```

Progress is emitted as immutable `RunEvent` records. Core defines them and holds no UI types;
the desktop layer turns them into rows on the Work Tape.

## Pause and steer

A user who sees the agent going the wrong way should be able to say so, not cancel and re-type
the whole job. `RunController` is the handle the UI holds on a run in flight: `Pause`, `Resume`,
`Steer` and `Abandon`.

Pausing takes effect **at the next step boundary**, never inside a step. Interrupting mid-step
would mean abandoning a tool call already sent to the provider, or leaving a half-finished write
behind — and neither leaves a conversation anything could resume from. Waiting for the boundary
costs a few seconds. It is the same reason compaction only happens there.

A correction is inserted as a user turn **before** the step it is meant to change, phrased so the
model treats it as the user changing their mind rather than as one more requirement stacked on
the original wording. Steering without pausing first is allowed: the correction is queued and
applied at the next boundary either way.

Stopping a run has to release a paused one as well, or the agent thread parks forever waiting for
a resume that is never coming — the same failure mode as an unanswered consent prompt.

## Run history

Every run is persisted under `runs/` as two files:

- `<id>.json` — the headline: goal, model, status, timings, step and tool counts.
- `<id>.events.json` — the full `RunEvent` stream.

Split because the History list would otherwise pay for every transcript it is not showing, and a
transcript carries every tool result the run ever saw. Individual results are capped at ~4,000
characters before storage.

The events are written polymorphically, with a short fixed discriminator per type. A saved
transcript outlives the build that wrote it, so a renamed class must not make yesterday's history
unreadable — and a test fails if any `RunEvent` subtype is added without one.

Opening a past run replays its stored events through the **same handler a live run uses**, rather
than through a second read-only renderer. One renderer means a past run looks exactly as it did
while it was happening, and there is no second place for the two to drift apart.

History is a convenience, never a precondition: a transcript that cannot be written is logged and
the run's result stands.

## The token meter

`UsageTrackingChatClient` wraps each client for the duration of a run and reports what every
request used into a `RunMeter`. It sits **outside** the function-invocation loop on purpose: one
agent step can issue several provider round trips as tools are called and their results fed back,
and every one of those is billed. Counting only the outermost call would report a fraction of a
tool-heavy run.

Cost is summed per model rather than from the run totals — a run that plans with one model and
executes with another has two prices, and one blended rate would be wrong for both.

Three things it will not do:

- **Invent a price.** There is no built-in price table. Both prices must be set on the model or
  no cost is reported. Half a price is not enough.
- **Round away a gap.** A provider that answers without reporting usage increments a separate
  counter, and the total is then presented as "at least *n*".
- **Estimate.** `TokenEstimator` exists for compaction, where over-estimating is the safe error.
  The meter reports what the provider said, or says it does not know.

## Approval rules

`ApprovalRuleEngine` is consulted by `ApprovalCoordinator` before anything reaches the user or the
per-run memory. Its constraints are described in
[security.md](security.md#standing-rules); the one that matters architecturally is that rule
evaluation happens at the *approval* layer, while `PathGuard` runs inside the tool body afterwards.

That ordering is the whole safety argument. A rule can only ever suppress a question about
something the sandbox was going to allow anyway — it is not in the path that decides what is
reachable, and cannot be made to be.

## Streaming

The step loop streams by default and accumulates the updates back into one response, because the
loop still wants a whole reply and a set of messages to append. Fragments go out as
`AssistantDeltaEvent`, each carrying the **running total for that step** rather than just the new
piece — so a view that misses an update, or starts watching late, still shows the right thing.

Partial tool calls are not this layer's problem. The function-invocation client sits underneath
and reassembles a call that arrives in fragments, then continues the stream with its result. What
the orchestrator adds is the accumulation and the events.

If streaming fails **before any content arrives**, the same request is made again without it —
some gateways advertise streaming and then reject it, and a run dying for that reason would be a
worse outcome than one that quietly waits. A failure *after* content has arrived is a real
failure and is left to the step's error handling: retrying then could repeat a tool call that
already ran.

## Saved jobs

`JobScheduler` watches the clock and the folders and asks for a job to be run. It does not know
what running one means — that is a delegate — so it is tested against a fake clock and a real
folder with no agent, provider or network in sight.

Three rules shape it:

- **One at a time.** A run holds the Work view and the approval broker. A job that comes due while
  another is running is skipped, not queued: "summarise yesterday" run twice in quick succession
  is worse than run once.
- **Missed time does not accumulate.** Due times are computed from the clock, so an app closed
  over a weekend wakes owing nothing.
- **A folder trigger can only watch what the sandbox grants.** Otherwise saving a job would be a
  way to make AutoWork read a folder it was never given.

Folder triggers wait for a quiet period before firing. Copying fifty files raises fifty events;
without it the job starts on the first one and reads a half-copied folder.

## Meetings

Transcription is off until configured and local unless the user chooses otherwise. AutoWork ships
no speech model — a few hundred megabytes of weights is not something to install behind someone's
back — so `LocalTranscriber` runs whichever one is already installed, as a command, and reads
back either its stdout or the `.txt`/`.srt` it wrote beside the audio.

Only transcription is a tool. Pulling out decisions and owners is reasoning, which the agent
already does better than a fixed prompt buried in a tool would, and writing the result is
`doc_create_word` or `knowledge_save`, which already exist.

## The browser session

`BrowserSession` drives a browser that is **already installed**, over the DevTools protocol,
against a profile of its own that persists. That combination is the point: `web_fetch` sees the
page a stranger sees, and the work worth automating is behind a login.

Three decisions:

- **No bundled browser.** Edge or Chrome is already there; downloading another one to automate is
  not a reasonable ask.
- **Its own profile, not the user's.** Attaching to the browser they have open fights for the
  profile lock. A separate directory still remembers logins between runs, which is what was
  wanted.
- **Visible unless asked otherwise.** Something acting as you should be watchable.

The connection is to a **page** target from `/json/list`, not the browser target from
`/json/version`. The latter is the obvious one to reach for and the wrong one: it speaks `Target`
and `Browser` but not `Page` or `Runtime`, so navigation and evaluation succeed and do nothing.

## Deleted files

Soft delete moves a file into `recycle/` under `AppPaths.Root`, which `PathGuard` refuses
unconditionally, so the agent cannot read back something it deleted. Alongside it,
`RecycleBin` keeps an append-only JSONL index of where each item came from — the piece that makes
"recoverable" true rather than nominal.

Appends close a torn trailing line first. Without that, a process killed mid-write costs not only
its own record but the *next* one, which concatenates onto the fragment.

Restoring is a user action on the user's own data, so it is not bound by the agent's folder
grants — a file deleted from a folder since un-granted must still be recoverable. The one hard
rule is that nothing may be written into AutoWork's own directory, so a doctored index line
cannot become a way to drop a file on top of `config.json`.

## Why step-shaped rather than one long conversation

A single long chat turn would be simpler. Steps buy three things worth the complexity:

- **A boundary for failure.** One bad step is contained and fed back to the model rather than
  ending the run.
- **Something to watch.** A user can see progress, which is what makes leaving a long job alone
  tolerable.
- **Something concrete to verify.** The verification pass checks declared criteria instead of
  re-deriving intent from a transcript.

## Auto-compaction

When a run approaches the model's context window, older turns are replaced by a summary.

The part that needs care is the **cut point**. Tool calls and their results are paired, and every
provider rejects a conversation where a result has no matching call. So the boundary is walked
backwards until it lands on a clean turn edge before anything is dropped. Leading system messages
and the original goal are always kept — they are the run's charter.

If the summarisation call itself fails, a mechanical summary (tools used, last note) is
substituted rather than losing the run.

`ContextCompactionTests` checks the boundary invariant at **every possible cut point**, because
this is the kind of bug that only appears on long runs, in production, at the worst moment.

## Token estimation

AutoWork talks to a dozen providers, and exact counts need each one's own tokenizer. The
estimator therefore deliberately **over-estimates** — 3.5 characters per token, plus per-message
overhead, plus tool schemas (which ride on every request), plus a flat ~1,200 tokens per image.

Compacting slightly early costs one summarisation call. Compacting late costs the whole run.

## Sub-agents

Independent steps run concurrently, each with its own conversation, so their token use does not
compound and the parent sees only their final reports.

The scheduler is conservative on purpose: only steps the planner **explicitly** marked as
depending on something are considered parallel-safe. A planner that simply forgot to fill in
`dependsOn` must not cause two agents to rename files in the same folder simultaneously.

## Providers

One code path — the OpenAI wire format — covers OpenAI, Azure OpenAI, Gemini, DeepSeek, Qwen,
Moonshot, OpenRouter, LM Studio and any gateway a user points at. Two get their own clients:

- **Anthropic**, because its Messages API differs materially and its OpenAI-compatible shim is
  explicitly a migration aid. The Brain is the part that most needs full tool-use and vision
  fidelity, so `AnthropicChatClient` speaks `/v1/messages` directly.
- **Ollama**, via OllamaSharp.

Clients are cached per profile, keyed on the resolved credential, so rotating a key invalidates
the cache. Function invocation is wrapped with an iteration cap so a model that keeps calling
tools without converging cannot burn a quota inside one step.

## Tools

A tool is an `AIFunction` plus metadata Microsoft.Extensions.AI does not carry: which organ owns
it, how dangerous it is, its category, and what to ask before running it.

Tools return **strings, not exceptions**. A refusal the model can read — `REFUSED: ~/Documents is
read-only` — lets it pick another approach; an exception just ends the turn. `ToolSetBase`
converts the exceptions tools realistically hit into that vocabulary and records every outcome.

Each call is wrapped in an `ObservableAIFunction` so the Work Tape can show it as it happens,
not only after the turn completes.

![Tool calls on the Work Tape, each attributed to the organ that owns it](../images/work-tools.png)

### Web search

`web_search` runs a chain of backends and stops at the first that answers: **Tavily** if a key is
configured, then **DuckDuckGo**, then **Wikipedia**.

The chain exists so that search is not a paid feature. A user who has signed up for nothing still
gets a working tool rather than one that refuses — the same rule that gives knowledge search a
keyword fallback when there is no embedding model. Tavily leads because it returns extracted page
content and a synthesised answer, which saves the agent fetching and stripping five pages before
it can reason. Wikipedia sits last because it is narrow but almost never unreachable, so the chain
does not end in silence on a network that blocks search engines.

A backend that returns nothing is not an error, it is a reason to ask the next one. The outbound
host allow-list is checked per backend, so a fallback can never reach a host the user has not
permitted.

![The Activity view, streaming the action log while a run is in progress](../images/activity.png)

### Skills

A skill is a folder: a manifest, plus the references, templates and scripts that shipped with it.
Installed skills contribute a line each in the system prompt (name plus when-to-use), and three
tools — `skill_open` for the instructions, `skill_file` for a bundled resource, and `skill_run`
for a script when the permission allows it.

That split is the whole design. Pasting a dozen skills into every request would cost tens of
thousands of tokens to make one of them relevant; naming them costs a few hundred and lets the
model choose. It is the same trade as searching knowledge bases instead of attaching them.

Both file-taking tools resolve their path and confirm it landed inside the skill's own folder —
the same check that runs when a bundle is written, because a path from a repository and a path
from a model deserve equal suspicion.

![The Skills gallery](../images/skills-gallery.png)

### MCP

MCP tools arrive from the C# SDK as `AIFunction`s already, so the integration is thin: connect,
list, wrap each in a `ToolDescriptor` so the Work Tape can attribute it like any other tool.

The awkward part is timing. `ToolRegistry.Build` is synchronous, but starting a stdio server takes
seconds. So the orchestrator exposes a `PrepareToolsAsync` hook that runs once before the tool set
is assembled, and clients are kept for the life of the app rather than per run.

Their tools are marked `ToolRisk.Write`, never `Safe`. AutoWork cannot see what an external tool
does, and a risk label is a claim about behaviour.

![The MCP gallery](../images/mcp-gallery.png)

## Knowledge bases

Plain JSON files, one per base, vectors inline, searched by cosine similarity over an in-memory
list.

A desktop knowledge base holds thousands of entries, not millions. A linear SIMD scan beats
carrying a vector database as a dependency, and it keeps the user's memory in a file they can
read, back up and delete. With no embedding model configured, search degrades to token overlap
rather than failing.

![The Knowledge view](../images/knowledge.png)

## The interface

**Design system.** The palette is derived from the product's own anatomy: Brain, Eyes and Hands
each own a hue, and every step, tool row and log line is tinted by whichever is responsible.
Colour carries information rather than decoration. Everything else is a disciplined neutral.

Organ tinting is applied through **styling classes bound to the row's organ**, not a value
converter, so switching theme repaints live instead of leaving stale brushes behind.

**The Work Tape** is the signature element: a continuous hairline spine with numbered stamps and
nested tool rows. Numbering is justified here because agent steps genuinely are a sequence and
the order carries information the reader needs.

**The organ rail** in the header is three segments that light in their own hue as each faculty
takes over — the honest answer to whether AutoWork is currently thinking, looking at your screen,
or touching your files.

**Composition** is hand-wired in `AppServices` rather than through a DI container. The graph is a
dozen objects that never change shape, and a container's reflection cost lands directly on
time-to-window.

## Project reference

| Project | Depends on | Holds |
|---|---|---|
| `AutoWork.Core` | — | Config, presets, secrets, `PathGuard`, approvals, action log, knowledge, agent contracts |
| `AutoWork.Providers` | Core | Chat client factory, Anthropic client, embeddings, connection testing |
| `AutoWork.Tools` | Core | Files, shell, documents, data, images, screen capture, input, web |
| `AutoWork.Agents` | Core, Providers, Tools | Planner, orchestrator, sub-agents, compaction, vision, knowledge tools, tool registry |
| `AutoWork.Integrations` | Core | Connector framework and connectors |
| `AutoWork.Desktop` | all | Avalonia UI, view models, theme, localisation |
