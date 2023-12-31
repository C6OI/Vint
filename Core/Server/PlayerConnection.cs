﻿using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using LinqToDB;
using NetCoreServer;
using Serilog;
using Vint.Core.Battles.Player;
using Vint.Core.Database;
using Vint.Core.Database.Models;
using Vint.Core.ECS.Components.Battle.User;
using Vint.Core.ECS.Components.Group;
using Vint.Core.ECS.Components.Item;
using Vint.Core.ECS.Components.Preset;
using Vint.Core.ECS.Components.User;
using Vint.Core.ECS.Entities;
using Vint.Core.ECS.Events;
using Vint.Core.ECS.Events.Entrance.Login;
using Vint.Core.ECS.Events.Items;
using Vint.Core.ECS.Templates.Avatar;
using Vint.Core.ECS.Templates.Covers;
using Vint.Core.ECS.Templates.Entrance;
using Vint.Core.ECS.Templates.Gold;
using Vint.Core.ECS.Templates.Graffiti;
using Vint.Core.ECS.Templates.Hulls;
using Vint.Core.ECS.Templates.Modules;
using Vint.Core.ECS.Templates.Money;
using Vint.Core.ECS.Templates.Paints;
using Vint.Core.ECS.Templates.Shells;
using Vint.Core.ECS.Templates.Skins;
using Vint.Core.ECS.Templates.User;
using Vint.Core.ECS.Templates.Weapons.Market;
using Vint.Core.ECS.Templates.Weapons.User;
using Vint.Core.Protocol.Codecs.Buffer;
using Vint.Core.Protocol.Codecs.Impl;
using Vint.Core.Protocol.Commands;
using Vint.Core.Utils;

namespace Vint.Core.Server;

public interface IPlayerConnection {
    public ILogger Logger { get; }

    public GameServer Server { get; }
    public Player Player { get; set; }
    public BattlePlayer? BattlePlayer { get; set; }
    public IEntity User { get; }
    public IEntity ClientSession { get; }

    public bool IsOnline { get; }
    public bool InBattle { get; }

    public List<IEntity> SharedEntities { get; }
    public Dictionary<string, List<IEntity>> UserEntities { get; }

    public void Register(
        string username,
        string encryptedPasswordDigest,
        string email,
        string hardwareFingerprint,
        bool subscribed,
        bool steam,
        bool quickRegistration);

    public void Login(
        bool rememberMe,
        string hardwareFingerprint);

    public void ChangePassword(string passwordDigest);

    public void ChangeReputation(int reputation);

    public void PurchaseItem(IEntity marketItem, int amount, int price, bool forXCrystals, bool mount);

    public void MountItem(IEntity userItem);

    public void SetCrystals(long crystals);

    public void SetXCrystals(long xCrystals);

    public void SetGoldBoxes(int goldBoxes);

    public void Send(ICommand command);

    public void Send(IEvent @event);

    public void Send(IEvent @event, params IEntity[] entities);

    public void Share(IEntity entity);

    public void Share(params IEntity[] entities);

    public void Share(IEnumerable<IEntity> entities);

    public void Unshare(IEntity entity);

    public void Unshare(params IEntity[] entities);

    public void Unshare(IEnumerable<IEntity> entities);
}

public class PlayerConnection(
    GameServer server,
    Protocol.Protocol protocol
) : TcpSession(server), IPlayerConnection {
    public bool IsSocketConnected => IsConnected && !IsDisposed && !IsSocketDisposed;
    public ILogger Logger { get; private set; } = Log.Logger.ForType(typeof(PlayerConnection));
    public Dictionary<string, List<IEntity>> UserEntities { get; } = new();

    public new GameServer Server { get; } = server;
    public Player Player { get; set; } = null!;
    public BattlePlayer? BattlePlayer { get; set; }
    public IEntity User { get; private set; } = null!;
    public IEntity ClientSession { get; private set; } = null!;
    public List<IEntity> SharedEntities { get; private set; } = [];

    public bool IsOnline => IsSocketConnected && ClientSession != null! && User != null! && Player != null!;
    public bool InBattle => BattlePlayer != null;

    public void Register(
        string username,
        string encryptedPasswordDigest,
        string email,
        string hardwareFingerprint,
        bool subscribed,
        bool steam,
        bool quickRegistration) {
        Logger.Information("Registering player '{Username}'", username);

        byte[] passwordHash = new Encryption().RsaDecrypt(Convert.FromBase64String(encryptedPasswordDigest));

        Player = new Player {
            Id = EntityRegistry.FreeId,
            Username = username,
            Email = email,
            CountryCode = IpUtils.GetCountryCode((Socket.RemoteEndPoint as IPEndPoint)!.Address) ?? "US",
            HardwareFingerprint = hardwareFingerprint,
            Subscribed = subscribed,
            RegistrationTime = DateTimeOffset.UtcNow,
            LastLoginTime = DateTimeOffset.UtcNow,
            PasswordHash = passwordHash
        };

        using (DbConnection database = new()) {
            database.Insert(Player);
        }

        Player.InitializeNew();

        Login(true, hardwareFingerprint);
    }

    public void Login(
        bool rememberMe,
        string hardwareFingerprint) {
        Player.LastLoginTime = DateTimeOffset.UtcNow;
        Player.HardwareFingerprint = hardwareFingerprint;

        if (rememberMe) {
            Encryption encryption = new();

            byte[] autoLoginToken = new byte[32];
            new Random().NextBytes(autoLoginToken);

            byte[] encryptedAutoLoginToken = encryption.EncryptAutoLoginToken(autoLoginToken, Player.PasswordHash);

            Player.AutoLoginToken = autoLoginToken;

            Send(new SaveAutoLoginTokenEvent(Player.Username, encryptedAutoLoginToken));
        }

        User = new UserTemplate().Create(Player);
        Share(User);

        ClientSession.AddComponent(User.GetComponent<UserGroupComponent>());

        Logger.Warning("'{Username}' logged in", Player.Username);

        using DbConnection database = new();

        database.Update(Player);
    }

    public void ChangePassword(string passwordDigest) {
        Encryption encryption = new();

        byte[] passwordHash = encryption.RsaDecrypt(Convert.FromBase64String(passwordDigest));
        Player.PasswordHash = passwordHash;

        using DbConnection database = new();

        database.Players
            .Where(player => player.Id == Player.Id)
            .Set(player => player.PasswordHash, Player.PasswordHash)
            .Update();
    }

    public void ChangeReputation(int reputation) {
        using DbConnection db = new();
        DateOnly date = DateOnly.FromDateTime(DateTime.Today);

        SeasonStatistics seasonStats = db.SeasonStatistics
            .Where(stats => stats.PlayerId == Player.Id)
            .OrderByDescending(stats => stats.SeasonNumber)
            .First();

        ReputationStatistics? reputationStats = db.ReputationStatistics
            .SingleOrDefault(repStats => repStats.PlayerId == Player.Id &&
                                         repStats.Date == date);

        int oldLeagueIndex = seasonStats.LeagueIndex;

        reputationStats ??= new ReputationStatistics {
            Player = Player,
            Date = date,
            SeasonNumber = seasonStats.SeasonNumber
        };

        seasonStats.Reputation = reputation;
        reputationStats.Reputation = reputation;

        User.ChangeComponent<UserReputationComponent>(component => component.Reputation = reputation);

        if (oldLeagueIndex != seasonStats.LeagueIndex) {
            User.RemoveComponent<LeagueGroupComponent>();
            User.AddComponent(seasonStats.League.GetComponent<LeagueGroupComponent>());
        }

        db.Update(seasonStats);
        db.InsertOrReplace(reputationStats);
    }

    public void PurchaseItem(IEntity marketItem, int amount, int price, bool forXCrystals, bool mount) {
        using DbConnection db = new();
        IEntity? userItem = null;

        switch (marketItem.TemplateAccessor!.Template) {
            case AvatarMarketItemTemplate: {
                db.Insert(new Avatar { Player = Player, Id = marketItem.Id });
                break;
            }

            case GraffitiMarketItemTemplate or ChildGraffitiMarketItemTemplate: {
                db.Insert(new Graffiti { Player = Player, Id = marketItem.Id });
                break;
            }

            case CrystalMarketItemTemplate: {
                SetCrystals(Player.Crystals + amount);
                db.Update(Player);
                break;
            }

            case XCrystalMarketItemTemplate: {
                SetXCrystals(Player.XCrystals + amount);
                db.Update(Player);
                break;
            }

            case GoldBonusMarketItemTemplate: {
                SetGoldBoxes(Player.GoldBoxItems + amount);
                db.Update(Player);
                break;
            }

            case TankMarketItemTemplate: {
                long skinId = GlobalEntities.DefaultSkins[marketItem.Id];

                db.Insert(new Hull { Player = Player, Id = marketItem.Id, SkinId = skinId });
                db.Insert(new HullSkin { Player = Player, Id = skinId, HullId = marketItem.Id });

                if (mount) MountItem(GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == skinId).GetUserEntity(this));
                break;
            }

            case WeaponMarketItemTemplate: {
                long skinId = GlobalEntities.DefaultSkins[marketItem.Id];
                long shellId = GlobalEntities.DefaultShells[marketItem.Id];

                db.Insert(new Weapon { Player = Player, Id = marketItem.Id, SkinId = skinId, ShellId = shellId });
                db.Insert(new WeaponSkin { Player = Player, Id = skinId, WeaponId = marketItem.Id });
                db.Insert(new Shell { Player = Player, Id = shellId, WeaponId = marketItem.Id });

                if (mount) {
                    MountItem(GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == skinId).GetUserEntity(this));
                    MountItem(GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == shellId).GetUserEntity(this));
                }

                break;
            }

            case HullSkinMarketItemTemplate: {
                long hullId = marketItem.GetComponent<ParentGroupComponent>().Key;

                db.Insert(new HullSkin { Player = Player, Id = marketItem.Id, HullId = hullId });
                break;
            }

            case WeaponSkinMarketItemTemplate: {
                long weaponId = marketItem.GetComponent<ParentGroupComponent>().Key;

                db.Insert(new WeaponSkin { Player = Player, Id = marketItem.Id, WeaponId = weaponId });
                break;
            }

            case TankPaintMarketItemTemplate: {
                db.Insert(new Paint { Player = Player, Id = marketItem.Id });
                break;
            }

            case WeaponPaintMarketItemTemplate: {
                db.Insert(new Cover { Player = Player, Id = marketItem.Id });
                break;
            }

            case ShellMarketItemTemplate: {
                long weaponId = marketItem.GetComponent<ParentGroupComponent>().Key;

                db.Insert(new Shell { Player = Player, Id = marketItem.Id, WeaponId = weaponId });
                break;
            }

            case ModuleCardMarketItemTemplate: {
                long moduleId = marketItem.GetComponent<ParentGroupComponent>().Key;
                Module? module = db.Modules
                    .Where(module => module.PlayerId == Player.Id)
                    .SingleOrDefault(module => module.Id == moduleId);

                module ??= new Module { Player = Player, Id = moduleId };
                module.Cards += amount;

                db.Insert(module);
                break;
            }

            default: throw new NotImplementedException();
        }

        userItem ??= marketItem.GetUserEntity(this);

        if (!userItem.HasComponent<UserGroupComponent>())
            userItem.AddComponent(new UserGroupComponent(User));

        if (price > 0) {
            if (forXCrystals) SetXCrystals(Player.XCrystals - price);
            else SetCrystals(Player.Crystals - price);

            db.Update(Player);
        }

        if (userItem.HasComponent<UserItemCounterComponent>() &&
            userItem.TemplateAccessor!.Template is GoldBonusMarketItemTemplate) {
            userItem.ChangeComponent<UserItemCounterComponent>(component => component.Count += amount);
            Send(new ItemsCountChangedEvent(amount), userItem);
        }

        if (mount) MountItem(userItem);
    }

    public void MountItem(IEntity userItem) {
        using DbConnection db = new();
        Preset currentPreset = Player.CurrentPreset;
        IEntity marketItem = userItem.GetMarketEntity(this);

        switch (userItem.TemplateAccessor!.Template) {
            case AvatarUserItemTemplate: {
                this.GetEntity(Player.CurrentAvatarId)!.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                userItem.AddComponent(new MountedItemComponent());

                Player.CurrentAvatarId = marketItem.Id;
                User.ChangeComponent(new UserAvatarComponent(this, Player.CurrentAvatarId));

                db.Update(Player);
                break;
            }

            case GraffitiUserItemTemplate: {
                currentPreset.Graffiti.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.Graffiti = marketItem;
                userItem.AddComponent(new MountedItemComponent());

                db.Update(currentPreset);
                break;
            }

            case TankUserItemTemplate: {
                currentPreset.Hull.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.Hull = marketItem;
                userItem.AddComponent(new MountedItemComponent());
                currentPreset.Entity!.GetComponent<PresetEquipmentComponent>().SetHullId(currentPreset.Hull.Id);

                Hull newHull = db.Hulls
                    .Where(hull => hull.PlayerId == Player.Id)
                    .Single(hull => hull.Id == currentPreset.Hull.Id);

                IEntity skin = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == newHull.SkinId);

                currentPreset.HullSkin.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.HullSkin = skin;
                currentPreset.HullSkin.GetUserEntity(this).AddComponent(new MountedItemComponent());

                db.Update(currentPreset);
                break;
            }

            case WeaponUserItemTemplate: {
                currentPreset.Weapon.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.Weapon = marketItem;
                userItem.AddComponent(new MountedItemComponent());
                currentPreset.Entity!.GetComponent<PresetEquipmentComponent>().SetWeaponId(currentPreset.Weapon.Id);

                Weapon newWeapon = db.Weapons
                    .Where(weapon => weapon.PlayerId == Player.Id)
                    .Single(weapon => weapon.Id == currentPreset.Weapon.Id);

                IEntity skin = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == newWeapon.SkinId);
                IEntity shell = GlobalEntities.AllMarketTemplateEntities.Single(entity => entity.Id == newWeapon.ShellId);

                currentPreset.WeaponSkin.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.WeaponSkin = skin;
                currentPreset.WeaponSkin.GetUserEntity(this).AddComponent(new MountedItemComponent());

                currentPreset.Shell.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.Shell = shell;
                currentPreset.Shell.GetUserEntity(this).AddComponent(new MountedItemComponent());

                db.Update(currentPreset);
                break;
            }

            case HullSkinUserItemTemplate: {
                HullSkin skin = db.HullSkins
                    .Where(skin => skin.PlayerId == Player.Id)
                    .Single(skin => skin.Id == marketItem.Id);

                if (skin.HullId != currentPreset.Hull.Id) return;

                currentPreset.HullSkin.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.HullSkin = marketItem;
                userItem.AddComponent(new MountedItemComponent());

                db.Hulls
                    .Where(hull => hull.PlayerId == Player.Id &&
                                   hull.Id == currentPreset.Hull.Id)
                    .Set(hull => hull.SkinId, currentPreset.HullSkin.Id)
                    .Update();

                db.Update(currentPreset);
                break;
            }

            case WeaponSkinUserItemTemplate: {
                WeaponSkin skin = db.WeaponSkins
                    .Where(skin => skin.PlayerId == Player.Id)
                    .Single(skin => skin.Id == marketItem.Id);

                if (skin.WeaponId != currentPreset.Weapon.Id) return;

                currentPreset.WeaponSkin.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.WeaponSkin = marketItem;
                userItem.AddComponent(new MountedItemComponent());

                db.Weapons
                    .Where(weapon => weapon.PlayerId == Player.Id &&
                                     weapon.Id == currentPreset.Weapon.Id)
                    .Set(weapon => weapon.SkinId, currentPreset.WeaponSkin.Id)
                    .Update();

                db.Update(currentPreset);
                break;
            }

            case TankPaintUserItemTemplate: {
                currentPreset.Paint.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.Paint = marketItem;
                userItem.AddComponent(new MountedItemComponent());

                db.Update(currentPreset);
                break;
            }

            case WeaponPaintUserItemTemplate: {
                currentPreset.Cover.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.Cover = marketItem;
                userItem.AddComponent(new MountedItemComponent());

                db.Update(currentPreset);
                break;
            }

            case ShellUserItemTemplate: {
                Shell shell = db.Shells
                    .Where(shell => shell.PlayerId == Player.Id)
                    .Single(shell => shell.Id == marketItem.Id);

                if (shell.WeaponId != currentPreset.Weapon.Id) return;

                currentPreset.Shell.GetUserEntity(this).RemoveComponent<MountedItemComponent>();
                currentPreset.Shell = marketItem;
                userItem.AddComponent(new MountedItemComponent());

                db.Weapons
                    .Where(weapon => weapon.PlayerId == Player.Id &&
                                     weapon.Id == currentPreset.Weapon.Id)
                    .Set(weapon => weapon.ShellId, currentPreset.Shell.Id)
                    .Update();

                db.Update(currentPreset);
                break;
            }

            default: throw new NotImplementedException();
        }

        if (!User.HasComponent<UserEquipmentComponent>()) return;

        User.RemoveComponent<UserEquipmentComponent>();
        User.AddComponent(new UserEquipmentComponent(Player.CurrentPreset.Weapon.Id, Player.CurrentPreset.Hull.Id));
    }

    public void SetCrystals(long crystals) {
        Player.Crystals = crystals;
        User.ChangeComponent<UserMoneyComponent>(component => component.Money = Player.Crystals);
    }

    public void SetXCrystals(long xCrystals) {
        Player.XCrystals = xCrystals;
        User.ChangeComponent<UserXCrystalsComponent>(component => component.Money = Player.XCrystals);
    }

    public void SetGoldBoxes(int goldBoxes) {
        Player.GoldBoxItems = goldBoxes;
        SharedEntities.Single(entity => entity.TemplateAccessor!.Template is GoldBonusUserItemTemplate)
            .ChangeComponent<UserItemCounterComponent>(component =>
                component.Count = Player.GoldBoxItems);
    }

    public void Send(ICommand command) {
        try {
            if (!IsSocketConnected) return;

            Logger.Debug("Sending {Command}", command);

            ProtocolBuffer buffer = new(new OptionalMap(), this);

            protocol.GetCodec(new TypeCodecInfo(typeof(ICommand))).Encode(buffer, command);

            using MemoryStream stream = new();
            using BinaryWriter writer = new BigEndianBinaryWriter(stream);

            buffer.Wrap(writer);

            byte[] bytes = stream.ToArray();

            SendAsync(bytes);

            Logger.Verbose("Sent {Command}: {Size} bytes ({Hex})", command, bytes.Length, Convert.ToHexString(bytes));
        } catch (Exception e) {
            Logger.Error(e, "Socket caught an exception while sending {Command}", command);
            Disconnect();
        }
    }

    public void Send(IEvent @event) => ClientSession.Send(@event);

    public void Send(IEvent @event, params IEntity[] entities) => Send(new SendEventCommand(@event, entities));

    public void Share(IEntity entity) => entity.Share(this);

    public void Share(params IEntity[] entities) => entities.ToList().ForEach(Share);

    public void Share(IEnumerable<IEntity> entities) => entities.ToList().ForEach(Share);

    public void Unshare(IEntity entity) => entity.Unshare(this);

    public void Unshare(params IEntity[] entities) => entities.ToList().ForEach(Unshare);

    public void Unshare(IEnumerable<IEntity> entities) => entities.ToList().ForEach(Unshare);

    protected override void OnConnecting() =>
        Logger = Logger.WithPlayer(this);

    protected override void OnConnected() {
        ClientSession = new ClientSessionTemplate().Create();

        Logger.Information("New socket connected");

        Send(new InitTimeCommand(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        ClientSession.Share(this);
    }

    protected override void OnDisconnected() {
        Logger.Information("Socket disconnected");

        try {
            if (!InBattle) return;

            if (BattlePlayer!.IsSpectator || BattlePlayer.InBattleAsTank)
                BattlePlayer.Battle.RemovePlayer(BattlePlayer);
            else
                BattlePlayer.Battle.RemovePlayerFromLobby(BattlePlayer);
        } catch (Exception e) {
            Logger.Error(e, "Caught an exception while disconnecting socket");
        } finally {
            if (User != null!)
                EntityRegistry.Remove(User.Id);

            foreach (IEntity entity in SharedEntities)
                entity.SharedPlayers.Remove(this);

            SharedEntities.Clear();
            UserEntities.Clear();
        }
    }

    protected override void OnError(SocketError error) =>
        Logger.Error("Socket caught an error: {Error}", error);

    protected override void OnReceived(byte[] bytes, long offset, long size) {
        try {
            Logger.Verbose("Received {Size} bytes ({Hex})", size, Convert.ToHexString(bytes[..(int)size]));

            ProtocolBuffer buffer = new(new OptionalMap(), this);
            MemoryStream stream = new(bytes);
            BinaryReader reader = new BigEndianBinaryReader(stream);

            if (!buffer.Unwrap(reader))
                throw new InvalidDataException("Failed to unwrap packet");

            long availableForRead = buffer.Stream.Length - buffer.Stream.Position;

            while (availableForRead > 0) {
                Logger.Verbose("Decode buffer bytes available: {Available}", availableForRead);

                ICommand command = (ICommand)protocol.GetCodec(new TypeCodecInfo(typeof(ICommand))).Decode(buffer);
                Logger.Debug("Received {Command}", command);

                availableForRead = buffer.Stream.Length - buffer.Stream.Position;

                try {
                    command.Execute(this);
                } catch (Exception e) {
                    Logger.Error(e, "Failed to execute {Command}", command);
                }
            }
        } catch (Exception e) {
            Logger.Error(e, "Socket caught an exception while receiving data");
            Disconnect();
        }
    }

    [SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract")]
    public override string ToString() => $"PlayerConnection {{ " +
                                         $"ClientSession Id: '{ClientSession?.Id}'; " +
                                         $"Username: '{Player?.Username}' }}";
}