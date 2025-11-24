using Newtonsoft.Json;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.AI.Agents;

public class AiAgentActionRequest : IDynamicJson
{
    public string Name;
    public string ToolId;
    public string Arguments;

    public AiAgentActionRequestType Type;
    public string SubConversation;
    // one sub-agent call, can have multiple user actions assosiated
    public int RefUserActions;

    public bool IsEqual(AiAgentActionRequest other)
    {
        if (other == null)
            return false;

        return
            Name == other.Name &&
            Arguments == other.Arguments &&
            Type == other.Type &&
            SubConversation == other.SubConversation &&
            RefUserActions == other.RefUserActions;
    }

    public override string ToString()
    {
        using(var ctx = JsonOperationContext.ShortTermSingleUse())
            return ctx.ReadObject(ToJson(), string.Empty).ToString();
    }

    public DynamicJsonValue ToJson()
    {
        return new DynamicJsonValue
        {
            [nameof(Name)] = Name,
            [nameof(ToolId)] = ToolId,
            [nameof(Arguments)] = Arguments,
            [nameof(Type)] = Type,
            [nameof(SubConversation)] = SubConversation,
            [nameof(RefUserActions)] = RefUserActions
        };
    }
}

public enum AiAgentActionRequestType
{
    UserAction,
    SubAgent
}
