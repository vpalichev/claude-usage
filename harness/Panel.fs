module Harness.Panel

open System
open System.Text.RegularExpressions
open Harness.Domain

/// Each rendered line is a sequence of colored segments, so a single row can
/// mix plain text with an alert colour (e.g. the last 10% of a progress bar
/// painted yellow). The panel text itself is always plain — no ANSI.
type LineColor = Default | Alert | Warn | Good

type Segment = { Text: string; Color: LineColor }
type Line = { Segments: Segment list }

let lineText (l: Line) : string = l.Segments |> List.map (fun s -> s.Text) |> String.concat ""
let lineLength (l: Line) : int = l.Segments |> List.sumBy (fun s -> s.Text.Length)

// --- Box drawing glyphs ---------------------------------------------------

let boxTL, boxTR, boxBL, boxBR = "\u250C", "\u2510", "\u2514", "\u2518"
let boxH, boxV = "\u2500", "\u2502"
let cellFull, cellWarn, cellEmpty = "\u2588", "\u2593", "\u2591"
let cellProjected = "\u2592"
let sep = "|"

let interior = 88
let frameWidth = interior + 2

let barInnerWidth = 69
let weeklySegments = 7
let weeklyCellsPerDay = 9
// 7 × 9 data cells + 6 separators = 69 → fits exactly into barInnerWidth.
let sessionSegments = 5
let sessionCellsPerHour = 13
// 5 × 13 data cells + 4 separators = 69 → same width as weekly.

// --- Rounding rules (pessimistic ceiling) ---------------------------------

/// Returns (fullCount, hasWarnCell, emptyCount).
/// Rules: ceiling rounding, min 1 cell if percent>0, never fully filled
/// unless percent ≥ 100 — the boundary "would-be-full" is marked with a
/// shaded warn cell instead.
let private fillCounts (total: int) (percent: int) : int * bool * int =
    if percent <= 0 then (0, false, total)
    elif percent >= 100 then (total, false, 0)
    else
        let raw = int (Math.Ceiling(float percent / 100.0 * float total))
        if raw >= total then (total - 1, true, 0)
        else
            let filled = max 1 raw
            (filled, false, total - filled)

let private cellAt (full: int) (warn: bool) (idx: int) : string =
    if idx < full then cellFull
    elif warn && idx = full then cellWarn
    else cellEmpty

/// Displayed percent derived from the bar's cell fill — never less than
/// what the bar shows, and never 100 unless percent ≥ 100.
let displayPercent (total: int) (percent: int) : int =
    let (full, warn, _) = fillCounts total percent
    let effective = if warn then full + 1 else full
    let raw = int (Math.Ceiling(float effective / float total * 100.0))
    if percent < 100 && raw >= 100 then 99 else raw

// --- Bar renderers --------------------------------------------------------

/// `projectedPercent` is the pessimistic (max-rate-based) estimate of where
/// the session will sit by the next refresh. Cells between `percent` and
/// `projectedPercent` render as the medium shade so the eye reads a three-
/// tier ramp: done / likely / untouched. Projected cells never overlay the
/// warn-boundary cell and never reach the last cell, preserving the
/// "only truly full at 100%" invariant.
let private sessionBar (percent: int) (projectedPercent: int) : string =
    let total = sessionSegments * sessionCellsPerHour
    let (full, warn, _) = fillCounts total percent
    let projCells =
        if warn || projectedPercent <= percent then 0
        else
            let (pFull, _, _) = fillCounts total projectedPercent
            max 0 (min (total - 1 - full) (pFull - full))
    let cellFor idx =
        if idx < full then cellFull
        elif warn && idx = full then cellWarn
        elif idx < full + projCells then cellProjected
        else cellEmpty
    let segment h =
        [ for j in 0 .. sessionCellsPerHour - 1 ->
            cellFor (h * sessionCellsPerHour + j) ]
        |> String.concat ""
    let segments = [ for h in 0 .. sessionSegments - 1 -> segment h ]
    sprintf "[%s]" (String.concat sep segments)

let private weeklyBar (percent: int) : string =
    let total = weeklySegments * weeklyCellsPerDay
    let (full, warn, _) = fillCounts total percent
    let segment d =
        [ for j in 0 .. weeklyCellsPerDay - 1 ->
            cellAt full warn (d * weeklyCellsPerDay + j) ]
        |> String.concat ""
    let segments = [ for d in 0 .. weeklySegments - 1 -> segment d ]
    sprintf "[%s]" (String.concat sep segments)

// --- Frame helpers --------------------------------------------------------

let private seg color text : Segment = { Text = text; Color = color }
let private plainLine (text: string) : Line = { Segments = [ seg Default text ] }
let private coloredLine (color: LineColor) (text: string) : Line = { Segments = [ seg color text ] }
let private segmentedLine (segs: Segment list) : Line = { Segments = segs }

let private boxLine (content: string) : Line =
    let pad = max 0 (interior - content.Length)
    plainLine (sprintf "%s%s%s%s" boxV content (String.replicate pad " ") boxV)

let private blankLine () = boxLine ""

let private topBorder (leftTxt: string) (rightTxt: string) : Line =
    let left = sprintf "%s%s %s " boxTL boxH leftTxt
    let right = sprintf " %s %s%s" rightTxt boxH boxTR
    let fillW = frameWidth - left.Length - right.Length
    plainLine (left + (if fillW > 0 then String.replicate fillW boxH else "") + right)

let private bottomBorder () =
    plainLine (sprintf "%s%s%s" boxBL (String.replicate interior boxH) boxBR)

/// Wrap a row whose bar body should have its last 10% painted yellow
/// (danger zone). `rowLabel` is "5H" / "AM" / etc. `barStr` is "[<body>]"
/// from sessionBar/weeklyBar — brackets included.
let private boxBarLine (rowLabel: string) (barStr: string) : Line =
    let body = barStr.Substring(1, barStr.Length - 2)
    let safeLen = int (Math.Floor(float body.Length * 0.9))
    let safe = body.Substring(0, safeLen)
    let danger = body.Substring(safeLen)
    let prefix = sprintf "  %s    [" rowLabel
    let visibleLen = prefix.Length + body.Length + 1   // +1 for ']'
    let pad = max 0 (interior - visibleLen)
    let tail = "]" + String.replicate pad " " + boxV
    segmentedLine [
        seg Default (boxV + prefix)
        seg Default safe
        seg Warn danger
        seg Default tail
    ]

/// Like boxLine but the inner content is drawn in `color` while the borders
/// stay default — used for the "session at >75%" alert row.
let private boxColoredLine (color: LineColor) (content: string) : Line =
    let pad = max 0 (interior - content.Length)
    let tail = String.replicate pad " "
    segmentedLine [
        seg Default boxV
        seg color content
        seg Default (tail + boxV)
    ]

let private rightAlign (text: string) (reserveAfter: int) : string =
    let leading = max 0 (interior - text.Length - reserveAfter)
    String.replicate leading " " + text + String.replicate reserveAfter " "

// --- Subtitle parsing -----------------------------------------------------

let private parseRemaining (subtitle: string) : TimeSpan option =
    if not (subtitle.StartsWith "Resets in ") then None
    else
        let hrMatch = Regex.Match(subtitle, @"(\d+)\s*hr")
        let minMatch = Regex.Match(subtitle, @"(\d+)\s*min")
        if not (hrMatch.Success || minMatch.Success) then None
        else
            let hr = if hrMatch.Success then int hrMatch.Groups.[1].Value else 0
            let mn = if minMatch.Success then int minMatch.Groups.[1].Value else 0
            Some (TimeSpan.FromMinutes(float (hr * 60 + mn)))

let private parseWeeklyReset (subtitle: string) : (DayOfWeek * int * int) option =
    let m = Regex.Match(subtitle, @"Resets\s+(Mon|Tue|Wed|Thu|Fri|Sat|Sun)\s+(\d+):(\d+)\s*(AM|PM)", RegexOptions.IgnoreCase)
    if not m.Success then None
    else
        let day =
            match m.Groups.[1].Value with
            | "Mon" -> Some DayOfWeek.Monday
            | "Tue" -> Some DayOfWeek.Tuesday
            | "Wed" -> Some DayOfWeek.Wednesday
            | "Thu" -> Some DayOfWeek.Thursday
            | "Fri" -> Some DayOfWeek.Friday
            | "Sat" -> Some DayOfWeek.Saturday
            | "Sun" -> Some DayOfWeek.Sunday
            | _ -> None
        let hr12 = int m.Groups.[2].Value
        let mn = int m.Groups.[3].Value
        let isPm = m.Groups.[4].Value.ToUpperInvariant() = "PM"
        let hr24 =
            if isPm && hr12 < 12 then hr12 + 12
            elif not isPm && hr12 = 12 then 0
            else hr12
        day |> Option.map (fun d -> d, hr24, mn)

let private previousReset (now: DateTime) (day: DayOfWeek) (hr: int) (mn: int) : DateTime =
    let daysSince = (int now.DayOfWeek - int day + 7) % 7
    let cand = now.Date.AddDays(-float daysSince).AddHours(float hr).AddMinutes(float mn)
    if cand <= now then cand else cand.AddDays(-7.0)

/// Round a computed reset time to the nearest :00 boundary.
/// Claude's 5-h session resets always land on the top of the hour; the
/// "Resets in X hr Y min" countdown is whole-minute, so capture+remaining
/// is off by at most a minute, which hour-rounding absorbs.
let private roundToHour (dt: DateTime) : DateTime =
    let baseDt = DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Kind)
    if dt.Minute >= 30 then baseDt.AddHours(1.0) else baseDt

/// Derive the current 5-hour session window (start, end) from a snapshot.
/// Returns None if the "Current session" bar subtitle is missing or
/// unparseable. Used by the tray clock tick to (a) advance the 5H triangle
/// on wall-clock time and (b) trigger a fresh scrape once the window ends.
let sessionWindow (snap: Snapshot) : (DateTime * DateTime) option =
    snap.Bars
    |> List.tryFind (fun b -> b.Label = "Current session")
    |> Option.bind (fun b -> b.Subtitle)
    |> Option.bind parseRemaining
    |> Option.map (fun remaining ->
        let capturedAt = snap.CapturedAt |> Option.defaultWith (fun () -> DateTimeOffset.UtcNow)
        let endTime = roundToHour (capturedAt.LocalDateTime + remaining)
        (endTime.AddHours(-5.0), endTime))

let private agoText (elapsed: TimeSpan) : string =
    if elapsed.TotalMinutes < 60.0 then sprintf "%d min ago" (max 0 (int elapsed.TotalMinutes))
    elif elapsed.TotalHours   < 24.0 then sprintf "%d hr ago"  (int elapsed.TotalHours)
    else sprintf "%d day ago" (int elapsed.TotalDays)

/// Append " | next refresh in N min" to a header label when the next refresh
/// time is known; return the base label unchanged otherwise.
let private withRefresh (baseLabel: string) (nextRefreshAt: DateTime option) : string =
    match nextRefreshAt with
    | Some t ->
        let mins = max 0 (int (Math.Ceiling((t - DateTime.Now).TotalMinutes)))
        sprintf "%s | next refresh in %d min" baseLabel mins
    | None -> baseLabel

// --- View mode ------------------------------------------------------------

/// Normal = bar-graph view. Info = compact diagnostics: raw scraped fields
/// on one line per bar, plus snapshot and profile paths. Toggled with `I`.
type ViewMode = Normal | Info

/// Greedy word-wrap for error messages that exceed the interior width.
let private wrapWords (width: int) (text: string) : string list =
    let words = text.Split ' '
    let buf = ResizeArray<string>()
    let mutable current = ""
    for w in words do
        let candidate = if current = "" then w else current + " " + w
        if candidate.Length > width && current <> "" then
            buf.Add current
            current <- w
        else
            current <- candidate
    if current <> "" then buf.Add current
    List.ofSeq buf

/// Overlay rows shown when the last refresh failed. Rendered in Alert colour
/// inside the frame; the last good snapshot below stays readable.
let private errorOverlay (lastError: (DateTimeOffset * string) option) : Line list =
    match lastError with
    | None -> []
    | Some (at, msg) ->
        let age =
            let e = DateTimeOffset.Now - at
            if e.TotalMinutes < 1.0 then "just now"
            elif e.TotalMinutes < 60.0 then sprintf "%d min ago" (int e.TotalMinutes)
            elif e.TotalHours < 24.0 then sprintf "%d hr ago"  (int e.TotalHours)
            else sprintf "%d day ago" (int e.TotalDays)
        let header = sprintf "  [!] Last refresh failed %s:" age
        let wrap = wrapWords (interior - 6) msg
        let buf = ResizeArray<Line>()
        buf.Add (boxColoredLine Alert header)
        for line in wrap do
            buf.Add (boxColoredLine Alert ("      " + line))
        buf.Add (blankLine ())
        List.ofSeq buf

// --- Main renderer --------------------------------------------------------

/// Status line that appears below the key-hint row during an in-flight scrape.
/// Kept in Warn colour so it stands out against the default-grey hint.
let private loadingRow (loadingStage: string option) : Line option =
    loadingStage |> Option.map (fun s -> boxColoredLine Warn (sprintf "  Loading: %s" s))

let render (snap: Snapshot) (nextRefreshAt: DateTime option) (lastError: (DateTimeOffset * string) option) (loadingStage: string option) (sessionProjectedPercent: int option) : Line list =
    let buf = ResizeArray<Line>()
    let add (l: Line) = buf.Add l

    let findBar (label: string) = snap.Bars |> List.tryFind (fun b -> b.Label = label)
    let session      = findBar "Current session"
    let weeklyAll    = findBar "All models"
    let weeklySonnet = findBar "Sonnet only"
    let weeklyDesign = findBar "Claude Design"

    let wallNow = DateTimeOffset.Now
    let capturedAt = snap.CapturedAt |> Option.defaultWith (fun () -> DateTimeOffset.UtcNow)
    let elapsed = wallNow - capturedAt
    let absStr = capturedAt.LocalDateTime.ToString("MMM dd, HH:mm", System.Globalization.CultureInfo.InvariantCulture)
    let now = capturedAt.LocalDateTime

    add (topBorder (withRefresh ("Captured " + agoText elapsed) nextRefreshAt) absStr)
    add (blankLine ())
    for l in errorOverlay lastError do add l

    // "  XX    [" is 9 chars — triangle position = 9 + cell column in bar body.
    let barPrefix = 9

    // --- Session (5-hour window): triangle and caption advance on the
    // wall-clock each minute, not with capture time. The window has fixed
    // :00-anchored start/end derived once per scrape.
    match session with
    | Some s ->
        let sessionHours = 5.0
        let elapsedHours =
            sessionWindow snap
            |> Option.map (fun (start, _) ->
                let e = (wallNow.LocalDateTime - start).TotalHours
                max 0.0 (min sessionHours e))
        match elapsedHours with
        | Some e ->
            let fraction = min 1.0 (e / sessionHours)
            let total = sessionSegments * sessionCellsPerHour
            let raw = int (Math.Floor(fraction * float total))
            let dataIdx = max 0 (min (total - 1) raw)
            let hourIdx = dataIdx / sessionCellsPerHour
            let cellInHour = dataIdx % sessionCellsPerHour
            let displayCol = hourIdx * (sessionCellsPerHour + 1) + cellInHour
            add (boxColoredLine Good (String.replicate (barPrefix + displayCol) " " + "\u25BC"))
        | None -> ()
        let projP = sessionProjectedPercent |> Option.defaultValue s.Percent
        add (boxBarLine "5H" (sessionBar s.Percent projP))
        let capText =
            match elapsedHours with
            | Some e -> sprintf "%.2f / %.2f H" e sessionHours
            | None -> s.Subtitle |> Option.defaultValue ""
        add (boxLine (rightAlign capText 2))
        add (blankLine ())

        // >75% → red alert line suggesting Sonnet.
        if s.Percent > 75 then
            let dp = displayPercent (sessionSegments * sessionCellsPerHour) s.Percent
            let alertText = sprintf "  [!] Session at %d%% — consider switching to Sonnet" dp
            add (boxColoredLine Alert alertText)
            add (blankLine ())
    | None -> ()

    // --- Weekly (AM / SO / CD): each bar has its own reset, triangle, caption.
    // Reset days differ between bars (user-specific), so we can't share a
    // day-label row. Uninitialized bars ("You haven't used X yet") have no
    // parseable reset — show no triangle and "(not yet used)" caption.
    let renderWeekly (code: string) (bar: UsageBar) =
        let elapsedDays =
            bar.Subtitle
            |> Option.bind parseWeeklyReset
            |> Option.map (fun (d, hr, mn) -> (now - previousReset now d hr mn).TotalDays)
        match elapsedDays with
        | Some e ->
            let total = weeklySegments * weeklyCellsPerDay
            let raw = int (Math.Floor(e / 7.0 * float total))
            let dataIdx = max 0 (min (total - 1) raw)
            let dayIdx    = dataIdx / weeklyCellsPerDay
            let cellInDay = dataIdx % weeklyCellsPerDay
            let displayCol = dayIdx * (weeklyCellsPerDay + 1) + cellInDay
            add (boxColoredLine Good (String.replicate (barPrefix + displayCol) " " + "\u25BC"))
        | None -> ()
        add (boxBarLine code (weeklyBar bar.Percent))
        let capText =
            match elapsedDays with
            | Some e -> sprintf "%.1f / 7.0 D" e
            | None -> "(not yet used)"
        add (boxLine (rightAlign capText 2))
        add (blankLine ())

    let weeklyRows =
        [ "AM", weeklyAll; "SO", weeklySonnet; "CD", weeklyDesign ]
        |> List.choose (fun (code, r) -> r |> Option.map (fun x -> code, x))

    if not (List.isEmpty weeklyRows) then
        add (boxLine "  Weekly")
        add (blankLine ())
        for (code, bar) in weeklyRows do
            renderWeekly code bar

    // Key hint row just above the bottom border, so the shortcuts are discoverable.
    add (boxLine "  R reload  \u00B7  W browser  \u00B7  Q quit  \u00B7  Esc hide  \u00B7  I info")
    loadingRow loadingStage |> Option.iter add
    add (bottomBorder ())

    List.ofSeq buf

/// Collapse CR/LF runs into " | " so multi-line scraped text renders inline.
let private compact (s: string) : string =
    Regex.Replace(s, @"\s*(\r\n|\r|\n)\s*", " | ")

/// Break `text` into chunks of at most `width` chars. Prefers breaking at a
/// space if one falls in the right half of the chunk; otherwise hard-cuts
/// mid-token (handles long paths with no spaces).
let private hardWrap (width: int) (text: string) : string list =
    if width <= 0 || text.Length <= width then [ text ]
    else
        let rec loop acc (s: string) =
            if s.Length <= width then List.rev (s :: acc)
            else
                let slice = s.Substring(0, width)
                let spaceIdx = slice.LastIndexOf ' '
                let cut = if spaceIdx > width / 2 then spaceIdx else width
                let head = s.Substring(0, cut).TrimEnd()
                let tail = s.Substring(cut).TrimStart()
                loop (head :: acc) tail
        loop [] text

/// Info view: compact diagnostics. One line per bar (label + percent +
/// subtitle + caption, CRLFs collapsed to " | "), plus the snapshot file path
/// and the Chromium profile path used for the fetch.
let renderInfo (snap: Snapshot) (nextRefreshAt: DateTime option) (lastError: (DateTimeOffset * string) option) (loadingStage: string option) (profilePath: string) (chromiumExe: string) : Line list =
    let buf = ResizeArray<Line>()
    let add (l: Line) = buf.Add l

    /// Emit one or more boxLines for `body`, prefixing the first with `prefix`
    /// and any wrapped continuation lines with `cont`. Wraps at the interior
    /// width minus the prefix length, so the right-hand boxV still aligns.
    let addWrapped (prefix: string) (cont: string) (body: string) =
        let avail = interior - prefix.Length
        match hardWrap avail body with
        | [] -> ()
        | first :: rest ->
            add (boxLine (prefix + first))
            for line in rest do
                add (boxLine (cont + line))

    let wallNow = DateTimeOffset.Now
    let capturedAt = snap.CapturedAt |> Option.defaultWith (fun () -> DateTimeOffset.UtcNow)
    let elapsed = wallNow - capturedAt
    let absStr = capturedAt.LocalDateTime.ToString("MMM dd, HH:mm", System.Globalization.CultureInfo.InvariantCulture)

    add (topBorder (withRefresh ("Captured " + agoText elapsed) nextRefreshAt) absStr)
    add (blankLine ())
    for l in errorOverlay lastError do add l

    let plan = snap.Plan |> Option.defaultValue "\u2014"
    addWrapped "  Plan: " "        " plan
    add (blankLine ())

    for b in snap.Bars do
        let sub = b.Subtitle |> Option.map compact |> Option.defaultValue "\u2014"
        let cap = b.Caption  |> Option.map compact |> Option.defaultValue "\u2014"
        let body = sprintf "%-18s %3d%%  %s  |  %s" b.Label b.Percent sub cap
        addWrapped "  " "      " body
    add (blankLine ())

    addWrapped "  snapshot: " "             " snap.SourceFile
    addWrapped "  profile:  " "             " profilePath
    addWrapped "  chromium: " "             " chromiumExe
    add (blankLine ())

    add (boxLine "  R reload  \u00B7  W browser  \u00B7  Q quit  \u00B7  Esc hide  \u00B7  I normal")
    loadingRow loadingStage |> Option.iter add
    add (bottomBorder ())

    List.ofSeq buf

/// Empty-state frame shown before the first successful scrape completes.
/// Same shape as the real panel: top border, empty bars, next-refresh label,
/// hint row, and loading status under the hints. Lets the UI "hydrate" on
/// arrival rather than swapping out a placeholder for the full panel.
let renderSkeleton (nextRefreshAt: DateTime option) (lastError: (DateTimeOffset * string) option) (loadingStage: string option) : Line list =
    let buf = ResizeArray<Line>()
    let add (l: Line) = buf.Add l

    add (topBorder (withRefresh "No data yet" nextRefreshAt) "\u2014")
    add (blankLine ())
    for l in errorOverlay lastError do add l

    add (boxBarLine "5H" (sessionBar 0 0))
    add (boxLine (rightAlign "\u2014 / 5.00 H" 2))
    add (blankLine ())

    add (boxLine "  Weekly")
    add (blankLine ())
    for code in [ "AM"; "SO"; "CD" ] do
        add (boxBarLine code (weeklyBar 0))
        add (boxLine (rightAlign "(awaiting data)" 2))
        add (blankLine ())

    add (boxLine "  R reload  \u00B7  W browser  \u00B7  Q quit  \u00B7  Esc hide  \u00B7  I info")
    loadingRow loadingStage |> Option.iter add
    add (bottomBorder ())

    List.ofSeq buf
