using Lextm.SharpSnmpLib;
using Raven.Server.Documents.ETL;
using Raven.Server.ServerWide;

namespace Raven.Server.Monitoring.Snmp.Objects.Server;

public class ServerEtlErrors : ScalarObjectBase<Integer32>
{
    private readonly ServerStore _store;
    
    public ServerEtlErrors(ServerStore store)
        : base(SnmpOids.Server.EtlErrors)
    {
        _store = store;
    }

    protected override Integer32 GetData()
    {
        var result = 0;
        
        foreach (var db in _store.DatabasesLandlord.DatabasesCache)
        {
            result += (int)db.Value.GetAwaiter().GetResult().TaskErrorsStorage.ReadTotalErrorsCount(TaskCategory.Etl);
        }
        
        return new Integer32(result);
    }
}
