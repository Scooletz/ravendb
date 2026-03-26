using Lextm.SharpSnmpLib;
using Raven.Server.Documents;

namespace Raven.Server.Monitoring.Snmp.Objects.Database;

public sealed class DatabaseAiTaskHealthStatus : DatabaseEtlScalarObjectBase<OctetString>
{
    public DatabaseAiTaskHealthStatus(string databaseName, string etlName, DatabasesLandlord landlord, int databaseIndex, int aiTaskIndex)
        : base(databaseName, etlName, landlord, databaseIndex, aiTaskIndex, SnmpOids.Databases.AiTasks.HealthStatus)
    {
    }

    protected override OctetString GetData(DocumentDatabase database)
    {
        var etl = GetEtl(database);
        if (etl == null)
            return null;

        return new OctetString(etl.Statistics.HealthStatus.ToString());
    }
}

