# F# Lessons Learned — Claude Usage Checker

## Thread safety: module-level mutables are not safe

`let mutable` at module scope is just a global variable. Two threads writing/reading it
concurrently have no synchronization. Fix: wrap every access in `lock lockObj (fun () -> ...)`.
Capture what you need inside the lock, do I/O outside it.

```fsharp
let private lockObj = obj ()
let mutable private active = false
let mutable private sink : string -> unit = ignore

// wrong
let stop () = active <- false

// right
let stop () =
    let toStop = lock lockObj (fun () ->
        if not active then None
        else
            active <- false
            let l = listener
            listener <- null
            Some l)
    match toStop with
    | None -> ()
    | Some l -> try l.Stop() with _ -> ()
```

## Anonymous records don't support mutable fields

`{| mutable x = 0 |}` is a compile error. If you need shared mutable state, use module-level
`let mutable` bindings — not anonymous records, not regular records (records are immutable
by default).

## `try...finally` doesn't catch — it only guarantees cleanup

`try ... finally cleanup()` runs `cleanup()` whether the body succeeded or threw, but the
exception still propagates. For error *handling* you need a separate `try ... with ex -> ...`.
To both handle and guarantee cleanup, nest them:

```fsharp
try
    try
        doWork ()
    with ex ->
        Failed (sprintf "error: %s" ex.Message)
finally
    cleanup ()
```

## `with _ -> ()` silently swallows everything — use it only at hard boundaries

The HTTP listener's request handler catches all exceptions so one bad request doesn't kill the
loop. But when invoking a callback inside that handler, nest a separate `try ... with _ -> ()`
around just the callback — not the entire handler. Blanket catches are fine at the outermost
boundary, wrong everywhere else.

## `Task.Run` exceptions disappear unless you handle them

An exception thrown inside `Task.Run(fun () -> ...)` is swallowed if the returned `Task` is
ignored (fire-and-forget style). In the tray app, if `Scrape.run` throws, the UI freezes
silently. Either catch inside the lambda or add a `.ContinueWith` on failure.

```fsharp
// wrong — exception vanishes
Task.Run(fun () -> doSomethingThatMayThrow ()) |> ignore

// right
Task.Run(fun () ->
    try
        doSomethingThatMayThrow ()
    with ex ->
        ui (fun () -> showError ex.Message)) |> ignore
```

## The sandwich model actually works

Pure input validation → thin effectful middle → pure output.
`Panel.fs` is entirely pure functions: no I/O, no mutable state, just `Snapshot → Line list`.
That made it trivial to reason about and audit. The effectful shell (`Scrape.fs`, `Tray.fs`)
stays thin. When bugs appeared, they were all in the effectful shell — never in the pure core.

## `Option` chains beat null checks

```fsharp
b.Subtitle
|> Option.bind parseWeeklyReset
|> Option.map (fun (d, _, _) -> d)
|> Option.defaultValue DayOfWeek.Monday
```

Reads as a pipeline: "try this, then try that, else use default." No null guards, no
`if x <> null` noise. Each step is independently testable.

## `ResizeArray` → `List.ofSeq` for building output

Accumulating into `ResizeArray<Line>` with `.Add()` inside loops, then sealing with
`List.ofSeq buf` at the end, gives O(1) appends during building and an immutable list as the
result. Cleaner than `List.append` in a loop, which is O(n²).

```fsharp
let render (snap: Snapshot) : Line list =
    let buf = ResizeArray<Line>()
    let add color text = buf.Add { Text = text; Color = color }
    // ... fill buf ...
    List.ofSeq buf
```

## `use` is your dispose — never forget it for GDI resources

`Graphics`, `Font`, `Bitmap`, `HttpListener` are all `IDisposable`. `use g = Graphics.FromHwnd(...)`
disposes automatically at scope exit. A bare `let g = ...` leaks GDI handles until GC, which
on a long-running tray app accumulates into a real leak.

## Mutate state before re-rendering, not after

If a callback both changes state the view depends on and triggers a redraw, the
redraw call has to come last. In `Tray.refresh`, the completion handler originally
did `rerender()` then `scheduleNextAutoRefresh()` — so the panel was painted with
the old `nextRefreshAt` (None on first run), and the new value only became visible
when an unrelated timer (the 60-second clock tick) happened to redraw. Symptom:
"next refresh in N min" label appeared seconds-to-a-minute after the first scrape
completed, not immediately.

```fsharp
// wrong — panel draws with stale nextRefreshAt
refreshing <- false
rerender ()
scheduleNextAutoRefresh ()

// right — set state first, then redraw
refreshing <- false
scheduleNextAutoRefresh ()
rerender ()
```

Rule: any mutation that changes what `render` would produce has to happen *before*
the `rerender` call in the same event, not after. Don't rely on the next timer
tick to pick up a change you made inline.

## F# string escapes are stored as literal characters in source files

`"█"` in F# source is 6 bytes on disk — backslash, `u`, `2`, `5`, `8`, `8` —
that the compiler resolves to U+2588 (█) at build time. This matters whenever
you try to manipulate source files with a tool that thinks `█` means the
glyph itself. The Edit tool transports `old_string` as JSON and JSON *also*
resolves `\uXXXX` escapes, so an Edit call that includes `"█"` in its
search string actually searches for the rendered █ — which isn't in the file.
Every edit to a line with `\u…` escapes fails with "String to replace not found."

Workaround: replace the escapes with an ASCII placeholder like `ZZZ_U_2588_`
across the file, make the edits, then reverse the substitution. `harness/_stub.py`
in this repo does exactly that in both directions with a simple regex pass.
Alternative: reach for sed/awk/python on the raw bytes directly — they see the
six literal characters and can match them without JSON mangling.

## DPI: measure from the screen DC, not a Bitmap

`Graphics.FromImage(new Bitmap(1, 1))` always returns a 96-DPI context. On a 150% or 200%
DPI monitor running `PerMonitorV2`, this gives the wrong cell size and misaligns the grid.
Use `Graphics.FromHwnd(IntPtr.Zero)` to get the actual screen DC with the correct DPI for
any pre-show measurements.

```fsharp
// wrong — always 96 DPI
let private measureCell (font: Font) : Size =
    use bmp = new Bitmap(1, 1)
    use g = Graphics.FromImage(bmp)
    TextRenderer.MeasureText(g, "\u2588", font, Size(10000, 10000), TextFormatFlags.NoPadding)

// right — matches actual display DPI
let private measureCell (font: Font) : Size =
    use g = Graphics.FromHwnd(IntPtr.Zero)
    TextRenderer.MeasureText(g, "\u2588", font, Size(10000, 10000), TextFormatFlags.NoPadding)
```
