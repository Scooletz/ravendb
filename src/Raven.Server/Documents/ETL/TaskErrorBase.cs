using System;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.ETL;

public abstract class TaskErrorBase
{
    public string Id => GetId();
    public string TaskName { get; set; }
    public DateTime CreatedAt { get; set; }
    public TaskErrorStep Step { get; set; }
    public string Error { get; set; }

    protected virtual string GetId() => throw new NotSupportedException();

    public virtual DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue
        {
            [nameof(Id)] = Id,
            [nameof(TaskName)] = TaskName,
            [nameof(CreatedAt)] = CreatedAt,
            [nameof(Step)] = Step,
            [nameof(Error)] = Error
        };
    }
}
