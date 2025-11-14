using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Raven.Client.Exceptions;
using Raven.Server;
using Raven.Server.Config;
using Raven.Server.Config.Settings;
using Raven.Client.ServerWide.Operations.Certificates;

namespace RequestHandler.Benchmark;

[MemoryDiagnoser]
[RankColumn]
public class RequestHandlerBenchmark
{
    private RavenServer _server = null!;
    private IDocumentStore _store = null!;
    private IServiceProvider _rootServiceProvider = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private RequestDelegate _requestDelegate = null!;
    private string _databaseName = null!;
    private ILoggerFactory _loggerFactory = null!;
    private string _dataDirectory = null!;

    [Params(false, true)]
    public bool UseRequestScope { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        RavenServerStartup.SkipHttpLogging = true;

        _dataDirectory = Path.Combine(Path.GetTempPath(), "RequestHandlerBenchmark", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDirectory);

        var configuration = RavenConfiguration.CreateForServer(null);
        configuration.Core.RunInMemory = true;
        configuration.Core.DataDirectory = new PathSetting(_dataDirectory);
        configuration.Core.ServerUrls = new[] { "http://127.0.0.1:0" };
        configuration.Http.UseResponseCompression = false;
        configuration.Initialize();

        _server = new RavenServer(configuration);
        _server.Initialize();

        _rootServiceProvider = _server.GetService<IServiceProvider>() ?? throw new InvalidOperationException("Missing service provider");
        _scopeFactory = _rootServiceProvider.GetRequiredService<IServiceScopeFactory>();
        _loggerFactory = _rootServiceProvider.GetRequiredService<ILoggerFactory>();

        await EnsureDatabaseAsync();
        await SeedSampleDocumentAsync();

        _requestDelegate = BuildRequestDelegate();
    }

    [GlobalCleanup]
    public Task GlobalCleanup()
    {
        if (_store != null)
        {
            _store.Dispose();
        }

        _loggerFactory?.Dispose();
        _server?.Dispose();

        if (_dataDirectory != null && Directory.Exists(_dataDirectory))
        {
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch
            {
                // ignored
            }
        }

        return Task.CompletedTask;
    }

    [Benchmark(Description = "GET /databases/{db}/docs?id=users/1-A")]
    public async Task ExecuteRequestAsync()
    {
        var context = CreateHttpContext();
        using var scope = UseRequestScope ? _scopeFactory.CreateScope() : null;

        context.RequestServices = scope?.ServiceProvider ?? _rootServiceProvider;

        try
        {
            await _requestDelegate(context);

            if (context.Response.StatusCode != StatusCodes.Status200OK)
                throw new InvalidOperationException($"Unexpected status code: {context.Response.StatusCode}");
        }
        finally
        {
            if (context.Response.Body is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private async Task EnsureDatabaseAsync()
    {
        _databaseName = "RequestHandlerBenchmark";

        _store = new DocumentStore
        {
            Urls = new[] { _server.WebUrl },
            Database = _databaseName
        };

        _store.Initialize();

        var databaseRecord = new DatabaseRecord(_databaseName)
        {
            Settings =
            {
                [RavenConfiguration.GetKey(x => x.Core.RunInMemory)] = bool.TrueString
            }
        };

        try
        {
            await _store.Maintenance.Server.SendAsync(new CreateDatabaseOperation(databaseRecord));
        }
        catch (ConcurrencyException)
        {
            // database already exists - this can happen when GlobalSetup is rerun by BenchmarkDotNet
        }
    }

    private async Task SeedSampleDocumentAsync()
    {
        using (var session = _store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "Benchmark" }, "users/1-A");
            await session.SaveChangesAsync();
        }
    }

    private RequestDelegate BuildRequestDelegate()
    {
        var startup = new RavenServerStartup();
        var appBuilder = new ApplicationBuilder(_rootServiceProvider);
        startup.Configure(appBuilder, _loggerFactory);
        return appBuilder.Build();
    }

    private DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = _rootServiceProvider
        };

        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("127.0.0.1");
        context.Request.Protocol = "HTTP/1.1";
        context.Request.Path = $"/databases/{_databaseName}/docs";
        context.Request.QueryString = new QueryString("?id=users/1-A");
        context.Request.Body = Stream.Null;
        context.Request.ContentLength = 0;
        context.Request.Headers.Host = context.Request.Host.Value;

        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(responseStream));

        context.Features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            RemoteIpAddress = IPAddress.Loopback,
            RemotePort = 0,
            LocalIpAddress = IPAddress.Loopback,
            LocalPort = 0
        });

        context.Features.Set<IHttpAuthenticationFeature>(CreateAuthenticationFeature());

        return context;
    }

    private RavenServer.AuthenticateConnection CreateAuthenticationFeature()
    {
        var feature = new RavenServer.AuthenticateConnection(_server.TwoFactor)
        {
            Status = RavenServer.AuthenticationStatus.Allowed
        };

        feature.AuthorizedDatabases[_databaseName] = DatabaseAccess.ReadWrite;
        return feature;
    }
}
