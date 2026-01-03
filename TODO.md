# Flan Implementation TODO

## Project Setup
- [ ] Update `Flan/Flan.fsproj` with tool packaging config (PackAsTool, ToolCommandName, etc.)
- [ ] Add NuGet metadata to `Flan/Flan.fsproj` (PackageId, Version, Authors, Description)
- [ ] Install main dependencies:
  - [ ] `dotnet add Flan/Flan.fsproj package Argu`
  - [ ] `dotnet add Flan/Flan.fsproj package Fli`
  - [ ] `dotnet add Flan/Flan.fsproj package FSharp.SystemTextJson`
- [ ] Create test project: `dotnet new xunit -lang F# -o Flan.Tests`
- [ ] Add test project to solution: `dotnet sln add Flan.Tests/Flan.Tests.fsproj`
- [ ] Install test dependencies:
  - [ ] `dotnet add Flan.Tests/Flan.Tests.fsproj package FsUnit.Light.xUnit`
- [ ] Add project reference: `dotnet add Flan.Tests/Flan.Tests.fsproj reference Flan/Flan.fsproj`
- [ ] Create README.md

## Core Modules

### Bun.fs
- [ ] Create `Flan/Bun.fs`
- [ ] Add to fsproj Compile list (before Program.fs)
- [ ] Implement `isInstalled` function (check if bun is available via `bun --version`)
- [ ] Implement `install` function (shell out to `bun install`)

### ProjectAnalyzer.fs
- [ ] Create `Flan/ProjectAnalyzer.fs`
- [ ] Add to fsproj Compile list
- [ ] Implement `findFsproj` - find .fsproj in directory
- [ ] Implement `getNugetPackagesDir` - run `dotnet nuget locals global-packages --list`
- [ ] Implement `getPackageReferences` - run `dotnet list package --include-transitive`, parse output

### NpmDeps.fs
- [ ] Create `Flan/NpmDeps.fs`
- [ ] Add to fsproj Compile list
- [ ] Define `NpmDep` type
- [ ] Implement `findPackageJson` - search NuGet package directory for package.json
- [ ] Implement `parsePackageJson` - extract dependencies/devDependencies

### PackageJson.fs
- [ ] Create `Flan/PackageJson.fs`
- [ ] Add to fsproj Compile list
- [ ] Define `PackageJsonModel` type with JsonExtensionData for preserving unknown fields
- [ ] Define `MergeWarning` type
- [ ] Implement `read` - read existing or create default package.json
- [ ] Implement `merge` - merge deps, detect conflicts, return warnings
- [ ] Implement `write` - write package.json to disk

### Program.fs
- [ ] Update `Flan/Program.fs`
- [ ] Define `SyncArgs` with `-p/--project` option
- [ ] Define `FlanArgs` with `sync` subcommand
- [ ] Implement `main` entry point with Argu parsing
- [ ] Implement `Sync.run` orchestration:
  - [ ] Check if bun is installed (fail early with clear message if not)
  - [ ] Find project
  - [ ] Get NuGet packages dir
  - [ ] Get package references
  - [ ] For each package, find and parse package.json
  - [ ] Merge all deps
  - [ ] Print warnings for conflicts
  - [ ] Write package.json
  - [ ] Run bun install

---

## Unit Tests (FsUnit.Light + xUnit)

### PackageJsonTests.fs

#### Reading package.json
- [ ] Read valid package.json with all fields
- [ ] Read package.json with only dependencies (no devDependencies)
- [ ] Read package.json with only devDependencies (no dependencies)
- [ ] Read package.json with neither dependencies nor devDependencies
- [ ] Read package.json with extra/unknown fields (scripts, main, etc.)
- [ ] Read empty JSON object `{}`
- [ ] Handle missing file (create default)
- [ ] Handle malformed JSON (return error)

#### Writing package.json
- [ ] Write preserves unknown fields (scripts, main, type, etc.)
- [ ] Write preserves field order where possible
- [ ] Write formats JSON with proper indentation
- [ ] Write handles empty dependencies maps

#### Merging dependencies
- [ ] Merge into empty package.json
- [ ] Merge single dependency
- [ ] Merge multiple dependencies from single source
- [ ] Merge dependencies from multiple sources (no conflicts)
- [ ] Merge devDependencies separately from dependencies
- [ ] Preserve existing user dependencies not from NuGet packages
- [ ] Preserve existing user devDependencies not from NuGet packages

#### Merge conflict detection
- [ ] Detect conflict: same package, different versions (^1.0.0 vs ^2.0.0)
- [ ] Detect conflict: same package, same major different minor (^1.0.0 vs ^1.5.0)
- [ ] Detect conflict: exact version vs range (1.0.0 vs ^1.0.0)
- [ ] Detect conflict: multiple sources requesting different versions
- [ ] No conflict: same package, same version from multiple sources
- [ ] No conflict: compatible ranges (^1.0.0 and ^1.0.0)
- [ ] Return correct warning with all sources listed
- [ ] Use last encountered version when conflict exists

#### Edge cases
- [ ] Handle package names with special characters (@scope/package)
- [ ] Handle version strings with all npm formats (^, ~, >=, *, latest, git urls)
- [ ] Handle very long dependency lists (100+ packages)
- [ ] Handle unicode in package names (if valid)

### BunTests.fs

#### Bun availability check
- [ ] `isInstalled` returns true when bun is available
- [ ] `isInstalled` returns false when bun is not available
- [ ] Error message is clear and actionable when bun not found

### NpmDepsTests.fs

#### Finding package.json in NuGet packages
- [ ] Find package.json at root of package directory
- [ ] Find package.json in content/ subdirectory
- [ ] Find package.json in contentFiles/any/any/ subdirectory
- [ ] Return None when no package.json exists
- [ ] Handle case-insensitive package names (Fable.Core vs fable.core)

#### Parsing package.json
- [ ] Parse dependencies only
- [ ] Parse devDependencies only
- [ ] Parse both dependencies and devDependencies
- [ ] Return empty list for package.json with no deps
- [ ] Handle malformed JSON gracefully
- [ ] Track source NuGet package in NpmDep records

### ProjectAnalyzerTests.fs

#### Finding .fsproj files
- [ ] Find single .fsproj in directory
- [ ] Error when no .fsproj in directory
- [ ] Error when multiple .fsproj in directory (list them in error)
- [ ] Use explicit path when provided via -p flag

#### Parsing dotnet list package output
- [ ] Parse single top-level package
- [ ] Parse multiple top-level packages
- [ ] Parse transitive dependencies
- [ ] Handle packages with four-part versions (1.0.0.0)
- [ ] Handle packages with prerelease tags (1.0.0-beta1)
- [ ] Handle empty package list
- [ ] Handle dotnet command failure

#### Getting NuGet packages directory
- [ ] Parse `dotnet nuget locals` output correctly
- [ ] Handle Windows paths
- [ ] Handle Unix/macOS paths
- [ ] Handle dotnet command failure

### Integration Tests

#### Full sync workflow
- [ ] Sync project with no NuGet packages (no-op)
- [ ] Sync project with NuGet packages that have no npm deps
- [ ] Sync project with single NuGet package with npm deps
- [ ] Sync project with multiple NuGet packages with npm deps
- [ ] Sync project with conflicting npm versions (verify warning output)
- [ ] Sync updates existing package.json (doesn't overwrite user deps)
- [ ] Sync creates package.json if missing

#### Error handling
- [ ] Graceful error when bun not installed
- [ ] Graceful error when dotnet not installed
- [ ] Graceful error when .fsproj not found
- [ ] Graceful error when NuGet packages dir not found

---

## Manual/E2E Testing
- [ ] Test with a real Fable project
- [ ] Test conflict warning output formatting
- [ ] Test `-p` flag with relative and absolute paths
- [ ] Test in directory with no .fsproj
- [ ] Test in directory with multiple .fsproj files
- [ ] Verify bun install actually runs and installs packages

---

## Packaging
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes all tests
- [ ] `dotnet pack` creates nupkg
- [ ] Test local install: `dotnet tool install --global --add-source ./nupkg Flan`
- [ ] Verify `flan sync` works after install
- [ ] Verify `flan --help` shows usage
