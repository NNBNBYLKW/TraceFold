using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TraceFold.Media.Desktop.Diagnostics;

/// <summary>
/// Open times of the probe's own connections, shared by the connection and command
/// interceptors so a command failure can report how long its connection had been open.
/// </summary>
internal static class PackageProbeConnectionLifetimes
{
    private static readonly ConditionalWeakTable<DbConnection, Lifetime> Lifetimes = new();

    public static void Opened(DbConnection connection) =>
        Lifetimes.AddOrUpdate(connection, new Lifetime(DateTimeOffset.UtcNow));

    public static bool TryGet(DbConnection connection, out DateTimeOffset openedUtc)
    {
        if (Lifetimes.TryGetValue(connection, out Lifetime? lifetime))
        {
            openedUtc = lifetime.OpenedUtc;
            return true;
        }

        openedUtc = default;
        return false;
    }

    private sealed class Lifetime(DateTimeOffset openedUtc)
    {
        public DateTimeOffset OpenedUtc { get; } = openedUtc;
    }
}

/// <summary>
/// Probe-only connection forensics: records each connection's database path, requested
/// mode, open and close time and the state of its files. Observations are best effort:
/// every one is guarded and the interceptor returns the same completed task as the base
/// class, so it can never change a probe result.
/// </summary>
internal sealed class PackageProbeConnectionTrace : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (!PackageProbeTrace.CaptureEnabled)
        {
            return;
        }

        try
        {
            PackageProbeConnectionLifetimes.Opened(connection);
            if (connection is SqliteConnection sqlite)
            {
                PackageProbeTrace.WriteConnectionState("opened", sqlite);
            }
        }
        catch (Exception error)
        {
            PackageProbeTrace.Write("connection.opened.unavailable type="
                + error.GetType().FullName);
        }
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ConnectionOpened(connection, eventData);
        return Task.CompletedTask;
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData) =>
        ObserveClose(connection);

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        ObserveClose(connection);
        return Task.CompletedTask;
    }

    private static void ObserveClose(DbConnection connection)
    {
        if (!PackageProbeTrace.CaptureEnabled)
        {
            return;
        }

        try
        {
            DateTimeOffset closedUtc = DateTimeOffset.UtcNow;
            string open = PackageProbeConnectionLifetimes.TryGet(connection, out DateTimeOffset openedUtc)
                ? "; openedUtc=" + openedUtc.ToString("O")
                    + "; openMs=" + (long)(closedUtc - openedUtc).TotalMilliseconds
                : "; openedUtc=unknown";
            PackageProbeTrace.Write("connection closed"
                + "; db=" + DataSourceOf(connection)
                + open
                + "; closedUtc=" + closedUtc.ToString("O"));
        }
        catch (Exception error)
        {
            PackageProbeTrace.Write("connection.closed.unavailable type="
                + error.GetType().FullName);
        }
    }

    private static string DataSourceOf(DbConnection connection)
    {
        try
        {
            return connection is SqliteConnection sqlite
                ? new SqliteConnectionStringBuilder(sqlite.ConnectionString).DataSource
                : connection.GetType().FullName ?? "unknown";
        }
        catch (Exception error)
        {
            return "unreadable:" + error.GetType().Name;
        }
    }
}

/// <summary>
/// Probe-only command forensics: at the moment a command fails it snapshots the failing
/// connection - database path, requested mode, open time, connection state, native
/// read-only state and the state of its files - which is where the real SqliteException
/// is still visible. Best effort, guarded, and it cannot change the probe result.
/// </summary>
internal sealed class PackageProbeCommandTrace : DbCommandInterceptor
{
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        ObserveFailure(command, eventData.Exception);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ObserveFailure(command, eventData.Exception);
        return Task.CompletedTask;
    }

    private static void ObserveFailure(DbCommand command, Exception? error)
    {
        if (!PackageProbeTrace.CaptureEnabled)
        {
            return;
        }

        try
        {
            DbConnection? connection = command.Connection;
            if (connection is not SqliteConnection sqlite)
            {
                PackageProbeTrace.Write("connection command_failed"
                    + "; db=" + (connection is null
                        ? "none"
                        : connection.GetType().FullName ?? "unknown"));
                PackageProbeTrace.WriteException("command_failed", error);
                return;
            }

            var options = new SqliteConnectionStringBuilder(sqlite.ConnectionString);
            string lifetime = PackageProbeConnectionLifetimes.TryGet(
                sqlite,
                out DateTimeOffset openedUtc)
                ? "; openedUtc=" + openedUtc.ToString("O")
                    + "; openMs=" + (long)(DateTimeOffset.UtcNow - openedUtc).TotalMilliseconds
                : "; openedUtc=unknown";
            PackageProbeTrace.Write("connection command_failed"
                + "; db=" + options.DataSource
                + "; requested=" + options.Mode
                + "; pooling=" + options.Pooling
                + "; state=" + sqlite.State
                + lifetime);
            PackageProbeTrace.WriteConnectionState("command_failed", sqlite);
            PackageProbeTrace.WriteException("command_failed", error);
        }
        catch (Exception observationError)
        {
            PackageProbeTrace.Write("connection.command_failed.unavailable type="
                + observationError.GetType().FullName);
        }
    }
}
