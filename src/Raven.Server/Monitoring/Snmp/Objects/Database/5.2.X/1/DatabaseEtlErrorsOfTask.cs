using System;
using System.Globalization;
using System.Linq;
using Lextm.SharpSnmpLib;
using Raven.Server.Documents;

namespace Raven.Server.Monitoring.Snmp.Objects.Database;


public sealed class DatabaseEtlErrorsOfTask : DatabaseEtlScalarObjectBase<OctetString>
{
    public DatabaseEtlErrorsOfTask(string databaseName, string etlName, DatabasesLandlord landlord, int databaseIndex, int etlIndex)
        : base(databaseName, etlName, landlord, databaseIndex, etlIndex, SnmpOids.Databases.Etls.EtlErrorsOfTask)
    {
    }
    
    public override ISnmpData Data
    {
        get
        {
            if (Landlord.IsDatabaseLoaded(DatabaseName))
            {
                var database = Landlord.TryGetOrCreateResourceStore(DatabaseName).Result;
                
                var processErrors = database.EtlErrorsStorage.ReadProcessErrorsOfEtl(EtlName);
                var itemErrors = database.EtlErrorsStorage.ReadItemErrorsOfEtl(EtlName);

                return new Integer32(processErrors.Count + itemErrors.Count);
            }

            return null;
        }
    }

    protected override OctetString GetData(DocumentDatabase database)
    {
        throw new NotSupportedException();
    }
}
