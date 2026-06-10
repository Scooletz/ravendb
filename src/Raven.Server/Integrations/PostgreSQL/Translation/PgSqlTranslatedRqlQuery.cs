using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Raven.Server.Documents;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.PowerBI;
using Sparrow.Json;

namespace Raven.Server.Integrations.PostgreSQL.Translation
{
    // Used for SQL queries that PgSqlToRqlTranslator handled with an EXPLICIT projection — i.e.
    // the user wrote `SELECT col1, col2 FROM t`, not `SELECT * FROM t`. The base RqlQuery class
    // is designed around PowerBI's needs and unconditionally appends `id()` and `json()` to the
    // result schema; that's right when serving PowerBI shapes (info_schema.columns reports those
    // pseudo-columns so PowerBI expects them), but wrong when a SQL client explicitly listed
    // its desired columns and would be surprised to find an extra `json()` blob alongside.
    //
    // SELECT * still goes through plain RqlQuery to preserve the all-visible-columns behavior.
    //
    // Const projections (`1 as "c0"` from PowerBI's row-preview shape) are supported here so
    // narrow projections with const markers route here — not to PowerBIRqlQuery — and avoid
    // picking up the unwanted id+json synthetic columns.
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
