using System;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.SchemaValidation;

public class SchemaDefinition
{
    public bool Disabled { get; set; }

    public string Schema { get; set; }

    public DateTime LastModifiedTime { get; } = DateTime.UtcNow;

    public DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue(3)
        {
            [nameof(Disabled)] = Disabled,
            [nameof(Schema)] = Schema,
            [nameof(LastModifiedTime)] = LastModifiedTime
        };
    }
}
