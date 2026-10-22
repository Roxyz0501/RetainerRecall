# Persistence validation — 2026-10-23

Scope: Retainer Listing Helper 1.0.0.2; the existing standalone plugin. No production data migration, game interaction, installed DLL replacement or shared specification/template edits were performed.

## Implementation

- UI changes apply immediately and mark a revision. One framework scheduler checks every 60 monotonic seconds, serializing only a dirty revision and comparing it with the last successful snapshot. Returning to the saved values writes nothing.
- Logout flushes pending settings. Framework unloading flushes, and Dispose detaches callbacks/stops listing and recall before flushing and releasing resources. Saves run synchronously behind one lock; there is no background task, timer, queue or child process. Small settings snapshots are the explicit synchronous-I/O tradeoff; game-frame latency has not been measured.
- `IDalamudPluginInterface.ConfigFile` retains the existing configuration path. UTF-8 JSON schema remains compatible, including previous `$type` metadata and absent pre-localization Language. JSON cannot instantiate arbitrary types. No production migration was executed.
- A flushed, validated temporary file atomically replaces the primary. `File.Replace` creates the previous valid generation under an immutable increasing name (`.backup.<20-digit sequence>.json`). Only after success are older valid generations removed, retaining three. Cleanup failure may retain extra backups rather than jeopardizing data. Corrupt backup candidates are retained, not counted as valid generations.
- Startup prefers a valid primary, then the newest valid backup. Recovered settings are announced and repaired at the next save boundary. A damaged primary is preserved as `.corrupt.<id>`. When nothing valid remains, loading fails without overwriting the evidence. Access failures also fail closed.
- Failed saves keep the pending revision, retry no faster than 60 seconds and stop after three consecutive failures. First and final failures notify and log the cause. Dispose may make the final bounded attempt without waiting for the interval. Fix permissions/free space and reload to resume; unsaved changes may be lost on reload. No infinite retry.
- Price history and transaction state remain session-only and separate from configuration. No native dispatch, packet, confirmation, quantity or transaction retry code changed. No persistent transaction journal, date-based history, all-date browser, migration or history-file UI exists in this plugin. Those acceptance items are not applicable; adding durable price history would contradict the user's session-only requirement.
- `StandardHistoryFilesTab.cs` was read but not copied: the shared specification explicitly excludes empty history tabs. Existing `StandardPluginTabs.cs` is unchanged and byte-identical to its canonical template (SHA-256 `062372ECBF0030427BCDFE471592C6691CE3A3D3F8835A7438EA95C7763B25E9`).

## Synthetic checks and measured logical writes

All persistence data used unique temporary directories; directories were removed after tests. The UI harness exercised the production Plugin with fake services, synthetic configuration and offscreen ImGui rendering.

| Scenario | Result |
|---|---|
| Ten unchanged 60-second periods | 0 saves, 0 logical bytes |
| 600 UI-equivalent setting changes within one period | 0 before boundary; 1 commit, 434 UTF-8 bytes at boundary |
| Change and restore previous value | 0 additional saves/bytes |
| Three injected pre-commit failures | 0 commits; 1,296 bytes written to temporary files; existing primary and generations unchanged |
| 20 simultaneous flush requests | 1 commit |
| Additional change during save / reentrant flush | No overlapping write; next flush preserves new revision |
| Three backup generations | Correct previous values, ordered newest to oldest |
| Corrupt primary and newest backup | Next valid generation restored; damaged primary preserved separately |
| Invalid temporary JSON / incomplete primary / no valid backup | Rejected without destructive replacement |
| Midnight and restart with missing primary | Monotonic scheduling maintained; backup recovery succeeds |
| Logout / normal exit / Dispose | Pending change flushed, duplicate flush does not write; callbacks detached |
| After Dispose, including a simulated next day | 0 additional writes; no asynchronous work exists |

These are application-level UTF-8 payload counts, not physical SSD writes or filesystem metadata traffic. Atomic moves do not rewrite all backup JSON payloads. Crash/forced kill can lose up to approximately 60 seconds of pending settings; shutdown callbacks and hardware power-loss durability cannot be guaranteed.

Validation commands: `dotnet run --project Tests/Persistence -c Release` (25 checks), `dotnet run --project Tests -c Release` (149 checks), `dotnet run --project Tests/Native -c Release` (16 checks), and the UI harness (29 checks). Release rebuild is also required before packaging. Initial NuGet vulnerability lookup warning cleared after network-enabled forced restore.

Not verified in a live game: actual logout/normal-exit event timing, rendering, UI interaction with other plugins, native listing/retrieval, crash fix acceptance, and the previously reported full-armoury retrieval stop. No claim of in-game safety or physical SSD performance is made from these isolated tests.
