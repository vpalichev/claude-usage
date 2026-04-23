module Harness.Tray

open System
open System.Drawing
open System.Threading.Tasks
open System.Windows.Forms
open Harness.Domain
open Harness.Panel

// --- Font -----------------------------------------------------------------

let private monoFont =
    let preferred = [ "Cascadia Mono"; "Consolas"; "Lucida Console" ]
    let installed =
        use col = new Text.InstalledFontCollection()
        col.Families |> Array.map (fun f -> f.Name) |> Set.ofArray
    let name =
        preferred
        |> List.tryFind (fun n -> Set.contains n installed)
        |> Option.defaultValue "Courier New"
    // Font.GraphicsUnit.Pixel makes sizing DPI-exact — no point-to-pixel rounding.
    new Font(name, 14.0f, FontStyle.Regular, GraphicsUnit.Pixel)

// --- Icon ------------------------------------------------------------------

/// 16×16 tray/window icon: three stacked horizontal bars mirroring the panel,
/// with a yellow segment at the end of the top bar echoing the danger zone.
/// Leaks one HICON per call (acceptable — created once at startup).
let private makeIcon () : Icon =
    use bmp = new Bitmap(16, 16)
    use g = Graphics.FromImage(bmp)
    g.Clear(Color.FromArgb(20, 20, 20))
    use fill = new SolidBrush(Color.FromArgb(220, 220, 220))
    use warn = new SolidBrush(Color.FromArgb(230, 200, 70))
    g.FillRectangle(fill, 2,  3, 8, 2)   // row 1: 8 filled
    g.FillRectangle(warn, 10, 3, 2, 2)   // row 1: 2 danger
    g.FillRectangle(fill, 2,  8, 5, 2)   // row 2: 5 filled
    g.FillRectangle(fill, 2, 12, 2, 2)   // row 3: 2 filled
    Icon.FromHandle(bmp.GetHicon())

// --- Custom control that paints Panel.Line list on a precise char grid ----
//
// RichTextBox adds paragraph spacing so box-drawing chars (│) don't touch
// vertically — ugly. We draw each line at (0, i * cellHeight) using GDI's
// TextRenderer (not GDI+ DrawString) with NoPadding to get exact monospace
// spacing. Cell height is measured off a block character "█" so the grid
// matches what the font was designed for.

type PanelView() as this =
    inherit Control()

    let mutable lines : Line list = []
    let mutable cachedCell : Size = Size(0, 0)

    do
        this.DoubleBuffered <- true
        this.SetStyle(
            ControlStyles.UserPaint |||
            ControlStyles.AllPaintingInWmPaint |||
            ControlStyles.OptimizedDoubleBuffer |||
            ControlStyles.ResizeRedraw, true)
        this.BackColor <- Color.FromArgb(20, 20, 20)
        this.ForeColor <- Color.FromArgb(220, 220, 220)
        this.TabStop <- false

    member private this.MeasureCell () : Size =
        if cachedCell.Width = 0 then
            use g = this.CreateGraphics()
            let s = TextRenderer.MeasureText(g, "\u2588", this.Font, Size(10000, 10000), TextFormatFlags.NoPadding)
            cachedCell <- s
        cachedCell

    member this.Lines
        with get () = lines
        and set value =
            lines <- value
            this.Invalidate()

    override this.OnFontChanged(e) =
        cachedCell <- Size(0, 0)
        base.OnFontChanged e

    override this.OnPaint(e) =
        let g = e.Graphics
        g.Clear(this.BackColor)
        let cell = this.MeasureCell()
        let flags =
            TextFormatFlags.NoPadding |||
            TextFormatFlags.SingleLine |||
            TextFormatFlags.NoPrefix |||
            TextFormatFlags.Left
        for i in 0 .. List.length lines - 1 do
            let line = lines.[i]
            let mutable x = 0
            for s in line.Segments do
                let color =
                    match s.Color with
                    | Default -> this.ForeColor
                    | Alert -> Color.FromArgb(255, 120, 80)
                    | Warn -> Color.FromArgb(230, 200, 70)
                    | Good -> Color.FromArgb(120, 210, 120)
                TextRenderer.DrawText(
                    g, s.Text, this.Font,
                    Point(x, i * cell.Height),
                    color, flags)
                x <- x + s.Text.Length * cell.Width

    member this.ContentSize () : Size =
        let cell = this.MeasureCell()
        let cols =
            if List.isEmpty lines then 0
            else lines |> List.map lineLength |> List.max
        Size(cols * cell.Width, List.length lines * cell.Height)

// --- Rendering helpers ----------------------------------------------------

/// Measure a character cell using the screen DC so DPI matches the actual display.
let private measureCell (font: Font) : Size =
    use g = Graphics.FromHwnd(IntPtr.Zero)
    TextRenderer.MeasureText(g, "\u2588", font, Size(10000, 10000), TextFormatFlags.NoPadding)

// --- Main ------------------------------------------------------------------

let run () =
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2) |> ignore
    Application.EnableVisualStyles()
    Application.SetCompatibleTextRenderingDefault(false)

    let form = new Form()
    form.Text <- "Claude Usage"
    form.Icon <- makeIcon ()
    form.FormBorderStyle <- FormBorderStyle.FixedSingle
    form.MaximizeBox <- false
    form.StartPosition <- FormStartPosition.CenterScreen
    form.KeyPreview <- true
    form.BackColor <- Color.FromArgb(20, 20, 20)
    form.ForeColor <- Color.FromArgb(220, 220, 220)

    let view = new PanelView()
    view.Font <- monoFont
    view.Location <- Point(6, 6)
    form.Controls.Add view

    // Tray icon + right-click menu.
    let tray = new NotifyIcon()
    tray.Icon <- makeIcon ()
    tray.Text <- "Claude Usage"
    tray.Visible <- true

    let menu = new ContextMenuStrip()
    let showItem    = new ToolStripMenuItem("Show")
    let refreshItem = new ToolStripMenuItem("Refresh")
    let topMostItem = new ToolStripMenuItem("Always on top")
    let exitItem    = new ToolStripMenuItem("Exit")
    topMostItem.CheckOnClick <- true
    topMostItem.CheckedChanged.Add(fun _ -> form.TopMost <- topMostItem.Checked)
    menu.Items.Add(showItem)    |> ignore
    menu.Items.Add(refreshItem) |> ignore
    menu.Items.Add(topMostItem) |> ignore
    menu.Items.Add(new ToolStripSeparator()) |> ignore
    menu.Items.Add(exitItem)    |> ignore
    tray.ContextMenuStrip <- menu

    let ui (action: unit -> unit) =
        if form.IsHandleCreated && form.InvokeRequired then
            form.Invoke(Action(action)) |> ignore
        else action ()

    // Window size is computed once from the panel's known dimensions and never
    // changes — avoids the "tiny → normal" flash the user saw during the first
    // scrape. The view is sized to fit inside with a small padding.
    let cell = measureCell monoFont
    let panelRows = 28          // upper bound: header + error overlay + session + alert + 3 weekly blocks + next-refresh + hint + loading row + border
    let marginPx = 6
    let clientSize =
        Size(
            Panel.frameWidth * cell.Width + marginPx * 2,
            panelRows * cell.Height + marginPx * 2)
    form.ClientSize <- clientSize
    view.Location <- Point(marginPx, marginPx)
    view.Size <- Size(clientSize.Width - marginPx * 2, clientSize.Height - marginPx * 2)

    let setLines (ls: Line list) = view.Lines <- ls

    let showWindow () =
        form.Show()
        form.WindowState <- FormWindowState.Normal
        form.Activate()

    let hideWindow () = form.Hide()

    let mutable refreshing = false
    let mutable lastSnapshot : Snapshot option = None
    let mutable autoRefreshTimer : Timer = null
    let mutable nextRefreshAt : DateTime option = None
    let mutable viewMode : Panel.ViewMode = Panel.Normal   // Toggle with I → Panel.Info
    let mutable lastError : (DateTimeOffset * string) option = None
    let mutable loadingStage : string option = None
    // Rolling history of (capturedAt, sessionPercent) samples inside the
    // current 5-hour window. Cleared when percent drops (new window) or
    // when the parsed window-start shifts. Used to pick the max observed
    // burn rate and project an overlay on the session bar.
    let mutable sessionRateHistory : (DateTimeOffset * int) list = []
    let rng = Random()

    let sessionPercent (snap: Snapshot) : int option =
        snap.Bars
        |> List.tryFind (fun b -> b.Label = "Current session")
        |> Option.map (fun b -> b.Percent)

    let recordSessionSample (snap: Snapshot) =
        match sessionPercent snap with
        | None -> ()
        | Some p ->
            let at = snap.CapturedAt |> Option.defaultWith (fun () -> DateTimeOffset.UtcNow)
            let windowChanged =
                match lastSnapshot, Panel.sessionWindow snap with
                | Some prev, Some (newStart, _) ->
                    match Panel.sessionWindow prev with
                    | Some (prevStart, _) -> newStart <> prevStart
                    | None -> false
                | _ -> false
            let percentDropped =
                match List.tryLast sessionRateHistory with
                | Some (_, lastP) -> p < lastP
                | None -> false
            if windowChanged || percentDropped then
                sessionRateHistory <- []
            sessionRateHistory <- sessionRateHistory @ [(at, p)]

    let projectedSessionPercent () : int option =
        match nextRefreshAt, lastSnapshot with
        | Some nxt, Some snap ->
            let rates =
                sessionRateHistory
                |> List.pairwise
                |> List.choose (fun ((t1, p1), (t2, p2)) ->
                    let dt = (t2 - t1).TotalMinutes
                    let dp = float (p2 - p1)
                    if dt > 0.0 && dp >= 0.0 then Some (dp / dt) else None)
            match rates, sessionPercent snap with
            | [], _ | _, None -> None
            | rs, Some current ->
                let maxRate = List.max rs
                let minutesAhead = max 0.0 ((nxt - DateTime.Now).TotalMinutes)
                let projected = float current + maxRate * minutesAhead
                Some (min 100 (int (Math.Ceiling projected)))
        | _ -> None

    let renderFor snap =
        match viewMode with
        | Panel.Normal -> Panel.render snap nextRefreshAt lastError loadingStage (projectedSessionPercent ())
        | Panel.Info   -> Panel.renderInfo snap nextRefreshAt lastError loadingStage Scrape.defaults.ProfileDir Scrape.defaults.ChromiumExe

    let rerender () =
        match lastSnapshot with
        | Some snap -> setLines (renderFor snap)
        | None -> setLines (Panel.renderSkeleton nextRefreshAt lastError loadingStage)

    // Mutual recursion: refresh() re-arms the auto-refresh on completion,
    // and auto-refresh's Tick fires refresh(). So every refresh (manual or
    // auto) resets the clock to the next 30-min ± 5-min window.
    let rec refresh () =
        if refreshing then () else
        refreshing <- true
        ui (fun () ->
            loadingStage <- Some "starting..."
            rerender ())
        Task.Run(fun () ->
            let onProgress (stage: string) =
                ui (fun () ->
                    loadingStage <- Some stage
                    rerender ())
            let outcome = Scrape.run Scrape.defaults onProgress
            ui (fun () ->
                loadingStage <- None
                match outcome with
                | Scrape.Ok snap ->
                    recordSessionSample snap
                    lastSnapshot <- Some snap
                    lastError <- None
                | Scrape.Failed (_, msg) ->
                    lastError <- Some (DateTimeOffset.Now, msg)
                refreshing <- false
                scheduleNextAutoRefresh ()
                rerender ())
        ) |> ignore

    and scheduleAutoRefreshIn (intervalMs: int) =
        if not (isNull autoRefreshTimer) then
            autoRefreshTimer.Stop()
            autoRefreshTimer.Dispose()
        let t = new Timer(Interval = max 1 intervalMs)
        t.Tick.Add(fun _ -> t.Stop(); refresh ())
        autoRefreshTimer <- t
        nextRefreshAt <- Some (DateTime.Now.AddMilliseconds(float intervalMs))
        t.Start()

    and scheduleNextAutoRefresh () =
        // Exponential inter-arrival: mean 30 min, floor 10 min. Occasional
        // short gaps and long tails make it look organic instead of a uniform
        // ~30-min grid. Interval is ms-precise (includes a random sub-minute
        // part and random seconds), so fires never land on :00.
        let meanMs = 30.0 * 60.0 * 1000.0
        let floorMs = 10 * 60 * 1000
        let u = max 1e-6 (rng.NextDouble())
        let intervalMs = floorMs + int (-meanMs * log u)
        scheduleAutoRefreshIn intervalMs

    // Clock tick: every minute, re-render the last snapshot so the
    // "Captured N min ago" label, the 5H triangle, and the next-refresh
    // countdown all advance with wall-clock time. Window rollover is NOT
    // auto-refreshed here — firing an immediate scrape on every 5H
    // boundary was creating a 5-hour-on-the-:00 fingerprint on top of the
    // otherwise-randomized inter-arrivals. The panel now shows a
    // "window ended" alert instead and the user presses R to refresh.
    let clockTimer = new Timer(Interval = 60_000)
    clockTimer.Tick.Add(fun _ ->
        if not refreshing then
            match lastSnapshot with
            | Some snap -> setLines (renderFor snap)
            | None -> ())
    clockTimer.Start()

    let exitApp () =
        tray.Visible <- false
        tray.Dispose()
        Application.Exit()

    showItem.Click.Add(fun _ -> showWindow ())
    refreshItem.Click.Add(fun _ -> refresh ())
    exitItem.Click.Add(fun _ -> exitApp ())
    tray.DoubleClick.Add(fun _ -> showWindow ())

    form.FormClosing.Add(fun e ->
        if e.CloseReason = CloseReason.UserClosing then
            e.Cancel <- true
            hideWindow ())

    let openInBrowser () =
        let psi = System.Diagnostics.ProcessStartInfo(Scrape.defaults.TargetUrl)
        psi.UseShellExecute <- true
        try System.Diagnostics.Process.Start psi |> ignore with _ -> ()

    form.KeyDown.Add(fun e ->
        match e.KeyCode with
        | Keys.R ->
            refresh ()
            e.SuppressKeyPress <- true
        | Keys.W ->
            openInBrowser ()
            e.SuppressKeyPress <- true
        | Keys.Q ->
            exitApp ()
            e.SuppressKeyPress <- true
        | Keys.Escape ->
            hideWindow ()
            e.SuppressKeyPress <- true
        | Keys.I ->
            viewMode <- (match viewMode with Panel.Normal -> Panel.Info | Panel.Info -> Panel.Normal)
            rerender ()
            e.SuppressKeyPress <- true
        | Keys.D5 | Keys.NumPad5 ->
            scheduleAutoRefreshIn (5 * 60 * 1000)
            rerender ()
            e.SuppressKeyPress <- true
        | Keys.T ->
            topMostItem.Checked <- not topMostItem.Checked
            e.SuppressKeyPress <- true
        | _ -> ())

    form.Shown.Add(fun _ -> refresh ())

    Application.Run(form)
