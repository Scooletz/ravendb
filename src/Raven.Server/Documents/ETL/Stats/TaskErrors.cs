using System.Linq;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.ETL.Stats;

public sealed class TaskErrors : IDynamicJson
{
    public string TaskName { get; set; }

    public TaskProcessError[] ProcessErrors { get; set; }

    public TaskItemError[] ItemErrors { get; set; }

    public DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue
        {
            [nameof(TaskName)] = TaskName,
            [nameof(ProcessErrors)] = new DynamicJsonArray(ProcessErrors.Select(x => x.ToJson())),
            [nameof(ItemErrors)] = new DynamicJsonArray(ItemErrors.Select(x => x.ToJson()))
        };
    }
}
