using Raven.Client.Documents.Operations.ETL;

namespace Raven.Server.Documents.ETL;

public enum TaskErrorSource
{
    Etl,
    Ai
}

public static class TaskTypeExtensions
{
    public static TaskErrorSource FromEtlType(EtlType etlType)
    {
        return etlType is EtlType.EmbeddingsGeneration or EtlType.GenAi
            ? TaskErrorSource.Ai
            : TaskErrorSource.Etl;
    }
}
