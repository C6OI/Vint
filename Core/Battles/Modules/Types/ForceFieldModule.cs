using Vint.Core.Battles.Effects;
using Vint.Core.Battles.Modules.Types.Base;
using Vint.Core.Battles.Player;
using Vint.Core.ECS.Components.Server.Effect;
using Vint.Core.ECS.Entities;
using Vint.Core.Utils;

namespace Vint.Core.Battles.Modules.Types;

public class ForceFieldModule : ActiveBattleModule {
    public override string ConfigPath => "garage/module/upgrade/properties/forcefield";

    public override ForceFieldEffect GetEffect() => new(Duration, Tank, Level);

    TimeSpan Duration { get; set; }

    public override void Activate() {
        if (!CanBeActivated) return;

        ForceFieldEffect? effect = Tank.Effects.OfType<ForceFieldEffect>().SingleOrDefault();

        if (effect != null) return;

        base.Activate();
        GetEffect().Activate();
    }

    public override void Init(BattleTank tank, IEntity userSlot, IEntity marketModule) {
        base.Init(tank, userSlot, marketModule);

        Duration = TimeSpan.FromMilliseconds(Leveling.GetStat<ModuleEffectDurationPropertyComponent>(ConfigPath, Level));
    }
}