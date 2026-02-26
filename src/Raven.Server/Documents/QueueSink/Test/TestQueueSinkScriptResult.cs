using System.Collections.Generic;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.QueueSink.Test
{
    public class TestQueueSinkScriptResult : IDynamicJson
    {
        public List<string> DebugOutput { get; set; }

        public DynamicJsonValue Actions { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue(2)
            {
                [nameof(DebugOutput)] = new DynamicJsonArray(DebugOutput),
                [nameof(Actions)] = Actions
            };
        }
    }
}
