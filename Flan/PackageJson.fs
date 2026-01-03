module Flan.PackageJson

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Flan.NpmDeps

type MergeWarning = {
    Package: string
    Sources: (string * string) list  // (NuGet package source, version requested)
    UsedVersion: string
}

/// Read package.json from disk, or return empty JsonObject if not found
let read (path: string) : JsonObject =
    if File.Exists(path) then
        try
            let json = File.ReadAllText(path)
            JsonNode.Parse(json).AsObject()
        with
        | _ -> JsonObject()
    else
        JsonObject()

/// Get or create a dependencies object in package.json
let private getOrCreateDepsObject (pkg: JsonObject) (propName: string) : JsonObject =
    let mutable node: JsonNode = null
    if pkg.TryGetPropertyValue(propName, &node) && node <> null then
        node.AsObject()
    else
        let deps = JsonObject()
        pkg[propName] <- deps
        deps

/// Merge npm dependencies into a package.json, detecting conflicts
let merge (existing: JsonObject) (deps: NpmDep list) : JsonObject * MergeWarning list =
    // Group deps by name to detect conflicts
    let grouped = 
        deps 
        |> List.groupBy (fun d -> (d.Name, d.IsDev))
    
    let warnings = ResizeArray<MergeWarning>()
    
    for (name, isDev), depsForPackage in grouped do
        let targetProp = if isDev then "devDependencies" else "dependencies"
        let targetObj = getOrCreateDepsObject existing targetProp
        
        // Check for version conflicts - get unique (source, version) pairs
        let versions = depsForPackage |> List.map (fun d -> (d.Source, d.Version)) |> List.distinct
        
        // Only a conflict if there are different VERSION values (not just different sources)
        let uniqueVersionValues = versions |> List.map snd |> List.distinct
        
        if uniqueVersionValues.Length > 1 then
            // Conflict detected - different versions requested
            let lastVersion = (depsForPackage |> List.last).Version
            warnings.Add({
                Package = name
                Sources = versions
                UsedVersion = lastVersion
            })
            targetObj[name] <- JsonValue.Create(lastVersion)
        else
            // No conflict, just add/update
            let version = (List.head depsForPackage).Version
            targetObj[name] <- JsonValue.Create(version)
    
    (existing, warnings |> Seq.toList)

/// Write package.json to disk with nice formatting
let write (path: string) (pkg: JsonObject) : unit =
    let options = JsonSerializerOptions(WriteIndented = true)
    let json = pkg.ToJsonString(options)
    File.WriteAllText(path, json + "\n")

/// Parse a package.json string into a JsonObject
let parse (json: string) : JsonObject =
    try
        JsonNode.Parse(json).AsObject()
    with
    | _ -> JsonObject()

/// Serialize a JsonObject to a formatted JSON string
let serialize (pkg: JsonObject) : string =
    let options = JsonSerializerOptions(WriteIndented = true)
    pkg.ToJsonString(options)
