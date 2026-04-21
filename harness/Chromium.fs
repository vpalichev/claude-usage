module Harness.Chromium

open System
open System.Diagnostics

type LaunchConfig = {
    ChromiumExe: string
    ProfileDir: string
    ExtensionDir: string
    TargetUrl: string
}

type LaunchResult =
    | Exited of exitCode: int
    | TimedOut

let private quote (s: string) = "\"" + s + "\""

let private buildArgs (cfg: LaunchConfig) : string =
    String.concat " " [
        // Open minimized so the window lives in the taskbar briefly, without
        // stealing focus or covering what you're doing. Headless=new was tried
        // but triggers Cloudflare's Turnstile challenge in front of claude.ai.
        "--start-minimized"
        "--window-position=-32000,-32000"
        "--window-size=800,600"
        "--user-data-dir=" + quote cfg.ProfileDir
        "--load-extension=" + quote cfg.ExtensionDir
        "--no-first-run"
        "--no-default-browser-check"
        "--disable-features=DisableLoadExtensionCommandLineSwitch"
        quote cfg.TargetUrl
    ]

/// Launches Chromium with the grabber extension, waits for it to exit cleanly
/// (the extension closes its own window on success, which terminates the process
/// since no other windows share this profile). On timeout, kills the whole
/// process tree. Stdout/stderr from Chromium are drained and discarded.
let launch (cfg: LaunchConfig) (timeoutMs: int) : LaunchResult =
    let psi = ProcessStartInfo(cfg.ChromiumExe, buildArgs cfg)
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true

    use p = new Process()
    p.StartInfo <- psi
    p.Start() |> ignore

    // Drain both streams asynchronously so Chromium never blocks on full pipes.
    let outTask = p.StandardOutput.ReadToEndAsync()
    let errTask = p.StandardError.ReadToEndAsync()

    if p.WaitForExit(timeoutMs) then
        outTask.Wait()
        errTask.Wait()
        Exited p.ExitCode
    else
        try p.Kill(entireProcessTree = true) with _ -> ()
        TimedOut
