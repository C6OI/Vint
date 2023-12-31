﻿using System.Diagnostics.CodeAnalysis;
using Serilog;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Events;
using Vint.Core.Protocol.Attributes;
using Vint.Core.Server;
using Vint.Core.Utils;

namespace Vint.Core.Protocol.Commands;

[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
public class SendEventCommand(
    IEvent @event,
    params IEntity[] entities
) : ICommand {
    [ProtocolIgnore] static ILogger Logger { get; } = Log.Logger.ForType(typeof(SendEventCommand));
    [ProtocolVaried, ProtocolPosition(0)] public IEvent Event { get; private set; } = @event;
    [ProtocolPosition(1)] public IEntity[] Entities { get; private set; } = entities;

    public void Execute(IPlayerConnection connection) {
        if (Event is not IServerEvent serverEvent) {
            Logger.Warning("Event {Event} is not IServerEvent", Event);
            return;
        }

        Logger.Information("Executing event {Name} for {Count} entities", serverEvent.GetType().Name, Entities.Length);

        serverEvent.Execute(connection, Entities);
    }

    public override string ToString() => $"SendEvent command {{ " +
                                         $"Event: {Event.GetType().Name}, " +
                                         $"Entities: {{ {Entities.ToString(true)} }} }}";
}