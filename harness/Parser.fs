module Harness.Parser

open System
open AngleSharp
open AngleSharp.Dom
open AngleSharp.Html.Parser
open Harness.Domain

let private tryAttr (name: string) (el: IElement) =
    match el.GetAttribute(name) with
    | null -> None
    | v -> Some v

let private tryText (el: IElement) =
    if isNull el then None
    else
        let t = el.TextContent.Trim()
        if String.IsNullOrWhiteSpace t then None else Some t

/// Walk up at most `maxDepth` parents.
let private ancestor (maxDepth: int) (el: IElement) : IElement option =
    let mutable cur = el
    let mutable steps = 0
    while not (isNull cur) && steps < maxDepth do
        cur <- cur.ParentElement
        steps <- steps + 1
    if isNull cur then None else Some cur

/// From a progressbar element, locate its "row" container (3 ancestors up
/// in the observed markup) and read the surrounding label / subtitle / caption.
let private barFromProgressbar (pb: IElement) : UsageBar option =
    let valueNow =
        pb
        |> tryAttr "aria-valuenow"
        |> Option.bind (fun s ->
            match Int32.TryParse s with
            | true, v -> Some v
            | _ -> None)
    match valueNow, ancestor 3 pb with
    | None, _ -> None
    | _, None -> None
    | Some percent, Some row ->
        let label =
            row.QuerySelector("p.text-text-100")
            |> tryText
            |> Option.defaultValue ""
        let subtitle =
            row.QuerySelector("p.text-text-400:not(.text-right):not(.min-w-\\[5\\.5rem\\])")
            |> tryText
            |> Option.filter (fun s -> not (s.EndsWith "used") && not (s.Contains " / "))
        let caption =
            // The caption is the <p> that accompanies the bar itself — either
            // "N% used" or "M / N" (for discrete counters). Find any descendant
            // <p> whose text matches that shape.
            row.QuerySelectorAll("p")
            |> Seq.map (fun p -> p.TextContent.Trim())
            |> Seq.tryFind (fun s ->
                s.EndsWith("used") || (s.Contains(" / ") && s |> Seq.exists Char.IsDigit))
        Some {
            Label = label
            Subtitle = subtitle
            Percent = percent
            Caption = caption
        }

/// Parse the plan-name span sitting next to the "Plan usage limits" heading.
let private findPlan (doc: IDocument) : string option =
    doc.QuerySelectorAll("h2")
    |> Seq.tryFind (fun h -> h.TextContent.Trim() = "Plan usage limits")
    |> Option.bind (fun h ->
        let parent = h.ParentElement
        if isNull parent then None
        else
            parent.QuerySelector("span")
            |> tryText)

/// Main entry point: parse a snapshot of claude.ai/settings/usage.
let parseSnapshot (sourceFile: string) (capturedAt: DateTimeOffset option) (html: string) : Snapshot =
    let ctx = BrowsingContext.New(Configuration.Default)
    let parser = ctx.GetService<IHtmlParser>()
    let doc = parser.ParseDocument(html)

    let bars =
        doc.QuerySelectorAll("div[role='progressbar'][aria-label='Usage']")
        |> Seq.choose barFromProgressbar
        |> Seq.toList

    {
        SourceFile = sourceFile
        CapturedAt = capturedAt
        Plan = findPlan doc
        Bars = bars
    }

/// Extract the ISO-ish timestamp our grabber embeds in snapshot filenames,
/// e.g.  settings-usage-2026-04-20T07-13-22-694Z.html
/// The colons and dot in the ISO timestamp were replaced with dashes when
/// the file was written, so we need to put them back before DateTimeOffset can parse.
let timestampFromFilename (path: string) : DateTimeOffset option =
    let name = System.IO.Path.GetFileNameWithoutExtension path
    let m = System.Text.RegularExpressions.Regex.Match(name, @"(\d{4}-\d{2}-\d{2})T(\d{2})-(\d{2})-(\d{2})-(\d{3})Z")
    if not m.Success then None
    else
        let iso = sprintf "%sT%s:%s:%s.%sZ" m.Groups.[1].Value m.Groups.[2].Value m.Groups.[3].Value m.Groups.[4].Value m.Groups.[5].Value
        match DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal ||| System.Globalization.DateTimeStyles.AdjustToUniversal) with
        | true, v -> Some v
        | _ -> None
