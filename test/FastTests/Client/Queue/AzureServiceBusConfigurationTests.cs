using System.Collections.Generic;
using Raven.Client.Documents.Operations.ETL.Queue;
using Xunit;

namespace FastTests.Client.Queue;

public class AzureServiceBusConfigurationTests
{
    private const string SampleConnectionString =
        "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dummy=";

    [Fact]
    public void Validate_should_fail_when_settings_missing()
    {
        var connectionString = new QueueConnectionString
        {
            Name = "AzureServiceBus",
            BrokerType = QueueBrokerType.AzureServiceBus
        };

        var errors = new List<string>();

        var isValid = connectionString.Validate(errors);

        Assert.False(isValid);
        Assert.Contains("AzureServiceBusConnectionSettings has no valid setting.", errors);
    }

    [Fact]
    public void Validate_should_succeed_with_connection_string()
    {
        var connectionString = new QueueConnectionString
        {
            Name = "AzureServiceBus",
            BrokerType = QueueBrokerType.AzureServiceBus,
            AzureServiceBusConnectionSettings = new AzureServiceBusConnectionSettings
            {
                ConnectionString = SampleConnectionString
            }
        };

        var errors = new List<string>();

        var isValid = connectionString.Validate(errors);

        Assert.True(isValid);
        Assert.Empty(errors);
    }

    [Fact]
    public void GetUrl_should_return_endpoint()
    {
        var connectionString = new QueueConnectionString
        {
            Name = "AzureServiceBus",
            BrokerType = QueueBrokerType.AzureServiceBus,
            AzureServiceBusConnectionSettings = new AzureServiceBusConnectionSettings
            {
                ConnectionString = SampleConnectionString
            }
        };

        var url = connectionString.GetUrl();

        Assert.Equal("sb://contoso.servicebus.windows.net/", url);
    }

    [Fact]
    public void UsingEncryptedCommunicationChannel_should_return_true_for_service_bus()
    {
        var connection = new QueueConnectionString
        {
            Name = "AzureServiceBus",
            BrokerType = QueueBrokerType.AzureServiceBus,
            AzureServiceBusConnectionSettings = new AzureServiceBusConnectionSettings
            {
                ConnectionString = SampleConnectionString
            }
        };

        var configuration = new QueueEtlConfiguration
        {
            Name = "AzureServiceBus",
            BrokerType = QueueBrokerType.AzureServiceBus
        };

        configuration.Initialize(connection);

        Assert.True(configuration.UsingEncryptedCommunicationChannel());
    }
}
