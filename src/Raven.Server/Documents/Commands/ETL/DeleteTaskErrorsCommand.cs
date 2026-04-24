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
    private readonly TaskErrorSource _taskErrorSource;
    private readonly StringValues _names;

    public DeleteTaskErrorsCommand(StringValues names, TaskErrorSource taskErrorSource, string nodeTag)
    {
        _taskErrorSource = taskErrorSource;
        _names = names;
        SelectedNodeTag = nodeTag;
    }

    public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
    {
        var path = _taskErrorSource switch
        {
            TaskErrorSource.Etl => "etl/errors",
            TaskErrorSource.Ai => "ai/errors",
            _ => throw new ArgumentOutOfRangeException(nameof(_taskErrorSource), _taskErrorSource, "Unknown task type")
        };

        url = $"{node.Url}/databases/{node.Database}/{path}";

        foreach (var name in _names)
            url = QueryHelpers.AddQueryString(url, "name", name);

        return new HttpRequestMessage { Method = HttpMethod.Delete };
    }
}
