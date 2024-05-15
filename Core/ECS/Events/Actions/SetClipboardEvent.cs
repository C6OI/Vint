using Vint.Core.Protocol.Attributes;

namespace Vint.Core.ECS.Events.Actions;

[ProtocolId(31217)]
public class SetClipboardEvent(
    string content
) : IEvent {
    public string Content { get; private set; } = content;
}