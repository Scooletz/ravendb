using System;
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

internal sealed class TaskErrorsHandlerProcessorForGetAllErrors : AbstractTaskErrorsHandlerProcessorForGetAllErrors<DatabaseRequestHandler, DocumentsOperationContext>
{
    public TaskErrorsHandlerProcessorForGetAllErrors([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }

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
        var processesByName = RequestHandler.Database.EtlLoader.Processes
            .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

        foreach (TaskCategory taskType in Enum.GetValues<TaskCategory>())
        {
            foreach (var (taskName, processErrors, itemErrors) in storage.ReadAllErrorsGroupedByTask(taskType))
            {
                processesByName.TryGetValue(taskName, out var process);

                response.Results.Add(new TaskErrors
                {
                    TaskName = taskName,
                    EtlType = process?.EtlType,
                    EtlSubType = process?.EtlSubType,
                    ProcessErrors = processErrors.Select(x => x.ToTaskProcessError()).ToArray(),
                    ItemErrors = itemErrors.Select(x => x.ToTaskItemError()).ToArray()
                });
            }
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

                writer.WriteArray(context, nameof(Response.Results), response.Results, (w, c, errors) => w.WriteObject(c.ReadObject(errors.ToJson(), "task-errors")));
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
