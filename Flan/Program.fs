module Flan.Program

open System
open System.IO
open Argu
open Flan

type SyncArgs =
    | [<AltCommandLine("-p")>] Project of path: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Project _ -> "Path to .fsproj file (defaults to current directory)"

[<CliPrefix(CliPrefix.None)>]
type FlanArgs =
    | [<CliPrefix(CliPrefix.None)>] Sync of ParseResults<SyncArgs>
    | Version

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Sync _ -> "Sync npm dependencies from NuGet packages and run bun install"
            | Version -> "Show version information"

/// Print version conflict warnings
let printWarnings (warnings: PackageJson.MergeWarning list) =
    for w in warnings do
        eprintfn $"Warning: Conflicting versions for '%s{w.Package}':"
        for source, version in w.Sources do
            eprintfn $"  - %s{source} requires %s{version}"
        eprintfn $"  Using: %s{w.UsedVersion}"
        eprintfn ""

/// Run the sync command
let runSync (projectPath: string option) : int =
    // Check if bun is installed first
    if not (Bun.isInstalled()) then
        eprintfn "Error: bun is not installed or not found in PATH."
        eprintfn "Install bun: https://bun.com"
        1
    else
        // Find project
        let projectDir, fsprojPath =
            match projectPath with
            | Some p when File.Exists(p) -> 
                (Path.GetDirectoryName(Path.GetFullPath(p)), Path.GetFullPath(p))
            | Some p when Directory.Exists(p) ->
                match ProjectAnalyzer.findFsproj p with
                | Ok path -> (p, path)
                | Error msg ->
                    eprintfn $"Error: %s{msg}"
                    exit 1
            | Some p ->
                eprintfn $"Error: Path not found: %s{p}"
                exit 1
            | None ->
                let cwd = Environment.CurrentDirectory
                match ProjectAnalyzer.findFsproj cwd with
                | Ok path -> (cwd, path)
                | Error msg ->
                    eprintfn $"Error: %s{msg}"
                    exit 1

        printfn $"Found project: %s{Path.GetFileName fsprojPath}"

        // Get NuGet packages directory
        let packagesDir =
            match ProjectAnalyzer.getNugetPackagesDir() with
            | Ok dir -> dir
            | Error msg ->
                eprintfn $"Error: %s{msg}"
                exit 1
        
        // Get package references
        printfn "Scanning NuGet packages..."
        let packages =
            match ProjectAnalyzer.getPackageReferences fsprojPath with
            | Ok pkgs -> pkgs
            | Error msg ->
                eprintfn $"Error: %s{msg}"
                exit 1
        
        // Collect npm dependencies from all packages
        let npmDeps = NpmDeps.collectAllDeps packagesDir packages
        
        // Print what we found
        for pkg in packages do
            let deps = npmDeps |> List.filter (fun d -> d.Source = $"{pkg.Name}@{pkg.Version}")
            if deps.IsEmpty then
                printfn $"  - %s{pkg.Name}@%s{pkg.Version} -> (no npm deps)"
            else
                for dep in deps do
                    let devTag = if dep.IsDev then " (dev)" else ""
                    printfn $"  - %s{pkg.Name}@%s{pkg.Version} -> %s{dep.Name} %s{dep.Version}%s{devTag}"

        if npmDeps.IsEmpty then
            printfn "No npm dependencies found."
            0
        else
            // Read/create package.json
            let packageJsonPath = Path.Combine(projectDir, "package.json")
            let existingPkg = PackageJson.read packageJsonPath
            
            // Merge dependencies
            let mergedPkg, warnings = PackageJson.merge existingPkg npmDeps
            
            // Print warnings
            if not warnings.IsEmpty then
                printfn ""
                printWarnings warnings
            
            // Write package.json
            printfn "Updating package.json..."
            PackageJson.write packageJsonPath mergedPkg
            
            // Run bun install
            printfn "Running bun install..."
            let exitCode = Bun.install projectDir
            
            if exitCode = 0 then
                printfn "Done!"
            
            exitCode

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<FlanArgs>(programName = "flan")
    
    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
        
        if results.Contains Version then
            printfn "flan v0.1.0"
            0
        else
            match results.TryGetSubCommand() with
            | Some (Sync syncResults) ->
                let projectPath = syncResults.TryGetResult SyncArgs.Project
                runSync projectPath
            | _ ->
                printfn $"%s{parser.PrintUsage()}"
                0
    with
    | :? ArguParseException as e ->
        eprintfn $"%s{e.Message}"
        1
    | ex ->
        eprintfn $"Error: %s{ex.Message}"
        1
