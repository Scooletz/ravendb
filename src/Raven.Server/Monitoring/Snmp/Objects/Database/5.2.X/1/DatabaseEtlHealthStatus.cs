using Lextm.SharpSnmpLib;
using Raven.Server.Documents;

namespace Raven.Server.Monitoring.Snmp.Objects.Database;

public sealed class DatabaseEtlHealthStatus : DatabaseEtlScalarObjectBase<OctetString>
{
    public DatabaseEtlHealthStatus(string databaseName, string etlName, DatabasesLandlord landlord, int databaseIndex, int etlIndex)
        : base(databaseName, etlName, landlord, databaseIndex, etlIndex, SnmpOids.Databases.Etls.HealthStatus)
    {
    }

    protected override OctetString GetData(DocumentDatabase database)
    {
        var healthStatus = GetEtl(database).Statistics.HealthStatus;

        return new OctetString(healthStatus.ToString());
    }
}
