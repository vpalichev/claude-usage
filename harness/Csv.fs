module Harness.Csv

/// A cell value with its type distinguished so numbers stay bare (no quotes)
/// while strings are always quoted — keeps Excel/locale-sensitive tools from
/// mis-interpreting digits or delimiters.
type Cell =
    | Text of string
    | Num of int
    | Empty

let private delimiter = ";"

let private quoteString (s: string) : string =
    "\"" + s.Replace("\"", "\"\"") + "\""

let private formatCell = function
    | Text s -> quoteString s
    | Num n -> string n
    | Empty -> ""

let row (cells: Cell list) : string =
    cells |> List.map formatCell |> String.concat delimiter

let private headerNames =
    [ "captured_at"; "source_file"; "plan"; "label"; "subtitle"; "percent"; "caption" ]

let header : string =
    headerNames |> List.map Text |> row
