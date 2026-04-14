using System;
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

        public static readonly PgTable TableSchemaSecondaryResponse = new()
        {
            Columns = new List<PgColumn>
            {
                new PgColumn("pk_table_schema", 0, PgName.Default, PgFormat.Binary),
                new PgColumn("pk_table_name", 1, PgName.Default, PgFormat.Binary),
                new PgColumn("pk_column_name", 2, PgName.Default, PgFormat.Binary),
                new PgColumn("fk_column_name", 3, PgName.Default, PgFormat.Binary),
                new PgColumn("ordinal", 4, PgInt4.Default, PgFormat.Binary),
                new PgColumn("fk_name", 5, PgName.Default, PgFormat.Binary),
            }
        };

        public static readonly PgTable ConstraintsResponse = new()
        {
            Columns = new List<PgColumn>
            {
                new PgColumn("index_name", 0, PgText.Default, PgFormat.Binary),
                new PgColumn("column_name", 1, PgName.Default, PgFormat.Binary),
                new PgColumn("ordinal_position", 2, PgInt4.Default, PgFormat.Binary),
                new PgColumn("primary_key", 3, PgText.Default, PgFormat.Binary),
            }
        };

        public static readonly PgTable CharacterSetsResponse = new()
        {
            Columns = new List<PgColumn>
            {
                new PgColumn("character_set_name", 0, PgName.Default, PgFormat.Text),
            },
            Data = new List<PgDataRow>
            {
                new()
                {
                    ColumnData = new ReadOnlyMemory<byte>?[]
                    {
                        PgName.Default.ToBytes("UTF8", PgFormat.Text)
                    }
                },
            }
        };
    }
}
