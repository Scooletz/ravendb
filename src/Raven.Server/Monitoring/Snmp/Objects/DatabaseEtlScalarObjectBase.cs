using System.Linq;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Pipeline;
using Raven.Server.Documents;
using Raven.Server.Documents.ETL;

namespace Raven.Server.Monitoring.Snmp.Objects;

public abstract class DatabaseEtlScalarObjectBase<TData> : DatabaseScalarObjectBase<TData>
    where TData : ISnmpData
{
    protected readonly string EtlName;

    private static readonly OctetString TaskOnDifferentNode = new OctetString("The task is on a different node");

    protected DatabaseEtlScalarObjectBase(string databaseName, string etlName, DatabasesLandlord landlord, int databaseIndex, int etlIndex, string dots)
        : base(databaseName, landlord, string.Format(dots, databaseIndex), etlIndex)
    {
        EtlName = etlName;
    }

    public override ISnmpData Data
    {
        get
        {
            var database = GetDatabase();
            if (database != null && GetEtl(database) == null)
                return TaskOnDifferentNode;

            return base.Data;
        }
        set => throw new AccessFailureException();
    }

    protected EtlProcess GetEtl(DocumentDatabase database)
    {
        return database.EtlLoader.Processes.SingleOrDefault(x => x.Name == EtlName);
    }
}
