# Azure Service Bus Queue Integration Plan

## Goals
- Support Azure Service Bus as both an **outgoing transport** (Queue ETL) and an **incoming transport** (Queue Sink) within RavenDB.
- Keep the user experience aligned with existing queue brokers (Kafka, RabbitMQ, Amazon SQS, Azure Queue Storage).
- Ensure that configuration, monitoring, scripting, and diagnostics work consistently across the new transport.

## High-Level Architecture
1. **Client API surface**
   - Extend `QueueBrokerType` with `AzureServiceBus` (done).
   - Introduce `AzureServiceBusConnectionSettings` describing namespace, entity path (queue or topic/subscription), and authentication (connection string, SAS, or Entra ID managed identity).
   - Update `QueueConnectionString` serialization/deserialization to persist the new settings and validate mutually exclusive authentication modes.
   - Extend `QueueEtlConfiguration` and `QueueSinkConfiguration` DTOs to accept Azure Service Bus specific options (entity declarations, prefetch, batching, dead-letter handling flags).

2. **Server-side ETL (Outgoing)**
   - Create `AzureServiceBusEtl` that derives from `QueueEtl<QueueItem>`.
   - Implement an `AzureServiceBusQueue` writer responsible for batching CloudEvent messages and publishing them using the `Azure.Messaging.ServiceBus` SDK.
   - Provide queue/topic declaration helpers honoring the `SkipAutomaticQueueDeclaration` flag and configurable entity creation policies.
   - Ensure checkpointing reuses existing queue ETL storage (no schema changes expected) but extend state to capture sequence numbers and session ids if required.

3. **Server-side Queue Sink (Incoming)**
   - Implement `AzureServiceBusQueueSink` with a `ServiceBusProcessor` (queue) and `ServiceBusSessionProcessor` (topic/subscription) based consumer.
   - Support concurrency controls (max concurrent calls, session lock renewal) and graceful shutdown semantics consistent with other sinks.
   - Map Service Bus message metadata to `QueueItemAttributes` (message id, session id, correlation id, delivery count, dead letter reason).
   - Integrate poison-message handling by routing repeatedly failing messages to the Service Bus dead-letter queue.

4. **Shared infrastructure**
   - Add connection string bootstrap validation for Service Bus namespaces and credentials.
   - Extend testing harnesses and queue simulators to understand the new broker type.
   - Update dashboard counters, ongoing tasks UI, and debugging tools to surface Azure Service Bus ETL/Sink counts and stats.

## Implementation Steps
1. **Model Layer**
   - [x] Extend `QueueBrokerType` with `AzureServiceBus` (part of this change).
   - [ ] Add `AzureServiceBusConnectionSettings` and hook it into `QueueConnectionString` (validation, equality, serialization).
   - [ ] Introduce ETL/Sink configuration knobs (e.g., entity type, max batch size, prefetch count, auto-declare entity, dead-letter options).

2. **Server ETL Pipeline**
   - [ ] Implement `AzureServiceBusEtl` class along with publisher abstractions.
   - [ ] Extend `QueueEtlConfiguration.UsingEncryptedCommunicationChannel` and connection bootstrap logic to compute encryption status using Service Bus endpoints.
   - [ ] Wire the ETL loader to instantiate the new process and track metrics/state.

3. **Queue Sink Pipeline**
   - [ ] Implement `AzureServiceBusQueueSink` and underlying consumer that maps Service Bus messages into `QueueItem` instances.
   - [ ] Integrate acknowledgement, retry, and dead-letter handling with `QueueSinkProcess` contracts.
   - [ ] Extend sink loader metadata and stats collectors.

4. **Studio & Tooling**
   - [ ] Update Studio models/commands to expose Azure Service Bus as a selectable broker type with connection string editors.
   - [ ] Enhance dashboard widgets and ongoing task pages to show counts and state for the new transport.
   - [ ] Provide documentation snippets and sample scripts covering both queue and topic/subscription scenarios.

## Testing Strategy
- **Unit / Fast Tests**
  - Validate configuration and factory behavior (see `AzureServiceBusConfigurationTests` added with this plan) to ensure unsupported scenarios fail with explicit messages until the full implementation arrives.
  - Add serialization round-trip tests for `AzureServiceBusConnectionSettings` once introduced.

- **Integration Tests (Slow Tests)**
  - Queue ETL:
    - Verify message publishing to queues and topics, including batched writes and CloudEvent metadata mapping.
    - Confirm checkpoint persistence and resume after restarts.
    - Test automatic entity creation toggles and failure reporting.
  - Queue Sink:
    - Validate message consumption for queues and topic subscriptions (with and without sessions).
    - Ensure message retries, dead-letter routing, and fallback intervals behave as expected.
    - Exercise script error handling and document deletion settings.

- **Stress / Reliability**
  - Long-running soak tests covering session-enabled subscriptions and high-throughput queues.
  - Fault-injection scenarios: transient network failures, SAS token rotation, entity removal.

## Deliverables Snapshot
- Enumerations and configuration models updated to include Azure Service Bus.
- Placeholder fast tests guaranteeing that unsupported usage surfaces actionable errors.
- Comprehensive plan for extending ETL and Queue Sink stacks, including server, client, and Studio work.
- Explicit testing roadmap to be executed alongside the implementation.
