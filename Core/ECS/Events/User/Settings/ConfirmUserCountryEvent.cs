﻿using LinqToDB;
using Vint.Core.Database;
using Vint.Core.ECS.Entities;
using Vint.Core.Protocol.Attributes;
using Vint.Core.Server;

namespace Vint.Core.ECS.Events.User.Settings;

[ProtocolId(1465192871085)]
public class ConfirmUserCountryEvent : IServerEvent {
    public string CountryCode { get; private set; } = null!;

    public void Execute(IPlayerConnection connection, IEnumerable<IEntity> entities) {
        using DbConnection db = new();
        connection.Player.CountryCode = CountryCode;
        db.Update(connection.Player);
    }
}