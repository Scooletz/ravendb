using System;
using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog
{
    internal abstract class ScalarFunction
    {
        public abstract string Name { get; }
        public abstract string ResultColumnName { get; }
        public abstract PgType PgType { get; }

        // ctx allows context-aware functions (current_database needs ctx.Database.Name). Pre-existing
        // ones that don't need ctx just ignore it.
        public abstract bool TryEvaluate(IReadOnlyList<object> args, VirtualQueryContext ctx, out object result);
    }

    internal sealed class VersionFunction : ScalarFunction
    {
        // Matches the value historically returned from NpgsqlConfig.VersionResponse
        // (loaded from version_query.csv before the virtual-tables interpreter took over).
        private const string Version =
            "PostgreSQL 13.3, compiled by Visual C++ build 1914, 64-bit";

        public override string Name => "version";
        public override string ResultColumnName => "version";
        public override PgType PgType => PgText.Default;

        public override bool TryEvaluate(IReadOnlyList<object> args, VirtualQueryContext ctx, out object result)
        {
            if (args is { Count: > 0 })
            {
                result = null;
                return false;
            }

            result = Version;
            return true;
        }
    }

    internal sealed class CurrentSettingFunction : ScalarFunction
    {
        public override string Name => "current_setting";
        public override string ResultColumnName => "current_setting";
        public override PgType PgType => PgText.Default;

        public override bool TryEvaluate(IReadOnlyList<object> args, VirtualQueryContext ctx, out object result)
        {
            result = null;

            if (args is not { Count: 1 })
                return false;

            if (args[0] is not string setting)
                return false;

            if (string.Equals(setting, "max_index_keys", System.StringComparison.OrdinalIgnoreCase))
            {
                result = "32";
                return true;
            }

            return false;
        }
    }

    // Returns the authenticated PG-protocol username for the connection. pgAdmin's role probe
    // uses this in `WHERE rolname = current_user` to find the connected role in pg_roles.
    // Also covers `session_user` since we don't distinguish the two (no SET ROLE / SESSION
    // AUTHORIZATION semantics on this surface).
    internal sealed class CurrentUserFunction : ScalarFunction
    {
        private readonly string _aliasName;
        public CurrentUserFunction(string name = "current_user") { _aliasName = name; }

        public override string Name => _aliasName;
        public override string ResultColumnName => _aliasName;
        public override PgType PgType => PgName.Default;

        public override bool TryEvaluate(IReadOnlyList<object> args, VirtualQueryContext ctx, out object result)
        {
            result = null;
            if (args is { Count: > 0 })
                return false;
            if (string.IsNullOrEmpty(ctx?.Username))
                return false;
            result = ctx.Username;
            return true;
        }
    }

    // Returns the active RavenDB database name. Used by pgAdmin's
    // `WHERE db.datname = current_database()` probe against pg_database.
    internal sealed class CurrentDatabaseFunction : ScalarFunction
    {
        public override string Name => "current_database";
        public override string ResultColumnName => "current_database";
        public override PgType PgType => PgName.Default;

        public override bool TryEvaluate(IReadOnlyList<object> args, VirtualQueryContext ctx, out object result)
        {
            result = null;
            if (args is { Count: > 0 })
                return false;
            if (ctx?.Database == null)
                return false;
            result = ctx.Database.Name;
            return true;
        }
    }

    // Maps a PG encoding integer to its name. We always serve UTF8 (encoding id 6 in PG); for any
    // input we return "UTF8" since that's the only encoding our wire format produces.
    internal sealed class PgEncodingToCharFunction : ScalarFunction
    {
        public override string Name => "pg_encoding_to_char";
        public override string ResultColumnName => "pg_encoding_to_char";
        public override PgType PgType => PgName.Default;

        public override bool TryEvaluate(IReadOnlyList<object> args, VirtualQueryContext ctx, out object result)
        {
            result = "UTF8";
            return args is { Count: 1 };
        }
    }

    // pgAdmin uses this to decide whether to show "Create object" actions. Our PG endpoint is
    // read-only, so technically the user has no real privileges — but pgAdmin works in degraded
    // mode if we say no. Returning true keeps the UI usable; any DDL actually attempted later
    // would fail at the SQL-handling layer anyway (we don't implement DDL).
    internal sealed class HasDatabasePrivilegeFunction : ScalarFunction
    {
        public override string Name => "has_database_privilege";
        public override string ResultColumnName => "has_database_privilege";
        public override PgType PgType => PgBool.Default;

        public override bool TryEvaluate(IReadOnlyList<object> args, VirtualQueryContext ctx, out object result)
        {
            result = true;
            return args is { Count: >= 1 and <= 3 };
        }
    }

    // Returns the server process ID for the current backend. pgAdmin uses this to filter
    // pg_stat_*/pg_locks views to just the current connection. We don't model multiple PG
    // backends, so any stable integer is fine; the host process id is a reasonable choice.
    internal sealed class PgBackendPidFunction : ScalarFunction
    {
        private static readonly long Pid = System.Environment.ProcessId;

        public override string Name => "pg_backend_pid";
        public override string ResultColumnName => "pg_backend_pid";
        public override PgType PgType => PgInt4.Default;

        public override bool TryEvaluate(IReadOnlyList<object> args, VirtualQueryContext ctx, out object result)
        {
            result = Pid;
            return args is null or { Count: 0 };
        }
    }
}
