module Harness.Program

open System
open System.IO
open Harness.Domain
open Harness.Parser

// ---------- CLI helpers (parse / parse-folder still work for debug) ---------

let private shortIso (t: DateTimeOffset) : string =
    t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)

let private isoOrEmpty (t: DateTimeOffset option) =
    t |> Option.map shortIso |> Option.defaultValue ""

let private parseFile (path: string) : Snapshot =
    let html = File.ReadAllText path
    let ts = timestampFromFilename path
    parseSnapshot path ts html

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

// ---------- Entry point -----------------------------------------------------

[<EntryPoint; STAThread>]
let main argv =
    match Array.toList argv with
    | [ "parse"; path ] when File.Exists path ->
        let snap = parseFile path
        printfn "%s" Csv.header
        for r in snapshotToRows snap do printfn "%s" r
        0

    | [ "parse-folder"; dir ] when Directory.Exists dir ->
        let files = Directory.EnumerateFiles(dir, "*.html") |> Seq.sort |> Seq.toList
        printfn "%s" Csv.header
        for f in files do
            for r in snapshotToRows (parseFile f) do printfn "%s" r
        0

    | [ "parse-folder"; dir; "--output"; outPath ] when Directory.Exists dir ->
        let files = Directory.EnumerateFiles(dir, "*.html") |> Seq.sort |> Seq.toList
        use w = new StreamWriter(outPath, append = false)
        w.WriteLine Csv.header
        for f in files do
            for r in snapshotToRows (parseFile f) do w.WriteLine r
        0

    | [ "--login" ] ->
        // Launch Chromium with the runtime profile dir but NO grabber
        // extension so the window stays open for manual interactive login.
        // Uses the same Chromium-discovery and profile-path logic the tray
        // uses at scrape time — single source of truth for those paths.
        let cfg = Scrape.defaults
        if not (File.Exists cfg.ChromiumExe) then
            System.Windows.Forms.MessageBox.Show(
                sprintf "Chromium not found at %s\n\nSet CLAUDE_USAGE_CHROMIUM to override." cfg.ChromiumExe,
                "claude-usage login",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error) |> ignore
            2
        else
            Directory.CreateDirectory cfg.ProfileDir |> ignore
            let args =
                String.concat " " [
                    sprintf "--user-data-dir=\"%s\"" cfg.ProfileDir
                    "--no-first-run"
                    "--no-default-browser-check"
                    "\"https://claude.ai/login\""
                ]
            let psi = System.Diagnostics.ProcessStartInfo(cfg.ChromiumExe, args)
            psi.UseShellExecute <- false
            use p = System.Diagnostics.Process.Start psi
            p.WaitForExit()
            p.ExitCode

    | _ ->
        // Default (no args, or anything unrecognised): launch the tray app.
        Tray.run ()
        0
