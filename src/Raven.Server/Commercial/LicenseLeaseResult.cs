using Sparrow.Json.Parsing;

namespace Raven.Server.Commercial
{
    public class LicenseLeaseResult
    {
        public LeaseStatus Status { get; set; }
        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue(1)
            {
                [nameof(Status)] = Status
            };
        }
    }
    public enum LeaseStatus
    {
        Updated,
        NotModified
    }
}
