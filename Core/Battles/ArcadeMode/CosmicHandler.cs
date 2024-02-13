using Vint.Core.Config.MapInformation;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Templates.Lobby;
using Vint.Core.Utils;

namespace Vint.Core.Battles.ArcadeMode;

public class CosmicHandler(
    Battle battle
) : ArcadeModeHandler(battle) {
    public override void Setup() {
        MapInfo mapInfo = TypeHandler.Maps.ToList().Shuffle().First();

        Battle.Properties = new BattleProperties(
            TypeHandler.BattleMode,
            GravityType.Moon,
            mapInfo.Id,
            false,
            true,
            true,
            false,
            mapInfo.MaxPlayers,
            15,
            150);

        Battle.MapInfo = mapInfo;
        Battle.MapEntity = GlobalEntities.GetEntities("maps").Single(map => map.Id == mapInfo.Id);
        Battle.LobbyEntity = new MatchMakingLobbyTemplate().Create(Battle.Properties, Battle.MapEntity);
    }
}