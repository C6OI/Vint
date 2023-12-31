﻿using Serilog;
using Vint.Core.Database;
using Vint.Core.Database.Models;
using Vint.Core.ECS.Entities;
using Vint.Core.Protocol.Attributes;
using Vint.Core.Server;
using Vint.Core.Utils;

namespace Vint.Core.ECS.Events.Entrance.Login;

[ProtocolId(1438075609642)]
public class AutoLoginUserEvent : IServerEvent {
    [ProtocolName("uid")] public string Username { get; private set; } = null!;
    public byte[] EncryptedToken { get; private set; } = null!;
    public string HardwareFingerprint { get; private set; } = null!;

    public void Execute(IPlayerConnection connection, IEnumerable<IEntity> entities) {
        ILogger logger = connection.Logger.ForType(GetType());

        logger.Information("Autologin '{Username}'", Username);

        using DbConnection database = new();
        Player? player = database.Players.SingleOrDefault(player => player.Username == Username);

        if (player == null) {
            connection.Send(new AutoLoginFailedEvent());
            return;
        }

        if (player.HardwareFingerprint == HardwareFingerprint)
            connection.Player = player;

        if (player.AutoLoginToken.SequenceEqual(new Encryption().RsaDecrypt(EncryptedToken)))
            connection.Send(new PersonalPasscodeEvent());
        else connection.Send(new AutoLoginFailedEvent());
    }
}