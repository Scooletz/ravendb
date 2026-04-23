using Raven.Client.Documents.Operations.ETL;

namespace Raven.Server.Documents.ETL;

public enum TaskType
{
    Etl,
    Ai
}

public static class TaskTypeExtensions
{
    public static TaskType FromEtlType(EtlType etlType)
    {
        return etlType is EtlType.EmbeddingsGeneration or EtlType.GenAi
            ? TaskType.Ai
            : TaskType.Etl;
    }
}
