using System;

namespace Raven.Server.Integrations.PostgreSQL.Translation
{
    // Machine-actionable categories for translation failures. Used to type the exception thrown
    // by the SQL→RQL translator when it bails on a shape it can't handle. Today these throws are
    // caught and swallowed inside TryParse — the dispatcher falls back to UnhandledQueryDiagnoser
    // for user-facing classification. The enum exists so the dispatcher can be migrated to short-
    // circuit the diagnoser for shapes the translator already classified, without re-parsing.
    internal enum TranslationFailureCategory
    {
        // Catch-all for ad-hoc messages that haven't been categorized yet.
        Other = 0,
        Join,
        Aggregate,
        GroupBy,
        Distinct,
        OrderBy,
        // SELECT projection shape we don't support (function calls in projection, expressions,
        // etc. that don't reduce to "column" or "aggregate(column)").
        SelectProjection,
        // Mixed aggregate + non-aggregated columns without a GROUP BY anchor — SQL forbids this,
        // and our translator rejects it with a clearer message than PG would.
        MixedAggregateAndNonAggregate,
    }

    // Carries the failure category up to TryParse's catch so the dispatcher can be wired (later)
    // to consume it. NotSupportedException base type keeps the existing catch at line 84 working
    // for any throw that hasn't been migrated yet.
    internal sealed class PgTranslationException : NotSupportedException
    {
        public TranslationFailureCategory Category { get; }

        public PgTranslationException(TranslationFailureCategory category, string message)
            : base(message)
        {
            Category = category;
        }
    }
}
