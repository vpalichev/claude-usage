# claude-usage

A Windows tray monitor that gets your [claude.ai](https://claude.ai) usage page on a schedule and shows quota bars in a dark panel. Useful on any Claude plan that exposes a `settings/usage` page — see where you are in the 5-hour session window and your weekly quotas at a glance, without alt-tabbing to the browser.

> _Screenshot placeholder — drop a `docs/screenshot.png` in the repo and link it here._

## What it does

- Launches a headless-ish minimized Chromium window every ~30 min (exponentially random interval, floor 10 min).
- A bundled MV3 extension waits for the usage page to finish rendering, saves the HTML, closes its own window.
- An F# parser extracts the four progress bars (`Current session`, `All models`, `Sonnet only`, `Claude Design`) and appends them to a daily CSV log.
- The tray panel renders them as Unicode block bars: `█` used / `▒` projected by next refresh / `░` untouched / `▓` end-boundary warn cell at ≥99%.
- Triangles tick across each bar on wall-clock time so you see *where in the window you are* independent of when the data was scraped.
- A pessimistic burn-rate projection overlays the session bar with where you're likely to end up by the next refresh, based on the maximum percent-per-minute rate observed in the current 5-hour window.

## Requirements

- **Windows 10 / 11** (WinForms + per-monitor V2 DPI; not cross-platform).
- **.NET 9 SDK** to build (`dotnet --version` ≥ 9.0.0). Runtime-only install also works if you use `--self-contained true` when publishing.
- **[Chromium](https://www.chromium.org/getting-involved/download-chromium/)** installed in one of the standard locations, or point `CLAUDE_USAGE_CHROMIUM` at the exe. Regular Chrome or Edge is not currently supported — the extension is loaded via `--load-extension`, which the stable channels will eventually drop.
- **Claude subscription** (Pro / Max / Team) — the settings/usage page is only visible when logged in.

## Build

```bat
git clone https://github.com/<you>/claude-usage
cd claude-usage\harness
dotnet publish -c Release -r win-x64 --self-contained false -o ..\dist
```

That produces `dist\claude-usage.exe` (~156 KB apphost) plus its `.dll`, `FSharp.Core.dll`, `AngleSharp.dll`, and a `dist\extension\` folder that Chromium will load.

## First-run login

The tray app drives Chromium in an isolated profile at `dist\profile\`. Before it can scrape, you need to log into claude.ai *inside that profile*. Double-click `login.cmd` in the repo root:

```bat
login.cmd
```

This runs `claude-usage.exe --login`, which opens Chromium against `https://claude.ai/login` with the extension disabled so the window stays open. Log in, make sure you can reach `https://claude.ai/settings/usage`, close the window.

## Usage

Double-click `dist\claude-usage.exe`, or if you want it to start on logon register it as a scheduled task:

```powershell
$action  = New-ScheduledTaskAction -Execute (Resolve-Path .\dist\claude-usage.exe).Path
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$princ   = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName "ClaudeUsageTray" -Action $action -Trigger $trigger -Principal $princ -Force
```

### Keyboard shortcuts

| Key   | Action                                           |
| ----- | ------------------------------------------------ |
| `R`   | Reload (immediate scrape)                        |
| `W`   | Open `claude.ai/settings/usage` in your browser  |
| `5`   | Reschedule next refresh to +5 minutes            |
| `T`   | Toggle always-on-top                             |
| `I`   | Toggle info view (raw scraped fields)            |
| `Esc` | Hide the panel (tray icon stays)                 |
| `Q`   | Quit                                             |

### Tray menu

Right-click the tray icon for **Show**, **Refresh**, **Always on top** (with check state), **Exit**.

## Configuration

- `CLAUDE_USAGE_CHROMIUM` — override Chromium discovery with an explicit path.
- All other paths (`profile/`, `extension/`, `logs/`) are resolved relative to the exe, so you can move the `dist/` folder anywhere.

## Architecture

Sandwich model: pure input validation → thin effectful middle → pure output.

```
┌──────────────────────────────────────────────────┐
│   Chromium (headful, minimized off-screen)       │
│   └── page-grabber extension (MV3)               │
│       ├─ polls DOM for sentinel string           │
│       ├─ dumps outerHTML to Downloads/           │
│       └─ POSTs progress events to localhost:N    │
└──────────────────────────────────────────────────┘
                       ↓ HTML file
┌──────────────────────────────────────────────────┐
│   claude-usage.exe (F# WinForms tray)            │
│   ├─ Scrape.fs   orchestrator                    │
│   ├─ Chromium.fs process lifecycle               │
│   ├─ Progress.fs ephemeral-port HttpListener     │
│   ├─ Parser.fs   AngleSharp CSS selectors        │
│   ├─ Panel.fs    pure render: Snapshot → Lines   │
│   ├─ Tray.fs     WinForms UI, timers, state      │
│   └─ Csv.fs, Io.fs, Domain.fs, Progress.fs       │
└──────────────────────────────────────────────────┘
```

The effectful shell is `Scrape`, `Tray`, `Chromium`, `Progress`, `Io`. Everything else is pure. `Panel.fs` in particular takes a `Snapshot` and returns a `Line list`; `Tray` paints those lines on a custom `PanelView` control.

## Development notes

See [LESSONS_LEARNED.md](LESSONS_LEARNED.md) for F# / WinForms gotchas accumulated building this, including:

- Thread safety of module-level mutables
- `try...finally` vs error handling
- Why `Task.Run` swallows exceptions
- DPI: measure from the screen DC, not a Bitmap
- Mutate state before re-rendering, not after
- F# string escapes are stored as literal characters in source files

## License

MIT — see [LICENSE](LICENSE).
