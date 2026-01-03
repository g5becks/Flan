module Flan.Bun

open Fli

/// Check if bun is installed and available on PATH
let isInstalled () : bool =
    try
        cli {
            Exec "bun"
            Arguments "--version"
        }
        |> Command.execute
        |> Output.toExitCode
        |> (=) 0
    with
    | _ -> false

/// Run bun install in the specified directory
let install (workingDir: string) : int =
    cli {
        Exec "bun"
        Arguments "install"
        WorkingDirectory workingDir
    }
    |> Command.execute
    |> Output.toExitCode
