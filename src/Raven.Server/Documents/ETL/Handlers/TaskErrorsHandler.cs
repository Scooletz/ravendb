using System.Threading.Tasks;
using Raven.Server.Documents.ETL.Handlers.Processors;
using Raven.Server.Routing;

namespace Raven.Server.Documents.ETL.Handlers;

public sealed class TaskErrorsHandler : DatabaseRequestHandler
{
    [RavenAction("/databases/*/task-errors", "DELETE", AuthorizationStatus.ValidUser, EndpointType.Write)]
    public async Task DeleteAllErrors()
    {
        using (var processor = new TaskErrorsHandlerProcessorForDeleteAllErrors(this))
            await processor.ExecuteAsync();
    }
}
