using System;
using System.Net.Http;
using Microsoft.AspNetCore.WebUtilities;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Server.Documents.ETL;
using Raven.Server.Documents.ETL.Stats;
using Sparrow.Json;

namespace Raven.Server.Documents.Commands.ETL;

internal sealed class GetTaskErrorsCommand : RavenCommand<TaskErrors[]>
{
    private readonly TaskType _taskType;
    private readonly string[] _names;

    public GetTaskErrorsCommand(TaskType taskType, string[] names, string nodeTag)
    {
        _taskType = taskType;
        _names = names;
        SelectedNodeTag = nodeTag;
    }

    public override bool IsReadRequest => true;

    public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
    {
        var endpoint = _taskType switch
        {
            TaskType.Etl => "etl/errors",
            TaskType.Ai => "ai-tasks/errors",
            _ => throw new ArgumentOutOfRangeException(nameof(_taskType), _taskType, null)
        };

        url = $"{node.Url}/databases/{node.Database}/{endpoint}";

        if (_names is { Length: > 0 })
        {
            foreach (var name in _names)
            {
                url = QueryHelpers.AddQueryString(url, "name", name);
            }
        }

        return new HttpRequestMessage { Method = HttpMethod.Get };
    }

    public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
    {
        if (response == null)
            return;

        Result = DocumentConventions.Default.Serialization.DefaultConverter.FromBlittable<TaskErrorsResponse>(response).Results;
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class TaskErrorsResponse
    {
        public TaskErrors[] Results { get; set; }
    }
}
