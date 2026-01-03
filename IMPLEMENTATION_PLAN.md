# Flan - A Bun-first Package Manager for Fable

## Philosophy

**"Let Bun be the only failure point."**

Flan is a thin bridge between Fable/F# projects and Bun. It does NOT:
- Query npm registries
- Resolve version conflicts
- Implement semver logic
- Try to be clever

It DOES:
- Extract npm deps from NuGet packages (via shipped `package.json` files)
- Merge them into the app's package.json
- Call `bun install`

If something fails, Bun tells you why.

---

## Commands

```bash
flan sync [-p path/to/project.fsproj]  # Extract deps from NuGet packages, merge into package.json, run bun install
```

That's it. One command.

---

## How It Works

### Library Development (Publishing Fable Libraries)

Library authors use bun normally:
1. `bun add date-fns` - adds to their package.json
2. Add to .fsproj to ship package.json with NuGet package:
```xml
<ItemGroup>
  <Content Include="package.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <PackageCopyToOutput>true</PackageCopyToOutput>
  </Content>
</ItemGroup>
```
3. `dotnet pack` - package.json travels with the NuGet package

No flan needed for library development.

### Application Development (Consuming Fable Libraries)

1. User runs `flan sync` (or `flan sync -p path/to/app.fsproj`)
2. Flan finds all NuGet package references (including transitive)
3. For each package in `~/.nuget/packages/{name}/{version}/`:
   - Look for `package.json` in the package contents
4. Merge all `dependencies` and `devDependencies` into app's package.json
   - If conflicts exist (same package, different versions), warn and use last encountered
5. Run `bun install`

---

## Project Structure

```
Flan/
├── Flan.sln
├── README.md
├── src/
│   └── Flan/
│       ├── Flan.fsproj        # .NET 9.0 tool
│       ├── Program.fs         # Entry point + CLI (Argu)
│       ├── ProjectAnalyzer.fs # Find fsproj, list NuGet packages
│       ├── NpmDeps.fs         # Find and parse package.json from NuGet packages  
│       ├── PackageJson.fs     # Read/write/merge package.json
│       └── Bun.fs             # Shell out to bun via Fli
└── tests/
    └── Flan.Tests/
        ├── Flan.Tests.fsproj
        ├── PackageJsonTests.fs
        ├── NpmDepsTests.fs
        └── ProjectAnalyzerTests.fs
```

---

## Implementation Details

### 1. CLI with Argu (Program.fs)

```fsharp
open Argu

type SyncArgs =
    | [<AltCommandLine("-p")>] Project of path: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Project _ -> "Path to .fsproj file (defaults to current directory)"

[<CliPrefix(CliPrefix.None)>]
type FlanArgs =
    | [<CliPrefix(CliPrefix.None)>] Sync of ParseResults<SyncArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Sync _ -> "Sync npm dependencies from NuGet packages and run bun install"

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<FlanArgs>(programName = "flan")
    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
        match results.GetSubCommand() with
        | Sync syncResults ->
            let projectPath = syncResults.TryGetResult Project
            Sync.run projectPath
        0
    with
    | :? ArguParseException as e ->
        eprintfn "%s" e.Message
        1
    | ex ->
        eprintfn "Error: %s" ex.Message
        1
```

### 2. ProjectAnalyzer.fs

**Purpose:** Find fsproj, get NuGet package references

```fsharp
type PackageRef = {
    Name: string
    Version: string
}

// Find .fsproj in directory (error if multiple or none)
let findFsproj (dir: string) : Result<string, string>

// Get NuGet global packages directory
let getNugetPackagesDir () : string =
    // Run: dotnet nuget locals global-packages --list
    // Parse output to get path (usually ~/.nuget/packages)

// List package references from a project (including transitive)
let getPackageReferences (fsprojPath: string) : PackageRef list =
    // Run: dotnet list {fsprojPath} package --include-transitive
    // Parse output to extract package names and versions
```

### 3. NpmDeps.fs

**Purpose:** Find and parse package.json from NuGet package directories

```fsharp
type NpmDep = {
    Name: string
    Version: string
    Source: string  // Which NuGet package required this
    IsDev: bool
}

// Find package.json in NuGet package directory
let findPackageJson (packagesDir: string) (pkg: PackageRef) : string option =
    // Check: {packagesDir}/{name.ToLower()}/{version}/package.json
    // Also check: {packagesDir}/{name.ToLower()}/{version}/content/package.json
    // Also check: {packagesDir}/{name.ToLower()}/{version}/contentFiles/any/any/package.json

// Parse package.json to extract dependencies
let parsePackageJson (path: string) : NpmDep list =
    // Read JSON, extract dependencies and devDependencies
```

### 4. PackageJson.fs

**Purpose:** Read, merge, and write package.json using FSharp.SystemTextJson

```fsharp
open System.Text.Json
open System.Text.Json.Serialization

// Only the fields we care about - other fields preserved via JsonExtensionData
type PackageJsonModel = {
    [<JsonPropertyName("name")>]
    Name: string option
    
    [<JsonPropertyName("version")>]
    Version: string option
    
    [<JsonPropertyName("dependencies")>]
    Dependencies: Map<string, string> option
    
    [<JsonPropertyName("devDependencies")>]
    DevDependencies: Map<string, string> option
    
    [<JsonExtensionData>]
    ExtensionData: Dictionary<string, JsonElement> option
}

type MergeWarning = {
    Package: string
    Sources: (string * string) list  // (NuGet package, version requested)
    UsedVersion: string
}

// Read existing package.json (or create default)
let read (path: string) : PackageJsonModel

// Merge deps, returning warnings for conflicts
let merge (existing: PackageJsonModel) (deps: NpmDep list) : PackageJsonModel * MergeWarning list

// Write back to disk (preserves unknown fields)
let write (path: string) (pkg: PackageJsonModel) : unit
```

### 5. Bun.fs

**Purpose:** Shell out to Bun using Fli

```fsharp
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
```

**Usage in Sync.run:**
```fsharp
if not (Bun.isInstalled()) then
    eprintfn "Error: bun is not installed or not found in PATH."
    eprintfn "Install bun: https://bun.com"
    1
else
    // proceed with sync...
```

---

## Dependencies

### Main Project (Flan.fsproj)
```xml
<ItemGroup>
    <PackageReference Include="Argu" Version="6.2.5" />
    <PackageReference Include="Fli" Version="1.111.10" />
    <PackageReference Include="FSharp.SystemTextJson" Version="1.3.13" />
</ItemGroup>
```

Minimal dependencies:
- **Argu**: CLI argument parsing
- **Fli**: Running shell commands (dotnet, bun)
- **FSharp.SystemTextJson**: JSON serialization with F# type support

### Test Project (Flan.Tests.fsproj)
```xml
<ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.1" />
    <PackageReference Include="FsUnit.Light.xUnit" Version="1.0.1" />
</ItemGroup>

<ItemGroup>
    <ProjectReference Include="../../src/Flan/Flan.fsproj" />
</ItemGroup>
```

Test dependencies:
- **xUnit**: Test framework
- **FsUnit.Light.xUnit**: F#-friendly assertions (`shouldEqual`, `shouldContain`, etc.)

---

## Merge Strategy

When multiple NuGet packages require the same npm package with different versions:

1. **Log a warning** showing all conflicting requirements:
   ```
   Warning: Conflicting versions for 'date-fns':
     - Fable.DateFns@2.1.0 requires ^2.0.0
     - Fable.SomeOther@1.0.0 requires ^3.0.0
     Using: ^3.0.0
   ```

2. **Use the last encountered version** (deterministic but arbitrary)

3. **Let bun install proceed** - if versions are truly incompatible, bun or runtime will fail

This follows the philosophy: warn, don't block, let Bun be the judge.

---

## Error Handling Strategy

1. **No .fsproj found:** Print clear message, exit 1
2. **Multiple .fsproj found:** List them, ask user to specify with `-p`
3. **dotnet commands fail:** Show dotnet's error output, exit 1
4. **Can't find NuGet packages dir:** Show error, exit 1
5. **Bun not found:** Show "bun not installed" message, exit 1
6. **Bun fails:** User sees Bun's output directly, exit with Bun's exit code

No swallowing errors. Fail fast, fail clearly.

---

## Testing Strategy

### Test Framework
- **xUnit** as the test runner
- **FsUnit.Light.xUnit** for F#-friendly assertions

### Test Structure

```
tests/Flan.Tests/
├── PackageJsonTests.fs    # Most critical - merge logic
├── NpmDepsTests.fs        # Package.json parsing
└── ProjectAnalyzerTests.fs # dotnet output parsing
```

### PackageJson.fs Tests (Highest Priority)

The merge logic is the core of Flan. Test extensively:

**Reading:**
```fsharp
[<Fact>]
let ``read parses valid package.json with all fields`` () =
    let json = """{"name":"test","dependencies":{"foo":"^1.0.0"}}"""
    let result = PackageJson.parse json
    result.Name |> shouldEqual (Some "test")
    result.Dependencies |> shouldEqual (Some (Map.ofList ["foo", "^1.0.0"]))
```

**Merging - No Conflicts:**
```fsharp
[<Fact>]
let ``merge combines deps from multiple sources`` () =
    let existing = { empty with Dependencies = Some (Map.ofList ["existing", "^1.0.0"]) }
    let deps = [
        { Name = "foo"; Version = "^2.0.0"; Source = "Fable.Foo"; IsDev = false }
        { Name = "bar"; Version = "^3.0.0"; Source = "Fable.Bar"; IsDev = false }
    ]
    let result, warnings = PackageJson.merge existing deps
    result.Dependencies.Value |> shouldContain ("foo", "^2.0.0")
    result.Dependencies.Value |> shouldContain ("bar", "^3.0.0")
    result.Dependencies.Value |> shouldContain ("existing", "^1.0.0")
    warnings |> shouldBeEmpty
```

**Merging - With Conflicts:**
```fsharp
[<Fact>]
let ``merge detects version conflicts and warns`` () =
    let existing = empty
    let deps = [
        { Name = "date-fns"; Version = "^2.0.0"; Source = "Fable.DateFns"; IsDev = false }
        { Name = "date-fns"; Version = "^3.0.0"; Source = "Fable.Other"; IsDev = false }
    ]
    let result, warnings = PackageJson.merge existing deps
    warnings |> shouldHaveLength 1
    warnings.[0].Package |> shouldEqual "date-fns"
    warnings.[0].Sources |> shouldContain ("Fable.DateFns", "^2.0.0")
    warnings.[0].Sources |> shouldContain ("Fable.Other", "^3.0.0")
    // Last wins
    result.Dependencies.Value.["date-fns"] |> shouldEqual "^3.0.0"
```

**Edge Cases:**
```fsharp
[<Fact>]
let ``merge handles scoped packages`` () =
    let deps = [{ Name = "@solid-js/core"; Version = "^1.8.0"; Source = "Fable.Solid"; IsDev = false }]
    let result, _ = PackageJson.merge empty deps
    result.Dependencies.Value |> shouldContain ("@solid-js/core", "^1.8.0")

[<Fact>]
let ``write preserves unknown fields`` () =
    let json = """{"name":"test","scripts":{"build":"tsc"},"dependencies":{}}"""
    let parsed = PackageJson.parse json
    let output = PackageJson.serialize parsed
    output |> shouldContainText "\"scripts\""
    output |> shouldContainText "\"build\""
```

### NpmDeps.fs Tests

```fsharp
[<Fact>]
let ``findPackageJson locates file in content directory`` () =
    // Setup: create temp dir structure mimicking NuGet cache
    let result = NpmDeps.findPackageJson packagesDir { Name = "Fable.Test"; Version = "1.0.0" }
    result |> shouldEqual (Some expectedPath)

[<Fact>]
let ``parsePackageJson extracts deps with source tracking`` () =
    let json = """{"dependencies":{"foo":"^1.0.0"},"devDependencies":{"bar":"^2.0.0"}}"""
    let deps = NpmDeps.parsePackageJson "Fable.Test@1.0.0" json
    deps |> shouldHaveLength 2
    deps |> List.find (fun d -> d.Name = "foo") |> fun d -> d.IsDev |> shouldEqual false
    deps |> List.find (fun d -> d.Name = "bar") |> fun d -> d.IsDev |> shouldEqual true
```

### ProjectAnalyzer.fs Tests

```fsharp
[<Fact>]
let ``parsePackageListOutput extracts packages with versions`` () =
    let output = """
Project 'MyApp' has the following package references
   [net9.0]:
   Top-level Package      Requested   Resolved
   > Fable.Core           4.0.0       4.0.0
   > Fable.Browser.Dom    2.4.0       2.4.0

   Transitive Package     Resolved
   > FSharp.Core          8.0.0
"""
    let packages = ProjectAnalyzer.parsePackageListOutput output
    packages |> shouldHaveLength 3
    packages |> shouldContain { Name = "Fable.Core"; Version = "4.0.0" }
```

---

## Example Usage

```bash
# In a Fable app directory with MyApp.fsproj
$ flan sync

Found project: MyApp.fsproj
Scanning NuGet packages...
  - Fable.DateFns@2.1.0 → date-fns ^2.0.0
  - Fable.Promise@3.2.0 → (no npm deps)
  - Fable.Solid@0.1.0 → solid-js ^1.8.0

Warning: Conflicting versions for 'date-fns':
  - Fable.DateFns@2.1.0 requires ^2.0.0
  - Fable.OtherLib@1.0.0 requires ^3.0.0
  Using: ^3.0.0

Updating package.json...
Running bun install...
bun install v1.x.x

+ solid-js@1.8.x
+ date-fns@3.x.x

Done!

# With explicit project path
$ flan sync -p src/MyApp/MyApp.fsproj
```

---

## Files to Create

```
/Users/takinprofit/Dev/Facet/Flan/
├── Flan.sln
├── README.md
├── src/
│   └── Flan/
│       ├── Flan.fsproj        # .NET 9.0 tool
│       ├── Program.fs         # Entry point + Argu CLI
│       ├── ProjectAnalyzer.fs # Find fsproj, list NuGet packages
│       ├── NpmDeps.fs         # Find/parse package.json from NuGet packages
│       ├── PackageJson.fs     # Read/write/merge package.json
│       └── Bun.fs             # Shell out to bun via Fli
└── tests/
    └── Flan.Tests/
        ├── Flan.Tests.fsproj  # Test project
        ├── PackageJsonTests.fs
        ├── NpmDepsTests.fs
        └── ProjectAnalyzerTests.fs
```

---

## Publishing as a .NET Tool

Flan is distributed as a .NET global/local tool via NuGet.

### Project File Configuration (Flan.fsproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    
    <!-- Tool packaging -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>flan</ToolCommandName>
    <PackageOutputPath>./nupkg</PackageOutputPath>
    
    <!-- NuGet metadata -->
    <PackageId>Flan</PackageId>
    <Version>0.1.0</Version>
    <Authors>Your Name</Authors>
    <Description>A Bun-first package manager for Fable - syncs npm dependencies from NuGet packages</Description>
    <PackageProjectUrl>https://github.com/yourname/flan</PackageProjectUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageTags>fable;fsharp;bun;npm;nuget</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>

  <ItemGroup>
    <None Include="../../README.md" Pack="true" PackagePath="/" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Argu" Version="6.2.5" />
    <PackageReference Include="Fli" Version="1.111.10" />
    <PackageReference Include="FSharp.SystemTextJson" Version="1.3.13" />
  </ItemGroup>

</Project>
```

### Key Properties

| Property | Description |
|----------|-------------|
| `PackAsTool` | Enables packaging as a .NET tool |
| `ToolCommandName` | The command users will type (`flan`) |
| `PackageOutputPath` | Where the .nupkg file is created |

### Build and Pack

```bash
# Build the tool
dotnet build

# Create the NuGet package
dotnet pack

# Output: ./nupkg/Flan.0.1.0.nupkg
```

### Installation

```bash
# Install globally
dotnet tool install --global Flan

# Install locally (per-project)
dotnet new tool-manifest  # if no manifest exists
dotnet tool install Flan

# Install from local nupkg (for testing)
dotnet tool install --global --add-source ./nupkg Flan
```

### Usage After Installation

```bash
# Global tool
flan sync
flan sync -p path/to/project.fsproj

# Local tool
dotnet flan sync
```

### Publishing to NuGet.org

```bash
# Pack with release configuration
dotnet pack -c Release

# Push to NuGet.org
dotnet nuget push ./nupkg/Flan.0.1.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

---

## Key Simplifications

| Original Plan | Simplified |
|---------------|------------|
| Multiple commands (sync, add, remove, list, init) | One command: `sync` |
| Solution/workspace support | Single project only |
| `<PackageJson>` CDATA parsing | Read shipped package.json files |
| SolutionParser.fs | Removed |
| Templates.fs | Removed |

---

## Future Considerations (Out of Scope for v1)

- `flan init` command to scaffold new Fable projects
- Solution/workspace support (multiple projects)
- Watch mode for development
- Support for legacy Femto `<NpmDependencies>` format
- `flan check` to validate without installing
