using Lextm.SharpSnmpLib;
using Raven.Server.Documents;
using Raven.Server.Documents.ETL;

namespace Raven.Server.Monitoring.Snmp.Objects.Database;

public sealed class DatabaseErrorsOfAiTask : DatabaseEtlScalarObjectBase<Integer32>
{
    public DatabaseErrorsOfAiTask(string databaseName, string etlName, DatabasesLandlord landlord, int databaseIndex, int aiTaskIndex)
        : base(databaseName, etlName, landlord, databaseIndex, aiTaskIndex, SnmpOids.Databases.AiTasks.AiTasksErrors)
    {
    }

    protected override Integer32 GetData(DocumentDatabase database)
    {
        var processErrors = database.TaskErrorsStorage.ReadProcessErrorsOfTask(TaskType.Ai, EtlName);
        var itemErrors = database.TaskErrorsStorage.ReadItemErrorsOfTask(TaskType.Ai, EtlName);

        return new Integer32(processErrors.Count + itemErrors.Count);
    }
}

