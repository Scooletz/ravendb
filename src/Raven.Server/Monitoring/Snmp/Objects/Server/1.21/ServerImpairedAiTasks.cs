using System.Linq;
using Lextm.SharpSnmpLib;
using Raven.Client.Documents.Operations.ETL;
using Raven.Server.Documents.ETL;
using Raven.Server.ServerWide;

namespace Raven.Server.Monitoring.Snmp.Objects.Server;

public sealed class ServerImpairedAiTasks : ScalarObjectBase<Integer32>
{
    private readonly ServerStore _store;

    public ServerImpairedAiTasks(ServerStore store)
        : base(SnmpOids.Server.NumberOfImpairedAiTasks)
    {
        _store = store;
    }

    protected override Integer32 GetData()
    {
        var result = 0;

        foreach (var db in _store.DatabasesLandlord.DatabasesCache)
        {
            result += db.Value.GetAwaiter().GetResult().EtlLoader.Processes
                .Count(x => x.EtlType is EtlType.EmbeddingsGeneration or EtlType.GenAi && x.Statistics.HealthStatus == EtlProcessHealthStatus.Impaired);
        }

        return new Integer32(result);
    }
}

