using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Raven.Client.Http;
using Raven.Server.Documents.Commands.ETL;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Web.Http;

namespace Raven.Server.Documents.Handlers.Processors;

internal sealed class TaskErrorsHandlerProcessorForDeleteErrors : AbstractHandlerProxyReadProcessor<object, DatabaseRequestHandler, DocumentsOperationContext>
{
    public TaskErrorsHandlerProcessorForDeleteErrors([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected override bool SupportsCurrentNode => true;

    protected override ValueTask HandleCurrentNodeAsync()
    {
        var names = RequestHandler.GetStringValuesQueryString("name", required: false);
        var storage = RequestHandler.Database.TaskErrorsStorage;

        if (names.Count > 0)
        {
            foreach (var name in names)
                storage.DeleteErrorsOfTask(name);
        }
        else
        {
            storage.DeleteAllErrors();
        }

        return ValueTask.CompletedTask;
    }

    protected override RavenCommand<object> CreateCommandForNode(string nodeTag)
    {
        var names = RequestHandler.GetStringValuesQueryString("name", required: false);

        if (names.Count > 0)
            return new DeleteNamedTaskErrorsCommand(names, nodeTag);

        return new DeleteAllTaskErrorsCommand(nodeTag);
    }

    protected override Task HandleRemoteNodeAsync(ProxyCommand<object> command, OperationCancelToken token)
        => RequestHandler.ExecuteRemoteAsync(command, token.Token);
}
