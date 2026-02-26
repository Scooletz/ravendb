using System.Collections.Generic;
using System.Linq;
using Sparrow.Json.Parsing;

namespace Raven.Server.Commercial
{
    public sealed class LicenseLimits
    {
        public LicenseLimits()
        {
            NodeLicenseDetails = new Dictionary<string, DetailsPerNode>();
        }

        public Dictionary<string, DetailsPerNode> NodeLicenseDetails { get; set; }

        public int TotalUtilizedCores => NodeLicenseDetails.Sum(x => x.Value.UtilizedCores);

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue(1)
            {
                [nameof(NodeLicenseDetails)] = DynamicJsonValue.Convert(NodeLicenseDetails)
            };
        }
    }
}
