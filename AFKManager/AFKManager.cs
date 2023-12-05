using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace AFKManager;

[MinimumApiVersion(63)]

public class AFKManagerConfig : BasePluginConfig
{
    [JsonPropertyName("ChatPrefix")] public string ChatPrefix { get; set; } = "[{LightRed}AFK{Default}]";
    [JsonPropertyName("ChatKickMessage")] public string ChatKickMessage { get; set; } = "{chatprefix} {playername} was kicked for being AFK.";
    [JsonPropertyName("ChatMoveMessage")] public string ChatMoveMessage { get; set; } = "{chatprefix} {playername} was moved to SPEC being AFK.";
    [JsonPropertyName("ChatKillMessage")] public string ChatKillMessage { get; set; } = "{chatprefix} {playername} was killed for being AFK.";
    [JsonPropertyName("ChatWarningKickMessage")] public string ChatWarningKickMessage { get; set; } = "{chatprefix} You\'re{LightRed} Idle/ AFK{Default}. Move or you\'ll be kicked in {Darkred}{time}{Default} seconds.";
    [JsonPropertyName("ChatWarningMoveMessage")] public string ChatWarningMoveMessage { get; set; } = "{chatprefix} You\'re{LightRed} Idle/ AFK{Default}. Move or you\'ll be moved to SPEC in {Darkred}{time}{Default} seconds.";
    [JsonPropertyName("ChatWarningKillMessage")] public string ChatWarningKillMessage { get; set; } = "{chatprefix} You\'re{LightRed} Idle/ AFK{Default}. Move or you\'ll killed in {Darkred}{time}{Default} seconds.";
    [JsonPropertyName("SpecWarnPlayerEveryXSeconds")] public float SpecWarnPlayerEveryXSeconds { get; set; } = 20.0f;
    [JsonPropertyName("SpecKickPlayerAfterXWarnings")] public int SpecKickPlayerAfterXWarnings { get; set; } = 5;
    [JsonPropertyName("SpecKickMinPlayers")] public int SpecKickMinPlayers { get; set; } = 5;
    [JsonPropertyName("SpecKickOnlyMovedByPlugin")] public bool SpecKickOnlyMovedByPlugin { get; set; } = false;
    [JsonPropertyName("SpecSkipFlag")] public string SpecSkipFlag { get; set; } = "@css/skipspec";
    [JsonPropertyName("SkipFlag")] public string SkipFlag { get; set; } = "@css/skipafk";
    [JsonPropertyName("Warnings")] public int Warnings { get; set; } = 3;
    [JsonPropertyName("Punishment")] public int Punishment { get; set; } = 1;
    [JsonPropertyName("Retake")] public bool Retake { get; set; } = false;
    [JsonPropertyName("Timer")] public float Timer { get; set; } = 5.0f;
}

public class AFKManager : BasePlugin, IPluginConfig<AFKManagerConfig>
{
    #region definitions
    public override string ModuleAuthor => "NiGHT & K4ryuu";
    public override string ModuleName => "AFK Manager";
    public override string ModuleVersion => "0.0.9";
    
    public required AFKManagerConfig Config { get; set; } 
    
    private static string ModifyColorValue(string msg)
    {
        if (!msg.Contains('{')) return string.IsNullOrEmpty(msg) ? "[TeamBet]" : msg;
        var modifiedValue = msg;
        foreach (var field in typeof(ChatColors).GetFields())
        {
            var pattern = $"{{{field.Name}}}";
            if (msg.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                modifiedValue = modifiedValue.Replace(pattern, field.GetValue(null)?.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            if (msg.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                modifiedValue = $" {modifiedValue}";
        }
        return modifiedValue;
    }
    
    public void OnConfigParsed(AFKManagerConfig config)
    {
        Config = config;
        
        Config.ChatPrefix = ModifyColorValue(Config.ChatPrefix);
        Config.ChatMoveMessage = ModifyColorValue(Config.ChatMoveMessage);
        Config.ChatKickMessage = ModifyColorValue(Config.ChatKickMessage);
        Config.ChatKillMessage = ModifyColorValue(Config.ChatKillMessage);
        Config.ChatWarningKillMessage = ModifyColorValue(Config.ChatWarningKillMessage);
        Config.ChatWarningMoveMessage = ModifyColorValue(Config.ChatWarningMoveMessage);
        Config.ChatWarningKickMessage = ModifyColorValue(Config.ChatWarningKickMessage);

        if (Config.Punishment is < 0 or > 2)
        {
            Config.Punishment = 1;
            Console.WriteLine("AFK Manager: Punishment value is invalid, setting to default value (1).");
        }

        if(Config.Timer < 0.1f)
        {                 
            Config.Timer = 5.0f;
            Console.WriteLine("AFK Manager: Timer value is invalid, setting to default value (5.0).");
        }

        if (Config.SpecWarnPlayerEveryXSeconds < Config.Timer)
        {
            Config.SpecWarnPlayerEveryXSeconds = Config.Timer;
            Console.WriteLine($"AFK Manager: The value of SpecWarnPlayerEveryXSeconds is less than the value of Timer, SpecWarnPlayerEveryXSeconds will be forced to {Config.Timer}");
        }
        
        _afkTimer?.Kill();
        _afkTimer = AddTimer(Config.Timer, AfkTimer_Callback, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }
    
    private CCSGameRules? _gGameRulesProxy;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _afkTimer;
    
    private class PlayerInfo
    {
        public QAngle? Angles { get; set; }
        public Vector? Origin { get; set; }
        public int WarningCount { get; set; }
        public int SpecWarningCount { get; set; }
        public float SpecAfkTime { get; set; }
        public bool MovedByPlugin { get; set; }
    }
    
    private Dictionary<uint, PlayerInfo> _gPlayerInfo = new Dictionary<uint, PlayerInfo>();
    #endregion

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            _afkTimer?.Kill();
            _afkTimer = null;

            AddTimer(0.5f, () =>
            {
                _afkTimer = AddTimer(Config.Timer, AfkTimer_Callback, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

            }, TimerFlags.STOP_ON_MAPCHANGE);
            
            _gGameRulesProxy = null;
        });
        
        RegisterListener<Listeners.OnServerHibernationUpdate>(hibernating =>
        {
            if (hibernating) return;
            _afkTimer ??= AddTimer(Config.Timer, AfkTimer_Callback, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
        
        #region OnClientConnected
        RegisterListener<Listeners.OnClientConnected>(playerSlot =>
        {
            var finalSlot = (uint)playerSlot + 1;

            if (!_gPlayerInfo.ContainsKey(finalSlot))
            {
                _gPlayerInfo.Add(finalSlot, new PlayerInfo
                {
                    Angles = new QAngle(),
                    Origin = new Vector(),
                    SpecWarningCount = 0,
                    SpecAfkTime = 0,
                    WarningCount = 0
                });
            }
        });
        
        RegisterListener<Listeners.OnClientDisconnectPost>(playerSlot =>
        {
            _gPlayerInfo.Remove((uint)playerSlot + 1);
        });
        
        RegisterEventHandler<EventPlayerTeam>((@event, info) =>
        {
            var player = @event.Userid;
            if (!player.IsValid || player.IsBot)
                return HookResult.Continue;
            
            var i = player.Index;

            if (!_gPlayerInfo.TryGetValue(i, out var value)) return HookResult.Continue;
            
            value.SpecAfkTime = 0;
            value.SpecWarningCount = 0;
            value.WarningCount = 0;
                
            if(@event.Team != 1)
                value.MovedByPlugin = false;
            return HookResult.Continue;
        });
        
        RegisterEventHandler<EventPlayerSpawn>((@event, info) =>
        {
            var player = @event.Userid;
            if(!player.IsValid || player.IsBot)
                return HookResult.Continue;

            AddTimer(0.2f, () =>
            {
                if (!player.IsValid || !player.PawnIsAlive)
                    return;
                
                var i = player.Index;
                if(!_gPlayerInfo.TryGetValue(i, out var data))
                    return;
                
                var angles = player.PlayerPawn.Value?.EyeAngles;
                var origin = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
                
                data.Angles = new QAngle(
                    x: angles?.X,
                    y: angles?.Y,
                    z: angles?.Z
                );
                
                data.Origin = new Vector(
                    x: origin?.X,
                    y: origin?.Y,
                    z: origin?.Z
                );

                data.SpecAfkTime = 0;
                data.SpecWarningCount = 0;
                data.MovedByPlugin = false;
            });
            
            return HookResult.Continue;
        });
        #endregion
        #region hotReload
        if (hotReload)
        {
            AddTimer(1.0f, () =>
            {
                var players = Utilities.GetPlayers().Where(x => !x.IsBot);

                foreach (var player in players)
                {
                    var i = player.Index;
                    
                    if (!_gPlayerInfo.ContainsKey(i))
                    {
                        _gPlayerInfo.Add(i, new PlayerInfo
                        {
                            Angles = new QAngle(),
                            Origin = new Vector(),
                            SpecWarningCount = 0,
                            SpecAfkTime = 0,
                            WarningCount = 0
                        });
                    }
                }
                
                _afkTimer ??= AddTimer(Config.Timer, AfkTimer_Callback, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            });
        }
        #endregion
    }
    
    private void AfkTimer_Callback()
    {
        if (_gGameRulesProxy == null)
        {
            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();

            _gGameRulesProxy = gameRulesProxy?.GameRules!;

            if (_gGameRulesProxy == null)
                return;
        }

        if (_gGameRulesProxy.FreezePeriod)
            return;

        string? sFormat = null; 
        
        var players = Utilities.GetPlayers().Where(x => x != null && !x.IsBot);
        var playersCount = players.Count();
        
        foreach (var player in players)
        {
            var i = player.Index;
            
            if (!_gPlayerInfo.TryGetValue(i, out var data) || player.ControllingBot)
                continue;
            
            #region AFK Time
            if (Config.Warnings != 0 && player is { PawnIsAlive: true, TeamNum: 2 or 3 })
            {
                if(Config.Retake && (CsTeam)player.TeamNum != CsTeam.CounterTerrorist)
                    continue;
                
                if(Config.SkipFlag != string.Empty && AdminManager.PlayerHasPermissions(player, Config.SkipFlag))
                    continue;
                
                var playerPawn = player.PlayerPawn.Value;
                var playerFlags = player.Pawn.Value!.Flags;

                if ((playerFlags & ((uint)PlayerFlags.FL_ONGROUND | (uint)PlayerFlags.FL_FROZEN)) != (uint)PlayerFlags.FL_ONGROUND)
                    continue;
                
                var angles = playerPawn?.EyeAngles;
                var origin = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;

                if (data.Angles.X == angles.X && data.Angles.Y == angles.Y &&
                    data.Origin.X == origin!.X && data.Origin.Y == origin.Y)
                {
                    if (data.WarningCount == Config.Warnings)
                    {
                        data.WarningCount = 0;

                        switch (Config.Punishment)
                        {
                            case 0:
                                sFormat = Config.ChatKillMessage.Replace("{chatprefix}", Config.ChatPrefix).Replace("{playername}", player.PlayerName);

                                playerPawn?.CommitSuicide(false, true);
                                Server.PrintToChatAll(sFormat);
                                break;
                            case 1:
                                sFormat = Config.ChatMoveMessage?.Replace("{chatprefix}", Config.ChatPrefix).Replace("{playername}", player.PlayerName);

                                playerPawn?.CommitSuicide(false, true);
                                player.ChangeTeam(CsTeam.Spectator);

                                Server.PrintToChatAll(sFormat ?? string.Empty);
                                break;
                            case 2:
                                sFormat = Config.ChatKickMessage?.Replace("{chatprefix}", Config.ChatPrefix).Replace("{playername}", player.PlayerName);

                                Server.ExecuteCommand($"kickid {player.UserId}");
                                Server.PrintToChatAll(sFormat ?? string.Empty);
                                break;
                        }
                        
                        continue;
                    }

                    sFormat = Config.Punishment switch
                    {
                        0 => Config.ChatWarningKillMessage,
                        1 => Config.ChatWarningMoveMessage,
                        2 => Config.ChatWarningKickMessage,
                        _ => sFormat
                    };

                    sFormat = sFormat?.Replace("{chatprefix}", Config.ChatPrefix).Replace("{playername}", player.PlayerName).Replace("{time}", $"{((Config.Warnings * Config.Timer) - (_gPlayerInfo[i].WarningCount * Config.Timer)):F1}");
                    
                    player.PrintToChat(sFormat ?? string.Empty);
                    data.WarningCount++;
                }
                else
                    data.WarningCount = 0;
                

                data.Angles = new QAngle(
                    x: angles.X,
                    y: angles.Y,
                    z: angles.Z
                );

                data.Origin = new Vector(
                    x: origin?.X,
                    y: origin?.Y,
                    z: origin?.Z
                );

                continue;
            }
            #endregion
            #region SPEC Time

            if (Config.SpecKickPlayerAfterXWarnings == 0
                || player.TeamNum != 1
                || playersCount < Config.SpecKickMinPlayers
                || Config.SpecKickOnlyMovedByPlugin && !_gPlayerInfo[i].MovedByPlugin
                || Config.SpecSkipFlag != string.Empty && AdminManager.PlayerHasPermissions(player, Config.SpecSkipFlag)) continue;
                
            data.SpecAfkTime += Config.Timer;

            if (!(data.SpecAfkTime >= Config.SpecWarnPlayerEveryXSeconds)) continue; // for example, warn player every 20 seconds ( add +1 warn every 20 sec )
            if (_gPlayerInfo[i].SpecWarningCount == Config.SpecKickPlayerAfterXWarnings)
            {
                sFormat = Config.ChatKickMessage?.Replace("{chatprefix}", Config.ChatPrefix).Replace("{playername}", player.PlayerName);
                        
                Server.PrintToChatAll(sFormat ?? string.Empty);
                Server.ExecuteCommand($"kickid {player.UserId}");

                _gPlayerInfo[i].SpecWarningCount = 0;
                _gPlayerInfo[i].SpecAfkTime = 0; // reset counter
                continue;
            }

            sFormat = Config.ChatWarningKickMessage?.Replace("{chatprefix}", Config.ChatPrefix)
                .Replace("{time}", $"{ ((Config.SpecKickPlayerAfterXWarnings * Config.SpecWarnPlayerEveryXSeconds) - (data.SpecWarningCount * Config.SpecWarnPlayerEveryXSeconds)):F1}");
                    
            player.PrintToChat(sFormat ?? string.Empty);
            data.SpecWarningCount++;
            data.SpecAfkTime = 0; // reset counter

            #endregion
        }
    }
}