using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Infrastructure.Filesystem;

namespace TraceFold.Media.Infrastructure.Diagnostics;

public sealed record RuntimeProductIdentity
{
    public RuntimeProductIdentity(
        string ProductVersion,
        string FileVersion,
        string InformationalVersion)
    {
        this.ProductVersion = Require(ProductVersion, nameof(ProductVersion));
        this.FileVersion = Require(FileVersion, nameof(FileVersion));
        this.InformationalVersion = Require(
            InformationalVersion,
            nameof(InformationalVersion));
    }

    public string ProductVersion { get; }

    public string FileVersion { get; }

    public string InformationalVersion { get; }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The version cannot be blank.", parameterName);
        }

        return value;
    }
}

public sealed record RuntimeDiagnosticOptions
{
    public RuntimeDiagnosticOptions(
        string BaseDirectory,
        RuntimeProductIdentity ProductIdentity)
    {
        if (string.IsNullOrWhiteSpace(BaseDirectory)
            || !Path.IsPathFullyQualified(BaseDirectory))
        {
            throw new ArgumentException(
                "The runtime base directory must be fully qualified.",
                nameof(BaseDirectory));
        }

        this.BaseDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(BaseDirectory));
        this.ProductIdentity = ProductIdentity
            ?? throw new ArgumentNullException(nameof(ProductIdentity));
    }

    public string BaseDirectory { get; }

    public RuntimeProductIdentity ProductIdentity { get; }

    public static RuntimeDiagnosticOptions FromAssembly(
        string baseDirectory,
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string productVersion = assembly.GetName().Version?.ToString()
            ?? "0.0.0.0";
        string fileVersion = string.IsNullOrWhiteSpace(assembly.Location)
            ? productVersion
            : FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion
                ?? productVersion;
        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? productVersion;
        return new RuntimeDiagnosticOptions(
            baseDirectory,
            new RuntimeProductIdentity(
                productVersion,
                fileVersion,
                informationalVersion));
    }
}

public sealed class RuntimeAndLogDiagnosticProbe(
    RuntimeDiagnosticOptions options,
    IAppDataPathProvider pathProvider,
    IDirectoryProbe directoryProbe) : ISystemDiagnosticProbe
{
    private const int MaximumManifestBytes = 64 * 1024;
    private const string Stage5CBVersionPrefix = "4.0.0-stage5cb+";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public SystemDiagnosticProbeDescriptor Descriptor { get; } =
        SystemDiagnosticProbeDescriptor.Create(
            "runtime",
            SystemDiagnosticCategory.RuntimeAndLogs,
            order: 0,
            failureSeverity: SystemDiagnosticSeverity.Unavailable,
            failureTitle: "运行环境检查失败",
            failureConclusion: "无法完成运行环境与日志检查",
            failureImpact: "当前无法确认程序版本或故障记录能力。",
            failureGuidance: "请重新检查；问题持续时重新安装完整程序包。",
            timeoutErrorCode: "diagnostics.probe.timeout");

    public async Task<IReadOnlyList<SystemDiagnosticItem>> CheckAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppDataPaths paths = pathProvider.GetPaths();
        ManifestObservation manifest = await ReadManifestAsync(cancellationToken);
        LogObservation logs = await ObserveLogsAsync(
            paths.LogsDirectory,
            cancellationToken);
        List<SystemDiagnosticFact> facts = CreateFacts(paths, manifest, logs);

        if (manifest.State == ManifestState.Invalid)
        {
            return [CreateIssue(
                SystemDiagnosticSeverity.Unavailable,
                "diagnostics.runtime.manifest_invalid",
                "程序包版本清单无效或与程序版本不一致",
                "无法确认当前程序文件来自同一个受支持的发布版本。",
                "请关闭程序并重新安装完整的正式包。",
                facts)];
        }

        if (manifest.State == ManifestState.Missing)
        {
            return [CreateIssue(
                SystemDiagnosticSeverity.Attention,
                "diagnostics.runtime.manifest_missing",
                "未找到程序包版本清单",
                "开发运行可以继续，但无法核对发布阶段与源版本。",
                "正式测试请使用包含 package-version.json 的完整程序包。",
                facts)];
        }

        if (!logs.Exists || !logs.Writable)
        {
            return [CreateIssue(
                SystemDiagnosticSeverity.Attention,
                "diagnostics.logs.unavailable",
                logs.Exists ? "日志目录不可写" : "日志目录不存在或不可访问",
                "媒体管理仍可运行，但发生故障时可能无法保存排查证据。",
                "请检查应用数据目录权限，然后重新检查。",
                facts)];
        }

        return [SystemDiagnosticItem.Create(
            "runtime.environment",
            SystemDiagnosticCategory.RuntimeAndLogs,
            SystemDiagnosticSeverity.Healthy,
            order: 0,
            title: "运行环境与日志",
            conclusion: "程序版本清单有效，日志目录可用",
            impact: null,
            guidance: null,
            errorCode: null,
            facts,
            actions: [OpenLogsAction()])];
    }

    private async Task<ManifestObservation> ReadManifestAsync(
        CancellationToken cancellationToken)
    {
        string manifestPath;
        try
        {
            manifestPath = ControlledPathGuard.RequireDirectChild(
                options.BaseDirectory,
                Path.Combine(options.BaseDirectory, "package-version.json"));
            if (Directory.Exists(manifestPath))
            {
                return ManifestObservation.Invalid;
            }

            if (!File.Exists(manifestPath))
            {
                return ManifestObservation.Missing;
            }

            ControlledReparseGuard.RejectPath(
                options.BaseDirectory,
                manifestPath,
                includeCandidate: true);
            var info = new FileInfo(manifestPath);
            if (info.Length > MaximumManifestBytes)
            {
                return ManifestObservation.Invalid;
            }

            byte[] content = new byte[checked((int)info.Length)];
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumManifestBytes)
            {
                return ManifestObservation.Invalid;
            }

            await stream.ReadExactlyAsync(content, cancellationToken);
            int byteOffset = content.AsSpan().StartsWith(
                [(byte)0xEF, (byte)0xBB, (byte)0xBF])
                ? 3
                : 0;
            string json = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(content.AsSpan(byteOffset));
            PackageVersionManifest? manifest =
                JsonSerializer.Deserialize<PackageVersionManifest>(
                    json,
                    ManifestJsonOptions);
            if (!IsValid(manifest))
            {
                return ManifestObservation.Invalid;
            }

            return new ManifestObservation(
                ManifestState.Valid,
                manifest!.StageCheckpoint,
                manifest.SourceCommit);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is ControlledPathException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or JsonException
            or DecoderFallbackException
            or OverflowException)
        {
            return ManifestObservation.Invalid;
        }
    }

    private async Task<LogObservation> ObserveLogsAsync(
        string logsDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            bool exists = await directoryProbe.ExistsAsync(
                logsDirectory,
                cancellationToken);
            bool writable = exists && await directoryProbe.CanWriteAsync(
                logsDirectory,
                cancellationToken);
            return new LogObservation(exists, writable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return new LogObservation(Exists: false, Writable: false);
        }
    }

    private bool IsValid(PackageVersionManifest? manifest)
    {
        if (manifest is null
            || !string.Equals(
                manifest.StageCheckpoint,
                "5C-B",
                StringComparison.Ordinal)
            || !IsLowercaseCommit(manifest.SourceCommit))
        {
            return false;
        }

        string expectedInformationalVersion =
            Stage5CBVersionPrefix + manifest.SourceCommit;
        return string.Equals(
                manifest.InformationalVersion,
                expectedInformationalVersion,
                StringComparison.Ordinal)
            && string.Equals(
                options.ProductIdentity.InformationalVersion,
                expectedInformationalVersion,
                StringComparison.Ordinal);
    }

    private static bool IsLowercaseCommit(string? value) =>
        value is { Length: 40 }
        && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private List<SystemDiagnosticFact> CreateFacts(
        AppDataPaths paths,
        ManifestObservation manifest,
        LogObservation logs)
    {
        var facts = new List<SystemDiagnosticFact>
        {
            Fact(
                "runtime.product_version",
                "产品版本",
                options.ProductIdentity.ProductVersion,
                SystemDiagnosticFactSensitivity.Shareable,
                order: 0),
            Fact(
                "runtime.file_version",
                "文件版本",
                options.ProductIdentity.FileVersion,
                SystemDiagnosticFactSensitivity.Shareable,
                order: 1),
            Fact(
                "runtime.informational_version",
                "信息版本",
                options.ProductIdentity.InformationalVersion,
                SystemDiagnosticFactSensitivity.Shareable,
                order: 2),
            Fact(
                "runtime.os",
                "Windows 运行环境",
                RuntimeInformation.OSDescription,
                SystemDiagnosticFactSensitivity.Shareable,
                order: 3),
            Fact(
                "runtime.framework",
                ".NET 运行时",
                RuntimeInformation.FrameworkDescription,
                SystemDiagnosticFactSensitivity.Shareable,
                order: 4),
            Fact(
                "runtime.app_data_root",
                "应用数据目录",
                paths.RootDirectory,
                SystemDiagnosticFactSensitivity.LocalOnly,
                order: 5),
            Fact(
                "runtime.logs.path",
                "日志目录",
                paths.LogsDirectory,
                SystemDiagnosticFactSensitivity.LocalOnly,
                order: 6),
            Fact(
                "runtime.logs.exists",
                "日志目录存在",
                logs.Exists ? "是" : "否",
                SystemDiagnosticFactSensitivity.Shareable,
                order: 7),
            Fact(
                "runtime.logs.writable",
                "日志目录可写",
                logs.Writable ? "是" : "否",
                SystemDiagnosticFactSensitivity.Shareable,
                order: 8)
        };
        if (manifest.State == ManifestState.Valid)
        {
            facts.Add(Fact(
                "runtime.stage_checkpoint",
                "阶段检查点",
                manifest.StageCheckpoint!,
                SystemDiagnosticFactSensitivity.Shareable,
                order: 9));
            facts.Add(Fact(
                "runtime.source_commit",
                "源提交",
                manifest.SourceCommit!,
                SystemDiagnosticFactSensitivity.Shareable,
                order: 10));
        }

        return facts;
    }

    private static SystemDiagnosticItem CreateIssue(
        SystemDiagnosticSeverity severity,
        string errorCode,
        string conclusion,
        string impact,
        string guidance,
        IReadOnlyList<SystemDiagnosticFact> facts) =>
        SystemDiagnosticItem.Create(
            "runtime.environment",
            SystemDiagnosticCategory.RuntimeAndLogs,
            severity,
            order: 0,
            title: "运行环境与日志",
            conclusion,
            impact,
            guidance,
            errorCode,
            facts,
            actions:
            [
                OpenLogsAction(),
                SystemDiagnosticAction.Create(
                    SystemDiagnosticActionKind.CopyErrorCode,
                    SystemDiagnosticActionTarget.None,
                    "复制错误代码")
            ]);

    private static SystemDiagnosticAction OpenLogsAction() =>
        SystemDiagnosticAction.Create(
            SystemDiagnosticActionKind.OpenRegisteredLogFolder,
            SystemDiagnosticActionTarget.None,
            "打开日志目录");

    private static SystemDiagnosticFact Fact(
        string key,
        string label,
        string value,
        SystemDiagnosticFactSensitivity sensitivity,
        int order) =>
        SystemDiagnosticFact.Create(key, label, value, sensitivity, order);

    private sealed record PackageVersionManifest(
        string? StageCheckpoint,
        string? SourceCommit,
        string? InformationalVersion);

    private enum ManifestState
    {
        Missing,
        Valid,
        Invalid
    }

    private sealed record ManifestObservation(
        ManifestState State,
        string? StageCheckpoint,
        string? SourceCommit)
    {
        public static ManifestObservation Missing { get; } =
            new(ManifestState.Missing, null, null);

        public static ManifestObservation Invalid { get; } =
            new(ManifestState.Invalid, null, null);
    }

    private sealed record LogObservation(bool Exists, bool Writable);
}
