module Flan.NpmDeps

open System.IO
open System.Text.Json
open Flan.ProjectAnalyzer

type NpmDep = {
    Name: string
    Version: string
    Source: string  // Which NuGet package required this (e.g., "Fable.DateFns@2.1.0")
    IsDev: bool
}

/// Find package.json in a NuGet package directory
/// Checks multiple possible locations where it might be shipped
let findPackageJson (packagesDir: string) (pkg: PackageRef) : string option =
    let packageDir = Path.Combine(packagesDir, pkg.Name.ToLowerInvariant(), pkg.Version)
    
    if not (Directory.Exists packageDir) then
        None
    else
        // Check possible locations in order of likelihood
        let possiblePaths = [
            Path.Combine(packageDir, "package.json")
            Path.Combine(packageDir, "content", "package.json")
            Path.Combine(packageDir, "contentFiles", "any", "any", "package.json")
        ]
        
        possiblePaths |> List.tryFind File.Exists

/// Parse a package.json string and extract dependencies
let parsePackageJsonContent (source: string) (json: string) : Result<NpmDep list, string> =
    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement
        
        let getDeps (propName: string) (isDev: bool) =
            match root.TryGetProperty(propName) with
            | true, deps when deps.ValueKind = JsonValueKind.Object ->
                deps.EnumerateObject()
                |> Seq.map (fun prop -> 
                    { Name = prop.Name
                      Version = prop.Value.GetString()
                      Source = source
                      IsDev = isDev })
                |> Seq.toList
            | _ -> []
        
        let dependencies = getDeps "dependencies" false
        let devDependencies = getDeps "devDependencies" true
        
        Ok (dependencies @ devDependencies)
    with
    | ex -> Error $"Failed to parse package.json from {source}: {ex.Message}"

/// Read and parse package.json from a NuGet package
let getPackageDeps (packagesDir: string) (pkg: PackageRef) : NpmDep list =
    let source = $"{pkg.Name}@{pkg.Version}"
    
    match findPackageJson packagesDir pkg with
    | None -> []
    | Some path ->
        try
            let json = File.ReadAllText(path)
            match parsePackageJsonContent source json with
            | Ok deps -> deps
            | Error msg ->
                eprintfn $"Warning: %s{msg}"
                []
        with
        | ex ->
            eprintfn $"Warning: Failed to read %s{path}: %s{ex.Message}"
            []

/// Collect all npm dependencies from a list of NuGet packages
let collectAllDeps (packagesDir: string) (packages: PackageRef list) : NpmDep list =
    packages
    |> List.collect (getPackageDeps packagesDir)
