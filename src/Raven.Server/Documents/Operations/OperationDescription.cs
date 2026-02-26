using System;
using Raven.Client.Documents.Operations;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Operations;

public sealed class OperationDescription
{
    public string Description;
    public OperationType TaskType;
    public DateTime StartTime;
    public DateTime? EndTime;
    public IOperationDetailedDescription DetailedDescription;

    public DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue(5)
        {
            [nameof(Description)] = Description,
            [nameof(TaskType)] = TaskType.ToString(),
            [nameof(StartTime)] = StartTime,
            [nameof(EndTime)] = EndTime,
            [nameof(DetailedDescription)] = DetailedDescription?.ToJson()
        };
    }
}
