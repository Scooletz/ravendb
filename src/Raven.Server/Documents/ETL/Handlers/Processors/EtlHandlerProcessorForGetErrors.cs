using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Raven.Server.Documents.ETL.Stats;
using Raven.Server.Documents.Sharding;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Web.Http;
using Sparrow.Json;

namespace Raven.Server.Documents.ETL.Handlers.Processors;

internal sealed class EtlHandlerProcessorForGetErrors : AbstractTaskErrorsHandlerProcessorForGetErrors<DatabaseRequestHandler, DocumentsOperationContext>
{
    public EtlHandlerProcessorForGetErrors([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected override TaskType TaskType => TaskType.Etl;

    protected override bool SupportsCurrentNode => true;

    protected override async ValueTask HandleCurrentNodeAsync()
    {
        var response = new Response
        {
            NodeTag = RequestHandler.ServerStore.NodeTag
        };

        if (RequestHandler.Database is ShardedDocumentDatabase shardedDatabase)
            response.ShardNumber = shardedDatabase.ShardNumber;

        var storage = RequestHandler.Database.TaskErrorsStorage;
        var taskNames = GetNames();

        foreach (var taskName in taskNames)
        {
            var processErrors = storage.ReadProcessErrorsOfTask(TaskType, taskName);
            var itemErrors = storage.ReadItemErrorsOfTask(TaskType, taskName);

            var taskErrors = new TaskErrors()
            {
                TaskName = taskName,
                ProcessErrors = processErrors.Select(x => x.ToTaskProcessError()).ToArray(),
                ItemErrors = itemErrors.Select(x => x.ToTaskItemError()).ToArray()
            };

            response.Results.Add(taskErrors);
        }

        using (ContextPool.AllocateOperationContext(out JsonOperationContext context))
        {
            await using (var writer = new AsyncBlittableJsonTextWriter(context, RequestHandler.ResponseBodyStream()))
            {
                writer.WriteStartObject();

                writer.WritePropertyName(nameof(Response.NodeTag));
                writer.WriteString(response.NodeTag);
                writer.WriteComma();

                writer.WritePropertyName(nameof(response.ShardNumber));
                if (response.ShardNumber != null)
                    writer.WriteInteger(response.ShardNumber.Value);
                else
                    writer.WriteNull();
                writer.WriteComma();

                writer.WriteArray(context, nameof(Response.Results), response.Results, (w, c, errors) => w.WriteObject(c.ReadObject(errors.ToJson(), "etl/errors")));
                writer.WriteEndObject();
            }
        }
    }

    protected override Task HandleRemoteNodeAsync(ProxyCommand<TaskErrors[]> command, OperationCancelToken token) => RequestHandler.ExecuteRemoteAsync(command, token.Token);

    internal class Response
    {
        public string NodeTag { get; set; }
        public int? ShardNumber { get; set; }
        public List<TaskErrors> Results { get; set; } = new List<TaskErrors>();
    }
}
