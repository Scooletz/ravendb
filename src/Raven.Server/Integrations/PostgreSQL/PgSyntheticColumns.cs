using System;

namespace Raven.Server.Integrations.PostgreSQL
{
    // PG-facing names for the two synthetic columns the PG endpoint exposes for every Raven
    // collection: the document identifier and the document's full JSON payload.
    //
    // RavenDB's RQL syntax uses the function-call forms `id()` / `json()` (and the
    // `Constants.Documents.Indexing.Fields.DocumentIdFieldName` / `PowerBIJsonFieldName`
    // constants are pinned to those literals because RQL parsing depends on them). Postgres
    // identifier syntax doesn't allow parentheses unless the identifier is double-quoted, and
    // PG-aware clients — notably PowerBI's mashup engine when interpreting PK metadata from
    // `information_schema.key_column_usage` — fall over when the metadata reports a column
    // name they can't parse as a bare identifier (`Nullable object must have a value`).
    //
    // Solution: keep `id()` / `json()` as the RQL-side names, but rename to `id` / `json` at
    // the PG endpoint surface (RowDescription, information_schema responses). Recognition of
    // legacy `id()` / `json()` references in client-supplied SQL is preserved for backward
    // compatibility with cached PowerBI metadata.
    internal static class PgSyntheticColumns
    {
        public const string DocumentId = "id";
        public const string Json = "json";

        // Legacy names still emitted by PowerBI for tables whose metadata was cached before
        // the rename. Recognized in SQL projections and routed back to the same RQL functions.
        public const string LegacyDocumentId = "id()";
        public const string LegacyJson = "json()";

        // True for `id` (current) or `id()` (legacy).
        public static bool IsDocumentIdColumn(string name) =>
            string.Equals(name, DocumentId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, LegacyDocumentId, StringComparison.OrdinalIgnoreCase);

        // True for `json` (current) or `json()` (legacy).
        public static bool IsJsonColumn(string name) =>
            string.Equals(name, Json, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, LegacyJson, StringComparison.OrdinalIgnoreCase);

        // True if the name is either synthetic column in either form.
        public static bool IsSyntheticColumn(string name) =>
            IsDocumentIdColumn(name) || IsJsonColumn(name);
    }
}
