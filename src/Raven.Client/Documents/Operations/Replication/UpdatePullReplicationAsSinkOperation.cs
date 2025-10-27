using System;
using System.Diagnostics;
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
        private readonly bool _useServerCertificate;

        // Kept for the binary compatibility purposes.
        public UpdatePullReplicationAsSinkOperation(PullReplicationAsSink pullReplication) : this(pullReplication, false)
        {
        }
        
        /// <summary>
        /// Initializes the update operation for the <see cref="PullReplicationAsSink"/>.
        /// </summary>
        /// <param name="pullReplication">The pull replication object.</param>
        /// <param name="useServerCertificate">Makes the replication use the server certificate. Requires <see cref="PullReplicationAsSink.CertificateWithPrivateKey"/> to be null.</param>
        /// <exception cref="AuthorizationException">If the <see cref="PullReplicationAsSink.CertificateWithPrivateKey"/> isn't null and cannot be parsed.</exception>
        public UpdatePullReplicationAsSinkOperation(PullReplicationAsSink pullReplication, bool useServerCertificate = false)
        {
            _pullReplication = pullReplication;
            _useServerCertificate = useServerCertificate;

            if (pullReplication.CertificateWithPrivateKey != null)
            {
                if (useServerCertificate)
                    throw new ArgumentException(
                        $"When {nameof(useServerCertificate)} is set to true, " +
                        $"{nameof(PullReplicationAsSink.CertificateWithPrivateKey)} should be null to use server certificate.");
                
                
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
            return new UpdatePullEdgeReplication(_pullReplication, _useServerCertificate);
        }

        private class UpdatePullEdgeReplication(PullReplicationAsSink pullReplication, bool useServerCertificate) : RavenCommand<ModifyOngoingTaskResult>, IRaftCommand
        {

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                DynamicJsonValue replication = pullReplication.ToJson();
                
                // Aligned with ServerStore.UpdatePullReplicationAsSink to not introduce breaking changes
                if (pullReplication.CertificateWithPrivateKey == null && useServerCertificate)
                {
                    int removed = replication.Properties.RemoveAll(pair => pair.Name == nameof(PullReplicationAsSink.CertificateWithPrivateKey));
                    Debug.Assert(removed > 0);
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
