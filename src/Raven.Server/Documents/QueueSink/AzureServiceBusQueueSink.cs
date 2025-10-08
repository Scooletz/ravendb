using Raven.Client.Documents.Operations.QueueSink;
using Raven.Server.Documents.ETL.Providers.Queue;

namespace Raven.Server.Documents.QueueSink;

public sealed class AzureServiceBusQueueSink : QueueSinkProcess
{
    public AzureServiceBusQueueSink(QueueSinkConfiguration configuration, QueueSinkScript script, DocumentDatabase database,
        string tag) : base(configuration, script, database, tag)
    {
    }

    protected override IQueueSinkConsumer CreateConsumer()
    {
        var client = QueueBrokerConnectionHelper.CreateAzureServiceBusClient(
            Configuration.Connection.AzureServiceBusConnectionSettings);

        return new AzureServiceBusSinkConsumer(client, Script.Queues);
    }
}
