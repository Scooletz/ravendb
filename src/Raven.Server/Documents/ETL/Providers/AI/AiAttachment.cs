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
    
    /// <summary>
    /// For remote attachments, stores the metadata needed to retrieve the attachment later.
    /// This allows deferred loading of remote attachments without requiring the document ID.
    /// </summary>
    public RemoteAttachmentMetadata RemoteMetadata { get; set; }

    public AiAttachment()
    {
        // for deserialization
    }

    public AiAttachment(string name, string type, AiAttachmentSource source, string dataAsBase64)
    {
        ValidationMethods.AssertNotNullOrEmpty(name, nameof(Name));
        ValidationMethods.AssertNotNullOrEmpty(type, nameof(Type));
        if (source != AiAttachmentSource.NotFound)
            ValidationMethods.AssertNotNullOrEmpty(dataAsBase64, nameof(Data));

        Name = name;
        Type = type;
        Source = source;
        Data = dataAsBase64;
    }

    public DynamicJsonValue ToJson()
    {
        var json = new DynamicJsonValue
        {
            [nameof(Name)] = Name,
            [nameof(Type)] = Type,
            [nameof(Source)] = Source,
            [nameof(Data)] = Data
        };

        return json;
    }
}

/// <summary>
/// Stores metadata for a remote attachment, allowing it to be retrieved later
/// without needing the document ID - only the remote storage identifier and hash are required.
/// </summary>
public class RemoteAttachmentMetadata
{
    public string Identifier { get; set; }
    public string Hash { get; set; }
}

public enum AiAttachmentSource
{
    FromAttachment, 
    FromScript, 
    NotFound
}
