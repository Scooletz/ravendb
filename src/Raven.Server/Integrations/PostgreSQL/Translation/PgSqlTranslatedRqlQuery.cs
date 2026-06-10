using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Raven.Server.Documents;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.PowerBI;
using Sparrow.Json;

namespace Raven.Server.Integrations.PostgreSQL.Translation
{
    // For SQL queries the translator handled with an EXPLICIT projection (`SELECT col1, col2 FROM t`,
    // not `SELECT *`). Base RqlQuery unconditionally appends id()/json() — right for PowerBI shapes,
    // wrong for a SQL client that listed its columns and shouldn't get an extra json() blob. SELECT *
    // still goes through plain RqlQuery. Const projections (`1 as "c0"`) are supported here so a narrow
    // projection carrying a const marker routes here, not to PowerBIRqlQuery, and avoids id()+json().
    internal sealed class PgSqlTranslatedRqlQuery : RqlQuery
    {
        private readonly IReadOnlyList<ConstProjection> _constProjections;

        public PgSqlTranslatedRqlQuery(string queryString, int[] parametersDataTypes, DocumentDatabase documentDatabase, int? limit = null, IReadOnlyList<ConstProjection> constProjections = null)
            : base(queryString, parametersDataTypes, documentDatabase, limit)
        {
            _constProjections = constProjections is { Count: > 0 } ? constProjections : null;
        }

        protected override bool IncludeDocumentIdColumn => false;

        protected override bool IncludePowerBIJsonColumn => false;

        protected override async Task<ICollection<PgColumn>> GenerateSchema()
        {
            await base.GenerateSchema();

            if (_constProjections != null)
            {
                foreach (var cp in _constProjections)
                {
                    if (string.IsNullOrWhiteSpace(cp.ColumnName))
                        continue;
                    if (Columns.ContainsKey(cp.ColumnName))
                        continue;
                    Columns[cp.ColumnName] = new PgColumn(cp.ColumnName, (short)Columns.Count, cp.PgType, GetDefaultResultsFormat());
                }
            }

            return Columns.Values;
        }

        protected override void AfterRow(BlittableJsonReaderObject jsonResult, ReadOnlyMemory<byte>?[] row, short? jsonIndex)
        {
            base.AfterRow(jsonResult, row, jsonIndex);

            if (_constProjections == null)
                return;

            foreach (var cp in _constProjections)
            {
                if (cp.Value == null)
                    continue;
                if (Columns.TryGetValue(cp.ColumnName, out var col) == false)
                    continue;
                row[col.ColumnIndex] = cp.PgType.ToBytes(cp.Value, col.FormatCode);
            }
        }
    }
}
