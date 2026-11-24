# Runtime probe control mailbox

The optional `example.panel` module probe can accept an explicit control request through its private plugin Data storage. The in-game consumer uses `IPluginStorageService` and `IGameplayControlService` on `FrameUpdate`. It does not act until a valid request arrives. Enable the existing probe before starting the game run by putting `{"Enabled":true}` in `<game>/BepInEx/RizToolbox/config/plugin-example.panel/config-runtime-module-proof.json`. This is the existing `IPluginConfiguration` path; it is separate from the mailbox Data directory.

## Send one request

Use PowerShell 7.2 or newer. Start the sender, then return focus to the game before `DelaySeconds` elapses. A terminal brought to the foreground on the same desktop can still trigger the game's focus pause.

```powershell
pwsh -NoProfile -File .\scripts\Send-ProbeControl.ps1 -GameDirectory 'C:\Games\Rizline' -Action pause -DelaySeconds 10
pwsh -NoProfile -File .\scripts\Send-ProbeControl.ps1 -GameDirectory 'C:\Games\Rizline' -Action set-speed -Value 0.75 -DelaySeconds 10
pwsh -NoProfile -File .\scripts\Send-ProbeControl.ps1 -GameDirectory 'C:\Games\Rizline' -Action seek -Value 30 -DelaySeconds 10
pwsh -NoProfile -File .\scripts\Send-ProbeControl.ps1 -GameDirectory 'C:\Games\Rizline' -Action seek -Value 96.5 -SeekUnit chart-coordinates -DelaySeconds 10
pwsh -NoProfile -File .\scripts\Send-ProbeControl.ps1 -GameDirectory 'C:\Games\Rizline' -Action resume -DelaySeconds 10
```

`-Value` is required for `set-speed` (a positive finite rate multiplier) and `seek` (a finite position). For `seek`, `-SeekUnit` defaults to `game-seconds` and also accepts `chart-coordinates`. `pause` and `resume` take neither parameter. `-DelaySeconds` is 0–600. `-WaitSeconds` is 1–600 and defaults to 180. After the delay, the sender waits up to `min(WaitSeconds, 60)` seconds for a fresh status, then up to `WaitSeconds` for the matching result. It sends one request and prints the consumer's result JSON unchanged.

## Mailbox location and contract

For `GameDirectory` `<game>`, the actual directory derived by `ToolboxHost` and `StoragePaths.PluginDirectory` is:

```text
<game>/BepInEx/RizToolbox/plugin-storage/storage/plugin-example.panel/data/runtime-module-proof/
```

The SDK Data logical names are `runtime-module-proof/control-status.json`, `runtime-module-proof/control-request.json`, and `runtime-module-proof/control-result.json`. The separate `sender.lock` file coordinates cooperating terminal senders. The sender refuses to overwrite a prior request unless `control-result.json` matches that prior request's session ID, nonce, and action.

All JSON property names are camel case. `control-status.json` has `schemaVersion: 1`, a fresh per-activation GUID `sessionId` in 32-character `N` form, `updatedUtc`, `active`, `commandsConsumed`, `maxCommands: 32`, and `state` (or null). A usable state contains a positive `controllerGeneration` and nonempty `observedRunId`, along with the control flags. The sender only binds to status no more than 15 seconds old, with up to five seconds of future clock tolerance. It does not switch to a later session or run after publishing its request.

`control-request.json` is at most 4096 UTF-8 bytes. The valueless shape has exactly seven properties:

```json
{"schemaVersion":1,"sessionId":"0123456789abcdef0123456789abcdef","nonce":"fedcba9876543210fedcba9876543210","expectedRunId":"observed-run-id","expectedGeneration":1,"action":"pause","expiresUtc":"2026-11-17T12:00:00.0000000+00:00"}
```

`action` is `pause`, `resume`, `set-speed`, or `seek`. For `set-speed`, the sender adds `value` with `unit: "rate"`. For `seek`, it sends `value` with the selected `unit` (`"game-seconds"` or `"chart-coordinates"`). It sets `expiresUtc` to 120 seconds after publication. The in-game consumer compares the request's run and generation with the acquired lease, then passes the value and unit to the SDK in an explicit `GameplayControlRequest`.

`control-result.json` carries `schemaVersion`, `sessionId`, `nonce`, `completedUtc`, `action`, `stage` (`validation`, `acquire`, `execute`, or `exception`), and `result`, the consumer's actual `Result<GameplayControlReceipt>` serialized with camel-case property names. Inspect `result.success`, `result.error`, and any receipt. A Pause or Resume `Dispatched` receipt means the invocation returned; it is not a confirmed gameplay or submission result. A SetSpeed or Seek `Confirmed` state refers to the prepared value and unit readback, not independent clock or result behavior.

The sender writes a same-directory temporary file and atomically moves it to the request name while holding `sender.lock`. The probe polls the fixed Data name because external file writes do not publish `PluginStorageChanged` events. If a result does not arrive, the sender reports **Outcome unknown** with the session ID and nonce and never resends. Inspect a later matching result before deciding on another command. A process exit after dispatch but before result publication can leave the outcome unknown; a new activation uses a new session ID so that an old request cannot replay.
