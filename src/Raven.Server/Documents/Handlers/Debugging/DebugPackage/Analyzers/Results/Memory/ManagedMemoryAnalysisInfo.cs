using Raven.Server.Dashboard.Cluster.Notifications;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Handlers.Debugging.DebugPackage.Analyzers.Results.Memory;

public class ManagedMemoryAnalysisInfo : IDynamicJson
{
    public string ManagedAllocations { get; set; }

    public string LuceneManagedAllocationsForTermCache { get; set; }

    public GcInfoPayload.GcMemoryInfo LastGcInfo { get; set; }
    public DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue(2)
        {
            [nameof(ManagedAllocations)] = ManagedAllocations,
            [nameof(LuceneManagedAllocationsForTermCache)] = LuceneManagedAllocationsForTermCache,
        };
    }
}
