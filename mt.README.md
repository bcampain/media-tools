# Upgraded `mt` CLI Design

**Executable:** `mt` (C#)

**Subcommand design**
  - `mt handbrake <target> [options]`
  - `mt normalize <target> [options]`
  - `mt promote <target> [options]`
  - `mt notify-discord <title> <message> [--log <path>]` (mostly passthrough, not a step)
  - `mt pipeline <target> [options]` ← **runs the whole chain in order**
  - (optional later) `mt plan ...` / `mt apply ...`

**Why this mapping works**
  - Preserves current, proven bash behavior.
  - C# will now perform the orchestration: input validation, scoping, logging, consistent UX, plan/dry-run, and calling notify-discord on events (though the majority of Discord notifications will still occur from the bash scrripts in v1).

## Command-line options

### Common options on every command:
  - `--incoming-root /incoming` (default /incoming; used by `handbrake` and `pipeline`)
  - `--staging-root /staging` (default /staging)
  - `--library-root /library` (default /library)
  - `--run-id <id>` (default: generated; passed through to scripts if they support it)
  - `--dry-run` (passes `--dry-run` to the underlying script in v1; file-level plan output is v2)
  - `--yes` (no confirmation)
  - `--log-dir <path>` (default `/logs`; in v1 this controls where the C#-level pipeline log is written — the bash scripts always write to `/logs/` and do not accept a `--log-dir` arg)
  - `--json` (structured output for future automation)
  - `--verbosity` quiet|normal|verbose

### Per-command options (passed through to the underlying script):

**`mt handbrake`:**
  - `--quality <RF>` (default 23)
  - `--preset <name>` (default fast)
  - `--max-depth <N>` (default 3; directory traversal depth)
  - `--force` (re-encode even if output already exists)

**`mt normalize`:**
  - `--target-i <LUFS>` (default -16)
  - `--true-peak <dBTP>` (default -1.5)
  - `--lra <LU>` (default 11)
  - `--stereo-track on|off` (default on; adds a downmixed stereo track alongside 5.1)
  - `--one-pass` (skip loudnorm measurement pass; faster but less accurate)
  - `--force` (re-normalize even if output already exists)

**`mt promote`:**
  - `--retention-days <N>` (default 30; prune archives older than N days)
  - `--overwrite` (overwrite existing library file if present; currently default behavior in script)

### Pipeline-only options:
  - `--stop-on-error` (default true)
  - `--notify` (default true; controls discord notifications)
  - `--step handbrake|normalize|promote` (start at a step)
  - `--until handbrake|normalize|promote` (stop after a step)

## Pipeline Safety Rails

### 1) Root scoping (no accidental “rm -rf /” accidents)
  - `mt handbrake` / `mt pipeline`: target must be under `--incoming-root`
  - `mt normalize` / `mt promote`: target must be under `--staging-root`
  - Refuse if the target equals the root itself (e.g. `/incoming` or `/staging` — must be a subfolder or file)
  - Enforce per-kind target shape:
    - `tv`: must be a directory exactly one level deep — `/incoming/tv/<Show Name>` (handbrake/pipeline) or `/staging/tv/<Show Name>` (normalize/promote); no deeper paths, no single files
    - `movies` / `clips`: accepts a directory at any depth under the appropriate root, **or** a single video file (`.mkv`/`.mp4`/`.m4v`) anywhere under that tree

### 2) Two-phase behavior (plan → apply)

**v1 behavior:** C# validates the target, derives script arguments, prints a summary of what it will invoke (script name + args), asks confirmation (unless `--yes`), then calls the scripts. File-level candidate discovery and plan building happen inside the scripts, not in C#. `--dry-run` simply passes `--dry-run` through to the underlying script.

**v2 behavior (future):** Each command will:
1. Discover candidates in C#: if `TargetMode=file` use the path directly; if `TargetMode=dir` traverse the target tree
2. Build a file-level plan:
  - which files will be converted (handbrake)
  - which files will be normalized (ffmpeg)
  - what will be moved to `/library`
  - what will be archived (originals + logs), based on promote rules
3. Print a full file-level summary + ask confirmation (unless `--yes`)
4. Execute with per-step logging

### 3) Idempotency, aligned with existing scripts

Existing scripts already handle re-run scenarios. The CLI adds guardrails:
  - If `handbrake_mp4` outputs exist, skip/reconcile (or provide `--force`)
  - If audio normalization marker exists (or normalized output exists), skip unless `--force`
  - Promote should be safe to re-run: if file already in /library, skip and log

### 4) First-class run tracking

RUN_ID is used in the existing scripts workflow. Make that the spine of the pipeline:
  - Every command gets a run id
  - Every log section includes it
  - Discord notifications include it
  - Promote archives under (per-kind):
```
  TV:            /staging/archive/tv/<Show Name>/_archived-<RunId>/
  Movies/Clips:  /staging/archive/<kind>/_archived-<RunId>/
```

## Architecture (modular + swappable, script-centered)

Keep the same layered architecture as the scripts, but rename concepts around the pipeline design:

Domain
  - PipelineRun { RunId, StartedAt, Target, TargetMode, Kind, StagingRoot, LibraryRoot }
    - TargetMode = `dir | file` (default to `dir` for `kind = tv`)
    - Kind = `tv | movies | clips`
  - PipelineStep = `Handbrake | NormalizeAudio | Promote`
  - Plan + Operation types (but operations map to script invocations in v1)

Application layer (handlers)
  - HandbrakeCommandHandler
  - NormalizeAudioCommandHandler
  - PromoteCommandHandler
  - PipelineCommandHandler (calls the three in order)
  - NotifyDiscordCommandHandler
    - Scaffolding for pipeline logging, future script conversion

Infrastructure
  - IProcessRunner (runs your scripts)
  - IFileSystem (for discovery + safety checks)
  - ILogSink (console + file; optional JSON)

Script adapter layer (key migration/conversion piece)

Wrap each bash script behind an interface, so later you can re-implement any step in pure C# without changing CLI behavior:
  - IHandbrakeRunner.Run(target, options, runContext)
  - INormalizeRunner.Run(target, options, runContext)
  - IPromoteRunner.Run(target, options, runContext)
  - IDiscordNotifier.Notify(title, message, logPath, runContext)

In v1, each adapter just does:
  - build argv
  - call /opt/media-tools/<scriptname> (or bash -lc if needed)
  - capture exit code + stdout/stderr

⸻

How the “pipeline” command should behave

```bash
RUN_ID=$(date +%m%d%y%H%M%S)

# TV show (directory, one level deep)
mt pipeline “/incoming/tv/My Favorite Show/” --run-id “$RUN_ID”

# Movies or clips (directory)
mt pipeline /incoming/movies/ --run-id “$RUN_ID”

# Movies or clips (single file)
mt pipeline /incoming/movies/Alien.mkv --run-id “$RUN_ID”
```

**Incoming → staging path mapping (orchestrator responsibility):**

The `pipeline` command receives an `/incoming/...` target. After handbrake runs, the orchestrator must derive the corresponding `/staging/...` path to pass to normalize and promote:

| Input target | Handbrake step receives | Normalize + Promote step receives |
|---|---|---|
| `/incoming/tv/<Show>` | same | `/staging/tv/<Show>` |
| `/incoming/movies[/subdir]` | same | `/staging/movies[/subdir]` |
| `/incoming/movies/Alien.mkv` | same | `/staging/movies/` (kind base dir, not the file) |
| `/incoming/clips/...` | same | same pattern as movies |

For single-file movie/clip targets, normalize and promote receive the staging **kind directory** (not a specific file path), because the orchestrator doesn't know the exact output filename (`{stem}.norm.mp4`) until handbrake has run.
	1.	handbrake_mp4

  - sends discord: “Starting Handbrake…”
  - runs script
  - sends discord on success/failure (include log tail)

	2.	normalize_audio

  - same pattern

	3.	promote

  - same pattern
  - current script design details:
    - moves processed outputs to `/library`
    - archives originals to `/staging/archive/<kind>/...`; logs stay in `/logs/`
    - cleanup differs by kind:
      - **TV:** `rm -rf` the entire incoming show folder, then `rm -rf` the entire staging show root
      - **Movies/Clips:** deletes per-file originals individually; removes only empty subdirectories; never touches the staging or incoming base directories

	4.	notify-discord

  - final summary message with run id, counts, and pointers to log locations

**Script exit code contract:**

| Code | Meaning | Scripts |
|------|---------|---------|
| 0 | Success | all |
| 2 | Validation failure (bad target path/shape) | all |
| 3 | Partial failure: some files failed | handbrake, normalize |
| 3 | Pre-check failed: `.norm.mp4` files still present | promote |
| 4 | Promote copy failure (rsync error) | promote |
| 5 | Archive failure (rsync error) | promote |

Note: rc=3 from handbrake/normalize means some files were processed but others failed. With `--stop-on-error` (default), this halts the pipeline. rc=4/5 from promote are always fatal.

Stop-on-error (default)

If any step returns a non-zero exit code:
  - pipeline stops
  - sends discord failure event (with log tail)
  - returns non-zero exit code

⸻

Logging model
  - All scripts write to a shared flat log directory (default `/logs/`), named:
    - TV: `/logs/<Show Name>-media-tools-<RunId>.log`
    - Movies/Clips: `/logs/<kind>-media-tools-<RunId>.log`
  - `normalize` and `promote` locate the prior step's log by searching `/logs/` for the newest matching prefix when no `--run-id` is given; passing `--run-id` is the reliable way to chain steps
  - A pipeline-level summary log (optional, not v1): `pipeline-<RunId>.log`
  - Includes log tails in discord failure messages

Discord notification strategy:
  - on step start
  - on step success
  - on step failure
  - final completion summary

⸻

Repo / solution layout (same shape, just renamed)

mt/
  src/
    MediaTools.Cli/
    MediaTools.App/
    MediaTools.Domain/
    MediaTools.Infrastructure/
    MediaTools.Scripts/          <-- adapters that call /opt/media-tools scripts
  test/
    MediaTools.Domain.Tests/
    MediaTools.App.Tests/


⸻

Milestones (aligned to current scripting world)

Milestone 1 — CLI skeleton + config
  - mt pipeline --help
  - defaults for /staging and /library
  - --run-id and logging directory creation

Milestone 2 — Safety checks + discovery
  - validate per-command root: `handbrake`/`pipeline` target under `/incoming`; `normalize`/`promote` target under `/staging`
  - validate per-kind target shape (TV = dir, 1-level deep; movies/clips = dir or single file)
  - refuse dangerous paths (target equals root, path traversal, etc.)
  - dry-run prints what it would invoke (script name + full arg list) without executing

Milestone 3 — Script adapters + pipeline execution
  - implement IProcessRunner
  - wire handbrake_mp4, normalize_audio, promote, notify-discord
  - exit codes + stderr capture
  - discord notifications at each stage

Milestone 4 — Polish
  - --step/--until
  - structured JSON output
  - better summaries (counts, durations)
  - optional concurrency later (likely only within handbrake/normalize file loops, if your scripts support it)

⸻

Make sure each script supports a non-interactive mode and predictable logging, because once C# is orchestrating:
  - the CLI should own prompting (--yes, confirmation)
  - scripts should just do work and return clear exit codes
