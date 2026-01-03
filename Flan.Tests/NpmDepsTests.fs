module Flan.Tests.NpmDepsTests

open Xunit
open FsUnit.Light
open Flan.NpmDeps
// ============================================================================
// Parsing package.json content
// ============================================================================

[<Fact>]
let ``parse dependencies only`` () =
    let json = """{"dependencies":{"foo":"^1.0.0","bar":"^2.0.0"}}"""
    let result = parsePackageJsonContent "TestPkg@1.0.0" json
    
    match result with
    | Ok deps ->
        deps |> shouldHaveLength 2
        deps |> List.exists (fun d -> d.Name = "foo" && d.Version = "^1.0.0") |> shouldEqual true
        deps |> List.exists (fun d -> d.Name = "bar" && d.Version = "^2.0.0") |> shouldEqual true
        deps |> List.forall (fun d -> d.IsDev = false) |> shouldEqual true
    | Error msg -> failwith msg

[<Fact>]
let ``parse devDependencies only`` () =
    let json = """{"devDependencies":{"typescript":"^5.0.0"}}"""
    let result = parsePackageJsonContent "TestPkg@1.0.0" json
    
    match result with
    | Ok deps ->
        deps |> shouldHaveLength 1
        deps[0].Name |> shouldEqual "typescript"
        deps[0].IsDev |> shouldEqual true
    | Error msg -> failwith msg

[<Fact>]
let ``parse both dependencies and devDependencies`` () =
    let json = """{"dependencies":{"solid-js":"^1.8.0"},"devDependencies":{"typescript":"^5.0.0"}}"""
    let result = parsePackageJsonContent "TestPkg@1.0.0" json
    
    match result with
    | Ok deps ->
        deps |> shouldHaveLength 2
        let solidJs = deps |> List.find (fun d -> d.Name = "solid-js")
        let typescript = deps |> List.find (fun d -> d.Name = "typescript")
        solidJs.IsDev |> shouldEqual false
        typescript.IsDev |> shouldEqual true
    | Error msg -> failwith msg

[<Fact>]
let ``return empty list for package.json with no deps`` () =
    let json = """{"name":"test","version":"1.0.0"}"""
    let result = parsePackageJsonContent "TestPkg@1.0.0" json
    
    match result with
    | Ok deps -> deps |> shouldBeEmpty
    | Error msg -> failwith msg

[<Fact>]
let ``return empty list for empty object`` () =
    let json = """{}"""
    let result = parsePackageJsonContent "TestPkg@1.0.0" json
    
    match result with
    | Ok deps -> deps |> shouldBeEmpty
    | Error msg -> failwith msg

[<Fact>]
let ``handle malformed JSON gracefully`` () =
    let json = """not valid json at all"""
    let result = parsePackageJsonContent "TestPkg@1.0.0" json
    
    match result with
    | Ok _ -> failwith "Should have returned error"
    | Error msg -> msg |> shouldContainText "Failed to parse"

[<Fact>]
let ``track source NuGet package in NpmDep records`` () =
    let json = """{"dependencies":{"foo":"^1.0.0"}}"""
    let result = parsePackageJsonContent "Fable.DateFns@2.1.0" json
    
    match result with
    | Ok deps ->
        deps[0].Source |> shouldEqual "Fable.DateFns@2.1.0"
    | Error msg -> failwith msg

[<Fact>]
let ``parse scoped npm packages`` () =
    let json = """{"dependencies":{"@solidjs/router":"^0.14.0","@types/node":"^20.0.0"}}"""
    let result = parsePackageJsonContent "TestPkg@1.0.0" json
    
    match result with
    | Ok deps ->
        deps |> shouldHaveLength 2
        deps |> List.exists (fun d -> d.Name = "@solidjs/router") |> shouldEqual true
        deps |> List.exists (fun d -> d.Name = "@types/node") |> shouldEqual true
    | Error msg -> failwith msg

[<Fact>]
let ``handle empty dependencies object`` () =
    let json = """{"dependencies":{},"devDependencies":{}}"""
    let result = parsePackageJsonContent "TestPkg@1.0.0" json
    
    match result with
    | Ok deps -> deps |> shouldBeEmpty
    | Error msg -> failwith msg

[<Fact>]
let ``handle null-ish values gracefully`` () =
    let json = """{"dependencies":null}"""
    let result = parsePackageJsonContent "TestPkg@1.0.0" json
    
    match result with
    | Ok deps -> deps |> shouldBeEmpty
    | Error _ -> () // Either empty or error is acceptable
