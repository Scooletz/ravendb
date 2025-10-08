using System;
using System.Collections.Generic;
using System.Threading;
using Azure.Messaging.ServiceBus;

namespace Raven.Server.Documents.QueueSink;

public sealed class AzureServiceBusSinkConsumer : IQueueSinkConsumer
{
    private readonly ServiceBusClient _client;
    private readonly List<ServiceBusReceiver> _receivers = new();
    private readonly List<(ServiceBusReceiver Receiver, ServiceBusReceivedMessage Message)> _pending = new();
    private readonly TimeSpan _defaultWaitTime = TimeSpan.FromSeconds(5);

    public AzureServiceBusSinkConsumer(ServiceBusClient client, IEnumerable<string> queueNames)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        if (queueNames == null)
            throw new ArgumentNullException(nameof(queueNames));

        foreach (var queue in queueNames)
        {
            if (string.IsNullOrWhiteSpace(queue))
                continue;

            _receivers.Add(_client.CreateReceiver(queue));
        }
    }

    public byte[] Consume(CancellationToken cancellationToken)
    {
        return ConsumeInternal(null, cancellationToken);
    }

    public byte[] Consume(TimeSpan timeout)
    {
        return ConsumeInternal(timeout, CancellationToken.None);
    }

    private byte[] ConsumeInternal(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (_receivers.Count == 0)
            return null;

        var waitTime = timeout ?? _defaultWaitTime;

        while (true)
        {
            foreach (var receiver in _receivers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ServiceBusReceivedMessage message;
                try
                {
                    message = receiver.ReceiveMessageAsync(waitTime, cancellationToken).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }

                if (message == null)
                    continue;

                _pending.Add((receiver, message));
                return message.Body.ToArray();
            }

            if (timeout.HasValue)
                return null;
        }
    }

    public void Commit()
    {
        foreach (var (receiver, message) in _pending)
        {
            receiver.CompleteMessageAsync(message).GetAwaiter().GetResult();
        }

        _pending.Clear();
    }

    public void Dispose()
    {
        foreach (var (receiver, message) in _pending)
        {
            try
            {
                receiver.AbandonMessageAsync(message).GetAwaiter().GetResult();
            }
            catch
            {
                // ignore failures during cleanup
            }
        }

        _pending.Clear();

        foreach (var receiver in _receivers)
        {
            try
            {
                receiver.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore failures during cleanup
            }
        }

        try
        {
            _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // ignore failures during cleanup
        }
    }
}
