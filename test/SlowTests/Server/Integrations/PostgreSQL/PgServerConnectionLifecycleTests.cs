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

// Pins PgServer's in-flight connection cleanup: every accepted connection must leave the dictionary
// once its session ends, else short-lived PowerBI traffic slowly leaks a tuple per connection.
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

        // Churn raw TCP connect/close — no real handshake needed; the session hits EOF and exits, and
        // cleanup fires whenever HandleConnection's task finishes.
        const int churn = 25;
        for (int i = 0; i < churn; i++)
        {
            using var c = new TcpClient();
            await c.ConnectAsync(IPAddress.Loopback, port);
            c.Close();
        }

        // Add (ListenToConnections) and remove (ContinueWith on the session task) are both async, so poll
        // briefly; a non-zero count past this window is a cleanup regression.
        var sw = Stopwatch.StartNew();
        while (pgServer.InFlightConnectionCount > 0 && sw.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(100);

        Assert.Equal(0, pgServer.InFlightConnectionCount);
    }
}
