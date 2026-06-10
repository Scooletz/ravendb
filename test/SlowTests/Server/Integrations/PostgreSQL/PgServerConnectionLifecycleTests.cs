using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using FastTests;
using Tests.Infrastructure;
using Xunit;

namespace SlowTests.Server.Integrations.PostgreSQL;

// Pins the cleanup contract on PgServer's in-flight connection dictionary: every accepted
// connection must be removed from the dictionary once its session task completes. Without
// this, short-lived PowerBI traffic accumulates one (TcpClient + completed-Task) tuple per
// connection ever made — a slow leak that only surfaces in long-running deployments.
public sealed class PgServerConnectionLifecycleTests : RavenTestBase
{
    public PgServerConnectionLifecycleTests(ITestOutputHelper output) : base(output)
    {
    }

    [RavenFact(RavenTestCategory.PostgreSql, LicenseRequired = true)]
    public async Task Dictionary_drains_after_connection_close()
    {
        using var server = GetNewServer(new ServerCreationOptions
        {
            CustomSettings = new ConcurrentDictionary<string, string>
            {
                ["Integrations.PostgreSQL.Enabled"] = "true",
                ["Integrations.PostgreSQL.Port"] = "0", // ask the OS for a free ephemeral port
                ["Features.Availability"] = "Experimental",
            }
        });

        var pgServer = server.ServerStore.Server.PostgresServer;
        Assert.NotNull(pgServer);

        // The PG server starts asynchronously after the host comes up. Wait briefly for it.
        await WaitForValueAsync(() => pgServer.Active, true, timeout: 10_000, interval: 50);
        Assert.True(pgServer.Active, "PgServer never activated — testing license must include PostgreSQL integration / PowerBI tier.");

        int port = pgServer.GetListenerPort();

        // Churn raw TCP connections — we don't bother with a real PG handshake. PgSession
        // reads the initial message, gets EOF, and the session exits via the IOException path.
        // The cleanup we're testing happens unconditionally whenever HandleConnection's task
        // finishes, regardless of whether the session ran a handshake or not.
        const int churn = 25;
        for (int i = 0; i < churn; i++)
        {
            using var c = new TcpClient();
            await c.ConnectAsync(IPAddress.Loopback, port);
            c.Close();
        }

        // Each accepted TcpClient is added to the dictionary in PgServer.ListenToConnections;
        // a ContinueWith on the session task removes it. Both steps are async — give them
        // time to settle. A retained entry past this window indicates a regression of the
        // cleanup contract.
        var sw = Stopwatch.StartNew();
        while (pgServer.InFlightConnectionCount > 0 && sw.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(100);

        Assert.Equal(0, pgServer.InFlightConnectionCount);
    }
}
