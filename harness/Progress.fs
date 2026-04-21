module Harness.Progress

open System
open System.Net
open System.Threading

/// Pick a random port in the dynamic/ephemeral range (60000–65535) and bind a
/// loopback HttpListener to it. The extension POSTs /progress/<stage> here so
/// the orchestrator can print scrape stages in real time.
///
/// We randomise the port instead of hard-coding so collisions are unlikely
/// and nothing else on the machine can pre-squat our endpoint.

let private rangeLow = 60000
let private rangeHigh = 65535
let private maxAttempts = 20

let private rng = Random()
let private lockObj = obj ()

let mutable private listener : HttpListener = null
let mutable private active = false
let mutable private sink : string -> unit = (fun s -> eprintfn "  \u2022 %s" s)

let private handleRequests (l: HttpListener) =
    // Check both active flag and that this thread's listener is still the current one,
    // so a stop→startWith cycle doesn't leave this thread spinning on a dead listener.
    let stillMine () = lock lockObj (fun () -> active && Object.ReferenceEquals(listener, l))
    while stillMine () do
        try
            let ctx = l.GetContext()
            let path = ctx.Request.Url.AbsolutePath.TrimStart '/'
            let stage =
                if path.StartsWith "progress/" then path.Substring 9 else path
                |> Uri.UnescapeDataString
            if stage <> "" then
                let cb = lock lockObj (fun () -> if stillMine () then Some sink else None)
                match cb with
                | Some cb -> try cb stage with _ -> ()
                | None -> ()
            ctx.Response.StatusCode <- 204
            ctx.Response.Close()
        with _ ->
            ()

let private tryBind () : (HttpListener * int) option =
    let rec loop attempts =
        if attempts >= maxAttempts then None
        else
            let port = rng.Next(rangeLow, rangeHigh + 1)
            let listener = new HttpListener()
            listener.Prefixes.Add (sprintf "http://localhost:%d/" port)
            try
                listener.Start()
                Some (listener, port)
            with _ ->
                try listener.Close() with _ -> ()
                loop (attempts + 1)
    loop 0

/// Returns the bound port if successful, or None if we couldn't secure one
/// (progress reporting is skipped silently in that case).
let startWith (onStage: string -> unit) : int option =
    lock lockObj (fun () ->
        if active then None
        else
            match tryBind () with
            | None ->
                onStage "(progress listener unavailable)"
                None
            | Some (l, port) ->
                listener <- l
                active <- true
                sink <- onStage
                let t = Thread(ThreadStart(fun () -> handleRequests l))
                t.IsBackground <- true
                t.Start()
                Some port)

let start () : int option =
    startWith (fun s -> eprintfn "  \u2022 %s" s)

let stop () =
    let listenerToStop =
        lock lockObj (fun () ->
            if not active then None
            else
                active <- false
                let l = listener
                listener <- null
                Some l)
    match listenerToStop with
    | None -> ()
    | Some l ->
        try l.Stop() with _ -> ()
        try l.Close() with _ -> ()
