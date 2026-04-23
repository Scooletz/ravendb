using Lextm.SharpSnmpLib;
using Raven.Server.Documents;
using Raven.Server.Documents.ETL;

namespace Raven.Server.Monitoring.Snmp.Objects.Database;


public sealed class DatabaseEtlErrorsOfTask : DatabaseEtlScalarObjectBase<Integer32>
{
    public DatabaseEtlErrorsOfTask(string databaseName, string etlName, DatabasesLandlord landlord, int databaseIndex, int etlIndex)
        : base(databaseName, etlName, landlord, databaseIndex, etlIndex, SnmpOids.Databases.Etls.EtlErrorsOfTask)
    {
    }

    protected override Integer32 GetData(DocumentDatabase database)
    {
        var processErrors = database.TaskErrorsStorage.ReadProcessErrorsOfTask(TaskType.Etl, EtlName);
        var itemErrors = database.TaskErrorsStorage.ReadItemErrorsOfTask(TaskType.Etl, EtlName);

        return new Integer32(processErrors.Count + itemErrors.Count);
    }
}
