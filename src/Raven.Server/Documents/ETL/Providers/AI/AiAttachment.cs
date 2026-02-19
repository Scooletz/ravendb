using System;
using Raven.Client.Util;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.ETL.Providers.AI;

public class AiAttachment
{
    public string Name { get; set; }
    public string Type { get; set; }
    public AiAttachmentSource Source { get; set; }
    public string Data { get; set; }
    
    // For deferred resolution: document ID for fetching attachment later
    public string DocumentId { get; set; }

    public AiAttachment()
    {
        // for deserialization
    }

    public AiAttachment(string name, string type, AiAttachmentSource source, string dataAsBase64, string documentId = null)
    {
        ValidationMethods.AssertNotNullOrEmpty(name, nameof(Name));
        ValidationMethods.AssertNotNullOrEmpty(type, nameof(Type));
        if (source != AiAttachmentSource.NotFound && source != AiAttachmentSource.Deferred)
            ValidationMethods.AssertNotNullOrEmpty(dataAsBase64, nameof(Data));

        Name = name;
        Type = type;
        Source = source;
        Data = dataAsBase64;
        DocumentId = documentId;
    }

    public DynamicJsonValue ToJson()
    {
        var json = new DynamicJsonValue
        {
            [nameof(Name)] = Name,
            [nameof(Type)] = Type,
            [nameof(Source)] = Source,
            [nameof(Data)] = Data,
            [nameof(DocumentId)] = DocumentId
        };

        return json;
    }
}

public enum AiAttachmentSource
{
    FromAttachment, 
    FromScript, 
    NotFound,
    Deferred // For attachments to be resolved later (e.g., remote attachments)
}
