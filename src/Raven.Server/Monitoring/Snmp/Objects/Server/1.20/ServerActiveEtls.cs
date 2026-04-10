using System;
using System.Linq;
using Lextm.SharpSnmpLib;
using Raven.Server.ServerWide;

namespace Raven.Server.Monitoring.Snmp.Objects.Server;

public sealed class ServerActiveEtls : ScalarObjectBase<Integer32>
{
    private readonly ServerStore _store;

    public ServerActiveEtls(ServerStore store)
        : base(SnmpOids.Server.NumberOfActiveEtls)
    {
        _store = store;
    }

    protected override Integer32 GetData()
    {
        var result = 0;
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);

        foreach (var db in _store.DatabasesLandlord.DatabasesCache)
        {
            result += db.Value.GetAwaiter().GetResult().EtlLoader.GetEtlProcesses()
                .Count(x => x.GetLatestPerformanceStats()?.StartTime > oneMinuteAgo);
        }

        return new Integer32(result);
    }
}

