"""Stub/restore Unicode escape sequences in F# source files.

WHY THIS EXISTS
    F# source stores string literals like "█" as six literal ASCII
    characters on disk (the F# compiler resolves them at build time).
    When Claude Code's Edit tool is used, its `old_string` parameter is
    transported as JSON, which *also* resolves \uXXXX escapes — so the
    tool ends up searching for the rendered glyph (█), which isn't in
    the file. Every Edit that touches a line containing \uXXXX fails
    with "String to replace not found."

    This helper swaps every \uXXXX on disk for an ASCII placeholder
    (ZZZ_U_XXXX_) so normal Edit-tool calls can match and modify those
    lines, then swaps the placeholders back after the edits.

USAGE
    python _stub.py stub     Panel.fs      # before editing
    python _stub.py restore  Panel.fs      # after editing

    If the filename argument is omitted it defaults to Panel.fs next
    to this script.

SEE ALSO
    LESSONS_LEARNED.md — "F# string escapes are stored as literal
    characters in source files."
"""

import os, re, sys

def main():
    if len(sys.argv) < 2:
        sys.exit("usage: _stub.py {stub|restore} [file]")
    mode = sys.argv[1]
    default = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Panel.fs")
    path = sys.argv[2] if len(sys.argv) >= 3 else default

    src = open(path, encoding="utf-8").read()
    if mode == "stub":
        out = re.sub(r"\\u([0-9A-Fa-f]{4})",
                     lambda m: f"ZZZ_U_{m.group(1).upper()}_", src)
    elif mode == "restore":
        out = re.sub(r"ZZZ_U_([0-9A-F]{4})_",
                     lambda m: f"\\u{m.group(1)}", src)
    else:
        sys.exit("mode must be 'stub' or 'restore'")

    open(path, "w", encoding="utf-8", newline="\n").write(out)
    print(f"{mode} ok: {path}")

if __name__ == "__main__":
    main()
