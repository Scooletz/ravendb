using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FastTests;
using Tests.Infrastructure;
using Xunit;

namespace SlowTests.Server.Integrations.PostgreSQL;

// Pins the security contract that PgSession must NOT prompt for a cleartext password on a
// plaintext socket. A secured RavenDB server (one configured with a certificate) requires
// password authentication; without this guard, a client connecting with `SSL Mode=Disable`
// would skip the SSLRequest, land on the raw TCP stream, and have its password travel in
// the clear. The fix in PgSession.Run refuses authentication unless the active stream is
// an SslStream; this test exercises the refusal path by speaking the PG wire protocol
// directly (no Npgsql dependency in this project).
public sealed class PgSessionTlsRequiredTests : RavenTestBase
{
    public PgSessionTlsRequiredTests(ITestOutputHelper output) : base(output)
    {
    }

    [RavenFact(RavenTestCategory.PostgreSql, LicenseRequired = true)]
    public async Task SecuredServer_RejectsPlaintextStartupMessage()
    {
        var customSettings = new ConcurrentDictionary<string, string>
        {
            ["Integrations.PostgreSQL.Enabled"] = "true",
            ["Integrations.PostgreSQL.Port"] = "0",
            ["Features.Availability"] = "Experimental",
        };

        // Adds CertificatePath + https ServerUrls into customSettings. The next GetNewServer
        // picks them up and brings the server up in secured mode.
        Certificates.SetupServerAuthentication(customSettings);

        using var server = GetNewServer(new ServerCreationOptions { CustomSettings = customSettings });

        var pgServer = server.ServerStore.Server.PostgresServer;
        Assert.NotNull(pgServer);
        await WaitForValueAsync(() => pgServer.Active, true, timeout: 10_000, interval: 50);
        Assert.True(pgServer.Active, "PgServer never activated — testing license must include PostgreSQL integration / PowerBI tier.");

        int port = pgServer.GetListenerPort();

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();

        // Send a PG v3.0 StartupMessage directly — NO SSLRequest first. This simulates a
        // client with `SSL Mode=Disable`. The server should refuse with an ErrorResponse
        // instead of asking for a cleartext password.
        await WriteStartupMessageAsync(stream, user: "anyone", database: "any-db");

        // The server replies with one byte for message type, then a 4-byte length (BE), then
        // body. We want type 'E' (ErrorResponse), NOT 'R' (Authentication).
        var msgType = (char)await ReadByteAsync(stream);
        Assert.Equal('E', msgType);

        var bodyLen = await ReadInt32BeAsync(stream) - sizeof(int);
        var body = new byte[bodyLen];
        await ReadExactlyAsync(stream, body, bodyLen);

        // ErrorResponse body is a sequence of (1-byte field code, null-terminated string) tuples
        // ending with a single null byte. We just scan for the TLS-required hint in the message.
        var asText = Encoding.UTF8.GetString(body);
        Assert.Contains("TLS", asText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SSL", asText, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteStartupMessageAsync(Stream stream, string user, string database)
    {
        // body: protocol(4) + (key\0value\0)* + final\0
        var ms = new MemoryStream();
        Span<byte> proto = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(proto, 196608); // PG v3.0
        ms.Write(proto);
        WriteCString(ms, "user");
        WriteCString(ms, user);
        WriteCString(ms, "database");
        WriteCString(ms, database);
        ms.WriteByte(0);

        var body = ms.ToArray();
        var totalLen = body.Length + sizeof(int);
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, totalLen);

        await stream.WriteAsync(len.ToArray());
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static void WriteCString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    private static async Task<byte> ReadByteAsync(Stream stream)
    {
        var buf = new byte[1];
        await ReadExactlyAsync(stream, buf, 1);
        return buf[0];
    }

    private static async Task<int> ReadInt32BeAsync(Stream stream)
    {
        var buf = new byte[4];
        await ReadExactlyAsync(stream, buf, 4);
        return BinaryPrimitives.ReadInt32BigEndian(buf);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, count - read));
            if (n <= 0)
                throw new EndOfStreamException();
            read += n;
        }
    }
}
