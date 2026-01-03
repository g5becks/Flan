module Flan.ProjectAnalyzer

open System
open System.IO
open System.Text.RegularExpressions
open Fli

type PackageRef = {
    Name: string
    Version: string
}

/// Find .fsproj file in directory. Returns error if none or multiple found.
let findFsproj (dir: string) : Result<string, string> =
    let fsprojFiles = Directory.GetFiles(dir, "*.fsproj")
    match fsprojFiles.Length with
    | 0 -> Error $"No .fsproj file found in {dir}"
    | 1 -> Ok fsprojFiles[0]
    | _ -> 
        let names = fsprojFiles |> Array.map Path.GetFileName |> String.concat ", "
        Error $"Multiple .fsproj files found in {dir}: {names}. Use -p to specify which one."

/// Get NuGet global packages directory
let getNugetPackagesDir () : Result<string, string> =
    try
        let result = 
            cli {
                Exec "dotnet"
                Arguments "nuget locals global-packages --list"
            }
            |> Command.execute
        
        let output = result |> Output.toText
        let exitCode = result |> Output.toExitCode
        
        if exitCode <> 0 then
            Error $"dotnet nuget locals failed with exit code {exitCode}"
        else
            // Output format: "global-packages: /path/to/packages"
            let parts = output.Split(':', StringSplitOptions.TrimEntries)
            if parts.Length >= 2 then
                Ok parts[1]
            else
                Error $"Unexpected output format from dotnet nuget locals: {output}"
    with
    | ex -> Error $"Failed to get NuGet packages directory: {ex.Message}"

/// Parse the output of `dotnet list package --include-transitive`
let parsePackageListOutput (output: string) : PackageRef list =
    // Match lines like:
    //    > Fable.Core           4.0.0       4.0.0     (top-level: requested resolved)
    //    > FSharp.Core                      8.0.0     (transitive: just resolved)
    //    > FSharp.Core          (A)         8.0.0     (auto-referenced with marker)
    // Pattern: > PackageName [optional stuff] Version [optional trailing]
    let packagePattern = Regex(@"^\s*>\s+(\S+)\s+.*?(\d+\.\S*)\s*$", RegexOptions.Multiline)
    
    output.Split('\n')
    |> Array.choose (fun line ->
        let m = packagePattern.Match(line)
        if m.Success then
            Some { Name = m.Groups[1].Value; Version = m.Groups[2].Value }
        else
            None
    )
    |> Array.toList

/// Get all package references from a project (including transitive)
let getPackageReferences (fsprojPath: string) : Result<PackageRef list, string> =
    try
        let result =
            cli {
                Exec "dotnet"
                Arguments $"list \"{fsprojPath}\" package --include-transitive"
            }
            |> Command.execute
        
        let output = result |> Output.toText
        let exitCode = result |> Output.toExitCode
        
        if exitCode <> 0 then
            Error $"dotnet list package failed with exit code {exitCode}: {output}"
        else
            Ok (parsePackageListOutput output)
    with
    | ex -> Error $"Failed to get package references: {ex.Message}"
