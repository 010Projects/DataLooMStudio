using Azure.Core;
using Azure.Messaging.ServiceBus;

using DataLooMStudio.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace DataLooMStudio.Infrastructure.Outbox;

public sealed class ServiceBusOutboxPublisher(
    IOptionsMonitor<DataLooMInfrastructureOptions> options,
    TokenCredential credential) : IOutboxPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim senderGate = new(1, 1);
    private ServiceBusClient? client;
    private ServiceBusSender? sender;
    private string? senderKey;

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(current.ServiceBusFullyQualifiedNamespace))
        {
            throw new InvalidOperationException("Service Bus namespace is not configured.");
        }

        var activeSender = await GetSenderAsync(current, cancellationToken);

        var busMessage = new ServiceBusMessage(message.PayloadJson)
        {
            ContentType = "application/json",
            CorrelationId = message.CorrelationId,
            MessageId = message.Id.ToString("D"),
            Subject = message.MessageType
        };

        busMessage.ApplicationProperties["tenantId"] = message.TenantId.ToString();
        busMessage.ApplicationProperties["workspaceId"] = message.WorkspaceId.ToString();
        busMessage.ApplicationProperties["owningModule"] = message.OwningModule;

        await activeSender.SendMessageAsync(busMessage, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await senderGate.WaitAsync();
        try
        {
            if (sender is not null)
            {
                await sender.DisposeAsync();
            }

            if (client is not null)
            {
                await client.DisposeAsync();
            }
        }
        finally
        {
            senderGate.Dispose();
        }
    }

    private async Task<ServiceBusSender> GetSenderAsync(
        DataLooMInfrastructureOptions current,
        CancellationToken cancellationToken)
    {
        var key = $"{current.ServiceBusFullyQualifiedNamespace}|{current.ServiceBusOutboxTopic}";
        if (sender is not null && key.Equals(senderKey, StringComparison.Ordinal))
        {
            return sender;
        }

        await senderGate.WaitAsync(cancellationToken);
        try
        {
            if (sender is not null && key.Equals(senderKey, StringComparison.Ordinal))
            {
                return sender;
            }

            if (sender is not null)
            {
                await sender.DisposeAsync();
            }

            if (client is not null)
            {
                await client.DisposeAsync();
            }

            client = new ServiceBusClient(current.ServiceBusFullyQualifiedNamespace, credential);
            sender = client.CreateSender(current.ServiceBusOutboxTopic);
            senderKey = key;
            return sender;
        }
        finally
        {
            senderGate.Release();
        }
    }
}