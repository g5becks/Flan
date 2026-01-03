module Flan.Tests.ProjectAnalyzerTests

open Xunit
open FsUnit.Light
open Flan.ProjectAnalyzer

// ============================================================================
// Parsing dotnet list package output
// ============================================================================

[<Fact>]
let ``parse single top-level package`` () =
    let output = """
Project 'MyApp' has the following package references
   [net9.0]:
   Top-level Package      Requested   Resolved
   > Fable.Core           4.0.0       4.0.0
"""
    let packages = parsePackageListOutput output
    
    packages |> shouldHaveLength 1
    packages[0].Name |> shouldEqual "Fable.Core"
    packages[0].Version |> shouldEqual "4.0.0"

[<Fact>]
let ``parse multiple top-level packages`` () =
    let output = """
Project 'MyApp' has the following package references
   [net9.0]:
   Top-level Package      Requested   Resolved
   > Fable.Core           4.0.0       4.0.0
   > Fable.Browser.Dom    2.4.0       2.4.0
   > Fable.Promise        3.2.0       3.2.0
"""
    let packages = parsePackageListOutput output
    
    packages |> shouldHaveLength 3
    packages |> List.exists (fun p -> p.Name = "Fable.Core") |> shouldEqual true
    packages |> List.exists (fun p -> p.Name = "Fable.Browser.Dom") |> shouldEqual true
    packages |> List.exists (fun p -> p.Name = "Fable.Promise") |> shouldEqual true

[<Fact>]
let ``parse transitive dependencies`` () =
    let output = """
Project 'MyApp' has the following package references
   [net9.0]:
   Top-level Package      Requested   Resolved
   > Fable.Core           4.0.0       4.0.0

   Transitive Package               Resolved
   > FSharp.Core                    8.0.0
   > System.Buffers                 4.5.1
"""
    let packages = parsePackageListOutput output
    
    packages |> shouldHaveLength 3
    packages |> List.exists (fun p -> p.Name = "Fable.Core" && p.Version = "4.0.0") |> shouldEqual true
    packages |> List.exists (fun p -> p.Name = "FSharp.Core" && p.Version = "8.0.0") |> shouldEqual true
    packages |> List.exists (fun p -> p.Name = "System.Buffers" && p.Version = "4.5.1") |> shouldEqual true

[<Fact>]
let ``handle packages with four-part versions`` () =
    let output = """
   Top-level Package      Requested   Resolved
   > Some.Package         1.0.0.0     1.0.0.0
"""
    let packages = parsePackageListOutput output
    
    packages |> shouldHaveLength 1
    packages[0].Version |> shouldEqual "1.0.0.0"

[<Fact>]
let ``handle packages with prerelease tags`` () =
    let output = """
   Top-level Package      Requested   Resolved
   > Fable.Core           4.0.0-beta1 4.0.0-beta1
"""
    let packages = parsePackageListOutput output
    
    packages |> shouldHaveLength 1
    packages[0].Version |> shouldEqual "4.0.0-beta1"

[<Fact>]
let ``handle empty package list`` () =
    let output = """
Project 'MyApp' has the following package references
   [net9.0]: No packages were found for this framework.
"""
    let packages = parsePackageListOutput output
    
    packages |> shouldBeEmpty

[<Fact>]
let ``handle output with no packages section`` () =
    let output = """
Some random output that doesn't contain packages
"""
    let packages = parsePackageListOutput output
    
    packages |> shouldBeEmpty

[<Fact>]
let ``handle packages with auto-referenced marker`` () =
    let output = """
   Top-level Package                      Requested   Resolved
   > FSharp.Core                (A)       8.0.0       8.0.0
   > Fable.Core                           4.0.0       4.0.0
"""
    let packages = parsePackageListOutput output
    
    // Should still parse both, extracting correct name
    packages |> shouldHaveLength 2

[<Fact>]
let ``handle mixed top-level and transitive`` () =
    let output = """
Project 'MyApp' has the following package references
   [net9.0]:
   Top-level Package      Requested   Resolved
   > Argu                 6.2.5       6.2.5
   > Fli                  1.111.10    1.111.10

   Transitive Package               Resolved
   > FSharp.Core                    8.0.0
   > FSharp.SystemTextJson          1.3.13
"""
    let packages = parsePackageListOutput output
    
    packages |> shouldHaveLength 4
    packages |> List.exists (fun p -> p.Name = "Argu") |> shouldEqual true
    packages |> List.exists (fun p -> p.Name = "FSharp.SystemTextJson") |> shouldEqual true
