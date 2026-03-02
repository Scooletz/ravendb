using System.Threading.Tasks;
using Raven.Server.Documents.ETL.Providers.AI;

namespace Raven.Server.Documents.Handlers.AI.Agents;

/// <summary>
/// Provides a capability to resolve an attachment that if of type <see cref="AiAttachmentSource.Deferred"/>
/// </summary>
public interface IDeferredAttachmentResolver
{
    /// <summary>
    /// Gets the deferred attachment.
    /// </summary>
    /// <param name="remoteStorageId">The identifier of the remote storage as in <see cref="Raven.Client.Documents.Operations.Attachments.RemoteAttachmentParameters.Identifier"/>.</param>
    /// <param name="hash">The hash of the attachment <see cref="Attachment.Base64Hash"/>.</param>
    /// <param name="type">The type <see cref="Attachment.ContentType"/></param>
    /// <returns>The payload of the defined attachment.</returns>
    ValueTask<string> ResolveAsync(string remoteStorageId, string hash, string type);
}
