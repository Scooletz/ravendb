using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog
{
    internal abstract class ScalarFunction
    {
        public abstract string Name { get; }
        public abstract string ResultColumnName { get; }
        public abstract PgType PgType { get; }

        public abstract bool TryEvaluate(IReadOnlyList<object> args, out object result);
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

        public override bool TryEvaluate(IReadOnlyList<object> args, out object result)
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

        public override bool TryEvaluate(IReadOnlyList<object> args, out object result)
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
}
