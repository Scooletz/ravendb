using System.Net.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Raven.Client.Http;
using Raven.Server.Documents.ETL;
using Sparrow.Json;

namespace Raven.Server.Documents.Commands.ETL;

internal sealed class DeleteNamedTaskErrorsCommand : RavenCommand
{
    private readonly StringValues _names;
    private readonly TaskCategory _taskCategory;

    public DeleteNamedTaskErrorsCommand(StringValues names, TaskCategory taskCategory, string nodeTag)
    {
        _names = names;
        _taskCategory = taskCategory;
        SelectedNodeTag = nodeTag;
    }

    public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
    {
        url = $"{node.Url}/databases/{node.Database}/tasks/errors";
        url = QueryHelpers.AddQueryString(url, "type", _taskCategory.ToString());

        foreach (var name in _names)
            url = QueryHelpers.AddQueryString(url, "name", name);

        return new HttpRequestMessage { Method = HttpMethod.Delete };
    }
}
