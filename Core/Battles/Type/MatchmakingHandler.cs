using System.Diagnostics;
using Vint.Core.Battles.Player;
using Vint.Core.Battles.States;
using Vint.Core.Config;
using Vint.Core.Config.MapInformation;
using Vint.Core.ECS.Components.Matchmaking;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Events.Matchmaking;
using Vint.Core.ECS.Templates.Lobby;
using Vint.Core.Server;
using Vint.Core.Utils;

namespace Vint.Core.Battles.Type;

public class MatchmakingHandler(
    Battle battle
) : TypeHandler(battle) {
    public BattleMode BattleMode { get; } = GetRandomMode();

    List<BattlePlayer> WaitingPlayers { get; } = [];

    public override void Setup() {
        MapInfo mapInfo = ConfigManager.MapInfos.Values
            .Where(map => map.MatchMaking)
            .ToList()
            .Shuffle()
            .First();

        Battle.Properties = new BattleProperties(
            BattleMode,
            GravityType.Earth,
            mapInfo.MapId,
            false,
            true,
            true,
            mapInfo.MaxPlayers,
            10,
            100);

        Battle.MapInfo = mapInfo;
        Battle.MapEntity = GlobalEntities.GetEntities("maps").Single(map => map.Id == mapInfo.MapId);
        Battle.LobbyEntity = new MatchMakingLobbyTemplate().Create(
            Battle.Properties,
            Battle.MapEntity,
            BattleProperties.GravityToForce[Battle.Properties.Gravity]);
    }

    public override void Tick() {
        foreach (BattlePlayer player in WaitingPlayers.ToArray()) {
            if (DateTimeOffset.UtcNow < player.BattleJoinTime) continue;

            player.Init();
            WaitingPlayers.Remove(player);
        }
    }

    public override void PlayerEntered(BattlePlayer battlePlayer) { // todo
        IPlayerConnection connection = battlePlayer.PlayerConnection;
        IEntity user = connection.User;

        user.AddComponent(new MatchMakingUserComponent());

        if (Battle.StateManager.CurrentState is not (WarmUp or Running)) return;

        connection.Send(new MatchMakingLobbyStartTimeEvent(battlePlayer.BattleJoinTime), user);
        WaitingPlayers.Add(battlePlayer);
    }

    public override void PlayerExited(BattlePlayer battlePlayer) {
        WaitingPlayers.Remove(battlePlayer);
        battlePlayer.PlayerConnection.User.RemoveComponent<MatchMakingUserComponent>();
    }

    static BattleMode GetRandomMode() => new Random().Next(0, 100) switch { // todo
        //< 34 => BattleMode.CTF,
        //< 67 => BattleMode.TDM,
        <= 100 => BattleMode.DM,
        _ => throw new UnreachableException()
    };
}