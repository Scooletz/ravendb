using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.OngoingTasks;
using Raven.Client.Exceptions.Security;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Json.Serialization;
using Raven.Client.Util;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.Replication
{
    public class UpdatePullReplicationAsSinkOperation : IMaintenanceOperation<ModifyOngoingTaskResult>
    {
        private readonly PullReplicationAsSink _pullReplication;
        private readonly bool _keepOriginalCertificateOnNull;

        // Kept for the binary compatibility purposes.
        public UpdatePullReplicationAsSinkOperation(PullReplicationAsSink pullReplication) : this(pullReplication, false)
        {
        }
        
        /// <summary>
        /// Initializes the update operation for the <see cref="PullReplicationAsSink"/>.
        /// </summary>
        /// <param name="pullReplication">The pull replication object.</param>
        /// <param name="keepOriginalCertificateOnNull">If <see cref="PullReplicationAsSink.CertificateWithPrivateKey"/> is null, whether to keep the original or remove.</param>
        /// <exception cref="AuthorizationException">If the <see cref="PullReplicationAsSink.CertificateWithPrivateKey"/> isn't null and cannot be parsed.</exception>
        public UpdatePullReplicationAsSinkOperation(PullReplicationAsSink pullReplication, bool keepOriginalCertificateOnNull = false)
        {
            _pullReplication = pullReplication;
            _keepOriginalCertificateOnNull = keepOriginalCertificateOnNull;

            if (pullReplication.CertificateWithPrivateKey != null)
            {
                var certBytes = Convert.FromBase64String(pullReplication.CertificateWithPrivateKey);
                using (var certificate = CertificateLoaderUtil.CreateCertificate(certBytes,
                    pullReplication.CertificatePassword,
                    CertificateLoaderUtil.FlagsForExport))
                {
                    if (certificate.HasPrivateKey == false)
                        throw new AuthorizationException("Certificate with private key is required");
                }
            }
        }

        public RavenCommand<ModifyOngoingTaskResult> GetCommand(DocumentConventions conventions, JsonOperationContext ctx)
        {
            return new UpdatePullEdgeReplication(_pullReplication, _keepOriginalCertificateOnNull);
        }

        private class UpdatePullEdgeReplication(PullReplicationAsSink pullReplication, bool keepOriginalCertificateOnNull) : RavenCommand<ModifyOngoingTaskResult>, IRaftCommand
        {

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                DynamicJsonValue replication = pullReplication.ToJson();
                
                // Aligned with ServerStore.UpdatePullReplicationAsSink to not introduce breaking changes
                if (pullReplication.CertificateWithPrivateKey == null && keepOriginalCertificateOnNull)
                {
                    replication.Remove(nameof(PullReplicationAsSink.CertificateWithPrivateKey));
                }
                
                url = $"{node.Url}/databases/{node.Database}/admin/tasks/sink-pull-replication";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    Content = new BlittableJsonContent(async stream =>
                    {
                        var json = new DynamicJsonValue
                        {
                            ["PullReplicationAsSink"] = replication
                        };

                        await ctx.WriteAsync(stream, ctx.ReadObject(json, "update-pull-replication")).ConfigureAwait(false);
                    })
                };

                return request;
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.ModifyOngoingTaskResult(response);
            }

            public override bool IsReadRequest => false;
            public string RaftUniqueRequestId { get; } = RaftIdGenerator.NewId();
        }
    }
}
