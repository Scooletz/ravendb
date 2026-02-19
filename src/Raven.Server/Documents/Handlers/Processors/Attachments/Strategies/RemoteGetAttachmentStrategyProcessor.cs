using System.IO;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Operations.Attachments;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.Attachments.Strategies
{
    internal sealed class RemoteGetAttachmentStrategyProcessor : AbstractGetAttachmentStrategyProcessor<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public RemoteGetAttachmentStrategyProcessor([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        public override void CheckAttachmentFlagAndConfigurationAndThrowIfNeeded(DocumentsOperationContext context, Attachment attachment, string documentId, string name)
        {
            IGetAttachmentStrategy.CheckAttachmentFlagAndConfigurationAndThrowIfNeededInternal(context, RequestHandler.Database, attachment, documentId, name, nameof(GetAttachmentOperation));
        }

        public override async Task WriteResponseStream(DocumentsOperationContext context, DocumentsTransaction tx, Attachment attachment, OperationCancelToken token)
        {
            var stream = await GetAttachmentStreamFromStorage(RequestHandler.Database, context, tx, attachment, token);

            await using (stream)
            {
                await stream.CopyToAsync(RequestHandler.ResponseBodyStream(), token.Token);
            }
        }

        public static async Task<Stream> GetAttachmentStreamFromStorage(DocumentDatabase database, DocumentsOperationContext context, DocumentsTransaction tx, Attachment attachment, OperationCancelToken token)
        {
            var stream = database.DocumentsStorage.AttachmentsStorage.GetAttachmentStream(context, attachment.Base64Hash);
            if (stream == null)
            {
                tx.Dispose(); // we are reading from remote, we can dispose the transaction
                using var downloader = database.DocumentsStorage.AttachmentsStorage.RemoteAttachmentsStorage.GetDownloader(attachment, token);
                stream = await database.DocumentsStorage.AttachmentsStorage.RemoteAttachmentsStorage.StreamForDownloadDestinationInternal(downloader, attachment.Base64Hash.ToString());
            }

            return stream;
        }
    }
}
