module Flan.Tests.PackageJsonTests

open System.Text.Json.Nodes
open Xunit
open FsUnit.Light
open Flan.PackageJson
open Flan.NpmDeps

// ============================================================================
// Reading package.json
// ============================================================================

[<Fact>]
let ``parse valid package.json with all fields`` () =
    let json = """{"name":"test-app","version":"1.0.0","dependencies":{"foo":"^1.0.0"},"devDependencies":{"bar":"^2.0.0"}}"""
    let result = parse json
    
    result["name"].GetValue<string>() |> shouldEqual "test-app"
    result["version"].GetValue<string>() |> shouldEqual "1.0.0"
    (result["dependencies"]["foo"]).GetValue<string>() |> shouldEqual "^1.0.0"
    (result["devDependencies"]["bar"]).GetValue<string>() |> shouldEqual "^2.0.0"

[<Fact>]
let ``parse package.json with only dependencies`` () =
    let json = """{"dependencies":{"foo":"^1.0.0"}}"""
    let result = parse json
    
    (result["dependencies"]["foo"]).GetValue<string>() |> shouldEqual "^1.0.0"
    result.ContainsKey("devDependencies") |> shouldEqual false

[<Fact>]
let ``parse package.json with only devDependencies`` () =
    let json = """{"devDependencies":{"bar":"^2.0.0"}}"""
    let result = parse json
    
    (result["devDependencies"]["bar"]).GetValue<string>() |> shouldEqual "^2.0.0"
    result.ContainsKey("dependencies") |> shouldEqual false

[<Fact>]
let ``parse package.json with neither dependencies nor devDependencies`` () =
    let json = """{"name":"test-app","version":"1.0.0"}"""
    let result = parse json
    
    result["name"].GetValue<string>() |> shouldEqual "test-app"
    result.ContainsKey("dependencies") |> shouldEqual false
    result.ContainsKey("devDependencies") |> shouldEqual false

[<Fact>]
let ``parse package.json with extra fields`` () =
    let json = """{"name":"test","scripts":{"build":"tsc"},"main":"index.js","type":"module"}"""
    let result = parse json
    
    (result["scripts"]["build"]).GetValue<string>() |> shouldEqual "tsc"
    result["main"].GetValue<string>() |> shouldEqual "index.js"
    result["type"].GetValue<string>() |> shouldEqual "module"

[<Fact>]
let ``parse empty JSON object`` () =
    let json = """{}"""
    let result = parse json
    
    result.Count |> shouldEqual 0

[<Fact>]
let ``parse malformed JSON returns empty object`` () =
    let json = """not valid json"""
    let result = parse json
    
    result.Count |> shouldEqual 0

// ============================================================================
// Writing package.json
// ============================================================================

[<Fact>]
let ``serialize preserves unknown fields`` () =
    let json = """{"name":"test","scripts":{"build":"tsc"},"dependencies":{"foo":"^1.0.0"}}"""
    let parsed = parse json
    let output = serialize parsed
    
    output |> shouldContainText "\"scripts\""
    output |> shouldContainText "\"build\""
    output |> shouldContainText "\"tsc\""

[<Fact>]
let ``serialize formats JSON with indentation`` () =
    let json = """{"name":"test"}"""
    let parsed = parse json
    let output = serialize parsed
    
    output |> shouldContainText "\n"

// ============================================================================
// Merging dependencies - no conflicts
// ============================================================================

[<Fact>]
let ``merge into empty package.json`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "foo"; Version = "^1.0.0"; Source = "Fable.Foo@1.0.0"; IsDev = false }
    ]
    
    let result, warnings = merge existing deps
    
    (result["dependencies"]["foo"]).GetValue<string>() |> shouldEqual "^1.0.0"
    warnings |> shouldBeEmpty

[<Fact>]
let ``merge single dependency`` () =
    let existing = parse """{"name":"test"}"""
    let deps = [
        { Name = "date-fns"; Version = "^2.0.0"; Source = "Fable.DateFns@1.0.0"; IsDev = false }
    ]
    
    let result, warnings = merge existing deps
    
    (result["dependencies"]["date-fns"]).GetValue<string>() |> shouldEqual "^2.0.0"
    result["name"].GetValue<string>() |> shouldEqual "test"  // preserves existing
    warnings |> shouldBeEmpty

[<Fact>]
let ``merge multiple dependencies from single source`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "foo"; Version = "^1.0.0"; Source = "Fable.Foo@1.0.0"; IsDev = false }
        { Name = "bar"; Version = "^2.0.0"; Source = "Fable.Foo@1.0.0"; IsDev = false }
    ]
    
    let result, warnings = merge existing deps
    
    (result["dependencies"]["foo"]).GetValue<string>() |> shouldEqual "^1.0.0"
    (result["dependencies"]["bar"]).GetValue<string>() |> shouldEqual "^2.0.0"
    warnings |> shouldBeEmpty

[<Fact>]
let ``merge dependencies from multiple sources no conflicts`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "foo"; Version = "^1.0.0"; Source = "Fable.Foo@1.0.0"; IsDev = false }
        { Name = "bar"; Version = "^2.0.0"; Source = "Fable.Bar@1.0.0"; IsDev = false }
    ]
    
    let result, warnings = merge existing deps
    
    (result["dependencies"]["foo"]).GetValue<string>() |> shouldEqual "^1.0.0"
    (result["dependencies"]["bar"]).GetValue<string>() |> shouldEqual "^2.0.0"
    warnings |> shouldBeEmpty

[<Fact>]
let ``merge devDependencies separately from dependencies`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "solid-js"; Version = "^1.8.0"; Source = "Fable.Solid@1.0.0"; IsDev = false }
        { Name = "typescript"; Version = "^5.0.0"; Source = "Fable.Solid@1.0.0"; IsDev = true }
    ]
    
    let result, warnings = merge existing deps
    
    (result["dependencies"]["solid-js"]).GetValue<string>() |> shouldEqual "^1.8.0"
    (result["devDependencies"]["typescript"]).GetValue<string>() |> shouldEqual "^5.0.0"
    warnings |> shouldBeEmpty

[<Fact>]
let ``merge preserves existing user dependencies`` () =
    let existing = parse """{"dependencies":{"existing-pkg":"^3.0.0"}}"""
    let deps = [
        { Name = "foo"; Version = "^1.0.0"; Source = "Fable.Foo@1.0.0"; IsDev = false }
    ]
    
    let result, warnings = merge existing deps
    
    (result["dependencies"]["existing-pkg"]).GetValue<string>() |> shouldEqual "^3.0.0"
    (result["dependencies"]["foo"]).GetValue<string>() |> shouldEqual "^1.0.0"
    warnings |> shouldBeEmpty

// ============================================================================
// Merge conflict detection
// ============================================================================

[<Fact>]
let ``detect conflict same package different major versions`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "date-fns"; Version = "^2.0.0"; Source = "Fable.DateFns@1.0.0"; IsDev = false }
        { Name = "date-fns"; Version = "^3.0.0"; Source = "Fable.Other@1.0.0"; IsDev = false }
    ]
    
    let _, warnings = merge existing deps
    
    warnings |> shouldHaveLength 1
    warnings[0].Package |> shouldEqual "date-fns"
    warnings[0].Sources |> shouldHaveLength 2
    warnings[0].UsedVersion |> shouldEqual "^3.0.0"  // last wins

[<Fact>]
let ``detect conflict same package different minor versions`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "foo"; Version = "^1.0.0"; Source = "Pkg.A@1.0.0"; IsDev = false }
        { Name = "foo"; Version = "^1.5.0"; Source = "Pkg.B@1.0.0"; IsDev = false }
    ]
    
    let _, warnings = merge existing deps
    
    warnings |> shouldHaveLength 1
    warnings[0].Package |> shouldEqual "foo"

[<Fact>]
let ``detect conflict exact version vs range`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "foo"; Version = "1.0.0"; Source = "Pkg.A@1.0.0"; IsDev = false }
        { Name = "foo"; Version = "^1.0.0"; Source = "Pkg.B@1.0.0"; IsDev = false }
    ]
    
    let _, warnings = merge existing deps
    
    warnings |> shouldHaveLength 1

[<Fact>]
let ``detect conflict multiple sources different versions`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "react"; Version = "^17.0.0"; Source = "Pkg.A@1.0.0"; IsDev = false }
        { Name = "react"; Version = "^18.0.0"; Source = "Pkg.B@1.0.0"; IsDev = false }
        { Name = "react"; Version = "^19.0.0"; Source = "Pkg.C@1.0.0"; IsDev = false }
    ]
    
    let _, warnings = merge existing deps
    
    warnings |> shouldHaveLength 1
    warnings[0].Sources |> shouldHaveLength 3
    warnings[0].UsedVersion |> shouldEqual "^19.0.0"  // last wins

[<Fact>]
let ``no conflict same package same version from multiple sources`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "foo"; Version = "^1.0.0"; Source = "Pkg.A@1.0.0"; IsDev = false }
        { Name = "foo"; Version = "^1.0.0"; Source = "Pkg.B@1.0.0"; IsDev = false }
    ]
    
    let result, warnings = merge existing deps
    
    warnings |> shouldBeEmpty
    (result["dependencies"]["foo"]).GetValue<string>() |> shouldEqual "^1.0.0"

[<Fact>]
let ``warning contains all sources listed`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "foo"; Version = "^1.0.0"; Source = "Fable.A@1.0.0"; IsDev = false }
        { Name = "foo"; Version = "^2.0.0"; Source = "Fable.B@2.0.0"; IsDev = false }
    ]
    
    let _, warnings = merge existing deps
    
    let sources = warnings[0].Sources
    sources |> List.exists (fun (s, _) -> s = "Fable.A@1.0.0") |> shouldEqual true
    sources |> List.exists (fun (s, _) -> s = "Fable.B@2.0.0") |> shouldEqual true

[<Fact>]
let ``last encountered version wins on conflict`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "foo"; Version = "^1.0.0"; Source = "Pkg.First@1.0.0"; IsDev = false }
        { Name = "foo"; Version = "^2.0.0"; Source = "Pkg.Second@1.0.0"; IsDev = false }
        { Name = "foo"; Version = "^3.0.0"; Source = "Pkg.Last@1.0.0"; IsDev = false }
    ]
    
    let result, _ = merge existing deps
    
    (result["dependencies"]["foo"]).GetValue<string>() |> shouldEqual "^3.0.0"

// ============================================================================
// Edge cases
// ============================================================================

[<Fact>]
let ``handle scoped package names`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "@solid-js/core"; Version = "^1.8.0"; Source = "Fable.Solid@1.0.0"; IsDev = false }
        { Name = "@types/node"; Version = "^20.0.0"; Source = "Fable.Node@1.0.0"; IsDev = true }
    ]
    
    let result, warnings = merge existing deps
    
    (result["dependencies"]["@solid-js/core"]).GetValue<string>() |> shouldEqual "^1.8.0"
    (result["devDependencies"]["@types/node"]).GetValue<string>() |> shouldEqual "^20.0.0"
    warnings |> shouldBeEmpty

[<Fact>]
let ``handle various npm version formats`` () =
    let existing = JsonObject()
    let deps = [
        { Name = "caret"; Version = "^1.0.0"; Source = "Pkg@1.0.0"; IsDev = false }
        { Name = "tilde"; Version = "~1.0.0"; Source = "Pkg@1.0.0"; IsDev = false }
        { Name = "exact"; Version = "1.0.0"; Source = "Pkg@1.0.0"; IsDev = false }
        { Name = "range"; Version = ">=1.0.0 <2.0.0"; Source = "Pkg@1.0.0"; IsDev = false }
        { Name = "star"; Version = "*"; Source = "Pkg@1.0.0"; IsDev = false }
        { Name = "latest"; Version = "latest"; Source = "Pkg@1.0.0"; IsDev = false }
    ]
    
    let result, warnings = merge existing deps
    
    (result["dependencies"]["caret"]).GetValue<string>() |> shouldEqual "^1.0.0"
    (result["dependencies"]["tilde"]).GetValue<string>() |> shouldEqual "~1.0.0"
    (result["dependencies"]["exact"]).GetValue<string>() |> shouldEqual "1.0.0"
    (result["dependencies"]["range"]).GetValue<string>() |> shouldEqual ">=1.0.0 <2.0.0"
    (result["dependencies"]["star"]).GetValue<string>() |> shouldEqual "*"
    (result["dependencies"]["latest"]).GetValue<string>() |> shouldEqual "latest"
    warnings |> shouldBeEmpty

[<Fact>]
let ``handle many dependencies`` () =
    let existing = JsonObject()
    let deps = 
        [1..100]
        |> List.map (fun i -> 
            { Name = $"package-{i}"; Version = $"^{i}.0.0"; Source = "Pkg@1.0.0"; IsDev = false })
    
    let result, warnings = merge existing deps
    
    let depsObj = result["dependencies"].AsObject()
    depsObj.Count |> shouldEqual 100
    warnings |> shouldBeEmpty
