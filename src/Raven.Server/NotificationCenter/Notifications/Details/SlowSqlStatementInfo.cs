using System;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.NotificationCenter.Notifications.Details
{
    public sealed class SlowSqlStatementInfo : IDynamicJson
    {
        public long Duration { get; set; }
        public DateTime Date { get; set; }
        public string Statement { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue(3)
            {
                [nameof(Duration)] = Duration,
                [nameof(Date)] = Date,
                [nameof(Statement)] = Statement
            };
        }
    }
}
