using System.Threading.Tasks;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Web.Http;

namespace Raven.Server.Documents.ETL.Handlers.Processors;

internal sealed class TaskErrorsHandlerProcessorForDeleteAllErrors : AbstractTaskErrorsHandlerProcessorForDeleteAllErrors<DatabaseRequestHandler, DocumentsOperationContext>
{
    public TaskErrorsHandlerProcessorForDeleteAllErrors(DatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected override bool SupportsCurrentNode => true;

    protected override ValueTask HandleCurrentNodeAsync()
    {
        RequestHandler.Database.TaskErrorsStorage.DeleteAllErrors();

        return ValueTask.CompletedTask;
    }

    protected override Task HandleRemoteNodeAsync(ProxyCommand<object> command, OperationCancelToken token) => RequestHandler.ExecuteRemoteAsync(command, token.Token);
}
