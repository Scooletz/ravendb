using System.Threading.Tasks;
using Raven.Server.Documents.Sharding.Handlers.Processors.ETL;
using Raven.Server.Routing;

namespace Raven.Server.Documents.Sharding.Handlers;

public sealed class ShardedTaskErrorsHandler : ShardedDatabaseRequestHandler
{
    [RavenShardedAction("/databases/*/task-errors", "GET")]
    public async Task GetAllErrors()
    {
        using (var processor = new ShardedTaskErrorsHandlerProcessorForGetAllErrors(this))
            await processor.ExecuteAsync();
    }

    [RavenShardedAction("/databases/*/task-errors", "DELETE")]
    public async Task DeleteAllErrors()
    {
        using (var processor = new ShardedTaskErrorsHandlerProcessorForDeleteAllErrors(this))
            await processor.ExecuteAsync();
    }
}
