using System;
using System.Net.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Raven.Client.Http;
using Raven.Server.Documents.ETL;
using Sparrow.Json;

namespace Raven.Server.Documents.Commands.ETL;

internal sealed class DeleteTaskErrorsCommand : RavenCommand
{
    private readonly TaskType _taskType;
    private readonly StringValues _names;

    public DeleteTaskErrorsCommand(TaskType taskType, string nodeTag, StringValues names)
    {
        _taskType = taskType;
        _names = names;
        SelectedNodeTag = nodeTag;
    }

    public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
    {
        var path = _taskType switch
        {
            TaskType.Etl => "etl/errors",
            TaskType.Ai => "ai-tasks/errors",
            _ => throw new ArgumentOutOfRangeException(nameof(_taskType), _taskType, "Unknown task type")
        };

        url = $"{node.Url}/databases/{node.Database}/{path}";

        foreach (var name in _names)
            url = QueryHelpers.AddQueryString(url, "name", name);

        return new HttpRequestMessage { Method = HttpMethod.Delete };
    }
}
