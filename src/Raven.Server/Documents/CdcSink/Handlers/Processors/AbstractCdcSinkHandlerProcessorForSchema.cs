using JetBrains.Annotations;
using Raven.Server.Documents.Handlers.Processors;
using Sparrow.Json;

namespace Raven.Server.Documents.CdcSink.Handlers.Processors;

/// <summary>
/// Per-database-mode processor base for <c>POST /admin/cdc-sink/schema</c>. The non-sharded
/// derivation (<see cref="CdcSinkHandlerProcessorForSchema"/>) carries the full discovery +
/// source-verification pipeline; sharded callers do not derive from this — they hit
/// <c>ShardedCdcSinkHandler</c> and are rejected via <c>NotSupportedInShardingProcessor</c>
/// because CDC sinks are not supported on sharded databases as of yet.
/// </summary>
internal abstract class AbstractCdcSinkHandlerProcessorForSchema<TRequestHandler, TOperationContext>
    : AbstractDatabaseHandlerProcessor<TRequestHandler, TOperationContext>
    where TOperationContext : JsonOperationContext
    where TRequestHandler : AbstractDatabaseRequestHandler<TOperationContext>
{
    protected AbstractCdcSinkHandlerProcessorForSchema([NotNull] TRequestHandler requestHandler) : base(requestHandler)
    {
    }
}
