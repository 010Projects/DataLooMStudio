using Azure.Core;
using Azure.Messaging.ServiceBus;

using DataLooMStudio.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace DataLooMStudio.Infrastructure.Outbox;

public sealed class ServiceBusOutboxPublisher(
    IOptionsMonitor<DataLooMInfrastructureOptions> options,
    TokenCredential credential) : IOutboxPublisher
{
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(current.ServiceBusFullyQualifiedNamespace))
        {
            throw new InvalidOperationException("Service Bus namespace is not configured.");
        }

        await using var client = new ServiceBusClient(current.ServiceBusFullyQualifiedNamespace, credential);
        var sender = client.CreateSender(current.ServiceBusOutboxTopic);

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

        await sender.SendMessageAsync(busMessage, cancellationToken);
    }
}