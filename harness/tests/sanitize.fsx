// Build a sanitized golden fixture from a raw capture.
//
//   dotnet fsi harness/tests/sanitize.fsx
//   dotnet fsi harness/tests/sanitize.fsx <input.html> <output.html>
//
// Reads a raw, PII-bearing capture from fixtures.local/, keeps only the
// <section> blocks that contain a [role="progressbar"] descendant (the Plan
// usage and Weekly limits sections), wraps them in minimal HTML, and writes
// the result to fixtures/settings-usage.html. Drops sidebar (chat list,
// account name), header (avatar), footer, and anything else outside the
// relevant sections.

#r "nuget: AngleSharp, 1.1.2"

open System
open System.IO
open AngleSharp
open AngleSharp.Html.Parser

let scriptDir = __SOURCE_DIRECTORY__
let defaultIn  = Path.Combine(scriptDir, "fixtures.local", "settings-usage.html")
let defaultOut = Path.Combine(scriptDir, "fixtures",       "settings-usage.html")

let args = fsi.CommandLineArgs
let inPath  = if args.Length > 1 then args.[1] else defaultIn
let outPath = if args.Length > 2 then args.[2] else defaultOut

if not (File.Exists inPath) then
    eprintfn "input not found: %s" inPath
    eprintfn "drop a raw capture from %%USERPROFILE%%\\Downloads\\page-grabber\\claude.ai\\ into fixtures.local/ first"
    exit 2

let html = File.ReadAllText inPath
let ctx = BrowsingContext.New(Configuration.Default)
let parser = ctx.GetService<IHtmlParser>()
let doc = parser.ParseDocument(html)

let kept =
    doc.QuerySelectorAll("section")
    |> Seq.filter (fun s -> not (isNull (s.QuerySelector("[role='progressbar']"))))
    |> Seq.map (fun s -> s.OuterHtml)
    |> Seq.toList

if List.isEmpty kept then
    eprintfn "no <section> containing a [role='progressbar'] found in %s" inPath
    exit 3

let body = String.concat "\n" kept
let output =
    sprintf
        "<!doctype html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>claude.ai usage (sanitized fixture)</title>\n</head>\n<body>\n%s\n</body>\n</html>\n"
        body

Directory.CreateDirectory(Path.GetDirectoryName outPath) |> ignore
File.WriteAllText(outPath, output)
printfn "wrote %d sections (%d bytes) to %s" kept.Length output.Length outPath
