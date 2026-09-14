# Shell performance

Run from the repository root on Windows with the .NET 10 SDK:

```powershell
dotnet run --project tools/Azunote.Performance/Azunote.Performance.csproj -c Release -p:PublishAot=false -p:PublishTrimmed=false
```

Both properties are required because the application project normally enables AOT and
trimming. The harness is a managed console process and does not open an editor window.
It creates and removes its own temporary workspace. It never launches an external tool.

The CSV contains 30 samples per case after three warm-up iterations, using CRLF text of
10,000, 1,000,000 and 10,000,000 characters, 0/10/100 tools, and a 128-character selection.
Each tool resolves the harness executable. The synthetic workspace contains `.git` and
`.env`. Cold means a new application cache, not a cold filesystem or process. The warm
case uses a one-minute expiry to keep expiry out of the measured loop, with watchers
disabled in this original comparison. Production expiry is described below. JIT tiering
remains enabled. These are small component measurements, not an SLA.

`two_menus_before` reconstructs the old two-menu call pattern using the current uncached
evaluator: one new context and one evaluation for every tool in each menu. It does not run
an old binary and excludes XAML reconstruction, accelerator registration, the native
editor, IME, and screen presentation. `one_batch_*` evaluates once per tool using prepared
settings, a shared document context, and the new cache. `saved_compare_before` reproduces
the previous two-string normalization algorithm; `saved_compare_after` calls DocumentSession.
Snapshot text and line indexes are warmed before reported tool-evaluation samples.

## Recorded sample

Windows x64, Ryzen 9 5950X, SDK 10.0.401, managed .NET 10.0.12; 2026-09-14.
See [sample.csv](sample.csv) for the complete run. Times are milliseconds.

| Workload | Previous call pattern p50 / p95 | New cold p50 / p95 | New warm p50 / p95 |
| --- | --- | --- | --- |
| 1M characters, 10 tools | 4.020 / 4.411 | 0.292 / 0.376 | 0.068 / 0.086 |
| 1M characters, 100 tools | 35.296 / 39.906 | 0.810 / 1.000 | 0.637 / 0.831 |
| 10M characters, 100 tools | 34.145 / 36.174 | 0.753 / 0.947 | 0.519 / 0.622 |

The 10M-character saved-content comparison changed from 23.511 / 29.650 ms (p50 / p95)
and 76,363,840 allocated bytes to 6.591 / 7.670 ms and zero allocated bytes per comparison.
The document-wide comparison is still O(n). This does not include materializing a newly
edited snapshot, and it does not establish whole-editor latency for large documents.

## Watched discovery comparison

```powershell
dotnet run --project tools/Azunote.Performance/Azunote.Performance.csproj -c Release -p:PublishAot=false -p:PublishTrimmed=false -- --watch-cache
```

This mode compares one-second TTL-only caching with five-minute watched discovery on a
local temporary workspace, using 1M characters and 100 tools. Both paths are warmed 100
times before the usual three warm-ups and 30 samples. A synthetic cache clock advances
two seconds per batch; there is no real two-second wait. Native watchers are active, but
no files change during measurement. Initial watcher setup is outside the measured loop,
and allocations count only the calling thread, not native buffers or callback threads.

On the same machine/date as above, TTL-only p50 / p95 was 1.035 / 1.222 ms; watched
discovery was 0.841 / 1.090 ms. That is about 0.19 ms (19%) less per batch at the median,
not a measured change in typing latency. See [watch-sample.csv](watch-sample.csv).
This small local-workspace comparison does not measure deep searches, network filesystems,
watcher setup/teardown, or high-rate filesystem notifications.

## Implementation and limits

- `LatestUiWork` uses a capacity-one `System.Threading.Channels` queue, one consumer,
  a 50ms throttle, and a generation check before and during UI application. It waits for
  each dispatcher callback, so a stalled UI cannot accumulate callbacks. New input makes
  old results ineligible; a batch checks for supersession between tools. An individual
  synchronous filesystem call cannot be forcibly cancelled.
- Only immutable snapshot references, positions and session state are captured on the UI
  thread. Text materialization, workspace discovery and availability evaluation happen
  on the worker. Empty tool sets do not materialize a document context.
- Settings are prepared on reload and copied for worker evaluation. Menu and context-menu
  views share the resulting states. Enabled-state changes reuse controls; visibility or
  definition-tree changes rebuild the external-tool section. Shortcuts stay registered
  and re-evaluate their candidates when invoked, preserving candidate order.
- A window-owned `Microsoft.Extensions.Caching.Memory` cache holds at most 512 entries,
  including negative lookups. It stores process environment, nearest `.env` results,
  workspace discoveries and launch resolution. Fully watched `.env` and default workspace
  searches (`.git` / root `.editorconfig`) have a five-minute safety expiry. Process
  environment, launch resolution and arbitrary workspace globs retain a one-second expiry.
  Keys include discovery inputs and, for launch resolution, PATH/PATHEXT and cwd. Commands
  longer than 4096 characters bypass caching. The bound is entry count, not bytes.
- A process-wide registry shares at most 128 non-recursive `FileSystemWatcher` instances
  across windows and cache entries. Subscriptions cover searched directories, including
  negative lookups, and their parent directories to detect replacement/rename. Only
  relevant names invalidate an entry. `CancellationChangeToken` connects notifications
  to `MemoryCache` expiration tokens and requests a coalesced background menu evaluation,
  including while the user is idle. Eviction/window closure releases subscriptions.
- Remote/unavailable locations, watcher limits/errors and transient read failures fall
  back to one-second expiry. Failed watchers are retired and creation retries have a
  two-second backoff. Watcher errors invalidate affected entries; the safety TTL covers
  missed notifications. TTLs are checked on access, not by background refresh timers:
  missed events and unmonitored changes can remain invisible until an evaluation after
  expiry. Menu entry, window activation, settings reload, editing and selection request
  evaluation. Commands always revalidate using uncached environment/filesystem resolution,
  and removed/replaced definitions cannot execute through an old menu callback.
- Closing the window cancels pending work; the cache is disposed after the worker exits.
  Environment values and document text are never emitted by the measurements.
- Saved text is normalized on load/save; comparison reads the current text without
  creating normalized copies. Undo/manual restoration and mixed-newline semantics remain
  unchanged. The existing provider scheduler's cancellation/generation handling remains
  in place; it was not replaced as part of this shell optimization.

## Live measurements

`ShellPerformance` publishes `Azunote.Shell` using `System.Diagnostics.Metrics`:

| Instrument | Meaning |
| --- | --- |
| `azunote.shell.operations` | Count, tagged by operation |
| `azunote.shell.duration` | Elapsed milliseconds, tagged by operation |

Operations include `text.changed`, `selection.changed`, `tools.batch`, `tools.evaluate`,
`tools.render`, `tools.rebuild`, `environment.read`, `dotenv.search`, `command.search`,
`workspace.search` and `document.compare`. Search counts measure resolver calls, not each
individual filesystem syscall. No timings are taken when the duration instrument has
no listener. Runtime metrics can separately report allocation and GC activity.

For a managed editor process (replace 1234 with its PID):

```powershell
dnx dotnet-counters monitor --process-id 1234 --counters Azunote.Shell,System.Runtime
```

See the [Microsoft dotnet-counters documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters).
Compare typing, selection dragging, caret movement, initial menu opening and settings
reload with 0/10/100 tools. Counts should show bounded batches and no repeated menu
reconstruction when visibility is unchanged. Full input-to-present latency, real IME
composition and slow/network filesystem behavior need a separate interactive trace.

## Verification

`dotnet test Azunyan.slnx` covers saved-content semantics, fake-time throttling, superseded
work before/during UI application, shutdown without a dispatcher pump, and cache expiry
for creation/edit/deletion and nearest-parent changes. Watcher tests additionally cover
sharing/refcounts, rename, negative lookups, invalidation during cache fill, limits/errors,
missed-event expiry, locked-file retry and native notifications. UI tests are opt-in; see
[the UI test instructions](../../tests/Azunote.UiTests/README.md).

Validation after watched-cache changes: 390 non-UI tests passed; both focused external-tool
UI tests passed on the managed build, and all 16 UI tests passed on the Native AOT build.
Release managed build and Release/win-x64 self-contained AOT publish succeeded.
Actual IME keyboard composition was not verified: desktop automation could not focus
the test window. The selection test uses UI Automation selection without synthetic
keyboard input; the watched-menu test updates `.env` without any editor interaction.
