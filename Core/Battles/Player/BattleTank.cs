using System.Numerics;
using Vint.Core.Battles.States;
using Vint.Core.Battles.Weapons;
using Vint.Core.Config;
using Vint.Core.Config.MapInformation;
using Vint.Core.Database.Models;
using Vint.Core.ECS.Components;
using Vint.Core.ECS.Components.Battle;
using Vint.Core.ECS.Components.Battle.Movement;
using Vint.Core.ECS.Components.Battle.Parameters.Chassis;
using Vint.Core.ECS.Components.Battle.Parameters.Health;
using Vint.Core.ECS.Components.Battle.Tank;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Events.Battle;
using Vint.Core.ECS.Movement;
using Vint.Core.ECS.Templates.Battle;
using Vint.Core.ECS.Templates.Battle.Graffiti;
using Vint.Core.ECS.Templates.Battle.Incarnation;
using Vint.Core.ECS.Templates.Battle.Tank;
using Vint.Core.ECS.Templates.Battle.Weapon;
using Vint.Core.ECS.Templates.Weapons.Market;
using Vint.Core.Server;

namespace Vint.Core.Battles.Player;

public class BattleTank {
    public BattleTank(BattlePlayer battlePlayer) {
        BattlePlayer = battlePlayer;
        BattleUser = battlePlayer.BattleUser;
        Battle = battlePlayer.Battle;

        StateManager = new TankStateManager(this);

        IPlayerConnection playerConnection = battlePlayer.PlayerConnection;

        Preset preset = playerConnection.Player.CurrentPreset;

        IEntity weapon = preset.Weapon;
        IEntity weaponSkin = preset.WeaponSkin;
        IEntity shell = preset.Shell;

        IEntity hull = preset.Hull;
        IEntity hullSkin = preset.HullSkin;

        IEntity cover = preset.Cover;
        IEntity paint = preset.Paint;
        IEntity graffiti = preset.Graffiti;

        OriginalSpeedComponent = ConfigManager.GetComponent<SpeedComponent>(hull.TemplateAccessor!.ConfigPath!);

        Tank = new TankTemplate().Create(hull, BattlePlayer.BattleUser);

        try {
            Weapon = weapon.TemplateAccessor!.Template switch {
                SmokyMarketItemTemplate => new SmokyBattleItemTemplate().Create(Tank, BattlePlayer),
                _ => throw new NotImplementedException()
            };
        } catch (NotImplementedException e) {
            Battle.Logger.Error(e, "Player equipped weapon that is not implemented yet");
            Battle.RemovePlayer(BattlePlayer);
            return;
        }

        HullSkin = new HullSkinBattleItemTemplate().Create(hullSkin, Tank);
        WeaponSkin = new WeaponSkinBattleItemTemplate().Create(weaponSkin, Tank);
        Cover = new WeaponPaintBattleItemTemplate().Create(cover, Tank);
        Paint = new TankPaintBattleItemTemplate().Create(paint, Tank);
        Graffiti = new GraffitiBattleItemTemplate().Create(graffiti, Tank);
        Shell = new ShellBattleItemTemplate().Create(shell, Tank);

        // todo modules

        Incarnation = new TankIncarnationTemplate().Create(Tank);
        RoundUser = new RoundUserTemplate().Create(BattlePlayer, Tank);

        try {
            WeaponHandler = Weapon.TemplateAccessor!.Template switch {
                SmokyBattleItemTemplate => new SmokyWeaponHandler(this),
                _ => throw new NotImplementedException()
            };
        } catch (NotImplementedException e) {
            Battle.Logger.Error(e, "Player equipped weapon that is not implemented yet");
            Battle.RemovePlayer(BattlePlayer);
            return;
        }

        MaxHealth = ConfigManager.GetComponent<HealthComponent>(hull.TemplateAccessor.ConfigPath!).MaxHealth;
        Health = MaxHealth;

        if (Tank.HasComponent<HealthComponent>())
            Tank.ChangeComponent<HealthComponent>(component => {
                component.CurrentHealth = Health;
                component.MaxHealth = MaxHealth;
            });
        else Tank.AddComponent(new HealthComponent(Health, MaxHealth));
    }

    public long CollisionsPhase { get; set; } = -1; // I don't understand what is this

    public float Health { get; private set; }
    public float MaxHealth { get; }

    public Vector3 PreviousPosition { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Orientation { get; set; }

    public DateTimeOffset? SelfDestructTime { get; set; }

    public SpawnPoint SpawnPoint { get; private set; } = null!;

    public WeaponHandler WeaponHandler { get; }
    public BattlePlayer BattlePlayer { get; }
    public Battle Battle { get; }
    public TankStateManager StateManager { get; set; }

    public IEnumerable<IEntity> Entities => [Incarnation, RoundUser, BattleUser, Tank, Weapon, HullSkin, WeaponSkin, Cover, Paint, Graffiti, Shell];

    public IEntity Incarnation { get; private set; }
    public IEntity RoundUser { get; }
    public IEntity BattleUser { get; }

    public IEntity Tank { get; }
    public IEntity Weapon { get; }

    public IEntity HullSkin { get; }
    public IEntity WeaponSkin { get; }

    public IEntity Cover { get; }
    public IEntity Paint { get; }

    public IEntity Graffiti { get; }
    public IEntity Shell { get; }

    SpeedComponent OriginalSpeedComponent { get; }

    public void Tick() {
        if (SelfDestructTime.HasValue && SelfDestructTime.Value <= DateTimeOffset.UtcNow) {
            SelfDestructTime = null;

            if (StateManager.CurrentState is not Dead) {
                StateManager.SetState(new Dead(StateManager));

                foreach (BattlePlayer battlePlayer in Battle.Players.Where(battlePlayer => battlePlayer.InBattle))
                    battlePlayer.PlayerConnection.Send(new SelfDestructionBattleUserEvent(), BattleUser);

                // todo statistics

                Position = default;
                PreviousPosition = default;
            }
        }

        StateManager.Tick();

        if (CollisionsPhase == Battle.BattleEntity.GetComponent<BattleTankCollisionsComponent>().SemiActiveCollisionsPhase) {
            if (Tank.HasComponent<TankStateTimeOutComponent>())
                Tank.RemoveComponent<TankStateTimeOutComponent>();

            Battle.BattleEntity.ChangeComponent<BattleTankCollisionsComponent>(component =>
                component.SemiActiveCollisionsPhase++);

            StateManager.SetState(new Active(StateManager));
            SetHealth(MaxHealth);
        }

        if (BattlePlayer.IsPaused &&
            (!BattlePlayer.KickTime.HasValue ||
             DateTimeOffset.UtcNow > BattlePlayer.KickTime)) {
            BattlePlayer.IsPaused = false;
            BattlePlayer.KickTime = null;
            BattlePlayer.PlayerConnection.Send(new KickFromBattleEvent(), BattleUser);
            Battle.RemovePlayer(BattlePlayer);
        }
    }

    public void Enable() { // todo
        Tank.AddComponent(new TankMovableComponent());
    }

    public void Disable() { // todo
        Tank.ChangeComponent(((IComponent)OriginalSpeedComponent).Clone());

        if (Tank.HasComponent<SelfDestructionComponent>())
            Tank.RemoveComponent<SelfDestructionComponent>();

        if (Tank.HasComponent<TankMovableComponent>())
            Tank.RemoveComponent<TankMovableComponent>();
    }

    public void Spawn() { // todo
        if (Tank.HasComponent<TankVisibleStateComponent>())
            Tank.RemoveComponent<TankVisibleStateComponent>();

        if (Tank.HasComponent<TankMovementComponent>()) {
            Tank.RemoveComponent<TankMovementComponent>();

            IEntity incarnation = Incarnation;
            Incarnation = new TankIncarnationTemplate().Create(Tank);

            foreach (IPlayerConnection playerConnection in incarnation.SharedPlayers) {
                playerConnection.Unshare(incarnation);
                playerConnection.Share(Incarnation);
            }
        }

        SpawnPoint = Battle.ModeHandler.GetRandomSpawnPoint();

        Movement movement = new() {
            Position = SpawnPoint.Position,
            Orientation = SpawnPoint.Rotation
        };

        Tank.AddComponent(new TankMovementComponent(movement, default, 0, 0));
    }

    public void SetHealth(float health) { // todo
        Health = health;

        HealthComponent healthComponent = Tank.GetComponent<HealthComponent>();
        healthComponent.CurrentHealth = health;

        Tank.RemoveComponent<HealthComponent>();
        Tank.AddComponent(healthComponent);

        foreach (BattlePlayer battlePlayer in Battle.Players.Where(player => player.InBattle))
            battlePlayer.PlayerConnection.Send(new HealthChangedEvent(), Tank);
    }
}