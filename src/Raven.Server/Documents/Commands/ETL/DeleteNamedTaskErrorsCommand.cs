using System.Net.Http;
using Microsoft.Extensions.Primitives;
using Raven.Client.Http;
using Sparrow.Json;

namespace Raven.Server.Documents.Commands.ETL;

internal sealed class DeleteNamedTaskErrorsCommand : RavenCommand
{
    private readonly StringValues _names;

    public DeleteNamedTaskErrorsCommand(StringValues names, string nodeTag)
    {
        _names = names;
        SelectedNodeTag = nodeTag;
    }

    public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
    {
        url = $"{node.Url}/databases/{node.Database}/tasks/errors";

        foreach (var name in _names)
            url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(url, "name", name);

        return new HttpRequestMessage { Method = HttpMethod.Delete };
    }
}
