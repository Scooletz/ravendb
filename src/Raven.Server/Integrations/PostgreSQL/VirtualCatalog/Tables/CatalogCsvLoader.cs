using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using Raven.Server.Integrations.PostgreSQL.Types;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog.Tables
{
    internal static class CatalogCsvLoader
    {
        public static List<object[]> Load(string fileName, IReadOnlyList<PgVirtualColumn> columns)
        {
            var resourceName = "Raven.Server.Integrations.PostgreSQL.VirtualCatalog.Tables.Data." + fileName;
            var assembly = typeof(CatalogCsvLoader).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName)
                               ?? throw new FileNotFoundException($"Catalog CSV resource not found: {resourceName}");

            var rows = new List<object[]>();
            using var parser = new TextFieldParser(stream)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true,
            };
            parser.SetDelimiters(",");

            string[] header = null;
            int[] columnIndex = null;

            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                if (fields == null)
                    continue;

                if (header == null)
                {
                    header = fields;
                    columnIndex = new int[columns.Count];
                    for (int i = 0; i < columns.Count; i++)
                    {
                        columnIndex[i] = Array.FindIndex(header, h => string.Equals(h, columns[i].Name, StringComparison.OrdinalIgnoreCase));
                        if (columnIndex[i] < 0)
                            throw new InvalidDataException($"Column '{columns[i].Name}' missing from CSV header in {fileName}.");
                    }
                    continue;
                }

                var row = new object[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    var raw = fields[columnIndex[i]];
                    row[i] = ParseCell(raw, columns[i].PgType);
                }
                rows.Add(row);
            }

            return rows;
        }

        private static object ParseCell(string raw, PgType pgType)
        {
            if (raw == null || raw == "NULL")
                return null;

            return raw switch
            {
                "False" => false,
                "True" => true,
                _ => pgType.FromString(raw),
            };
        }
    }
}
