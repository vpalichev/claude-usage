module Harness.Domain

open System

/// One usage row on the Claude settings/usage page.
///   Label:    e.g. "Current session", "All models", "Daily included routine runs"
///   Subtitle: e.g. "Resets in 4 hr 46 min", "You haven't used Sonnet yet"
///   Percent:  aria-valuenow on the <div role="progressbar">
///   Caption:  the right-hand text, e.g. "3% used" or "0 / 15"
type UsageBar = {
    Label: string
    Subtitle: string option
    Percent: int
    Caption: string option
}

type Snapshot = {
    SourceFile: string
    CapturedAt: DateTimeOffset option
    Plan: string option
    Bars: UsageBar list
}
