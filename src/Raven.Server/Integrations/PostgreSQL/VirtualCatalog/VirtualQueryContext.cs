using Raven.Server.Documents;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog
{
    internal sealed class VirtualQueryContext
    {
        public DocumentDatabase Database { get; init; }
    }
}
