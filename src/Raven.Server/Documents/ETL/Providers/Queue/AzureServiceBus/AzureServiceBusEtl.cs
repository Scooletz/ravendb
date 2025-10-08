using System;
using System.Collections.Generic;
using System.Threading;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Raven.Client.Documents.Operations.ETL;
using Raven.Client.Documents.Operations.ETL.Queue;
using Raven.Server.Documents.ETL.Stats;
using Raven.Server.Documents.ETL.Providers.Queue;
using Raven.Server.Exceptions.ETL.QueueEtl;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.ETL.Providers.Queue.AzureServiceBus;

public sealed class AzureServiceBusEtl : QueueEtl<AzureServiceBusItem>
{
    private readonly Dictionary<string, ServiceBusSender> _senders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _declaredEntities = new(StringComparer.OrdinalIgnoreCase);

    private ServiceBusClient _client;
    private ServiceBusAdministrationClient _administrationClient;

    public AzureServiceBusEtl(Transformation transformation, QueueEtlConfiguration configuration,
        DocumentDatabase database, ServerStore serverStore) : base(transformation, configuration, database, serverStore)
    {
    }

    protected override
        EtlTransformer<QueueItem, QueueWithItems<AzureServiceBusItem>, EtlStatsScope, EtlPerformanceOperation>
        GetTransformer(DocumentsOperationContext context, EtlStatsScope stats)
    {
        return new AzureServiceBusDocumentTransformer<AzureServiceBusItem>(Transformation, Database, context,
            Configuration);
    }

    protected override int PublishMessages(List<QueueWithItems<AzureServiceBusItem>> itemsPerQueue,
        BlittableJsonEventBinaryFormatter formatter, out List<string> idsToDelete)
    {
        if (itemsPerQueue.Count == 0)
        {
            idsToDelete = null;
            return 0;
        }

        EnsureClients();

        idsToDelete = new List<string>();
        var count = 0;

        foreach (QueueWithItems<AzureServiceBusItem> queue in itemsPerQueue)
        {
            var entityName = queue.Name;

            if (Configuration.SkipAutomaticQueueDeclaration == false)
                EnsureEntityExists(entityName);

            var sender = GetOrCreateSender(entityName);

            foreach (AzureServiceBusItem queueItem in queue.Items)
            {
                CancellationToken.ThrowIfCancellationRequested();

                var cloudEvent = CreateCloudEvent(queueItem);
                var body = formatter.EncodeBinaryModeEventData(cloudEvent);

                var message = new ServiceBusMessage(new BinaryData(body))
                {
                    ContentType = cloudEvent.DataContentType ?? "application/json",
                    Subject = cloudEvent.Subject,
                    MessageId = cloudEvent.Id
                };

                if (queueItem.Attributes?.PartitionKey != null)
                    message.PartitionKey = queueItem.Attributes.PartitionKey;
                else if (queueItem.DocumentId != null)
                    message.PartitionKey = queueItem.DocumentId;

                if (cloudEvent.Time != null)
                    message.ApplicationProperties["ce-time"] = cloudEvent.Time.Value.UtcDateTime;

                if (cloudEvent.Source != null)
                    message.ApplicationProperties["ce-source"] = cloudEvent.Source.ToString();

                if (cloudEvent.Type != null)
                    message.ApplicationProperties["ce-type"] = cloudEvent.Type;

                if (cloudEvent.DataSchema != null)
                    message.ApplicationProperties["ce-dataschema"] = cloudEvent.DataSchema.ToString();

                message.ApplicationProperties["raven-document-id"] = queueItem.DocumentId;
                message.ApplicationProperties["raven-change-vector"] = queueItem.ChangeVector;

                try
                {
                    sender.SendMessageAsync(message).GetAwaiter().GetResult();
                    count++;

                    if (queue.DeleteProcessedDocuments)
                        idsToDelete.Add(queueItem.DocumentId);
                }
                catch (ServiceBusException ex)
                {
                    throw new QueueLoadException(
                        $"Failed to deliver message. Azure Service Bus reason: '{ex.Reason}'. Error: '{ex.Message}'", ex);
                }
                catch (Exception ex)
                {
                    throw new QueueLoadException($"Failed to deliver message, error reason: '{ex.Message}'", ex);
                }
            }
        }

        return count;
    }

    private void EnsureClients()
    {
        if (_client == null)
            _client = QueueBrokerConnectionHelper.CreateAzureServiceBusClient(
                Configuration.Connection.AzureServiceBusConnectionSettings);

        if (Configuration.SkipAutomaticQueueDeclaration == false && _administrationClient == null)
            _administrationClient = QueueBrokerConnectionHelper.CreateAzureServiceBusAdministrationClient(
                Configuration.Connection.AzureServiceBusConnectionSettings);
    }

    private void EnsureEntityExists(string entityName)
    {
        if (_administrationClient == null)
            return;

        if (_declaredEntities.Contains(entityName))
            return;

        try
        {
            if (_administrationClient.QueueExistsAsync(entityName).GetAwaiter().GetResult() == false)
                _administrationClient.CreateQueueAsync(entityName).GetAwaiter().GetResult();

            _declaredEntities.Add(entityName);
        }
        catch (ServiceBusException ex)
        {
            throw new QueueLoadException(
                $"Failed to ensure queue '{entityName}' exists. Azure Service Bus reason: '{ex.Reason}'. Error: '{ex.Message}'",
                ex);
        }
        catch (Exception ex)
        {
            throw new QueueLoadException($"Failed to ensure queue '{entityName}' exists, error reason: '{ex.Message}'", ex);
        }
    }

    private ServiceBusSender GetOrCreateSender(string entityName)
    {
        if (_senders.TryGetValue(entityName, out var sender))
            return sender;

        sender = _client.CreateSender(entityName);
        _senders[entityName] = sender;
        return sender;
    }

    protected override void OnProcessStopped()
    {
        foreach (ServiceBusSender sender in _senders.Values)
        {
            try
            {
                sender.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore disposal errors
            }
        }

        _senders.Clear();
        _declaredEntities.Clear();

        if (_client != null)
        {
            try
            {
                _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore disposal errors
            }

            _client = null;
        }

        if (_administrationClient != null)
        {
            try
            {
                (_administrationClient as IAsyncDisposable)?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore disposal errors
            }

            _administrationClient = null;
        }
    }
}
