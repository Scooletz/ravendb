using System.Collections.Generic;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public static class PowerBIConfig
    {
        public static readonly PgTable TableSchemaResponse = new()
        {
            Columns = new List<PgColumn>
            {
                new PgColumn("pk_column_name", 0, PgName.Default, PgFormat.Binary),
                new PgColumn("fk_table_schema", 1, PgName.Default, PgFormat.Binary),
                new PgColumn("fk_table_name", 2, PgName.Default, PgFormat.Binary),
                new PgColumn("fk_column_name", 3, PgName.Default, PgFormat.Binary),
                new PgColumn("ordinal", 4, PgInt4.Default, PgFormat.Binary),
                new PgColumn("fk_name", 5, PgName.Default, PgFormat.Binary),
            }
        };

    }
}
