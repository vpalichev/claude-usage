module Harness.Tests.ParserTests

open System
open System.IO
open Xunit
open Harness.Domain
open Harness.Parser

let private fixturePath =
    Path.Combine(AppContext.BaseDirectory, "fixtures", "settings-usage.html")

let private parseFixture () : Snapshot =
    let html = File.ReadAllText fixturePath
    parseSnapshot fixturePath None html

let private knownLabels =
    [ "Current session"; "All models"; "Sonnet only"; "Claude Design" ]

[<Fact>]
let ``parser finds all four known bars`` () =
    let snap = parseFixture ()
    let labels = snap.Bars |> List.map (fun b -> b.Label) |> Set.ofList
    for known in knownLabels do
        Assert.True(
            Set.contains known labels,
            sprintf "Expected bar with label '%s'. Got: %A" known (Set.toList labels))

[<Fact>]
let ``every parsed bar has a non-empty label`` () =
    let snap = parseFixture ()
    Assert.NotEmpty snap.Bars
    for bar in snap.Bars do
        Assert.False(
            String.IsNullOrWhiteSpace bar.Label,
            sprintf "Bar with percent=%d has empty label — selectors are stale" bar.Percent)

[<Fact>]
let ``current session has a parseable Resets-in subtitle`` () =
    let snap = parseFixture ()
    let session = snap.Bars |> List.find (fun b -> b.Label = "Current session")
    Assert.True(session.Subtitle.IsSome, "session subtitle missing")
    Assert.StartsWith("Resets in ", session.Subtitle.Value)

[<Fact>]
let ``percent values are within 0..100`` () =
    let snap = parseFixture ()
    for bar in snap.Bars do
        Assert.InRange(bar.Percent, 0, 100)

[<Fact>]
let ``plan name is extracted from the Plan usage limits heading`` () =
    let snap = parseFixture ()
    Assert.True(snap.Plan.IsSome, "Plan name was not extracted")
    let plan = snap.Plan.Value
    Assert.False(String.IsNullOrWhiteSpace plan, "Plan name is blank")
    Assert.DoesNotContain("Plan usage limits", plan)

[<Fact>]
let ``known bars have either a percent caption or a counter caption`` () =
    let snap = parseFixture ()
    for bar in snap.Bars |> List.filter (fun b -> List.contains b.Label knownLabels) do
        let cap = bar.Caption |> Option.defaultValue ""
        let isPercent = cap.EndsWith("used")
        let isCounter = cap.Contains(" / ")
        Assert.True(
            isPercent || isCounter,
            sprintf "Bar '%s' has caption %A — neither 'N%% used' nor 'M / N'" bar.Label bar.Caption)
