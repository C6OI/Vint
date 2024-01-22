using Vint.Core.Database;
using Vint.Core.Database.Models;
using Vint.Core.ECS.Entities;
using Vint.Core.Protocol.Attributes;
using Vint.Core.Server;
using Vint.Core.Utils;

namespace Vint.Core.ECS.Events.ElevatedAccess;

[ProtocolId(1503470140938)]
public class ElevatedAccessUserBlockUserEvent : ElevatedAccessUserBasePunishEvent {
    public override void Execute(IPlayerConnection connection, IEnumerable<IEntity> entities) {
        if (!connection.Player.IsAdmin) return;
        
        IPlayerConnection? targetConnection = connection.Server.PlayerConnections
            .ToArray()
            .Where(conn => conn.IsOnline)
            .SingleOrDefault(conn => conn.Player.Username == Username);
        
        Player? targetPlayer = targetConnection?.Player;
        IEntity? notifyChat = null;
        List<IPlayerConnection>? notifiedConnections = null;
        
        if (targetConnection != null) {
            if (targetConnection.InLobby) {
                Battles.Battle battle = targetConnection.BattlePlayer!.Battle;
                
                if (targetConnection.BattlePlayer.InBattleAsTank) {
                    notifyChat = battle.BattleChatEntity;
                    notifiedConnections = battle.Players
                        .Where(battlePlayer => battlePlayer.InBattle)
                        .Select(battlePlayer => battlePlayer.PlayerConnection)
                        .ToList();
                } else {
                    notifyChat = battle.LobbyChatEntity;
                    notifiedConnections = battle.Players
                        .Where(battlePlayer => !battlePlayer.InBattle)
                        .Select(battlePlayer => battlePlayer.PlayerConnection)
                        .ToList();
                }
            }
        } else {
            using DbConnection db = new();
            targetPlayer = db.Players.SingleOrDefault(player => player.Username == Username);
        }
        
        if (targetPlayer == null) {
            ChatUtils.SendMessage("Player not found", GlobalChat, [connection], null);
            return;
        } 
            
        if (targetPlayer.IsAdmin) {
            ChatUtils.SendMessage($"Player {Username} is admin", GlobalChat, [connection], null);
            return;
        }
        
        Punishment punishment = targetPlayer.Ban(Reason, null);
        targetConnection?.Kick(Reason);
        
        if (notifyChat == null || notifiedConnections == null) {
            notifyChat = GlobalChat;
            notifiedConnections = connection.Server.PlayerConnections.ToList();
        }
        
        ChatUtils.SendMessage($"{Username} was {punishment}", notifyChat, notifiedConnections, null);
    }
}