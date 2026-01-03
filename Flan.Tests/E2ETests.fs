module Flan.Tests.E2ETests

open System
open System.IO
open System.Text.Json.Nodes
open Xunit
open FsUnit.Light
open Flan.NpmDeps
open Flan.PackageJson
open Flan.ProjectAnalyzer

// ============================================================================
// Test fixtures - realistic package.json files from NuGet packages
// ============================================================================

module Fixtures =
    /// Fable.SolidJs - SolidJS bindings
    let fableSolidJs = """{
  "name": "fable-solid-js",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "solid-js": "^1.8.0"
  }
}"""

    /// Fable.SolidJs.Router - SolidJS Router bindings
    let fableSolidJsRouter = """{
  "name": "fable-solid-js-router",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "@solidjs/router": "^0.14.0",
    "solid-js": "^1.8.0"
  }
}"""

    /// Fable.DateFns - date-fns bindings
    let fableDateFns = """{
  "name": "fable-date-fns",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "date-fns": "^3.6.0"
  }
}"""

    /// Fable.Promise - Promise bindings (no npm deps)
    let fablePromise = """{
  "name": "fable-promise",
  "version": "0.0.0",
  "private": true
}"""

    /// Fable.Fetch - Fetch API bindings (no npm deps, empty deps object)
    let fableFetch = """{
  "name": "fable-fetch",
  "version": "0.0.0",
  "private": true,
  "dependencies": {}
}"""

    /// Fable.Remoting.Client - includes dev dependencies
    let fableRemotingClient = """{
  "name": "fable-remoting-client",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "cross-fetch": "^4.0.0"
  },
  "devDependencies": {
    "@types/node": "^20.0.0"
  }
}"""

    /// Fable.Mocha - test framework bindings
    let fableMocha = """{
  "name": "fable-mocha",
  "version": "0.0.0",
  "private": true,
  "devDependencies": {
    "mocha": "^10.0.0",
    "@types/mocha": "^10.0.0"
  }
}"""

    /// Conflicting package A - wants older solid-js
    let conflictingPackageA = """{
  "name": "conflicting-a",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "solid-js": "^1.7.0"
  }
}"""

    /// Conflicting package B - wants newer solid-js
    let conflictingPackageB = """{
  "name": "conflicting-b",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "solid-js": "^1.9.0"
  }
}"""

    /// Package with scoped dependencies
    let scopedDeps = """{
  "name": "scoped-pkg",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "@tanstack/solid-query": "^5.0.0",
    "@solid-primitives/storage": "^3.0.0"
  },
  "devDependencies": {
    "@types/node": "^22.0.0"
  }
}"""

    /// Package with various version formats
    let versionFormats = """{
  "name": "version-formats",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "caret-version": "^1.2.3",
    "tilde-version": "~1.2.3",
    "exact-version": "1.2.3",
    "range-version": ">=1.0.0 <2.0.0",
    "star-version": "*",
    "latest-version": "latest",
    "git-version": "github:user/repo#v1.0.0",
    "url-version": "https://example.com/pkg.tgz"
  }
}"""

    /// Package with many dependencies (stress test)
    let manyDeps = 
        let deps = 
            [1..50] 
            |> List.map (fun i -> $"    \"pkg-{i}\": \"^{i}.0.0\"")
            |> String.concat ",\n"
        $"""{{
  "name": "many-deps",
  "version": "0.0.0",
  "private": true,
  "dependencies": {{
{deps}
  }}
}}"""

    /// Malformed JSON
    let malformedJson = """{ this is not valid json }"""

    /// Empty JSON object
    let emptyJson = """{}"""


// ============================================================================
// Helper to set up fake NuGet packages directory
// ============================================================================

let setupFakePackagesDir (packages: (string * string * string) list) : string =
    let tempDir = Path.Combine(Path.GetTempPath(), $"flan-e2e-{Guid.NewGuid()}")
    Directory.CreateDirectory(tempDir) |> ignore
    
    for (packageName, version, packageJsonContent) in packages do
        // NuGet stores packages as lowercase
        let packageDir = Path.Combine(tempDir, packageName.ToLowerInvariant(), version)
        Directory.CreateDirectory(packageDir) |> ignore
        File.WriteAllText(Path.Combine(packageDir, "package.json"), packageJsonContent)
    
    tempDir

let cleanupDir (dir: string) =
    if Directory.Exists(dir) then
        Directory.Delete(dir, true)


// ============================================================================
// E2E Tests: Full workflow from NuGet packages to merged package.json
// ============================================================================

[<Fact>]
let ``E2E: single package with one dependency`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.DateFns", "3.0.0", Fixtures.fableDateFns)
    ]
    
    try
        let packages = [ { Name = "Fable.DateFns"; Version = "3.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        npmDeps |> shouldHaveLength 1
        npmDeps.[0].Name |> shouldEqual "date-fns"
        npmDeps.[0].Version |> shouldEqual "^3.6.0"
        npmDeps.[0].Source |> shouldEqual "Fable.DateFns@3.0.0"
        npmDeps.[0].IsDev |> shouldEqual false
        
        (result.["dependencies"].["date-fns"]).GetValue<string>() |> shouldEqual "^3.6.0"
        warnings |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: package with multiple dependencies`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.SolidJs.Router", "1.0.0", Fixtures.fableSolidJsRouter)
    ]
    
    try
        let packages = [ { Name = "Fable.SolidJs.Router"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        npmDeps |> shouldHaveLength 2
        
        let solidDep = npmDeps |> List.find (fun d -> d.Name = "solid-js")
        let routerDep = npmDeps |> List.find (fun d -> d.Name = "@solidjs/router")
        
        solidDep.Version |> shouldEqual "^1.8.0"
        routerDep.Version |> shouldEqual "^0.14.0"
        
        (result.["dependencies"].["solid-js"]).GetValue<string>() |> shouldEqual "^1.8.0"
        (result.["dependencies"].["@solidjs/router"]).GetValue<string>() |> shouldEqual "^0.14.0"
        warnings |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: multiple packages same dependency no conflict`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.SolidJs", "1.0.0", Fixtures.fableSolidJs)
        ("Fable.SolidJs.Router", "1.0.0", Fixtures.fableSolidJsRouter)
    ]
    
    try
        let packages = [
            { Name = "Fable.SolidJs"; Version = "1.0.0" }
            { Name = "Fable.SolidJs.Router"; Version = "1.0.0" }
        ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        // solid-js appears twice but same version - no conflict
        let solidDeps = npmDeps |> List.filter (fun d -> d.Name = "solid-js")
        solidDeps |> shouldHaveLength 2
        solidDeps |> List.forall (fun d -> d.Version = "^1.8.0") |> shouldEqual true
        
        // Result should have both deps, no warnings
        (result.["dependencies"].["solid-js"]).GetValue<string>() |> shouldEqual "^1.8.0"
        (result.["dependencies"].["@solidjs/router"]).GetValue<string>() |> shouldEqual "^0.14.0"
        warnings |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: multiple packages same dependency WITH conflict`` () =
    let packagesDir = setupFakePackagesDir [
        ("Conflicting.A", "1.0.0", Fixtures.conflictingPackageA)
        ("Conflicting.B", "1.0.0", Fixtures.conflictingPackageB)
    ]
    
    try
        let packages = [
            { Name = "Conflicting.A"; Version = "1.0.0" }
            { Name = "Conflicting.B"; Version = "1.0.0" }
        ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        // Should have conflict warning
        warnings |> shouldHaveLength 1
        warnings.[0].Package |> shouldEqual "solid-js"
        warnings.[0].Sources |> shouldHaveLength 2
        warnings.[0].UsedVersion |> shouldEqual "^1.9.0"  // last wins
        
        // Result uses last version
        (result.["dependencies"].["solid-js"]).GetValue<string>() |> shouldEqual "^1.9.0"
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: package with dev dependencies`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.Remoting.Client", "1.0.0", Fixtures.fableRemotingClient)
    ]
    
    try
        let packages = [ { Name = "Fable.Remoting.Client"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        npmDeps |> shouldHaveLength 2
        
        let regularDep = npmDeps |> List.find (fun d -> d.Name = "cross-fetch")
        let devDep = npmDeps |> List.find (fun d -> d.Name = "@types/node")
        
        regularDep.IsDev |> shouldEqual false
        devDep.IsDev |> shouldEqual true
        
        (result.["dependencies"].["cross-fetch"]).GetValue<string>() |> shouldEqual "^4.0.0"
        (result.["devDependencies"].["@types/node"]).GetValue<string>() |> shouldEqual "^20.0.0"
        warnings |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: only dev dependencies`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.Mocha", "1.0.0", Fixtures.fableMocha)
    ]
    
    try
        let packages = [ { Name = "Fable.Mocha"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        npmDeps |> shouldHaveLength 2
        npmDeps |> List.forall (fun d -> d.IsDev) |> shouldEqual true
        
        result.ContainsKey("dependencies") |> shouldEqual false
        (result.["devDependencies"].["mocha"]).GetValue<string>() |> shouldEqual "^10.0.0"
        (result.["devDependencies"].["@types/mocha"]).GetValue<string>() |> shouldEqual "^10.0.0"
        warnings |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: scoped package dependencies`` () =
    let packagesDir = setupFakePackagesDir [
        ("Scoped.Pkg", "1.0.0", Fixtures.scopedDeps)
    ]
    
    try
        let packages = [ { Name = "Scoped.Pkg"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        npmDeps |> shouldHaveLength 3
        
        (result.["dependencies"].["@tanstack/solid-query"]).GetValue<string>() |> shouldEqual "^5.0.0"
        (result.["dependencies"].["@solid-primitives/storage"]).GetValue<string>() |> shouldEqual "^3.0.0"
        (result.["devDependencies"].["@types/node"]).GetValue<string>() |> shouldEqual "^22.0.0"
        warnings |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: various npm version formats`` () =
    let packagesDir = setupFakePackagesDir [
        ("Version.Formats", "1.0.0", Fixtures.versionFormats)
    ]
    
    try
        let packages = [ { Name = "Version.Formats"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        npmDeps |> shouldHaveLength 8
        
        (result.["dependencies"].["caret-version"]).GetValue<string>() |> shouldEqual "^1.2.3"
        (result.["dependencies"].["tilde-version"]).GetValue<string>() |> shouldEqual "~1.2.3"
        (result.["dependencies"].["exact-version"]).GetValue<string>() |> shouldEqual "1.2.3"
        (result.["dependencies"].["range-version"]).GetValue<string>() |> shouldEqual ">=1.0.0 <2.0.0"
        (result.["dependencies"].["star-version"]).GetValue<string>() |> shouldEqual "*"
        (result.["dependencies"].["latest-version"]).GetValue<string>() |> shouldEqual "latest"
        (result.["dependencies"].["git-version"]).GetValue<string>() |> shouldEqual "github:user/repo#v1.0.0"
        (result.["dependencies"].["url-version"]).GetValue<string>() |> shouldEqual "https://example.com/pkg.tgz"
        warnings |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: package with no npm dependencies`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.Promise", "1.0.0", Fixtures.fablePromise)
    ]
    
    try
        let packages = [ { Name = "Fable.Promise"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        
        npmDeps |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: package with empty dependencies object`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.Fetch", "1.0.0", Fixtures.fableFetch)
    ]
    
    try
        let packages = [ { Name = "Fable.Fetch"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        
        npmDeps |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: package not found in cache`` () =
    let packagesDir = setupFakePackagesDir []  // Empty directory
    
    try
        let packages = [ { Name = "NonExistent.Package"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        
        npmDeps |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: malformed package.json is skipped`` () =
    let packagesDir = setupFakePackagesDir [
        ("Malformed.Package", "1.0.0", Fixtures.malformedJson)
        ("Fable.DateFns", "3.0.0", Fixtures.fableDateFns)  // Valid one
    ]
    
    try
        let packages = [
            { Name = "Malformed.Package"; Version = "1.0.0" }
            { Name = "Fable.DateFns"; Version = "3.0.0" }
        ]
        let npmDeps = collectAllDeps packagesDir packages
        
        // Should only get deps from valid package
        npmDeps |> shouldHaveLength 1
        npmDeps.[0].Name |> shouldEqual "date-fns"
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: empty package.json object`` () =
    let packagesDir = setupFakePackagesDir [
        ("Empty.Package", "1.0.0", Fixtures.emptyJson)
    ]
    
    try
        let packages = [ { Name = "Empty.Package"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        
        npmDeps |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: many dependencies stress test`` () =
    let packagesDir = setupFakePackagesDir [
        ("Many.Deps", "1.0.0", Fixtures.manyDeps)
    ]
    
    try
        let packages = [ { Name = "Many.Deps"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        npmDeps |> shouldHaveLength 50
        result.["dependencies"].AsObject().Count |> shouldEqual 50
        warnings |> shouldBeEmpty
        
        // Verify a few specific ones
        (result.["dependencies"].["pkg-1"]).GetValue<string>() |> shouldEqual "^1.0.0"
        (result.["dependencies"].["pkg-50"]).GetValue<string>() |> shouldEqual "^50.0.0"
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: large realistic project with many packages`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.SolidJs", "1.0.0", Fixtures.fableSolidJs)
        ("Fable.SolidJs.Router", "1.0.0", Fixtures.fableSolidJsRouter)
        ("Fable.DateFns", "3.0.0", Fixtures.fableDateFns)
        ("Fable.Remoting.Client", "1.0.0", Fixtures.fableRemotingClient)
        ("Fable.Mocha", "1.0.0", Fixtures.fableMocha)
        ("Fable.Promise", "1.0.0", Fixtures.fablePromise)
        ("Scoped.Pkg", "1.0.0", Fixtures.scopedDeps)
    ]
    
    try
        let packages = [
            { Name = "Fable.SolidJs"; Version = "1.0.0" }
            { Name = "Fable.SolidJs.Router"; Version = "1.0.0" }
            { Name = "Fable.DateFns"; Version = "3.0.0" }
            { Name = "Fable.Remoting.Client"; Version = "1.0.0" }
            { Name = "Fable.Mocha"; Version = "1.0.0" }
            { Name = "Fable.Promise"; Version = "1.0.0" }
            { Name = "Scoped.Pkg"; Version = "1.0.0" }
        ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        // Expected: solid-js (2x same version), @solidjs/router, date-fns, cross-fetch,
        // @types/node (2x from different sources - CONFLICT), mocha, @types/mocha,
        // @tanstack/solid-query, @solid-primitives/storage
        
        // Check dependencies
        (result.["dependencies"].["solid-js"]).GetValue<string>() |> shouldEqual "^1.8.0"
        (result.["dependencies"].["@solidjs/router"]).GetValue<string>() |> shouldEqual "^0.14.0"
        (result.["dependencies"].["date-fns"]).GetValue<string>() |> shouldEqual "^3.6.0"
        (result.["dependencies"].["cross-fetch"]).GetValue<string>() |> shouldEqual "^4.0.0"
        (result.["dependencies"].["@tanstack/solid-query"]).GetValue<string>() |> shouldEqual "^5.0.0"
        (result.["dependencies"].["@solid-primitives/storage"]).GetValue<string>() |> shouldEqual "^3.0.0"
        
        // Check devDependencies
        (result.["devDependencies"].["mocha"]).GetValue<string>() |> shouldEqual "^10.0.0"
        (result.["devDependencies"].["@types/mocha"]).GetValue<string>() |> shouldEqual "^10.0.0"
        
        // @types/node has conflict: ^20.0.0 from Fable.Remoting.Client, ^22.0.0 from Scoped.Pkg
        warnings |> shouldHaveLength 1
        warnings.[0].Package |> shouldEqual "@types/node"
        (result.["devDependencies"].["@types/node"]).GetValue<string>() |> shouldEqual "^22.0.0"  // last wins
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: merge preserves existing package.json fields`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.DateFns", "3.0.0", Fixtures.fableDateFns)
    ]
    
    try
        let packages = [ { Name = "Fable.DateFns"; Version = "3.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        
        // Existing package.json with user's own deps and config
        let existing = parse """{
  "name": "my-app",
  "version": "1.0.0",
  "type": "module",
  "scripts": {
    "build": "vite build",
    "dev": "vite"
  },
  "dependencies": {
    "my-existing-dep": "^1.0.0"
  }
}"""
        
        let result, warnings = merge existing npmDeps
        
        // User's fields preserved
        result.["name"].GetValue<string>() |> shouldEqual "my-app"
        result.["version"].GetValue<string>() |> shouldEqual "1.0.0"
        result.["type"].GetValue<string>() |> shouldEqual "module"
        (result.["scripts"].["build"]).GetValue<string>() |> shouldEqual "vite build"
        
        // User's existing dep preserved
        (result.["dependencies"].["my-existing-dep"]).GetValue<string>() |> shouldEqual "^1.0.0"
        
        // New dep added
        (result.["dependencies"].["date-fns"]).GetValue<string>() |> shouldEqual "^3.6.0"
        
        warnings |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: merge updates existing dependency version`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.DateFns", "3.0.0", Fixtures.fableDateFns)  // wants ^3.6.0
    ]
    
    try
        let packages = [ { Name = "Fable.DateFns"; Version = "3.0.0" } ]
        let npmDeps = collectAllDeps packagesDir packages
        
        // Existing package.json has older version
        let existing = parse """{
  "dependencies": {
    "date-fns": "^2.0.0"
  }
}"""
        
        let result, warnings = merge existing npmDeps
        
        // Version updated to new one
        (result.["dependencies"].["date-fns"]).GetValue<string>() |> shouldEqual "^3.6.0"
        warnings |> shouldBeEmpty
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: three-way conflict resolution`` () =
    let conflictC = """{
  "name": "conflicting-c",
  "version": "0.0.0",
  "private": true,
  "dependencies": {
    "solid-js": "^1.8.5"
  }
}"""
    
    let packagesDir = setupFakePackagesDir [
        ("Conflicting.A", "1.0.0", Fixtures.conflictingPackageA)  // ^1.7.0
        ("Conflicting.B", "1.0.0", Fixtures.conflictingPackageB)  // ^1.9.0
        ("Conflicting.C", "1.0.0", conflictC)                     // ^1.8.5
    ]
    
    try
        let packages = [
            { Name = "Conflicting.A"; Version = "1.0.0" }
            { Name = "Conflicting.B"; Version = "1.0.0" }
            { Name = "Conflicting.C"; Version = "1.0.0" }
        ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, warnings = merge (JsonObject()) npmDeps
        
        // Should report conflict with all 3 sources
        warnings |> shouldHaveLength 1
        warnings.[0].Package |> shouldEqual "solid-js"
        warnings.[0].Sources |> shouldHaveLength 3
        warnings.[0].UsedVersion |> shouldEqual "^1.8.5"  // last wins (C)
        
        (result.["dependencies"].["solid-js"]).GetValue<string>() |> shouldEqual "^1.8.5"
    finally
        cleanupDir packagesDir

[<Fact>]
let ``E2E: package.json in content subdirectory`` () =
    // Some NuGet packages ship content in "content" subdirectory
    let tempDir = Path.Combine(Path.GetTempPath(), $"flan-e2e-{Guid.NewGuid()}")
    let packageDir = Path.Combine(tempDir, "fable.special", "1.0.0", "content")
    Directory.CreateDirectory(packageDir) |> ignore
    File.WriteAllText(Path.Combine(packageDir, "package.json"), Fixtures.fableDateFns)
    
    try
        let packages = [ { Name = "Fable.Special"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps tempDir packages
        
        npmDeps |> shouldHaveLength 1
        npmDeps.[0].Name |> shouldEqual "date-fns"
    finally
        cleanupDir tempDir

[<Fact>]
let ``E2E: package.json in contentFiles subdirectory`` () =
    // Some NuGet packages use contentFiles/any/any/ path
    let tempDir = Path.Combine(Path.GetTempPath(), $"flan-e2e-{Guid.NewGuid()}")
    let packageDir = Path.Combine(tempDir, "fable.contentfiles", "1.0.0", "contentFiles", "any", "any")
    Directory.CreateDirectory(packageDir) |> ignore
    File.WriteAllText(Path.Combine(packageDir, "package.json"), Fixtures.fableDateFns)
    
    try
        let packages = [ { Name = "Fable.ContentFiles"; Version = "1.0.0" } ]
        let npmDeps = collectAllDeps tempDir packages
        
        npmDeps |> shouldHaveLength 1
        npmDeps.[0].Name |> shouldEqual "date-fns"
    finally
        cleanupDir tempDir

[<Fact>]
let ``E2E: write and read round-trip`` () =
    let packagesDir = setupFakePackagesDir [
        ("Fable.SolidJs", "1.0.0", Fixtures.fableSolidJs)
        ("Fable.DateFns", "3.0.0", Fixtures.fableDateFns)
    ]
    
    let tempOutputDir = Path.Combine(Path.GetTempPath(), $"flan-output-{Guid.NewGuid()}")
    Directory.CreateDirectory(tempOutputDir) |> ignore
    
    try
        let packages = [
            { Name = "Fable.SolidJs"; Version = "1.0.0" }
            { Name = "Fable.DateFns"; Version = "3.0.0" }
        ]
        let npmDeps = collectAllDeps packagesDir packages
        let result, _ = merge (JsonObject()) npmDeps
        
        // Write to disk
        let outputPath = Path.Combine(tempOutputDir, "package.json")
        write outputPath result
        
        // Read back
        let readBack = read outputPath
        
        // Verify
        (readBack.["dependencies"].["solid-js"]).GetValue<string>() |> shouldEqual "^1.8.0"
        (readBack.["dependencies"].["date-fns"]).GetValue<string>() |> shouldEqual "^3.6.0"
    finally
        cleanupDir packagesDir
        cleanupDir tempOutputDir
