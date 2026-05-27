using System;
using System.Collections.Generic;
using Raven.Server.Documents;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog
{
    internal sealed class VirtualQueryContext
    {
        public DocumentDatabase Database { get; init; }

        // Equality predicates pre-extracted from the current SELECT's WHERE clause, keyed by
        // column name (case-insensitive). Set by the interpreter before each TryExecuteAsRows run
        // so virtual tables can scope their enumeration without re-walking the SQL AST. Example:
        // information_schema.columns reads Predicates["table_name"] to introspect just one
        // collection instead of every one. Save/restore on recursive sub-FROM calls — the inner
        // SELECT has its own WHERE and predicate set.
        public IReadOnlyDictionary<string, object> Predicates { get; set; }
            = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }
}
