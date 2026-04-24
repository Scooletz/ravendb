using System.Linq;
using Raven.Client.Documents.Operations.ETL;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.ETL.Stats;

public sealed class TaskErrors : IDynamicJson
{
    public string TaskName { get; set; }

    /// <summary>
    /// The ETL type of this task (e.g. Raven, EmbeddingsGeneration).
    /// Populated from the process EtlType when the process is currently loaded;
    /// null when the process configuration has been removed but errors remain in storage.
    /// </summary>
    public EtlType? EtlType { get; set; }

    /// <summary>
    /// For Queue ETL tasks, the broker sub-type (e.g. Kafka, RabbitMq).
    /// Null for all other task types.
    /// </summary>
    public string EtlSubType { get; set; }

    public TaskProcessError[] ProcessErrors { get; set; }

    public TaskItemError[] ItemErrors { get; set; }

    public DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue
        {
            [nameof(TaskName)] = TaskName,
            [nameof(EtlType)] = EtlType?.ToString(),
            [nameof(EtlSubType)] = EtlSubType,
            [nameof(ProcessErrors)] = new DynamicJsonArray(ProcessErrors.Select(x => x.ToJson())),
            [nameof(ItemErrors)] = new DynamicJsonArray(ItemErrors.Select(x => x.ToJson()))
        };
    }
}
