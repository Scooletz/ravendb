using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Raven.Server.Documents;
using Raven.Server.Integrations.PostgreSQL.Messages;
using Sparrow.Json;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    public class PowerBIRqlQuery : RqlQuery
    {
        private readonly Dictionary<string, ReplaceColumnValue> _replaces;

        public PowerBIRqlQuery(string queryString, int[] parametersDataTypes, DocumentDatabase documentDatabase, Dictionary<string, ReplaceColumnValue> replaces = null, int? limit = null) : base(queryString, parametersDataTypes, documentDatabase, limit)
        {
            _replaces = replaces;
        }

        protected override async Task<ICollection<PgColumn>> GenerateSchema()
        {
            await base.GenerateSchema();

            if (_replaces == null)
                return Columns.Values;

            var orderedReplaces = _replaces.Values
                .OrderBy(x => x.SrcColumnName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DstColumnName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // The longest possible chain is bounded by the number of replace entries.
            for (var i = 0; i < _replaces.Count; i++)
            {
                var added = false;

                foreach (var replace in orderedReplaces)
                {
                    if (string.IsNullOrEmpty(replace.DstColumnName))
                        continue;

                    if (string.Equals(replace.SrcColumnName, replace.DstColumnName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (Columns.ContainsKey(replace.DstColumnName))
                        continue;

                    if (Columns.TryGetValue(replace.SrcColumnName, out var originalColumn) == false)
                        continue;

                    if (Columns.TryAdd(replace.DstColumnName,
                            new PgColumn(replace.DstColumnName, (short)Columns.Count, originalColumn.PgType, originalColumn.FormatCode)))
                    {
                        added = true;
                    }
                }

                if (added == false)
                    break;
            }

            return Columns.Values;
        }

        protected override void HandleSpecialColumnsIfNeeded(string columnName, BlittableJsonReaderObject.PropertyDetails property, object value, ref ReadOnlyMemory<byte>?[] row)
        {
            if (_replaces == null) 
                return;

            if (_replaces.TryGetValue(columnName, out var replace))
            {
                var replaceColumn = Columns[replace.DstColumnName];

                object replacedValue = null;

                switch (property.Token & BlittableJsonReaderBase.TypesMask)
                {
                    case BlittableJsonToken.String:
                    case BlittableJsonToken.StartArray:
                    case BlittableJsonToken.StartObject:
                    case BlittableJsonToken.CompressedString:
                    case BlittableJsonToken.EmbeddedBlittable:
                        if (value != null)
                        {
                            var columnValue = value.ToString();

                            replacedValue = columnValue?.Replace(replace.OldValue?.ToString() ?? string.Empty, replace.NewValue.ToString());
                        }
                        break;
                }

                ReadOnlyMemory<byte>? replaceValueBytes;

                if (replacedValue != null) 
                    replaceValueBytes = GetValueByType(property, replacedValue, replaceColumn);
                else
                    replaceValueBytes = Array.Empty<byte>();

                row[replaceColumn.ColumnIndex] = replaceValueBytes;

                HandleSpecialColumnsIfNeeded(replace.DstColumnName, property, replacedValue, ref row);
            }
        }
    }
}
