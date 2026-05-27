using System.Collections.Generic;
using PgSqlParser;

namespace Raven.Server.Integrations.PostgreSQL.PowerBI
{
    // Data records produced by PowerBI recognition (PowerBIWrapperRecognizer / PowerBIShapeClassifier)
    // and consumed by the rewriters in PowerBIDirectQuery. Lifted out of PowerBIDirectQuery's private
    // nested types so the recognizer and classifier can live in their own files. See
    // POWERBI-REFACTOR-DESIGN.md for the architectural rationale.

    internal sealed record DirectQueryShape(
        List<string> ProjectionCols,
        int Limit);

    internal sealed record Aggregate(
        string FunctionName,
        string FieldName,
        string OutputColumn);

    internal sealed record GroupedAggregateShape(
        List<string> GroupByFields,
        List<Aggregate> Aggregates,
        List<string> OrderByCols,
        List<bool> OrderByDescFlags,
        int Limit);

    internal enum GroupedOrderByKind
    {
        Output,
        GroupKey
    }

    internal sealed record GroupedAggregateOrderByPart(
        GroupedOrderByKind Kind,
        int? GroupKeyIndex,
        int? AggregateIndex,
        bool Desc);

    internal sealed record GroupedAggregateRqlParts(
        string FromText,
        string WhereText,
        List<string> GroupByFields,
        List<Aggregate> Aggregates,
        List<GroupedAggregateOrderByPart> OrderBy,
        int Limit);

    internal sealed record NormalizedWrapper(
        List<string> OuterProjectedColumns,
        List<string> GroupByColumns,
        List<string> OrderByColumns,
        List<bool> OrderByDescFlags,
        Node OuterWhereClause,
        int? Limit,
        int? Offset,
        List<Aggregate> Aggregates);
}
