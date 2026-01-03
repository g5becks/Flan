# Flan

A Bun-first package manager for Fable. Syncs npm dependencies from NuGet packages into your project's `package.json` and runs `bun install`.

## Philosophy

**"Let Bun be the only failure point."**

Flan is a thin bridge between Fable/F# projects and Bun. It does NOT:
- Query npm registries
- Resolve version conflicts
- Implement semver logic
- Try to be clever

It DOES:
- Extract npm dependencies from NuGet packages (via shipped `package.json` files)
- Merge them into your app's `package.json`
- Call `bun install`

If something fails, Bun tells you why.

## Requirements

- [.NET 10.0+](https://dotnet.microsoft.com/download)
- [Bun](https://bun.sh/) installed and available on PATH

## Installation

```bash
# Install globally from NuGet
dotnet tool install --global Flan

# Or install locally (per-project)
dotnet new tool-manifest  # if no manifest exists
dotnet tool install Flan
```

## Usage

```bash
# In a Fable app directory (auto-detects .fsproj)
flan sync

# Or specify a project path
flan sync -p path/to/MyApp.fsproj

# Show version
flan version
```

That's it. One command.

## Example Output

```
$ flan sync

Found project: MyApp.fsproj
Scanning NuGet packages...
  - Fable.DateFns@3.0.0 -> date-fns ^3.6.0
  - Fable.Promise@3.2.0 -> (no npm deps)
  - Fable.SolidJs@1.0.0 -> solid-js ^1.8.0
  - Fable.SolidJs.Router@1.0.0 -> @solidjs/router ^0.14.0, solid-js ^1.8.0

Updating package.json...
Running bun install...
bun install v1.x.x

+ solid-js@1.8.x
+ date-fns@3.x.x
+ @solidjs/router@0.14.x

Done!
```

### Version Conflict Warnings

When multiple NuGet packages require the same npm package with different versions, Flan warns you but doesn't block:

```
Warning: Conflicting versions for 'date-fns':
  - Fable.DateFns@2.1.0 requires ^2.0.0
  - Fable.OtherLib@1.0.0 requires ^3.0.0
  Using: ^3.0.0
```

The last encountered version wins. Bun will be the final judge of compatibility.

## How It Works

### For App Developers (Consuming Fable Libraries)

1. Add NuGet package references to your `.fsproj` as usual
2. Run `flan sync`
3. Flan finds all NuGet packages (including transitive dependencies)
4. For each package in `~/.nuget/packages/{name}/{version}/`:
   - Looks for `package.json` in the package contents
5. Merges all `dependencies` and `devDependencies` into your app's `package.json`
6. Runs `bun install`

### For Library Authors (Publishing Fable Libraries)

Library authors ship their npm dependencies as a `package.json` file inside their NuGet package:

1. Create a `package.json` with your npm dependencies:
   ```json
   {
     "name": "my-fable-lib",
     "private": true,
     "dependencies": {
       "date-fns": "^3.6.0"
     }
   }
   ```

2. Add to your `.fsproj` to include it in the NuGet package:
   ```xml
   <ItemGroup>
     <Content Include="package.json" PackagePath="/" />
   </ItemGroup>
   ```

3. `dotnet pack` - the `package.json` travels with your NuGet package

Alternative locations Flan checks:
- `{package}/package.json` (root)
- `{package}/content/package.json`
- `{package}/contentFiles/any/any/package.json`

## Merge Behavior

| Scenario | Behavior |
|----------|----------|
| Same package, same version from multiple sources | No warning, added once |
| Same package, different versions | Warning printed, last version wins |
| Regular dependencies | Added to `dependencies` |
| Dev dependencies | Added to `devDependencies` |
| Existing deps in your `package.json` | Preserved (Flan adds/updates, never removes) |
| Unknown fields in `package.json` | Preserved (`scripts`, `type`, etc.) |

## Project Structure

```
Flan/
├── Flan.slnx
├── Flan/
│   ├── Flan.fsproj        # .NET 10.0 tool
│   ├── Program.fs         # Entry point + CLI (Argu)
│   ├── Bun.fs             # Shell out to bun via Fli
│   ├── ProjectAnalyzer.fs # Find fsproj, list NuGet packages
│   ├── NpmDeps.fs         # Find and parse package.json from NuGet packages
│   └── PackageJson.fs     # Read/write/merge package.json
└── Flan.Tests/
    ├── Flan.Tests.fsproj
    ├── PackageJsonTests.fs
    ├── NpmDepsTests.fs
    ├── ProjectAnalyzerTests.fs
    └── E2ETests.fs
```

## Dependencies

| Package | Purpose |
|---------|---------|
| [Argu](https://github.com/fsprojects/Argu) | CLI argument parsing |
| [Fli](https://github.com/CaptnCodr/Fli) | Running shell commands |
| [FSharp.SystemTextJson](https://github.com/Tarmil/FSharp.SystemTextJson) | JSON handling |

## Building from Source

```bash
# Clone the repo
git clone https://github.com/g5becks/Flan.git
cd Flan

# Build
dotnet build

# Run tests
dotnet test

# Create NuGet package
dotnet pack Flan/Flan.fsproj -c Release

# Install locally for testing
dotnet tool install --global --add-source ./Flan/nupkg Flan
```

## Error Handling

Flan fails fast and clearly:

| Error | Message |
|-------|---------|
| No `.fsproj` found | "No .fsproj file found in {directory}" |
| Multiple `.fsproj` found | "Multiple .fsproj files found. Use -p to specify." |
| Bun not installed | "bun is not installed or not found in PATH" |
| dotnet command fails | Shows dotnet's error output |
| Bun install fails | Shows Bun's error output (Bun's exit code is returned) |

## FAQ

**Q: Why Bun and not npm/yarn/pnpm?**

A: Bun is significantly faster for installs and has excellent compatibility. Flan's philosophy is to delegate all package management complexity to Bun.

**Q: What about Femto?**

A: [Femto](https://github.com/Zaid-Ajaj/Femto) is a great tool that inspired Flan. Flan takes a simpler approach: instead of parsing XML metadata, it uses standard `package.json` files shipped with NuGet packages. This makes it easier for library authors and more predictable.

**Q: Does Flan support npm/yarn/pnpm?**

A: No. Flan is Bun-first by design. Use Femto if you need support for other package managers.

**Q: Can I use Flan with solution files?**

A: Currently Flan works with individual `.fsproj` files. For solutions, run `flan sync -p path/to/each/project.fsproj` for each project that needs npm dependencies.

**Q: Does Flan remove dependencies?**

A: No. Flan only adds or updates dependencies. If you remove a NuGet package, manually remove its npm dependencies from your `package.json`, or delete `package.json` and `node_modules` and run `flan sync` again.

## License

MIT

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
