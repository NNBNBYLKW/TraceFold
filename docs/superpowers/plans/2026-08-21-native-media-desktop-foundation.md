# Native Media Desktop Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the stage-1 WinUI 3 desktop foundation: a real native window, clean four-layer .NET solution, safe directory setup, empty SQLite database, startup readiness, local logs, single-instance activation, clean shutdown, and a self-contained Windows 11 x64 checkpoint package.

**Architecture:** Add an isolated `apps/media-desktop` solution. The WinUI project is the composition root, calls application services in-process, and references infrastructure only to register concrete implementations; application code references domain abstractions only, while infrastructure implements persistence and Windows/file-system boundaries. Stage 1 never scans, moves, renames, processes, or deletes user media.

**Tech Stack:** .NET SDK 10.0.300, C# 14, .NET 10 LTS, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Hosting 10.0.11, EF Core SQLite 10.0.11, Serilog, xUnit.net v3.

**Spec:** `docs/superpowers/specs/2026-08-21-native-media-desktop-design.md`

## Global Constraints

- Target Windows 11 x64 only; use `net10.0-windows10.0.26100.0` for the WinUI project and `win-x64` as its only runtime identifier.
- The end-user checkpoint build must run offline and must not start Python, FastAPI, React, Edge, Chrome, or a console window.
- Create the new app under `apps/media-desktop`; do not modify or import `apps/desktop`, `apps/media-api`, or `apps/media-web`.
- Create a new database under `%LocalAppData%\TraceFold\MediaDesktop`; never open or migrate the Web version's database.
- During stage 1, do not enumerate or mutate any configured media directory beyond a short writable-directory probe file that is always removed in a `finally` block.
- `TraceFold.Media.Domain` references no other project. `Application` references only `Domain`. `Infrastructure` references `Application` and `Domain`. `Desktop` is the composition root.
- ViewModels must not reference EF Core, SQLite, `System.IO.File`, `System.Diagnostics.Process`, or FFmpeg.
- The three configured folders must be absolute, existing, writable, pairwise different, and pairwise non-nested after canonicalization.
- The main window closes the entire app; no tray, installer, updater, multi-window, or startup registration is part of this plan.
- Keep `PublishTrimmed=false` and `PublishSingleFile=false`; WinUI native dependencies remain beside the EXE in the ZIP checkpoint.
- Use TDD for application and infrastructure behavior, stable `AutomationId` values for WinUI controls, and one focused commit per task.
- Pin the versions below in `Directory.Packages.props`; do not use floating versions or previews:
  - `Microsoft.WindowsAppSDK` 2.3.1
  - `CommunityToolkit.Mvvm` 8.4.2
  - `Microsoft.Extensions.Hosting` 10.0.11
  - `Microsoft.Extensions.Logging.Abstractions` 10.0.11
  - `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.11
  - `Microsoft.EntityFrameworkCore.Sqlite` 10.0.11
  - `Microsoft.EntityFrameworkCore.Design` 10.0.11
  - `Serilog.Extensions.Hosting` 10.0.0
  - `Serilog.Sinks.File` 7.0.0
  - `xunit.v3` 3.2.2
  - `xunit.runner.visualstudio` 4.0.0
  - `Microsoft.NET.Test.Sdk` 18.9.0

## File Structure

```text
apps/media-desktop/
  TraceFold.MediaDesktop.slnx                 # solution and project grouping
  Directory.Build.props                       # nullable, warnings, language defaults
  Directory.Packages.props                    # central package versions
  README.md                                   # developer run/test/publish instructions
  src/
    TraceFold.Media.Domain/
      TraceFold.Media.Domain.csproj
      DomainAssembly.cs                       # architecture marker
    TraceFold.Media.Application/
      TraceFold.Media.Application.csproj
      ApplicationAssembly.cs                  # architecture marker
      Common/AppDataPaths.cs                  # app-owned local paths
      Common/IAppDataPathProvider.cs
      Settings/LibraryFolders.cs
      Settings/DirectorySettingsContracts.cs
      Settings/DirectorySettingsService.cs
      Startup/StartupContracts.cs
      Startup/StartupCoordinator.cs
    TraceFold.Media.Infrastructure/
      TraceFold.Media.Infrastructure.csproj
      InfrastructureAssembly.cs               # architecture marker
      DependencyInjection.cs                  # concrete service registration
      Paths/LocalAppDataPathProvider.cs
      Settings/LocalDirectoryProbe.cs
      Persistence/MediaDesktopDbContext.cs
      Persistence/AppSettingsRow.cs
      Persistence/EfDirectorySettingsStore.cs
      Persistence/DatabaseInitializer.cs
      Persistence/DesignTimeDbContextFactory.cs
      Persistence/Migrations/*                 # generated EF migration and snapshot
      Logging/LoggingBootstrapper.cs
    TraceFold.Media.Desktop/
      TraceFold.Media.Desktop.csproj
      app.manifest
      App.xaml
      App.xaml.cs
      Program.cs
      MainWindow.xaml
      MainWindow.xaml.cs
      Composition/DesktopCompositionRoot.cs
      Lifecycle/SingleInstanceCoordinator.cs
      Lifecycle/AppShutdownCoordinator.cs
      Shell/ShellPage.xaml
      Shell/ShellPage.xaml.cs
      Shell/ShellViewModel.cs
      Pages/InboxPage.xaml(.cs)
      Pages/LibraryPage.xaml(.cs)
      Pages/TaxonomyPage.xaml(.cs)
      Pages/SettingsPage.xaml(.cs)
      Pages/SettingsViewModel.cs
      Services/FolderPickerContracts.cs
      Services/WindowsFolderPicker.cs
      Services/WindowContext.cs
  tests/
    TraceFold.Media.Architecture.Tests/
    TraceFold.Media.Application.Tests/
    TraceFold.Media.Infrastructure.Tests/
    TraceFold.Media.Desktop.Tests/
scripts/
  publish-media-desktop.ps1                   # exact, guarded checkpoint publisher
  smoke-media-desktop-package.ps1             # launch/single-instance/exit smoke test
```

---

### Task 1: Create the solution and lock dependency boundaries

**Files:**
- Create: `global.json`
- Modify: `.gitignore`
- Create: `apps/media-desktop/TraceFold.MediaDesktop.slnx`
- Create: `apps/media-desktop/Directory.Build.props`
- Create: `apps/media-desktop/Directory.Packages.props`
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/TraceFold.Media.Domain.csproj`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/TraceFold.Media.Application.csproj`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/TraceFold.Media.Infrastructure.csproj`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj`
- Create: `apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj`
- Create: `apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/ArchitectureTests.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/DomainAssembly.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/ApplicationAssembly.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/InfrastructureAssembly.cs`

**Interfaces:**
- Consumes: None; this is the root task.
- Produces: `TraceFold.Media.Domain.DomainAssembly`, `TraceFold.Media.Application.ApplicationAssembly`, and `TraceFold.Media.Infrastructure.InfrastructureAssembly` marker types; the four project reference graph used by every later task.

- [ ] **Step 1: Pin the SDK and ignore only generated .NET artifacts**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.300",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

Append to `.gitignore`:

```gitignore
apps/media-desktop/**/bin/
apps/media-desktop/**/obj/
```

Run: `dotnet --version`

Expected: `10.0.300`.

- [ ] **Step 2: Create central build and package files**

Create `apps/media-desktop/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

Create `apps/media-desktop/Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.WindowsAppSDK" Version="2.3.1" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.11" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.11" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.11" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.11" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.11" />
    <PackageVersion Include="Serilog.Extensions.Hosting" Version="10.0.0" />
    <PackageVersion Include="Serilog.Sinks.File" Version="7.0.0" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="4.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create the four project files with the allowed reference graph**

Create Domain with no project or package references:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>TraceFold.Media.Domain</AssemblyName>
    <RootNamespace>TraceFold.Media.Domain</RootNamespace>
  </PropertyGroup>
</Project>
```

Create Application with its Domain reference temporarily omitted for the failing architecture test in Step 5:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>TraceFold.Media.Application</AssemblyName>
    <RootNamespace>TraceFold.Media.Application</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

Create Infrastructure with EF Core and its allowed references:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>TraceFold.Media.Infrastructure</AssemblyName>
    <RootNamespace>TraceFold.Media.Infrastructure</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Serilog.Sinks.File" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\TraceFold.Media.Application\TraceFold.Media.Application.csproj" />
    <ProjectReference Include="..\TraceFold.Media.Domain\TraceFold.Media.Domain.csproj" />
  </ItemGroup>
</Project>
```

Create the Desktop project with this exact platform baseline:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.26100.0</TargetPlatformMinVersion>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <SelfContained>true</SelfContained>
    <PublishTrimmed>false</PublishTrimmed>
    <PublishSingleFile>false</PublishSingleFile>
    <AssemblyName>TraceFold.Media</AssemblyName>
    <RootNamespace>TraceFold.Media.Desktop</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Serilog.Extensions.Hosting" />
    <PackageReference Include="Serilog.Sinks.File" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\TraceFold.Media.Application\TraceFold.Media.Application.csproj" />
    <ProjectReference Include="..\TraceFold.Media.Infrastructure\TraceFold.Media.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

Do not add a Desktop reference to Domain; it reaches domain types only through Application contracts.

Create the architecture test project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TraceFold.Media.Domain\TraceFold.Media.Domain.csproj" />
    <ProjectReference Include="..\..\src\TraceFold.Media.Application\TraceFold.Media.Application.csproj" />
    <ProjectReference Include="..\..\src\TraceFold.Media.Infrastructure\TraceFold.Media.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Write the failing architecture test**

Create these three marker classes in their matching files:

```csharp
namespace TraceFold.Media.Domain
{
    public static class DomainAssembly { }
}

namespace TraceFold.Media.Application
{
    public static class ApplicationAssembly { }
}

namespace TraceFold.Media.Infrastructure
{
    public static class InfrastructureAssembly
    {
        public static System.Reflection.Assembly Application =>
            typeof(TraceFold.Media.Application.ApplicationAssembly).Assembly;
        public static System.Reflection.Assembly Domain =>
            typeof(TraceFold.Media.Domain.DomainAssembly).Assembly;
    }
}
```

Then add this test:

```csharp
using System.Reflection;
using Xunit;

namespace TraceFold.Media.Architecture.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_has_no_project_dependencies()
    {
        string[] references = typeof(TraceFold.Media.Domain.DomainAssembly).Assembly
            .GetReferencedAssemblies()
            .Select(item => item.Name ?? string.Empty)
            .Where(name => name.StartsWith("TraceFold.Media.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(references);
    }

    [Fact]
    public void Application_references_domain_but_not_infrastructure_or_desktop()
    {
        string[] references = ProjectReferences(
            typeof(TraceFold.Media.Application.ApplicationAssembly).Assembly);

        Assert.Contains("TraceFold.Media.Domain", references);
        Assert.DoesNotContain("TraceFold.Media.Infrastructure", references);
        Assert.DoesNotContain("TraceFold.Media.Desktop", references);
    }

    [Fact]
    public void Infrastructure_never_references_desktop()
    {
        string[] references = ProjectReferences(
            typeof(TraceFold.Media.Infrastructure.InfrastructureAssembly).Assembly);

        Assert.Contains("TraceFold.Media.Application", references);
        Assert.DoesNotContain("TraceFold.Media.Desktop", references);
    }

    private static string[] ProjectReferences(Assembly assembly) => assembly
        .GetReferencedAssemblies()
        .Select(item => item.Name ?? string.Empty)
        .Where(name => name.StartsWith("TraceFold.Media.", StringComparison.Ordinal))
        .ToArray();
}
```

Initially omit the Application → Domain project reference.

- [ ] **Step 5: Run the test to verify it fails**

Run:

```powershell
dotnet restore apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj --no-restore
```

Expected: FAIL because `Application_references_domain_but_not_infrastructure_or_desktop` cannot find `TraceFold.Media.Domain`.

- [ ] **Step 6: Add the correct references and solution entries**

Add the Application → Domain project reference described in Step 3:

```xml
<ItemGroup>
  <ProjectReference Include="..\TraceFold.Media.Domain\TraceFold.Media.Domain.csproj" />
</ItemGroup>
```

Then change `ApplicationAssembly` so the compiler records that dependency:

```csharp
namespace TraceFold.Media.Application
{
    public static class ApplicationAssembly
    {
        public static System.Reflection.Assembly Domain =>
            typeof(TraceFold.Media.Domain.DomainAssembly).Assembly;
    }
}
```

Create the `.slnx` and add all source and architecture test projects:

```powershell
dotnet new sln --name TraceFold.MediaDesktop --format slnx --output apps/media-desktop
dotnet sln apps/media-desktop/TraceFold.MediaDesktop.slnx add (Get-ChildItem apps/media-desktop -Recurse -Filter *.csproj | Select-Object -ExpandProperty FullName)
```

- [ ] **Step 7: Restore, build, and run the architecture test**

Run:

```powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx --no-restore
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj --no-build
```

Expected: build succeeds with zero warnings; all 3 architecture tests pass.

- [ ] **Step 8: Commit**

```powershell
git add global.json .gitignore apps/media-desktop
git commit -m "build(media-desktop): establish WinUI solution boundaries"
```

---

### Task 2: Implement safe directory settings rules

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Settings/LibraryFolders.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Settings/DirectorySettingsContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Settings/DirectorySettingsService.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Settings/DirectorySettingsServiceTests.cs`

**Interfaces:**
- Consumes: `TraceFold.Media.Application` and `TraceFold.Media.Domain` projects from Task 1.
- Produces: `LibraryFolders`, `IDirectoryProbe`, `IDirectorySettingsStore`, `DirectorySettingsService.LoadAsync`, `ValidateAsync`, and `SaveAsync` for persistence and UI tasks.

- [ ] **Step 1: Write failing directory settings tests**

Use these contracts in the tests before they exist:

```csharp
public sealed record LibraryFolders(string InboxDirectory, string LibraryDirectory, string WorkDirectory);
public sealed record SettingsError(string Code, string Field, string Message);
public sealed record DirectorySettingsResult(
    bool Succeeded,
    LibraryFolders? Folders,
    IReadOnlyList<SettingsError> Errors);
```

Create the Application test project exactly as follows:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TraceFold.Media.Application\TraceFold.Media.Application.csproj" />
  </ItemGroup>
</Project>
```

Add the Application test project to the solution:

```powershell
dotnet sln apps/media-desktop/TraceFold.MediaDesktop.slnx add apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj
```

The test class must contain these exact behaviors:

```csharp
[Fact]
public async Task ValidateAsync_rejects_nested_directories()
{
    string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    var probe = new FakeDirectoryProbe(root, Path.Combine(root, "library"), Path.Combine(root, "work"));
    var service = new DirectorySettingsService(probe, new InMemorySettingsStore());

    DirectorySettingsResult result = await service.ValidateAsync(
        new LibraryFolders(root, Path.Combine(root, "library"), Path.Combine(root, "work")),
        CancellationToken.None);

    Assert.False(result.Succeeded);
    Assert.Contains(result.Errors, error => error.Code == "directory.overlap");
}

[Fact]
public async Task SaveAsync_persists_only_the_canonical_valid_value()
{
    using var directories = TestDirectories.CreateThree();
    var store = new InMemorySettingsStore();
    var service = new DirectorySettingsService(new FakeDirectoryProbe(directories.All), store);

    DirectorySettingsResult result = await service.SaveAsync(
        new LibraryFolders(
            Path.Combine(directories.Inbox, "."),
            directories.Library,
            directories.Work),
        CancellationToken.None);

    Assert.True(result.Succeeded);
    Assert.Equal(Path.GetFullPath(directories.Inbox), store.Saved!.InboxDirectory);
    Assert.Equal(store.Saved, result.Folders);
}

[Theory]
[InlineData("", "directory.required")]
[InlineData("relative-folder", "directory.not_absolute")]
public async Task ValidateAsync_rejects_missing_or_relative_inbox(
    string inbox,
    string expectedCode)
{
    using var directories = TestDirectories.CreateThree();
    var service = new DirectorySettingsService(
        new FakeDirectoryProbe(directories.All),
        new InMemorySettingsStore());

    DirectorySettingsResult result = await service.ValidateAsync(
        new LibraryFolders(inbox, directories.Library, directories.Work),
        CancellationToken.None);

    Assert.Contains(result.Errors, error =>
        error.Field == "inbox" && error.Code == expectedCode);
}

[Fact]
public async Task ValidateAsync_reports_not_found_without_attempting_write_probe()
{
    using var directories = TestDirectories.CreateThree();
    var probe = new FakeDirectoryProbe(directories.All)
    {
        MissingPath = directories.Inbox
    };
    var service = new DirectorySettingsService(probe, new InMemorySettingsStore());

    DirectorySettingsResult result = await service.ValidateAsync(
        directories.Value,
        CancellationToken.None);

    Assert.Contains(result.Errors, error => error.Code == "directory.not_found");
    Assert.DoesNotContain(directories.Inbox, probe.WriteChecks);
}

[Fact]
public async Task ValidateAsync_reports_not_writable()
{
    using var directories = TestDirectories.CreateThree();
    var probe = new FakeDirectoryProbe(directories.All)
    {
        ReadOnlyPath = directories.Work
    };
    var service = new DirectorySettingsService(probe, new InMemorySettingsStore());

    DirectorySettingsResult result = await service.ValidateAsync(
        directories.Value,
        CancellationToken.None);

    Assert.Contains(result.Errors, error =>
        error.Field == "work" && error.Code == "directory.not_writable");
}
```

Keep the test doubles in the same test file so no production file-system implementation is used:

```csharp
private sealed class InMemorySettingsStore : IDirectorySettingsStore
{
    public LibraryFolders? Saved { get; private set; }
    public Task<LibraryFolders?> LoadAsync(CancellationToken _) => Task.FromResult(Saved);
    public Task SaveAsync(LibraryFolders folders, CancellationToken _)
    {
        Saved = folders;
        return Task.CompletedTask;
    }
}

private sealed class FakeDirectoryProbe(params string[] existing) : IDirectoryProbe
{
    private readonly HashSet<string> _existing = new(existing, StringComparer.OrdinalIgnoreCase);
    public string? MissingPath { get; init; }
    public string? ReadOnlyPath { get; init; }
    public List<string> WriteChecks { get; } = [];

    public ValueTask<bool> ExistsAsync(string path, CancellationToken _) =>
        ValueTask.FromResult(
            !StringComparer.OrdinalIgnoreCase.Equals(path, MissingPath)
            && _existing.Contains(path));

    public ValueTask<bool> CanWriteAsync(string path, CancellationToken _)
    {
        WriteChecks.Add(path);
        return ValueTask.FromResult(
            !StringComparer.OrdinalIgnoreCase.Equals(path, ReadOnlyPath));
    }
}

private sealed class TestDirectories : IDisposable
{
    private TestDirectories(string root, string inbox, string library, string work)
    {
        Root = root;
        Inbox = inbox;
        Library = library;
        Work = work;
    }

    public string Root { get; }
    public string Inbox { get; }
    public string Library { get; }
    public string Work { get; }
    public string[] All => [Inbox, Library, Work];
    public LibraryFolders Value => new(Inbox, Library, Work);

    public static TestDirectories CreateThree()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tracefold-settings-{Guid.NewGuid():N}");
        string inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;
        string library = Directory.CreateDirectory(Path.Combine(root, "library")).FullName;
        string work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        return new TestDirectories(root, inbox, library, work);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj`

Expected: FAIL with missing `DirectorySettingsService` and settings contract types.

- [ ] **Step 3: Add the settings ports and service**

Create these interfaces:

```csharp
public interface IDirectoryProbe
{
    ValueTask<bool> ExistsAsync(string path, CancellationToken cancellationToken);
    ValueTask<bool> CanWriteAsync(string path, CancellationToken cancellationToken);
}

public interface IDirectorySettingsStore
{
    Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(LibraryFolders folders, CancellationToken cancellationToken);
}
```

Implement `DirectorySettingsService` with these public methods:

```csharp
public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken);
public Task<DirectorySettingsResult> ValidateAsync(
    LibraryFolders folders,
    CancellationToken cancellationToken);
public Task<DirectorySettingsResult> SaveAsync(
    LibraryFolders folders,
    CancellationToken cancellationToken);
```

Canonicalize with `Path.GetFullPath`. Reject a pair when either path equals the other using `StringComparer.OrdinalIgnoreCase`, or when `Path.GetRelativePath(parent, child)` is neither `.` nor rooted and does not start with `..` plus a directory separator. Call `IDirectoryProbe` only after syntactic validation. `SaveAsync` calls `ValidateAsync` and calls the store only when `Succeeded` is true.

Use this exact nesting helper and call it in both directions for each pair:

```csharp
private static bool IsSameOrNested(string parent, string candidate)
{
    if (StringComparer.OrdinalIgnoreCase.Equals(parent, candidate))
    {
        return true;
    }

    string relative = Path.GetRelativePath(parent, candidate);
    return !Path.IsPathRooted(relative)
        && relative != ".."
        && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
}
```

Use these stable error codes and Chinese messages:

```csharp
new SettingsError("directory.required", field, "请选择文件夹。")
new SettingsError("directory.not_absolute", field, "文件夹必须使用绝对路径。")
new SettingsError("directory.not_found", field, "文件夹不存在。")
new SettingsError("directory.not_writable", field, "文件夹不可写。")
new SettingsError("directory.overlap", field, "待入库、正式库和工作目录不能相同或互相包含。")
```

- [ ] **Step 4: Run the application tests**

Run: `dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj`

Expected: all directory settings tests pass.

- [ ] **Step 5: Run the architecture test and full build**

Run:

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx
```

Expected: all tests pass; Application still has no Infrastructure or Desktop reference.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/tests/TraceFold.Media.Application.Tests apps/media-desktop/TraceFold.MediaDesktop.slnx
git commit -m "feat(media-desktop): validate library folder settings"
```

---

### Task 3: Create the app-owned SQLite database and settings repository

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Common/AppDataPaths.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Common/IAppDataPathProvider.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Common/IDatabaseInitializer.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Paths/LocalAppDataPathProvider.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Settings/LocalDirectoryProbe.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/AppSettingsRow.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaDesktopDbContext.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfDirectorySettingsStore.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/DatabaseInitializer.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/*`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/DatabaseInitializationTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/EfDirectorySettingsStoreTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Settings/LocalDirectoryProbeTests.cs`
- Create: `.config/dotnet-tools.json`

**Interfaces:**
- Consumes: `LibraryFolders`, `IDirectoryProbe`, and `IDirectorySettingsStore` from Task 2.
- Produces: `AppDataPaths`, `IAppDataPathProvider.GetPaths()`, `IDatabaseInitializer.InitializeAsync()`, `AddMediaInfrastructure(...)`, and the `InitialDesktopSettings` migration.

- [ ] **Step 1: Write failing path and database tests**

Create the Infrastructure test project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TraceFold.Media.Application\TraceFold.Media.Application.csproj" />
    <ProjectReference Include="..\..\src\TraceFold.Media.Infrastructure\TraceFold.Media.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

Add the Infrastructure test project to the solution:

```powershell
dotnet sln apps/media-desktop/TraceFold.MediaDesktop.slnx add apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj
```

Define the expected app-data contract in Application:

```csharp
public sealed record AppDataPaths(
    string RootDirectory,
    string DatabasePath,
    string LogsDirectory,
    string DefaultWorkDirectory);
```

Write this path test:

```csharp
[Fact]
public void GetPaths_keeps_every_app_owned_path_under_the_override_root()
{
    using var temp = TestDirectory.Create();

    AppDataPaths paths = new LocalAppDataPathProvider(temp.Path).GetPaths();

    Assert.Equal(Path.GetFullPath(temp.Path), paths.RootDirectory);
    Assert.Equal(Path.Combine(paths.RootDirectory, "media-desktop.db"), paths.DatabasePath);
    Assert.Equal(Path.Combine(paths.RootDirectory, "logs"), paths.LogsDirectory);
    Assert.Equal(Path.Combine(paths.RootDirectory, "work"), paths.DefaultWorkDirectory);
}
```

Write this integration test:

```csharp
[Fact]
public async Task Initialize_and_settings_round_trip_survive_a_new_service_provider()
{
    using var temp = TestDirectory.Create();
    var folders = new LibraryFolders(
        Path.Combine(temp.Path, "inbox"),
        Path.Combine(temp.Path, "library"),
        Path.Combine(temp.Path, "work-selected"));

    await using (ServiceProvider first = BuildServices(temp.Path))
    {
        await first.GetRequiredService<IDatabaseInitializer>()
            .InitializeAsync(CancellationToken.None);
        await first.GetRequiredService<IDirectorySettingsStore>()
            .SaveAsync(folders, CancellationToken.None);
    }

    Assert.True(File.Exists(Path.Combine(temp.Path, "media-desktop.db")));

    await using ServiceProvider second = BuildServices(temp.Path);
    LibraryFolders? loaded = await second.GetRequiredService<IDirectorySettingsStore>()
        .LoadAsync(CancellationToken.None);
    Assert.Equal(folders, loaded);
}

private static ServiceProvider BuildServices(string root) => new ServiceCollection()
    .AddLogging()
    .AddMediaInfrastructure(root)
    .BuildServiceProvider(validateScopes: true);
```

Pin probe cleanup with this test:

```csharp
[Fact]
public async Task CanWriteAsync_removes_its_probe_file()
{
    using var temp = TestDirectory.Create();
    var probe = new LocalDirectoryProbe();

    bool writable = await probe.CanWriteAsync(temp.Path, CancellationToken.None);

    Assert.True(writable);
    Assert.Empty(Directory.GetFiles(temp.Path, ".tracefold-write-probe-*.tmp"));
}
```

Use this test-only directory owner in both test classes so cleanup is explicit and constrained:

```csharp
private sealed class TestDirectory : IDisposable
{
    private TestDirectory(string path) => Path = path;
    public string Path { get; }

    public static TestDirectory Create()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"tracefold-media-desktop-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TestDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
```

The resulting paths must be exactly:

```text
<tempRoot>
<tempRoot>\media-desktop.db
<tempRoot>\logs
<tempRoot>\work
```

- [ ] **Step 2: Run the infrastructure tests to verify they fail**

Run: `dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj`

Expected: FAIL because the path provider, context, initializer, and repository do not exist.

- [ ] **Step 3: Implement app data paths and the writable-directory probe**

Use this production root when no constructor override is supplied:

```csharp
Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "TraceFold",
    "MediaDesktop")
```

Allow the exact environment variable `TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT` to override it for source tests and package smoke tests. `LocalDirectoryProbe.CanWriteAsync` creates a uniquely named `.tracefold-write-probe-<guid>.tmp` with `FileMode.CreateNew`, closes it, and removes it in `finally`. It returns `false` for `UnauthorizedAccessException`, `IOException`, and `DirectoryNotFoundException`; it does not swallow cancellation.

`LocalAppDataPathProvider` selects the first non-blank value from its constructor override, `TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT`, and the fixed LocalAppData default. Reject a selected root unless `Path.IsPathFullyQualified(root)` is true, canonicalize it once with `Path.GetFullPath`, and always append the fixed filename `media-desktop.db`; never accept a database filename or connection string through the environment.

Implement the probe with this cleanup shape:

```csharp
public async ValueTask<bool> CanWriteAsync(string path, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    string probePath = Path.Combine(path, $".tracefold-write-probe-{Guid.NewGuid():N}.tmp");
    try
    {
        await using var stream = new FileStream(
            probePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1,
            FileOptions.Asynchronous);
        await stream.FlushAsync(cancellationToken);
        return true;
    }
    catch (Exception exception) when (
        exception is UnauthorizedAccessException
        or IOException)
    {
        return false;
    }
    finally
    {
        if (File.Exists(probePath))
        {
            File.Delete(probePath);
        }
    }
}
```

- [ ] **Step 4: Implement the EF Core settings schema**

Use one row with key `1`:

```csharp
internal sealed class AppSettingsRow
{
    public int Id { get; set; } = 1;
    public required string InboxDirectory { get; set; }
    public required string LibraryDirectory { get; set; }
    public required string WorkDirectory { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
```

Configure `MediaDesktopDbContext` so `AppSettingsRow.Id` is the primary key, each path is required with maximum length 4096, and the table is named `app_settings`. `EfDirectorySettingsStore.SaveAsync` performs an insert-or-update of row `1` and commits exactly once.

The context and repository boundaries are:

```csharp
public sealed class MediaDesktopDbContext(
    DbContextOptions<MediaDesktopDbContext> options) : DbContext(options)
{
    internal DbSet<AppSettingsRow> AppSettings => Set<AppSettingsRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<AppSettingsRow> row = modelBuilder.Entity<AppSettingsRow>();
        row.ToTable("app_settings");
        row.HasKey(item => item.Id);
        row.Property(item => item.InboxDirectory).IsRequired().HasMaxLength(4096);
        row.Property(item => item.LibraryDirectory).IsRequired().HasMaxLength(4096);
        row.Property(item => item.WorkDirectory).IsRequired().HasMaxLength(4096);
    }
}

public sealed class EfDirectorySettingsStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : IDirectorySettingsStore
{
    public async Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        AppSettingsRow? row = await context.AppSettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        return row is null
            ? null
            : new LibraryFolders(row.InboxDirectory, row.LibraryDirectory, row.WorkDirectory);
    }

    public async Task SaveAsync(LibraryFolders folders, CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        AppSettingsRow? row = await context.AppSettings.FindAsync([1], cancellationToken);
        if (row is null)
        {
            row = new AppSettingsRow
            {
                InboxDirectory = folders.InboxDirectory,
                LibraryDirectory = folders.LibraryDirectory,
                WorkDirectory = folders.WorkDirectory
            };
            context.AppSettings.Add(row);
        }
        else
        {
            row.InboxDirectory = folders.InboxDirectory;
            row.LibraryDirectory = folders.LibraryDirectory;
            row.WorkDirectory = folders.WorkDirectory;
        }

        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
```

Define:

```csharp
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
```

`DatabaseInitializer.InitializeAsync` creates only the app root, logs, and default work directories, then calls `Database.MigrateAsync`. It never creates or probes the user-selected inbox or library directories.

Implement it against the factory so no long-lived context is retained:

```csharp
public async Task InitializeAsync(CancellationToken cancellationToken)
{
    AppDataPaths paths = _pathProvider.GetPaths();
    Directory.CreateDirectory(paths.RootDirectory);
    Directory.CreateDirectory(paths.LogsDirectory);
    Directory.CreateDirectory(paths.DefaultWorkDirectory);
    await using MediaDesktopDbContext context =
        await _contextFactory.CreateDbContextAsync(cancellationToken);
    await context.Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 5: Register infrastructure services**

Implement this exact entry point:

```csharp
public static IServiceCollection AddMediaInfrastructure(
    this IServiceCollection services,
    string? dataRootOverride = null)
```

Register one `LocalAppDataPathProvider`, `IAppDataPathProvider`, `IDbContextFactory<MediaDesktopDbContext>`, `IDirectorySettingsStore`, `IDirectoryProbe`, and `IDatabaseInitializer`. Build the SQLite connection string from `AppDataPaths.DatabasePath`; do not accept a connection string from Web configuration.

Use this registration body:

```csharp
var pathProvider = new LocalAppDataPathProvider(dataRootOverride);
AppDataPaths paths = pathProvider.GetPaths();
services.AddSingleton<IAppDataPathProvider>(pathProvider);
services.AddDbContextFactory<MediaDesktopDbContext>(options =>
    options.UseSqlite(new SqliteConnectionStringBuilder
    {
        DataSource = paths.DatabasePath
    }.ToString()));
services.AddSingleton<IDirectorySettingsStore, EfDirectorySettingsStore>();
services.AddSingleton<IDirectoryProbe, LocalDirectoryProbe>();
services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
return services;
```

`DesignTimeDbContextFactory.CreateDbContext` uses `Data Source=media-desktop.design.db` only to generate migrations; no runtime service may instantiate this factory.

- [ ] **Step 6: Generate and inspect the first migration**

Create a local tool manifest with `dotnet-ef` 10.0.11, then run:

```powershell
dotnet tool restore
dotnet ef migrations add InitialDesktopSettings `
  --project apps/media-desktop/src/TraceFold.Media.Infrastructure/TraceFold.Media.Infrastructure.csproj `
  --startup-project apps/media-desktop/src/TraceFold.Media.Infrastructure/TraceFold.Media.Infrastructure.csproj `
  --output-dir Persistence/Migrations
```

Expected migration `Up` creates only `app_settings` and `__EFMigrationsHistory`. It must not contain any legacy media tables or path to an existing database.

- [ ] **Step 7: Run persistence and full tests**

Run:

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj
```

Expected: all tests pass; every test database and probe file is under a per-test temporary directory and is deleted by test cleanup.

- [ ] **Step 8: Commit**

```powershell
git add .config apps/media-desktop/src/TraceFold.Media.Application/Common apps/media-desktop/src/TraceFold.Media.Infrastructure apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests apps/media-desktop/TraceFold.MediaDesktop.slnx
git commit -m "feat(media-desktop): add isolated SQLite settings store"
```

---

### Task 4: Add startup readiness and durable local logging

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Startup/StartupContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Startup/StartupCoordinator.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Logging/LoggingBootstrapper.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Startup/StartupCoordinatorTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Logging/LoggingBootstrapperTests.cs`

**Interfaces:**
- Consumes: `IDatabaseInitializer`, `DirectorySettingsService`, and `AppDataPaths` from Tasks 2–3.
- Produces: `StartupState`, `StartupStatus`, `IStartupCoordinator.InitializeAsync`, `IStartupCoordinator.StatusChanged`, and `LoggingBootstrapper.CreateLogger(AppDataPaths) : Serilog.Core.Logger` for the WinUI composition root.

- [ ] **Step 1: Write failing readiness tests**

Use these exact states:

```csharp
public enum StartupState
{
    Initializing,
    SetupRequired,
    Ready,
    Failed
}

public sealed record StartupStatus(
    StartupState State,
    string Message,
    IReadOnlyList<SettingsError> Errors);
```

Test these outcomes:

1. Database initialization runs before settings load.
2. No saved folders produces `SetupRequired` and `"请先配置三个工作目录。"`.
3. Valid saved folders produces `Ready` and no errors.
4. Invalid saved folders produces `SetupRequired` with the validator errors.
5. An initializer exception produces `Failed`, logs through `ILogger<StartupCoordinator>`, and does not expose a stack trace in `StartupStatus.Message`.

Anchor the suite with these exact tests and implement cases 3–4 as theories using the same fakes:

```csharp
[Fact]
public async Task InitializeAsync_initializes_database_before_loading_settings()
{
    var calls = new List<string>();
    var initializer = new FakeDatabaseInitializer(() => calls.Add("database"));
    var store = new FakeSettingsStore(null, () => calls.Add("settings"));
    var coordinator = new StartupCoordinator(
        initializer,
        new DirectorySettingsService(new AlwaysWritableProbe(), store),
        new RecordingLogger<StartupCoordinator>());

    await coordinator.InitializeAsync(CancellationToken.None);

    Assert.Equal(["database", "settings"], calls);
    Assert.Equal(StartupState.SetupRequired, coordinator.Current.State);
    Assert.Equal("请先配置三个工作目录。", coordinator.Current.Message);
}

[Fact]
public async Task InitializeAsync_converts_initializer_exception_to_safe_failed_status()
{
    var logger = new RecordingLogger<StartupCoordinator>();
    var initializer = new FakeDatabaseInitializer(
        () => throw new InvalidOperationException("sensitive stack detail"));
    var coordinator = new StartupCoordinator(
        initializer,
        new DirectorySettingsService(new AlwaysWritableProbe(), new FakeSettingsStore(null)),
        logger);

    await coordinator.InitializeAsync(CancellationToken.None);

    Assert.Equal(StartupState.Failed, coordinator.Current.State);
    Assert.Equal("软件初始化失败，请查看本地日志。", coordinator.Current.Message);
    Assert.DoesNotContain("sensitive", coordinator.Current.Message, StringComparison.Ordinal);
    Assert.Contains(logger.Events, item => item.Contains("DesktopStartupFailed", StringComparison.Ordinal));
}
```

The fake initializer invokes its delegate from `InitializeAsync`; the fake store invokes its optional load callback and returns its configured `LibraryFolders`; `AlwaysWritableProbe` returns `true` from both methods. `RecordingLogger<T>` records the formatter result from `ILogger.Log` and does not write to disk.

- [ ] **Step 2: Run the readiness tests to verify they fail**

Run: `dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj --filter StartupCoordinatorTests`

Expected: FAIL because startup contracts do not exist.

- [ ] **Step 3: Implement the startup coordinator**

Define:

```csharp
public interface IStartupCoordinator
{
    StartupStatus Current { get; }
    event EventHandler<StartupStatus>? StatusChanged;
    Task InitializeAsync(CancellationToken cancellationToken);
}
```

Start at `Initializing`. `InitializeAsync` calls `IDatabaseInitializer.InitializeAsync`, loads settings, validates them without saving, and publishes exactly one terminal state. Catch non-cancellation exceptions, log them with event name `DesktopStartupFailed`, and publish `Failed` with message `"软件初始化失败，请查看本地日志。"`. Re-throw `OperationCanceledException` when the provided token is canceled.

- [ ] **Step 4: Write and run the failing rolling-log test**

Write this test with a test-owned temporary root:

```csharp
[Fact]
public void CreateLogger_writes_and_flushes_a_rolling_file_under_app_data()
{
    using var temp = TestDirectory.Create();
    var paths = new AppDataPaths(
        temp.Path,
        Path.Combine(temp.Path, "media-desktop.db"),
        Path.Combine(temp.Path, "logs"),
        Path.Combine(temp.Path, "work"));

    using (Serilog.Core.Logger logger = LoggingBootstrapper.CreateLogger(paths))
    {
        logger.Information("stage-one-log-probe");
    }

    string[] logs = Directory.GetFiles(paths.LogsDirectory, "tracefold-*.log");
    Assert.Single(logs);
    Assert.Contains("stage-one-log-probe", File.ReadAllText(logs[0]), StringComparison.Ordinal);
}
```

Run: `dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj --filter LoggingBootstrapperTests`

Expected: FAIL because `LoggingBootstrapper` does not exist.

- [ ] **Step 5: Implement structured rolling logs**

Configure Serilog in code, not through a mutable file beside the EXE:

```csharp
return new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.WithProperty("Application", "TraceFold.MediaDesktop")
    .WriteTo.File(
        Path.Combine(paths.LogsDirectory, "tracefold-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1))
    .CreateLogger();
```

Ensure `paths.LogsDirectory` exists before creating the logger.
Declare the method as `public static Serilog.Core.Logger CreateLogger(AppDataPaths paths)` so ownership and disposal are explicit.

- [ ] **Step 6: Run all non-UI tests**

Run: `dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx --no-restore`

Expected: architecture, application, and infrastructure tests pass; no media files are enumerated or changed.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Startup apps/media-desktop/src/TraceFold.Media.Infrastructure/Logging apps/media-desktop/tests
git commit -m "feat(media-desktop): coordinate startup readiness and logs"
```

---

### Task 5: Build the native three-column WinUI shell and settings page

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/app.manifest`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/App.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/App.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/MainWindow.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/MainWindow.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Shell/ShellPage.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Shell/ShellPage.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Shell/ShellViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Services/FolderPickerContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Services/WindowsFolderPicker.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Services/WindowContext.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsViewModelTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Shell/ShellViewModelTests.cs`

**Interfaces:**
- Consumes: `IStartupCoordinator`, `DirectorySettingsService`, `AddMediaInfrastructure`, and `LoggingBootstrapper` from Tasks 2–4.
- Produces: `MainWindow`, `WindowContext`, `IFolderPicker`, `ShellViewModel`, and `SettingsViewModel`; stable navigation and settings `AutomationId` values.

- [ ] **Step 1: Write failing ViewModel tests**

Create the Desktop test project with a matching Windows target and explicit Windows App SDK bootstrap:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PlatformTarget>x64</PlatformTarget>
    <WindowsAppSdkBootstrapInitialize>true</WindowsAppSdkBootstrapInitialize>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.WindowsAppSDK" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TraceFold.Media.Application\TraceFold.Media.Application.csproj" />
    <ProjectReference Include="..\..\src\TraceFold.Media.Desktop\TraceFold.Media.Desktop.csproj" />
  </ItemGroup>
</Project>
```

Add the Desktop test project to the solution:

```powershell
dotnet sln apps/media-desktop/TraceFold.MediaDesktop.slnx add apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj
```

Define this picker boundary in the tests:

```csharp
public interface IFolderPicker
{
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken);
}
```

Test that:

- `ShellViewModel` reflects `Initializing`, `SetupRequired`, `Ready`, and `Failed` status changes.
- `SetupRequired` sets `SelectedPageKey` to `"settings"` and shows the startup message.
- Choosing a folder updates only its matching property.
- Save calls `DirectorySettingsService.SaveAsync`; validation errors appear in `Errors` and do not report success.
- Successful save sets `StatusMessage` to `"设置已保存。"` and reruns `IStartupCoordinator.InitializeAsync`.

Use these two tests to pin the event and save paths; cover the remaining bullets with focused tests in the same files:

```csharp
[Fact]
public void SetupRequired_selects_settings_and_exposes_the_message()
{
    var startup = new FakeStartupCoordinator();
    var viewModel = new ShellViewModel(startup);

    startup.Publish(new StartupStatus(
        StartupState.SetupRequired,
        "请先配置三个工作目录。",
        []));

    Assert.Equal("settings", viewModel.SelectedPageKey);
    Assert.Equal(StartupState.SetupRequired, viewModel.StartupState);
    Assert.Equal("请先配置三个工作目录。", viewModel.StartupMessage);
    Assert.True(viewModel.IsStartupInfoBarOpen);
}

[Fact]
public async Task SaveCommand_saves_valid_folders_and_rechecks_startup()
{
    string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    var store = new InMemorySettingsStore();
    var startup = new FakeStartupCoordinator();
    var viewModel = new SettingsViewModel(
        new QueueFolderPicker(),
        new DirectorySettingsService(new AlwaysWritableProbe(), store),
        startup)
    {
        InboxDirectory = Path.Combine(root, "inbox"),
        LibraryDirectory = Path.Combine(root, "library"),
        WorkDirectory = Path.Combine(root, "work")
    };

    await viewModel.SaveCommand.ExecuteAsync(null);

    Assert.Equal("设置已保存。", viewModel.StatusMessage);
    Assert.Empty(viewModel.Errors);
    Assert.NotNull(store.Saved);
    Assert.Equal(1, startup.InitializeCallCount);
}
```

- [ ] **Step 2: Run Desktop tests to verify they fail**

Run: `dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj`

Expected: FAIL because the shell and settings ViewModels do not exist.

- [ ] **Step 3: Implement ViewModels with CommunityToolkit.Mvvm**

Use `[ObservableProperty]` and `[RelayCommand]`; keep all ViewModel methods free of WinUI types. `SettingsViewModel` has these observable properties:

```csharp
string InboxDirectory
string LibraryDirectory
string WorkDirectory
string StatusMessage
IReadOnlyList<SettingsError> Errors
bool IsBusy
```

Expose `ChooseInboxCommand`, `ChooseLibraryCommand`, `ChooseWorkCommand`, and `SaveCommand`. Disable all four while `IsBusy` is true. Do not create directories from the ViewModel.

`ShellViewModel` exposes exactly `SelectedPageKey`, `StartupState`, `StartupMessage`, and `IsStartupInfoBarOpen`. Subscribe once to `IStartupCoordinator.StatusChanged`, apply `Current` in the constructor, redirect only `SetupRequired` to Settings, and leave the user's current page unchanged for `Ready` or `Failed`.

- [ ] **Step 4: Build the app composition root**

`DesktopCompositionRoot.BuildHost(AppDataPaths paths)` must:

1. Create a Generic Host.
2. Route Microsoft logging through the Serilog logger created in Task 4.
3. Call `AddMediaInfrastructure(paths.RootDirectory)`.
4. Register `DirectorySettingsService`, `IStartupCoordinator`, `StartupCoordinator`, `WindowContext`, `IFolderPicker`, all ViewModels, all pages, and `MainWindow`.

Do not register a web server, HTTP client, Python process, browser launcher, FFmpeg process, media scanner, or background queue in stage 1.

Set `Serilog.Log.Logger = LoggingBootstrapper.CreateLogger(paths)` before building the host and call `.UseSerilog(Log.Logger, dispose: true)`. Register `AppDataPaths` itself as the exact singleton instance passed into `BuildHost`; do not recompute it inside a ViewModel.

- [ ] **Step 5: Create the WinUI application and main window**

Use `app.manifest` with Windows 11 compatibility and `requestedExecutionLevel level="asInvoker"`. `App.OnLaunched` starts the host, resolves and activates `MainWindow`, then awaits `IStartupCoordinator.InitializeAsync`.

Set:

```text
Window title: TraceFold 媒体库
Minimum intended content size: 1100 × 700
Initial content size: 1360 × 840
```

Do not use `webbrowser`, WebView2, URI navigation, or a localhost port.

After `InitializeComponent`, set the concrete initial size and content floor:

```csharp
Title = "TraceFold 媒体库";
AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 840));
ShellRoot.MinWidth = 1100;
ShellRoot.MinHeight = 700;
```

Stage 1 treats `1100 × 700` as the responsive content floor; it does not add a Win32 window-procedure hook solely to prevent further manual resizing.

- [ ] **Step 6: Create the three-column shell**

Use a left `NavigationView`, a central `Frame`, and a fixed-width right inspector placeholder. The navigation items and exact `AutomationId` values are:

```text
待入库      NavInbox
正式文件库  NavLibrary
词表管理    NavTaxonomy
设置        NavSettings
```

The top `InfoBar` uses `AutomationId="StartupInfoBar"`. The right pane uses `AutomationId="InspectorPane"` and shows `"选择媒体后在这里查看资料。"`. Inbox, Library, and Taxonomy pages use truthful empty states: `"尚无待入库文件。"`, `"正式文件库为空。"`, and `"尚无词表项。"`; do not label them as unfinished or coming later.

Use this shell topology (styles and spacing may be extracted into resources without changing names or structure):

```xml
<Grid x:Name="ShellRoot" RowDefinitions="Auto,*">
  <InfoBar x:Name="StartupInfoBar"
           AutomationProperties.AutomationId="StartupInfoBar"
           IsOpen="{x:Bind ViewModel.IsStartupInfoBarOpen, Mode=OneWay}"
           Message="{x:Bind ViewModel.StartupMessage, Mode=OneWay}" />
  <NavigationView x:Name="Navigation"
                  Grid.Row="1"
                  IsSettingsVisible="False"
                  PaneDisplayMode="Left"
                  SelectionChanged="OnSelectionChanged">
    <NavigationView.MenuItems>
      <NavigationViewItem Content="待入库" Tag="inbox"
                          AutomationProperties.AutomationId="NavInbox" />
      <NavigationViewItem Content="正式文件库" Tag="library"
                          AutomationProperties.AutomationId="NavLibrary" />
      <NavigationViewItem Content="词表管理" Tag="taxonomy"
                          AutomationProperties.AutomationId="NavTaxonomy" />
      <NavigationViewItem Content="设置" Tag="settings"
                          AutomationProperties.AutomationId="NavSettings" />
    </NavigationView.MenuItems>
    <Grid ColumnDefinitions="*,320">
      <Frame x:Name="ContentFrame" />
      <Border Grid.Column="1" Padding="20" BorderThickness="1,0,0,0"
              AutomationProperties.AutomationId="InspectorPane">
        <TextBlock Text="选择媒体后在这里查看资料。" TextWrapping="Wrap" />
      </Border>
    </Grid>
  </NavigationView>
</Grid>
```

- [ ] **Step 7: Implement the native folder picker and settings XAML**

`WindowContext` stores the one `MainWindow` instance and returns its HWND via `WinRT.Interop.WindowNative.GetWindowHandle`. `WindowsFolderPicker` creates `Windows.Storage.Pickers.FolderPicker`, adds `"*"` to `FileTypeFilter`, and calls `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)` before `PickSingleFolderAsync`.

Keep the window-specific code behind these signatures:

```csharp
public sealed class WindowContext
{
    public MainWindow? MainWindow { get; private set; }
    public void SetMainWindow(MainWindow window) => MainWindow = window;
    public nint GetWindowHandle() => MainWindow is null
        ? throw new InvalidOperationException("Main window is not available.")
        : WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
}

public async Task<string?> PickFolderAsync(
    string title,
    CancellationToken cancellationToken)
{
    var picker = new Windows.Storage.Pickers.FolderPicker
    {
        SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary,
        CommitButtonText = title
    };
    picker.FileTypeFilter.Add("*");
    WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowContext.GetWindowHandle());
    Windows.Storage.StorageFolder? folder =
        await picker.PickSingleFolderAsync().AsTask(cancellationToken);
    return folder?.Path;
}
```

Settings controls use:

```text
InboxFolderPath, PickInboxFolder
LibraryFolderPath, PickLibraryFolder
WorkFolderPath, PickWorkFolder
SaveFolderSettings
SettingsValidationErrors
```

Path text boxes are read-only; users select folders through the picker. Bind validation errors visibly below the three rows.

Use one labeled row for each folder, bind the read-only `TextBox` to the matching path property, bind each button to its matching choose command, and bind the save button to `SaveCommand`. Render `Errors` through this stable container:

```xml
<ItemsControl AutomationProperties.AutomationId="SettingsValidationErrors"
              ItemsSource="{x:Bind ViewModel.Errors, Mode=OneWay}">
  <ItemsControl.ItemTemplate>
    <DataTemplate x:DataType="settings:SettingsError">
      <TextBlock Text="{x:Bind Message}" Foreground="{ThemeResource SystemFillColorCriticalBrush}" />
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

The three `TextBox`/`Button` pairs and save button must carry the `AutomationId` values listed above; the page must not permit free typing of physical paths.

- [ ] **Step 8: Run ViewModel tests and build the native app**

Run:

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj
dotnet build apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Debug -p:Platform=x64
```

Expected: tests pass; the WinUI project builds with zero warnings.

- [ ] **Step 9: Manual native-window smoke test**

Set `TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT` to a new temporary directory, launch the built EXE, and verify:

1. A window titled `TraceFold 媒体库` opens.
2. No browser and no console window opens.
3. The four navigation entries work.
4. First launch selects Settings and shows the setup InfoBar.
5. Invalid overlapping folders show validation errors.
6. Three valid test folders save successfully and readiness changes to `Ready`.
7. No file outside the temporary app-data and three selected test folders changes.

- [ ] **Step 10: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests apps/media-desktop/TraceFold.MediaDesktop.slnx
git commit -m "feat(media-desktop): add native three-column shell"
```

---

### Task 6: Enforce single instance and clean process shutdown

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Program.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Lifecycle/SingleInstanceCoordinator.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Lifecycle/AppShutdownCoordinator.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/App.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/MainWindow.xaml.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Lifecycle/AppShutdownCoordinatorTests.cs`

**Interfaces:**
- Consumes: `MainWindow`, Generic Host, and `WindowContext` from Task 5.
- Produces: single-instance key `TraceFold.MediaDesktop`, activation redirection to the existing window, and idempotent `AppShutdownCoordinator.StopAsync`.

- [ ] **Step 1: Write the failing idempotent shutdown test**

Define:

```csharp
public interface IDesktopHostStopper
{
    Task StopAsync(CancellationToken cancellationToken);
}
```

Call `AppShutdownCoordinator.StopAsync` twice concurrently and assert the fake stopper is invoked once. Add a second test where the stopper exceeds five seconds; the coordinator must cancel it, log `DesktopShutdownTimedOut`, and complete without throwing to the Window closed event.

Make the timeout constructor-injectable for a fast deterministic test while keeping the production default at five seconds:

```csharp
[Fact]
public async Task StopAsync_called_concurrently_stops_the_host_once()
{
    var stopper = new RecordingHostStopper();
    var coordinator = new AppShutdownCoordinator(
        stopper,
        new RecordingLogger<AppShutdownCoordinator>(),
        TimeSpan.FromSeconds(1));

    await Task.WhenAll(
        coordinator.StopAsync(CancellationToken.None),
        coordinator.StopAsync(CancellationToken.None));

    Assert.Equal(1, stopper.CallCount);
}

[Fact]
public async Task StopAsync_logs_timeout_and_returns_without_throwing()
{
    var logger = new RecordingLogger<AppShutdownCoordinator>();
    var stopper = new RecordingHostStopper(
        cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
    var coordinator = new AppShutdownCoordinator(
        stopper,
        logger,
        TimeSpan.FromMilliseconds(20));

    await coordinator.StopAsync(CancellationToken.None);

    Assert.True(stopper.LastToken.IsCancellationRequested);
    Assert.Contains(logger.Events, item =>
        item.Contains("DesktopShutdownTimedOut", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run the lifecycle test to verify it fails**

Run: `dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj --filter AppShutdownCoordinatorTests`

Expected: FAIL because the lifecycle types do not exist.

- [ ] **Step 3: Implement idempotent host shutdown**

Use a private lock and cached `_stopTask`. Link the caller token with a new five-second timeout token. Log failures, but never call `Environment.FailFast` and never delete app data. The production `DesktopHostStopper` calls `IHost.StopAsync` and then disposes the host.

Use `TimeSpan.FromSeconds(5)` when the optional timeout is omitted. Catch timeout cancellation separately from caller cancellation, log it as `DesktopShutdownTimedOut`, and swallow it only at this window-close boundary. Cache the full stop-and-dispose task so the host is disposed exactly once.

- [ ] **Step 4: Disable generated Main and add the single-instance entry point**

Append this project constant without overwriting generated constants:

```xml
<DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
```

In `Program.Main`, call `WinRT.ComWrappersSupport.InitializeComWrappers()`, get activation arguments from `AppInstance.GetCurrent()`, and call:

```csharp
AppInstance primary = AppInstance.FindOrRegisterForKey("TraceFold.MediaDesktop");
```

If `primary.IsCurrent` is false, redirect the current activation with `RedirectActivationToAsync` from a worker task and wait with `CoWaitForMultipleObjects`, following the Windows App SDK STA-safe pattern. Exit without building a host or opening a window. If current, pass `primary` to `App`, which owns the activation subscription, and start XAML with:

```csharp
Application.Start(_ =>
{
    var context = new DispatcherQueueSynchronizationContext(
        DispatcherQueue.GetForCurrentThread());
    SynchronizationContext.SetSynchronizationContext(context);
    _ = new App(primary);
});
```

The `SingleInstanceCoordinator` owns these exact P/Invoke declarations:

```csharp
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
private static extern nint CreateEvent(
    nint eventAttributes,
    [MarshalAs(UnmanagedType.Bool)] bool manualReset,
    [MarshalAs(UnmanagedType.Bool)] bool initialState,
    string? name);

[DllImport("kernel32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool SetEvent(nint handle);

[DllImport("kernel32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool CloseHandle(nint handle);

[DllImport("ole32.dll")]
private static extern uint CoWaitForMultipleObjects(
    uint flags,
    uint milliseconds,
    ulong handleCount,
    nint[] handles,
    out uint index);
```

Always close the event handle in `finally`. Treat a non-zero wait result as startup failure, log it to the Windows event/debug channel when the file logger is not yet available, and return a non-zero process exit code.

Implement the STA-safe redirect with a bounded wait:

```csharp
public static int RedirectToPrimary(
    AppInstance primary,
    AppActivationArguments activationArguments)
{
    nint completed = CreateEvent(0, manualReset: true, initialState: false, name: null);
    if (completed == 0)
    {
        return 1;
    }

    Exception? redirectError = null;
    try
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await primary.RedirectActivationToAsync(activationArguments);
            }
            catch (Exception exception)
            {
                redirectError = exception;
            }
            finally
            {
                SetEvent(completed);
            }
        });

        uint waitResult = CoWaitForMultipleObjects(
            flags: 0,
            milliseconds: 30_000,
            handleCount: 1,
            handles: [completed],
            out _);
        return waitResult == 0 && redirectError is null ? 0 : 1;
    }
    finally
    {
        CloseHandle(completed);
    }
}
```

`Program.Main` must obtain `AppActivationArguments activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs()` before registration, return `RedirectToPrimary(primary, activationArguments)` for a secondary launch, and build no service provider on that branch.

- [ ] **Step 5: Activate the existing window on redirected launch**

Pass the primary `AppInstance` into `App`. In its `Activated` handler, enqueue on `MainWindow.DispatcherQueue`, call `MainWindow.Activate()`, and use the window `AppWindow` to restore it when minimized. Do not open a second page, database, or host.

The UI-thread activation body is:

```csharp
MainWindow window = _windowContext.MainWindow
    ?? throw new InvalidOperationException("Main window is not available.");
if (window.AppWindow.Presenter is OverlappedPresenter presenter
    && presenter.State == OverlappedPresenterState.Minimized)
{
    presenter.Restore();
}
window.Activate();
```

- [ ] **Step 6: Wire window close to orderly shutdown**

On `MainWindow.Closed`, call `AppShutdownCoordinator.StopAsync`, call `AppInstance.GetCurrent().UnregisterKey()`, flush and close Serilog, and let the process exit. Guard the handler so it runs once. `App.UnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` log structured events without displaying stack traces in the UI.

- [ ] **Step 7: Run lifecycle tests and manual two-launch test**

Run:

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj
dotnet build apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Debug -p:Platform=x64
```

Then launch the EXE twice. Expected: the first window is brought to the foreground, only one long-lived `TraceFold.Media` process remains, and closing it leaves no desktop process behind.

- [ ] **Step 8: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests
git commit -m "feat(media-desktop): enforce single instance lifecycle"
```

---

### Task 7: Publish and verify the stage-1 offline checkpoint

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Properties/PublishProfiles/win-x64.pubxml`
- Create: `scripts/publish-media-desktop.ps1`
- Create: `scripts/smoke-media-desktop-package.ps1`
- Create: `apps/media-desktop/README.md`

**Interfaces:**
- Consumes: the completed native EXE, single-instance behavior, and local-data override from Tasks 3–6.
- Produces: `dist/TraceFold-MediaDesktop-Stage1-win-x64/`, its ZIP archive, and repeatable publish/smoke commands. This checkpoint intentionally does not bundle FFmpeg; FFmpeg first appears in stage 3.

- [ ] **Step 1: Add the self-contained publish profile**

Create `win-x64.pubxml`:

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <PublishTrimmed>false</PublishTrimmed>
    <PublishSingleFile>false</PublishSingleFile>
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Write the guarded publish script**

The script resolves the repository root from `$PSScriptRoot`, fixes the output to `dist\TraceFold-MediaDesktop-Stage1-win-x64`, and verifies the resolved output begins with the resolved repository `dist` directory before any recursive removal. Implement the complete script as:

```powershell
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
$packageDir = [IO.Path]::GetFullPath(
    (Join-Path $distRoot 'TraceFold-MediaDesktop-Stage1-win-x64'))
$zipPath = [IO.Path]::GetFullPath(
    (Join-Path $distRoot 'TraceFold-MediaDesktop-Stage1-win-x64.zip'))
$requiredPrefix = $distRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar

if (-not $packageDir.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe publish directory: $packageDir"
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $DotNetArguments)
    & dotnet @DotNetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE"
    }
}

$solution = Join-Path $repoRoot 'apps\media-desktop\TraceFold.MediaDesktop.slnx'
$project = Join-Path $repoRoot 'apps\media-desktop\src\TraceFold.Media.Desktop\TraceFold.Media.Desktop.csproj'
Invoke-DotNet -DotNetArguments @('restore', $solution)
Invoke-DotNet -DotNetArguments @('test', $solution, '-c', 'Release', '--no-restore')
Invoke-DotNet -DotNetArguments @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishProfile=win-x64',
    '-o', $packageDir)

$exePath = Join-Path $packageDir 'TraceFold.Media.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Published executable is missing: $exePath"
}
if (Get-ChildItem -LiteralPath $packageDir -Recurse -File -Filter '*.pdb') {
    throw 'Published package contains PDB files.'
}
$forbidden = @('python.exe', 'uvicorn', 'uvicorn.exe', 'node.exe', 'msedge.exe', 'chrome.exe')
$forbiddenFiles = @(Get-ChildItem -LiteralPath $packageDir -Recurse -File |
    Where-Object { $forbidden -contains $_.Name.ToLowerInvariant() })
if ($forbiddenFiles.Count -ne 0) {
    throw "Published package contains forbidden executables: $($forbiddenFiles.Name -join ', ')"
}

Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath
Write-Output "Published: $packageDir"
Write-Output "Archive: $zipPath"
```

Fail if `TraceFold.Media.exe` is absent, if a `.pdb` remains, or if the output contains `python.exe`, `uvicorn.exe`, `node.exe`, `msedge.exe`, or `chrome.exe`. Create `dist\TraceFold-MediaDesktop-Stage1-win-x64.zip` only after all checks pass.

- [ ] **Step 3: Write the package smoke script**

The script must:

1. Resolve the package EXE and create a unique root beneath `[IO.Path]::GetTempPath()`.
2. Set `TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT` to that root for child processes.
3. Record current Edge and Chrome process IDs.
4. Launch the EXE and wait up to 15 seconds for a non-empty `MainWindowTitle`.
5. Launch the EXE a second time and require that process to exit within 10 seconds.
6. Assert exactly one package process remains and no new Edge or Chrome process was created.
7. Call `CloseMainWindow()` on the primary process and require exit within 10 seconds.
8. Assert `<tempRoot>\media-desktop.db` and at least one rolling log exist.
9. In `finally`, verify the resolved temporary root starts with the system temporary directory and matches the script-created GUID name before `Remove-Item -LiteralPath ... -Recurse -Force`.

Implement those requirements with this complete script:

```powershell
[CmdletBinding()]
param(
    [string] $PackageDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot 'dist\TraceFold-MediaDesktop-Stage1-win-x64'
}
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
$exePath = [IO.Path]::GetFullPath((Join-Path $packageRoot 'TraceFold.Media.exe'))
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Package executable is missing: $exePath"
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeName = "tracefold-media-desktop-smoke-$([Guid]::NewGuid().ToString('N'))"
$tempRoot = [IO.Path]::GetFullPath((Join-Path $tempBase $smokeName))
$oldDataRoot = $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT
$primary = $null
$secondary = $null

function Get-BrowserProcessIds {
    return @(Get-Process -Name 'msedge', 'chrome' -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id)
}

function Get-PackageProcesses {
    return @(Get-Process -Name 'TraceFold.Media' -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                [IO.Path]::GetFullPath($_.Path) -eq $exePath
            }
            catch {
                $false
            }
        })
}

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT = $tempRoot
    $browserBefore = @(Get-BrowserProcessIds)

    $primary = Start-Process -FilePath $exePath -PassThru -WindowStyle Normal
    $windowDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 200
        $primary.Refresh()
        if ($primary.HasExited) {
            throw "Primary process exited early with code $($primary.ExitCode)."
        }
    } while ([string]::IsNullOrWhiteSpace($primary.MainWindowTitle) -and
             [DateTime]::UtcNow -lt $windowDeadline)

    if ([string]::IsNullOrWhiteSpace($primary.MainWindowTitle)) {
        throw 'Native main window did not appear within 15 seconds.'
    }

    $secondary = Start-Process -FilePath $exePath -PassThru -WindowStyle Normal
    if (-not $secondary.WaitForExit(10_000)) {
        throw 'Secondary launch did not redirect and exit within 10 seconds.'
    }

    $packageProcesses = @(Get-PackageProcesses)
    if ($packageProcesses.Count -ne 1) {
        throw "Expected one package process, found $($packageProcesses.Count)."
    }

    $browserAfter = @(Get-BrowserProcessIds)
    $newBrowserIds = @($browserAfter | Where-Object { $_ -notin $browserBefore })
    if ($newBrowserIds.Count -ne 0) {
        throw "Application opened a browser process: $($newBrowserIds -join ', ')"
    }

    if (-not $primary.CloseMainWindow()) {
        throw 'Primary native window did not accept CloseMainWindow.'
    }
    if (-not $primary.WaitForExit(10_000)) {
        throw 'Primary process did not exit cleanly within 10 seconds.'
    }

    if (-not (Test-Path -LiteralPath (Join-Path $tempRoot 'media-desktop.db') -PathType Leaf)) {
        throw 'Stage-1 database was not created under the smoke root.'
    }
    $logs = @(Get-ChildItem -LiteralPath (Join-Path $tempRoot 'logs') -File -Filter 'tracefold-*.log')
    if ($logs.Count -eq 0) {
        throw 'No rolling log was created under the smoke root.'
    }

    Write-Output 'PASS: native window, single instance, offline startup, and clean exit verified'
}
finally {
    @(Get-PackageProcesses) | ForEach-Object {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $oldDataRoot) {
        Remove-Item Env:\TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT = $oldDataRoot
    }

    $tempPrefix = $tempBase.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if ($tempRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($tempRoot) -eq $smokeName -and
        (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
```

Print one final line on success:

```text
PASS: native window, single instance, offline startup, and clean exit verified
```

- [ ] **Step 4: Document source and package verification**

`apps/media-desktop/README.md` must contain these exact entry commands:

```powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx
dotnet run --project apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Debug -p:Platform=x64
powershell -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1
powershell -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1
```

State explicitly that stage 1 is a foundation checkpoint, uses an empty database, does not scan or move media, and does not yet include FFmpeg.

- [ ] **Step 5: Run the full verification sequence**

Run:

```powershell
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release
powershell -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1
powershell -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1
git status --short
```

Expected:

- all tests pass;
- build has zero warnings;
- publish creates the folder and ZIP;
- smoke script prints the exact PASS line;
- no browser or console stays open;
- `git status --short` shows only the intended plan implementation files, because `dist/` and app `bin/obj` are ignored.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Properties apps/media-desktop/README.md scripts/publish-media-desktop.ps1 scripts/smoke-media-desktop-package.ps1
git commit -m "build(media-desktop): publish stage-one offline checkpoint"
```

- [ ] **Step 7: Record the stage-1 acceptance result**

Before writing the acceptance record, copy the ZIP to a clean Windows 11 x64 VM or Windows Sandbox that has no .NET SDK, Visual Studio, Python, Node.js, Edge automation, or repository checkout. Disconnect that guest from the network, expand the ZIP, double-click `TraceFold.Media.exe`, complete the three-folder setup using guest-local test folders, launch it again to verify single-instance activation, and close the window. Run the smoke script from the host only as an additional automated check; the clean-guest launch is a required release gate, not optional evidence.

Create `docs/media-desktop-stage1-acceptance.md` containing the commit hash, build-machine OS build, clean-guest OS build and installed-runtime evidence, .NET SDK version used for compilation, test counts, ZIP name and SHA-256, automated smoke PASS line, clean-guest manual result, and the explicit statement:

```text
Stage 1 does not scan, process, move, rename, or delete user media files.
```

Run `Get-FileHash dist\TraceFold-MediaDesktop-Stage1-win-x64.zip -Algorithm SHA256`, insert the real value, and commit:

```powershell
git add docs/media-desktop-stage1-acceptance.md
git commit -m "docs(media-desktop): record stage-one acceptance"
```

## Final Verification Gate

Before reporting stage 1 complete, run all commands below from the repository root and capture their output in the acceptance document:

```powershell
dotnet --version
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build
powershell -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1
powershell -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1
git status --short --branch
```

Expected final state:

- SDK is `10.0.300`.
- Every architecture, application, infrastructure, and desktop test passes.
- Release build reports zero warnings and zero errors.
- `TraceFold.Media.exe` opens a native WinUI window without browser or console.
- Repeated launch activates the existing instance.
- Closing the window stops the host and leaves no app process.
- The package uses only its own empty database under the smoke-test data root.
- The same ZIP has run with networking disconnected on a clean Windows 11 x64 guest without developer tools.
- The worktree is clean after the acceptance commit.
