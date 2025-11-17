using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Raven.Client.Documents;
using Raven.Server;
using Raven.Server.Web;
using Raven.Tests.Core.Utils.Entities;
using Tests.Infrastructure;
using Xunit.Abstractions;

namespace BenchmarkTests;

public class RequestHandlingTest(ITestOutputHelper output) : BenchmarkTestBase(output)
{
    private const int Iterations = 1000;
    private const string DocumentId = "users/1-A";

    [RavenTheory(RavenTestCategory.None)]
    [RavenData(DatabaseMode = RavenDatabaseMode.Single)]
    public async Task Request_handling_routing(Options options)
    {
        using (var store = GetDocumentStore())
        {
            string databaseName = store.Database;
            using (var session = store.OpenAsyncSession())
            {
                await session.StoreAsync(new User(), DocumentId);
                await session.SaveChangesAsync();
            }

            RavenServer server = GetServers().Single();

            for (int i = 0; i < Iterations; i++)
            {
                DefaultHttpContext context = CreateHttpContext(server, databaseName);

                // Test routing
                using var requestHandlerContext = new RequestHandlerContext();
                requestHandlerContext.HttpContext = context;

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                await server.Router.HandlePath(requestHandlerContext);
            }
        }
    }

    private static DefaultHttpContext CreateHttpContext(RavenServer server, string databaseName)
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Method = HttpMethods.Get,
                Scheme = "http",
                Host = new HostString("127.0.0.1"),
                Protocol = "HTTP/1.1",
                Path = $"/databases/{databaseName}/docs",
                QueryString = new QueryString($"?id={DocumentId}"),
                Body = Stream.Null,
                ContentLength = 0
            }
        };

        context.Request.Headers.Host = context.Request.Host.Value;

        // NullStream for responses
        context.Response.Body = Stream.Null;
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));

        context.Features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            RemoteIpAddress = IPAddress.Loopback,
            RemotePort = 0,
            LocalIpAddress = IPAddress.Loopback,
            LocalPort = 0
        });

        return context;
    }

    public override Task InitAsync(DocumentStore store) => Task.CompletedTask;
}
