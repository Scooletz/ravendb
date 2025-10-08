namespace Raven.Server.Documents.ETL.Providers.Queue.AzureServiceBus;

public sealed class AzureServiceBusItem : QueueItem
{
    public AzureServiceBusItem(QueueItem item) : base(item)
    {
    }
}
