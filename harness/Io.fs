module Harness.Io

open System
open System.IO

/// Find the newest *.html file in `folder` that was written after `threshold`.
/// Used by the orchestrator to locate the snapshot just produced by Chromium.
let findFreshestSnapshot (folder: string) (threshold: DateTime) : string option =
    if not (Directory.Exists folder) then None
    else
        Directory.EnumerateFiles(folder, "*.html")
        |> Seq.filter (fun f -> File.GetLastWriteTime f >= threshold)
        |> Seq.sortByDescending File.GetLastWriteTime
        |> Seq.tryHead

/// Any snapshot whose name carries -NOSENTINEL means the grabber gave up on
/// the readiness sentinel; the HTML is likely incomplete or a login wall.
let isFailedSnapshot (path: string) : bool =
    (Path.GetFileNameWithoutExtension path).Contains "-NOSENTINEL"

/// Append rows to a log file, writing the header first if the file is new.
let appendRows (logPath: string) (header: string) (rows: string seq) : unit =
    let existed = File.Exists logPath
    let dir = Path.GetDirectoryName logPath
    if not (String.IsNullOrEmpty dir) && not (Directory.Exists dir) then
        Directory.CreateDirectory dir |> ignore
    use w = new StreamWriter(logPath, append = true)
    if not existed then w.WriteLine header
    for r in rows do w.WriteLine r
