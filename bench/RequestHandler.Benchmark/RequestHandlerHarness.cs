using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents;
using Raven.Client.Exceptions;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide.Operations.Certificates;
using Raven.Server;
using Raven.Server.Config;
using Raven.Server.Config.Settings;

namespace RequestHandler.Benchmark;

internal sealed class RequestHandlerHarness : IAsyncDisposable
{
    private const string DatabaseName = "RequestHandlerBenchmark";
    private readonly bool _useRequestScope;
    private readonly string _dataDirectory;
    private RavenServer _server = null!;
    private IServiceProvider _rootServiceProvider = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IDocumentStore _store = null!;
    private RequestDelegate _requestDelegate = null!;

    private RequestHandlerHarness(bool useRequestScope, string dataDirectory)
    {
        _useRequestScope = useRequestScope;
        _dataDirectory = dataDirectory;
    }

    public static async Task<RequestHandlerHarness> CreateAsync(bool useRequestScope)
    {
        RavenServerStartup.SkipHttpLogging = true;

        var dataDirectory = Path.Combine(Path.GetTempPath(), "RequestHandlerHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        var harness = new RequestHandlerHarness(useRequestScope, dataDirectory);
        await harness.InitializeAsync();
        return harness;
    }

    private async Task InitializeAsync()
    {
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

    public async Task WarmupAsync()
    {
        // Warmup a single request to make sure caches are ready before measurement.
        await ExecuteRequestAsync();
    }

    public async Task<(TimeSpan Elapsed, int Failures, Exception LastFailure)> ExecuteAsync(int iterations)
    {
        var stopwatch = Stopwatch.StartNew();
        var failures = 0;
        Exception lastFailure = null;

        for (var i = 0; i < iterations; i++)
        {
            try
            {
                await ExecuteRequestAsync();
            }
            catch (Exception e)
            {
                failures++;
                lastFailure = e;
            }
        }

        stopwatch.Stop();
        return (stopwatch.Elapsed, failures, lastFailure);
    }

    private async Task ExecuteRequestAsync()
    {
        var context = CreateHttpContext();
        using var scope = _useRequestScope ? _scopeFactory.CreateScope() : null;

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
        _store = new DocumentStore
        {
            Urls = new[] { _server.WebUrl },
            Database = DatabaseName
        };

        _store.Initialize();

        var databaseRecord = new DatabaseRecord(DatabaseName)
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
            // Database already exists.
        }
    }

    private async Task SeedSampleDocumentAsync()
    {
        using var session = _store.OpenAsyncSession();
        await session.StoreAsync(new { Name = "Benchmark" }, "users/1-A");
        await session.SaveChangesAsync();
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
        context.Request.Path = $"/databases/{DatabaseName}/docs";
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

        feature.AuthorizedDatabases[DatabaseName] = DatabaseAccess.ReadWrite;
        return feature;
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        _loggerFactory?.Dispose();
        _server?.Dispose();

        if (Directory.Exists(_dataDirectory))
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

        return ValueTask.CompletedTask;
    }
}
