using Lextm.SharpSnmpLib;
using Raven.Server.Documents;

namespace Raven.Server.Monitoring.Snmp.Objects.Database;

public sealed class DatabaseAiTaskErrors : DatabaseEtlScalarObjectBase<Integer32>
{
    public DatabaseAiTaskErrors(string databaseName, string etlName, DatabasesLandlord landlord, int databaseIndex, int aiTaskIndex)
        : base(databaseName, etlName, landlord, databaseIndex, aiTaskIndex, SnmpOids.Databases.AiTasks.AiTasksErrors)
    {
    }

    protected override Integer32 GetData(DocumentDatabase database)
    {
        var processErrors = database.EtlErrorsStorage.ReadProcessErrorsOfEtl(EtlName);
        var itemErrors = database.EtlErrorsStorage.ReadItemErrorsOfEtl(EtlName);

        return new Integer32(processErrors.Count + itemErrors.Count);
    }
}

