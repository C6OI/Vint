using Vint.Core.ECS.Components.Battle.User;
using Vint.Core.ECS.Components.Group;
using Vint.Core.ECS.Entities;
using Vint.Core.Protocol.Attributes;

namespace Vint.Core.ECS.Templates.Battle.User;

[ProtocolId(-2043703779834243389)]
public class BattleUserTemplate : EntityTemplate {
    IEntity Create(IEntity user, IEntity battle) => Entity("battle/battleuser",
        builder => builder
            .AddComponent(user.GetComponent<UserGroupComponent>())
            .AddComponent(battle.GetComponent<BattleGroupComponent>())
            .AddComponent(new BattleUserComponent()));

    public IEntity CreateAsTank(IEntity user, IEntity battle, IEntity? team) {
        IEntity entity = Create(user, battle);

        if (team != null)
            entity.AddComponent(team.GetComponent<TeamGroupComponent>());

        entity.AddComponent(new UserInBattleAsTankComponent());
        return entity;
    }

    public IEntity CreateAsSpectator(IEntity user, IEntity battle) {
        IEntity entity = Create(user, battle);

        entity.AddComponent(new UserInBattleAsSpectatorComponent(battle.Id));
        return entity;
    }
}