using System.Text.Json;
using System.Text;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Infrastructure.Diagnostics;
using TraceFold.Media.Infrastructure.Settings;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Diagnostics;

public sealed class RuntimeAndLogDiagnosticProbeTests
{
    private const string SourceCommit =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string InformationalVersion =
        "4.0.0-stage5cb+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Missing_manifest_is_attention_in_a_development_layout()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe()
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Attention, item.Severity);
        Assert.Equal("diagnostics.runtime.manifest_missing", item.ErrorCode);
    }

    [Fact]
    public async Task Manifest_must_match_the_assembly_informational_version()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        fixture.WriteManifest(
            stageCheckpoint: "5C-B",
            sourceCommit: new string('a', 40),
            informationalVersion: "4.0.0-stage5cb+different");

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe()
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.runtime.manifest_invalid", item.ErrorCode);
    }

    [Fact]
    public async Task Manifest_source_commit_must_match_the_version_suffix()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        fixture.WriteManifest(
            stageCheckpoint: "5C-B",
            sourceCommit: new string('b', 40),
            informationalVersion: InformationalVersion);

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe()
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.runtime.manifest_invalid", item.ErrorCode);
    }

    [Fact]
    public async Task Manifest_rejects_a_matching_but_noncanonical_version()
    {
        const string noncanonical = "4.0.0-stage5cb+short";
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        fixture.WriteManifest("5C-B", SourceCommit, noncanonical);

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe(
                informationalVersion: noncanonical)
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.runtime.manifest_invalid", item.ErrorCode);
    }

    [Theory]
    [InlineData("5C-B", "stage5cb")]
    [InlineData("5C-C", "stage5cc")]
    public async Task Valid_manifest_and_writable_log_root_are_healthy(
        string stageCheckpoint,
        string versionStage)
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        string version = $"4.0.0-{versionStage}+{SourceCommit}";
        fixture.WriteManifest(stageCheckpoint, SourceCommit, version);

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe(
                informationalVersion: version)
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Healthy, item.Severity);
        Assert.Null(item.ErrorCode);
        Assert.Contains(item.Facts, fact => fact.Key == "runtime.stage_checkpoint"
            && fact.Value == stageCheckpoint
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact => fact.Key == "runtime.source_commit"
            && fact.Value == SourceCommit
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact => fact.Key == "runtime.logs.writable"
            && fact.Value == "是");
    }

    [Theory]
    [InlineData("5C-B", "stage5cc", "stage5cc")]
    [InlineData("5C-C", "stage5cb", "stage5cb")]
    [InlineData("5C-C", "stage5cc", "stage5cb")]
    [InlineData("5C-D", "stage5cd", "stage5cd")]
    public async Task Stage_and_assembly_identity_must_both_match_the_manifest(
        string stageCheckpoint,
        string manifestStage,
        string assemblyStage)
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        fixture.WriteManifest(stageCheckpoint, SourceCommit,
            $"4.0.0-{manifestStage}+{SourceCommit}");

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe(
                informationalVersion: $"4.0.0-{assemblyStage}+{SourceCommit}")
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.runtime.manifest_invalid", item.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(InvalidManifestJson))]
    public async Task Manifest_schema_and_values_are_exact(string json)
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        File.WriteAllText(fixture.ManifestPath, json);

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe()
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.runtime.manifest_invalid", item.ErrorCode);
    }

    [Theory]
    [InlineData("NotRun")]
    [InlineData("Passed")]
    public async Task Stage5cc_manifest_accepts_the_publishers_probe_status(string probeStatus)
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        string version = $"4.0.0-stage5cc+{SourceCommit}";
        File.WriteAllText(fixture.ManifestPath, JsonSerializer.Serialize(new
        {
            StageCheckpoint = "5C-C",
            SourceCommit,
            InformationalVersion = version,
            ProductLaunchProbe = probeStatus
        }));

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe(
                informationalVersion: version)
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Healthy, item.Severity);
        Assert.Null(item.ErrorCode);
    }

    [Theory]
    [InlineData("5C-B", "stage5cb", "\"Passed\"")]
    [InlineData("5C-C", "stage5cc", "null")]
    [InlineData("5C-C", "stage5cc", "true")]
    [InlineData("5C-C", "stage5cc", "0")]
    [InlineData("5C-C", "stage5cc", "\"Unknown\"")]
    [InlineData("5C-C", "stage5cc", "\"passed\"")]
    public async Task Launch_probe_status_is_typed_and_limited_to_Stage5cc(
        string stage, string versionStage, string probeJson)
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        string version = $"4.0.0-{versionStage}+{SourceCommit}";
        File.WriteAllText(fixture.ManifestPath,
            "{\"StageCheckpoint\":\"" + stage + "\",\"SourceCommit\":\"" + SourceCommit
            + "\",\"InformationalVersion\":\"" + version + "\",\"ProductLaunchProbe\":" + probeJson + "}");

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe(
                informationalVersion: version)
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.runtime.manifest_invalid", item.ErrorCode);
    }

    public static TheoryData<string> InvalidManifestJson =>
        new()
        {
            "{ malformed",
            JsonSerializer.Serialize(new
            {
                StageCheckpoint = "5C",
                SourceCommit = new string('a', 40),
                InformationalVersion
            }),
            JsonSerializer.Serialize(new
            {
                StageCheckpoint = "5C-B",
                SourceCommit = new string('A', 40),
                InformationalVersion
            }),
            JsonSerializer.Serialize(new
            {
                StageCheckpoint = "5C-B",
                SourceCommit = new string('a', 39),
                InformationalVersion
            }),
            JsonSerializer.Serialize(new
            {
                StageCheckpoint = "5C-B",
                SourceCommit = new string('a', 40)
            }),
            JsonSerializer.Serialize(new
            {
                stageCheckpoint = "5C-B",
                SourceCommit = new string('a', 40),
                InformationalVersion
            }),
            JsonSerializer.Serialize(new
            {
                StageCheckpoint = "5C-B",
                SourceCommit = new string('a', 40),
                InformationalVersion,
                Unexpected = true
            })
        };

    [Fact]
    public async Task Manifest_larger_than_exactly_sixty_four_kibibytes_is_invalid()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        File.WriteAllText(fixture.ManifestPath, new string('x', (64 * 1024) + 1));

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe()
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.runtime.manifest_invalid", item.ErrorCode);
    }

    [Fact]
    public async Task Manifest_at_exact_limit_and_with_utf8_bom_is_accepted()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        string json = ValidManifestJson();
        string padded = json + new string(' ', (64 * 1024) - 3 - json.Length);
        File.WriteAllText(
            fixture.ManifestPath,
            padded,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe()
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(64 * 1024, new FileInfo(fixture.ManifestPath).Length);
        Assert.Equal(SystemDiagnosticSeverity.Healthy, item.Severity);
    }

    [Fact]
    public async Task Reparse_or_non_file_manifest_is_invalid_and_not_followed()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        using TestDirectory outside = TestDirectory.Create();
        string outsideManifest = Path.Combine(outside.Path, "outside.json");
        File.WriteAllText(outsideManifest, ValidManifestJson());
        File.CreateSymbolicLink(fixture.ManifestPath, outsideManifest);

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe()
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.runtime.manifest_invalid", item.ErrorCode);
        Assert.Equal(ValidManifestJson(), File.ReadAllText(outsideManifest));
    }

    [Fact]
    public async Task Product_runtime_and_roots_have_explicit_sensitivity()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        fixture.WriteValidManifest();

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe()
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Contains(item.Facts, fact => fact.Key == "runtime.product_version"
            && fact.Value == "4.0.0"
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact => fact.Key == "runtime.file_version"
            && fact.Value == "4.0.0.0"
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact => fact.Key == "runtime.informational_version"
            && fact.Value == InformationalVersion
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact => fact.Key == "runtime.os"
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact => fact.Key == "runtime.framework"
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact => fact.Key == "runtime.app_data_root"
            && fact.Value == fixture.DataRoot
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.LocalOnly);
        Assert.Contains(item.Facts, fact => fact.Key == "runtime.logs.path"
            && fact.Value == fixture.LogsRoot
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.LocalOnly);
        Assert.DoesNotContain(item.Facts, fact =>
            fact.Key.Contains("username", StringComparison.OrdinalIgnoreCase)
            || fact.Key.Contains("hostname", StringComparison.OrdinalIgnoreCase)
            || fact.Key.Contains("machine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_log_directory_is_attention_and_is_not_created()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: false);
        fixture.WriteValidManifest();

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe()
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Attention, item.Severity);
        Assert.Equal("diagnostics.logs.unavailable", item.ErrorCode);
        Assert.False(Directory.Exists(fixture.LogsRoot));
    }

    [Fact]
    public async Task Unwritable_log_directory_is_attention()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        fixture.WriteValidManifest();
        var directoryProbe = new FixedDirectoryProbe(exists: true, writable: false);

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe(directoryProbe)
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Attention, item.Severity);
        Assert.Equal("diagnostics.logs.unavailable", item.ErrorCode);
        Assert.Equal(1, directoryProbe.WriteCallCount);
    }

    [Fact]
    public async Task Real_log_write_probe_leaves_zero_residue()
    {
        using RuntimeFixture fixture = RuntimeFixture.Create(logDirectory: true);
        fixture.WriteValidManifest();
        string[] before = Directory.GetFiles(fixture.LogsRoot);

        SystemDiagnosticItem item = Assert.Single(await fixture.Probe(
            new LocalDirectoryProbe()).CheckAsync(
                TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Healthy, item.Severity);
        Assert.Equal(before, Directory.GetFiles(fixture.LogsRoot));
        Assert.Empty(Directory.GetFiles(
            fixture.LogsRoot,
            ".tracefold-write-probe-*.tmp"));
    }

    private static string ValidManifestJson() => JsonSerializer.Serialize(new
    {
        StageCheckpoint = "5C-B",
        SourceCommit,
        InformationalVersion
    });

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly TestDirectory _root;

        private RuntimeFixture(TestDirectory root)
        {
            _root = root;
            DataRoot = Path.Combine(root.Path, "private-user-data");
            LogsRoot = Path.Combine(DataRoot, "logs");
        }

        public string PackageRoot => _root.Path;

        public string DataRoot { get; }

        public string LogsRoot { get; }

        public string ManifestPath => Path.Combine(
            PackageRoot,
            "package-version.json");

        public static RuntimeFixture Create(bool logDirectory)
        {
            var fixture = new RuntimeFixture(TestDirectory.Create());
            Directory.CreateDirectory(fixture.DataRoot);
            if (logDirectory)
            {
                Directory.CreateDirectory(fixture.LogsRoot);
            }

            return fixture;
        }

        public RuntimeAndLogDiagnosticProbe Probe(
            IDirectoryProbe? directoryProbe = null,
            string informationalVersion = InformationalVersion) =>
            new(
                new RuntimeDiagnosticOptions(
                    PackageRoot,
                    new RuntimeProductIdentity(
                        "4.0.0",
                        "4.0.0.0",
                        informationalVersion)),
                new FixedAppDataPathProvider(new AppDataPaths(
                    DataRoot,
                    Path.Combine(DataRoot, "media-desktop.db"),
                    LogsRoot,
                    Path.Combine(DataRoot, "work"))),
                directoryProbe ?? new LocalDirectoryProbe());

        public void WriteValidManifest() =>
            File.WriteAllText(ManifestPath, ValidManifestJson());

        public void WriteManifest(
            string stageCheckpoint,
            string sourceCommit,
            string informationalVersion) =>
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new
            {
                StageCheckpoint = stageCheckpoint,
                SourceCommit = sourceCommit,
                InformationalVersion = informationalVersion
            }));

        public void Dispose() => _root.Dispose();
    }

    private sealed class FixedAppDataPathProvider(AppDataPaths paths)
        : IAppDataPathProvider
    {
        public AppDataPaths GetPaths() => paths;
    }

    private sealed class FixedDirectoryProbe(bool exists, bool writable)
        : IDirectoryProbe
    {
        public int WriteCallCount { get; private set; }

        public ValueTask<bool> ExistsAsync(
            string path,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(exists);

        public ValueTask<bool> CanWriteAsync(
            string path,
            CancellationToken cancellationToken)
        {
            WriteCallCount++;
            return ValueTask.FromResult(writable);
        }
    }
}
