using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Serilog;
using Vint.Core.Battles;
using Vint.Core.ChatCommands;
using Vint.Core.ECS.Events.Ping;
using Vint.Core.Utils;

namespace Vint.Core.Server;

public class GameServer(
    IPAddress host,
    ushort port
) {
    ILogger Logger { get; } = Log.Logger.ForType(typeof(GameServer));
    Protocol.Protocol Protocol { get; } = new();
    TcpListener Listener { get; } = new(host, port);

    public ConcurrentDictionary<Guid, IPlayerConnection> PlayerConnections { get; } = new();
    public IBattleProcessor BattleProcessor { get; private set; } = null!;
    public IMatchmakingProcessor MatchmakingProcessor { get; private set; } = null!;
    public IChatCommandProcessor ChatCommandProcessor { get; private set; } = null!;

    public bool IsStarted { get; private set; }
    public bool IsAccepting { get; private set; }

    public void Start() {
        if (IsStarted) return;

        Listener.Start();
        IsStarted = true;
        OnStarted();

        IsAccepting = true;
        Task.Run(async () => await Accept());
    }

    public void OnStarted() {
        Logger.Information("Started");

        ChatCommandProcessor chatCommandProcessor = new();

        BattleProcessor = new BattleProcessor();
        MatchmakingProcessor = new MatchmakingProcessor(BattleProcessor);
        ChatCommandProcessor = chatCommandProcessor;

        new Thread(() => MatchmakingProcessor.StartTicking()) { Name = "Matchmaking ticker" }.Start();
        new Thread(() => BattleProcessor.StartTicking()) { Name = "Battle ticker" }.Start();
        new Thread(PingLoop) { Name = "Ping loop" }.Start();

        chatCommandProcessor.RegisterCommands();
    }

    public void OnConnected(SocketPlayerConnection connection) => connection.OnConnected();

    async Task Accept() {
        while (IsAccepting) {
            try {
                Socket socket = await Listener.AcceptSocketAsync();
                SocketPlayerConnection connection = new(this, socket, Protocol);
                OnConnected(connection);

                bool tryAdd = PlayerConnections.TryAdd(connection.Id, connection);

                if (tryAdd) continue;

                Logger.Error("Cannot add {Connection}", connection);
                connection.Kick("Internal error");
            } catch (Exception e) {
                Logger.Error(e, "");
            }
        }
    }

    void PingLoop() {
        while (true) {
            if (!IsStarted) return;

            foreach (IPlayerConnection playerConnection in PlayerConnections.Values.ToArray()) {
                try {
                    playerConnection.Send(new PingEvent(DateTimeOffset.UtcNow));
                } catch (Exception e) {
                    Logger.Error(e, "Socket caught an exception while sending ping event");
                }
            }

            Thread.Sleep(10000);
        }
    }
}