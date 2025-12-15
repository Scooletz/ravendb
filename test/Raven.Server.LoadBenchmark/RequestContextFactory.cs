using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Raven.Server;
using Raven.Server.Web;

namespace Raven.Server.LoadBenchmark
{
    public sealed class RequestContextFactory
    {
        private readonly RavenServer _server;
        private readonly string _method;
        private readonly PathString _path;
        private readonly QueryString _queryString;

        public RequestContextFactory(RavenServer server, string method, string path, string queryString)
        {
            _server = server;
            _method = method;
            _path = new PathString(path);
            _queryString = string.IsNullOrEmpty(queryString) ? QueryString.Empty : new QueryString(queryString);
        }

        public RequestHandlerContext CreateContext()
        {
            var httpContext = new DefaultHttpContext
            {
                Request =
                {
                    Method = _method,
                    Scheme = "http",
                    Host = new HostString("127.0.0.1"),
                    Protocol = "HTTP/1.1",
                    Path = _path,
                    QueryString = _queryString,
                    Body = Stream.Null,
                    ContentLength = 0
                }
            };

            httpContext.Request.Headers.Host = httpContext.Request.Host.Value;

            // Use a memory stream for response body to absorb output
            var responseStream = new MemoryStream();
            httpContext.Response.Body = responseStream;
            httpContext.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(responseStream));
            httpContext.Features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
            {
                RemoteIpAddress = IPAddress.Loopback,
                RemotePort = 0,
                LocalIpAddress = IPAddress.Loopback,
                LocalPort = 0
            });

            var requestHandlerContext = new RequestHandlerContext
            {
                HttpContext = httpContext
            };

            return requestHandlerContext;
        }
    }
}
