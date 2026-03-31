using System.Threading.Tasks;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Web.Http;

namespace Raven.Server.Documents.ETL.Handlers.Processors;

internal sealed class EtlHandlerProcessorForDeleteErrors : AbstractEtlHandlerProcessorForDeleteErrors<DatabaseRequestHandler, DocumentsOperationContext>
{
    public EtlHandlerProcessorForDeleteErrors(DatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected override bool SupportsCurrentNode => true;
    
    protected override ValueTask HandleCurrentNodeAsync()
    {
        var names = GetEtlProcessNames();

        if (names.Count == 0)
        {
            RequestHandler.Database.EtlErrorsStorage.DeleteAllEtlErrors();
        }
        else
        {
            foreach (var name in names)
                RequestHandler.Database.EtlErrorsStorage.DeleteErrorsOfEtl(name);
        }

        return ValueTask.CompletedTask;
    }

    protected override Task HandleRemoteNodeAsync(ProxyCommand<object> command, OperationCancelToken token) => RequestHandler.ExecuteRemoteAsync(command, token.Token);
}
