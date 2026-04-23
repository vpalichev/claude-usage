module Harness.Scrape

open System
open System.IO
open Harness.Domain
open Harness.Parser

type Config = {
    ChromiumExe: string
    ProfileDir: string
    ExtensionDir: string
    TargetUrl: string
    SnapshotDir: string
    LogDir: string
    TimeoutMs: int
}

/// Locate a Chromium install. Checked in order: a user override via the
/// CLAUDE_USAGE_CHROMIUM env var, then the standard per-machine install
/// paths, then per-user. Returns the first existing file; if none exist,
/// returns the most likely expected path so the run-time error message
/// points at a canonical location ("not found at ...").
let findChromium () : string =
    let env = Environment.GetEnvironmentVariable "CLAUDE_USAGE_CHROMIUM"
    let lad = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
    let candidates = [
        if not (String.IsNullOrWhiteSpace env) then yield env
        yield @"C:\Program Files\Chromium\Application\chrome.exe"
        yield @"C:\Program Files (x86)\Chromium\Application\chrome.exe"
        yield Path.Combine(lad, "Chromium", "Application", "chrome.exe")
    ]
    candidates
    |> List.tryFind File.Exists
    |> Option.defaultValue (List.head candidates)

let defaults : Config =
    // All runtime paths resolve relative to the exe directory so the bundle
    // (exe + extension + profile + logs) stays portable; no hard-coded repo
    // path. Only the snapshot handoff lives in the user's Downloads folder
    // because the Chromium extension writes via chrome.downloads.download.
    let baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/')
    let downloads =
        Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, "Downloads")
    {
        ChromiumExe = findChromium ()
        ProfileDir  = Path.Combine(baseDir, "profile")
        ExtensionDir = Path.Combine(baseDir, "extension")
        TargetUrl   = "https://claude.ai/settings/usage"
        SnapshotDir = Path.Combine(downloads, "page-grabber", "claude.ai")
        LogDir      = Path.Combine(baseDir, "logs")
        TimeoutMs   = 60_000
    }

/// Outcome of a single scrape run. The tray shows the Snapshot on Ok, an
/// error message on Failed.
type Outcome =
    | Ok of Snapshot
    | Failed of code: int * message: string

let private parseFile (path: string) : Snapshot =
    let html = File.ReadAllText path
    let ts = timestampFromFilename path
    parseSnapshot path ts html

let private shortIso (t: DateTimeOffset) : string =
    t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)

let private isoOrEmpty (t: DateTimeOffset option) =
    t |> Option.map shortIso |> Option.defaultValue ""

let private dailyLogPath (logDir: string) (snap: Snapshot) : string =
    let date =
        snap.CapturedAt
        |> Option.map (fun t -> t.UtcDateTime.Date)
        |> Option.defaultValue DateTime.UtcNow.Date
    Path.Combine(logDir, sprintf "usage-%s.csv" (date.ToString "yyyy-MM-dd"))

let private snapshotToRows (snap: Snapshot) : string list =
    snap.Bars
    |> List.map (fun b ->
        Csv.row [
            Csv.Text (isoOrEmpty snap.CapturedAt)
            Csv.Text (Path.GetFileName snap.SourceFile)
            Csv.Text (snap.Plan |> Option.defaultValue "")
            Csv.Text b.Label
            Csv.Text (b.Subtitle |> Option.defaultValue "")
            Csv.Num b.Percent
            Csv.Text (b.Caption |> Option.defaultValue "")
        ])

/// Full cycle: launch Chromium, wait for the snapshot, parse it, append to
/// the daily CSV log, return the parsed Snapshot. Progress events from the
/// extension are routed through `onProgress`.
let run (cfg: Config) (onProgress: string -> unit) : Outcome =
    let chromiumCfg : Chromium.LaunchConfig = {
        ChromiumExe = cfg.ChromiumExe
        ProfileDir = cfg.ProfileDir
        ExtensionDir = cfg.ExtensionDir
        TargetUrl = cfg.TargetUrl
    }

    let startedAt = DateTime.Now.AddSeconds -2.0

    let portOpt = Progress.startWith onProgress
    let effectiveUrl =
        match portOpt with
        | Some p -> sprintf "%s#harness_port=%d" cfg.TargetUrl p
        | None   -> cfg.TargetUrl
    let chromiumCfg = { chromiumCfg with TargetUrl = effectiveUrl }

    onProgress "launching Chromium..."

    try
        if not (System.IO.File.Exists cfg.ChromiumExe) then
            Failed (2, sprintf "Chromium not found at %s. Move it back, or update the path in Scrape.fs." cfg.ChromiumExe)
        else

        match Chromium.launch chromiumCfg cfg.TimeoutMs with
        | Chromium.TimedOut ->
            Failed (3, sprintf "Scrape timed out after %ds. Check your internet, or re-auth at claude.ai (press W)." (cfg.TimeoutMs / 1000))
        | Chromium.Exited n when n <> 0 ->
            Failed (4, sprintf "Chromium exited with code %d — unexpected termination. Try again; if it persists, the profile may be corrupted." n)
        | Chromium.Exited _ ->
            match Io.findFreshestSnapshot cfg.SnapshotDir startedAt with
            | None ->
                Failed (5, sprintf "Chromium ran but produced no snapshot. The grabber extension may not have loaded from %s." cfg.ExtensionDir)
            | Some path when Io.isFailedSnapshot path ->
                Failed (6, "Page never reached the ready state. Likely causes: offline, or logged out — press W to open claude.ai and re-auth.")
            | Some path ->
                let parsed : Result<Snapshot, string> =
                    try Result.Ok (parseFile path)
                    with ex -> Result.Error (sprintf "Parser error: %s. The page structure may have changed." ex.Message)
                match parsed with
                | Result.Error msg -> Failed (8, msg)
                | Result.Ok snap ->
                    let rows = snapshotToRows snap
                    if List.isEmpty rows then
                        Failed (7, "Snapshot loaded but contained no usage bars. You may be on a login screen — press W to open claude.ai and check.")
                    else
                        let logPath = dailyLogPath cfg.LogDir snap
                        Io.appendRows logPath Csv.header rows
                        Ok snap
    finally
        Progress.stop ()
