using System;
using Raven.Client.Documents.Operations.ETL.SQL;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.ETL.Queue;

public sealed class AzureServiceBusConnectionSettings
{
    public string ConnectionString { get; set; }

    public AzureServiceBusEntraId EntraId { get; set; }

    public AzureServiceBusPasswordless Passwordless { get; set; }

    public bool IsValidConnection()
    {
        if (IsOnlyOneConnectionProvided() == false)
            return false;

        if (EntraId != null && EntraId.IsValid() == false)
            return false;

        if (Passwordless != null && Passwordless.IsValid() == false)
            return false;

        if (ConnectionString != null)
        {
            try
            {
                _ = GetEndpoint();
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private bool IsOnlyOneConnectionProvided()
    {
        int count = 0;

        if (!string.IsNullOrWhiteSpace(ConnectionString))
            count++;

        if (EntraId != null)
            count++;

        if (Passwordless != null)
            count++;

        return count == 1;
    }

    public string GetEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            var endpoint = SqlConnectionStringParser.GetConnectionStringValue(ConnectionString, ["Endpoint"]);
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint not found in the connection string", nameof(ConnectionString));

            return EnsureTrailingSlash(endpoint);
        }

        var fullyQualifiedNamespace = GetFullyQualifiedNamespace();
        return string.IsNullOrWhiteSpace(fullyQualifiedNamespace)
            ? null
            : $"sb://{fullyQualifiedNamespace}/";
    }

    public string GetFullyQualifiedNamespace()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            var endpoint = SqlConnectionStringParser.GetConnectionStringValue(ConnectionString, ["Endpoint"]);
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint not found in the connection string", nameof(ConnectionString));

            var uri = new Uri(endpoint);
            return uri.Host;
        }

        if (EntraId != null)
            return EntraId.FullyQualifiedNamespace;

        if (Passwordless != null)
            return Passwordless.FullyQualifiedNamespace;

        return null;
    }

    public DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue
        {
            [nameof(ConnectionString)] = ConnectionString,
            [nameof(EntraId)] = EntraId?.ToJson(),
            [nameof(Passwordless)] = Passwordless?.ToJson()
        };
    }

    public DynamicJsonValue ToAuditJson()
    {
        return new DynamicJsonValue
        {
            [nameof(EntraId)] = EntraId?.ToAuditJson(),
            [nameof(Passwordless)] = Passwordless?.ToAuditJson(),
            ["ConnectionStringDefined"] = string.IsNullOrWhiteSpace(ConnectionString) == false
        };
    }

    private static string EnsureTrailingSlash(string endpoint)
    {
        if (endpoint.EndsWith("/", StringComparison.Ordinal))
            return endpoint;

        return endpoint + "/";
    }

    private bool Equals(AzureServiceBusConnectionSettings other)
    {
        return string.Equals(ConnectionString, other.ConnectionString, StringComparison.Ordinal) &&
               Equals(EntraId, other.EntraId) &&
               Equals(Passwordless, other.Passwordless);
    }

    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj) || obj is AzureServiceBusConnectionSettings other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ConnectionString, EntraId, Passwordless);
    }
}

public sealed class AzureServiceBusEntraId
{
    public string FullyQualifiedNamespace { get; set; }

    public string TenantId { get; set; }

    public string ClientId { get; set; }

    public string ClientSecret { get; set; }

    public bool IsValid()
    {
        return string.IsNullOrWhiteSpace(FullyQualifiedNamespace) == false &&
               string.IsNullOrWhiteSpace(TenantId) == false &&
               string.IsNullOrWhiteSpace(ClientId) == false &&
               string.IsNullOrWhiteSpace(ClientSecret) == false;
    }

    internal DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue
        {
            [nameof(FullyQualifiedNamespace)] = FullyQualifiedNamespace,
            [nameof(TenantId)] = TenantId,
            [nameof(ClientId)] = ClientId,
            [nameof(ClientSecret)] = ClientSecret
        };
    }

    internal DynamicJsonValue ToAuditJson()
    {
        return new DynamicJsonValue
        {
            [nameof(FullyQualifiedNamespace)] = FullyQualifiedNamespace,
            [nameof(TenantId)] = TenantId,
            [nameof(ClientId)] = ClientId,
            ["ClientSecretDefined"] = string.IsNullOrWhiteSpace(ClientSecret) == false
        };
    }

    private bool Equals(AzureServiceBusEntraId other)
    {
        return string.Equals(FullyQualifiedNamespace, other.FullyQualifiedNamespace, StringComparison.Ordinal) &&
               string.Equals(TenantId, other.TenantId, StringComparison.Ordinal) &&
               string.Equals(ClientId, other.ClientId, StringComparison.Ordinal) &&
               string.Equals(ClientSecret, other.ClientSecret, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj) || obj is AzureServiceBusEntraId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FullyQualifiedNamespace, TenantId, ClientId, ClientSecret);
    }
}

public sealed class AzureServiceBusPasswordless
{
    public string FullyQualifiedNamespace { get; set; }

    public bool IsValid()
    {
        return string.IsNullOrWhiteSpace(FullyQualifiedNamespace) == false;
    }

    internal DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue
        {
            [nameof(FullyQualifiedNamespace)] = FullyQualifiedNamespace
        };
    }

    internal DynamicJsonValue ToAuditJson()
    {
        return new DynamicJsonValue
        {
            [nameof(FullyQualifiedNamespace)] = FullyQualifiedNamespace
        };
    }

    private bool Equals(AzureServiceBusPasswordless other)
    {
        return string.Equals(FullyQualifiedNamespace, other.FullyQualifiedNamespace, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj) || obj is AzureServiceBusPasswordless other && Equals(other);
    }

    public override int GetHashCode()
    {
        return FullyQualifiedNamespace != null ? FullyQualifiedNamespace.GetHashCode(StringComparison.Ordinal) : 0;
    }
}
