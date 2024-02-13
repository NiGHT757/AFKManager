using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace AFKManager;

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
    [JsonPropertyName("SpecSkipFlag")] public string SpecSkipFlag { get; set; } = "";
    [JsonPropertyName("SkipFlag")] public string SkipFlag { get; set; } = "";
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
    public override string ModuleVersion => "0.1.1";
    
    public required AFKManagerConfig Config { get; set; }
    private CCSGameRules? _gGameRulesProxy;
    
    private static string ModifyColorValue(string msg)
    {
        if (!msg.Contains('{')) return string.IsNullOrEmpty(msg) ? "" : msg;
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

    private static char GetTeamColor(CsTeam team)
    {
        return team switch
        {
            CsTeam.Spectator => ChatColors.Grey,
            CsTeam.Terrorist => ChatColors.Red,
            CsTeam.CounterTerrorist => ChatColors.Blue,
            _ => ChatColors.Default
        };
    }
    
    public void OnConfigParsed(AFKManagerConfig config)
    {
        Config = config;
        
        Config.ChatPrefix = ModifyColorValue(Config.ChatPrefix);
        Config.ChatMoveMessage = ModifyColorValue(Config.ChatMoveMessage).Replace("{chatprefix}", Config.ChatPrefix);
        Config.ChatKickMessage = ModifyColorValue(Config.ChatKickMessage).Replace("{chatprefix}", Config.ChatPrefix);
        Config.ChatKillMessage = ModifyColorValue(Config.ChatKillMessage).Replace("{chatprefix}", Config.ChatPrefix);
        Config.ChatWarningKillMessage = ModifyColorValue(Config.ChatWarningKillMessage).Replace("{chatprefix}", Config.ChatPrefix);
        Config.ChatWarningMoveMessage = ModifyColorValue(Config.ChatWarningMoveMessage).Replace("{chatprefix}", Config.ChatPrefix);
        Config.ChatWarningKickMessage = ModifyColorValue(Config.ChatWarningKickMessage).Replace("{chatprefix}", Config.ChatPrefix);

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
        
        AddTimer(Config.Timer, AfkTimer_Callback, TimerFlags.REPEAT);
    }
    
    private class PlayerInfo
    {
        public QAngle? Angles { get; set; }
        public Vector? Origin { get; set; }
        public int WarningCount { get; set; }
        public int SpecWarningCount { get; set; }
        public float SpecAfkTime { get; set; }
        public bool MovedByPlugin { get; set; }
    }
    
    private readonly Dictionary<uint, PlayerInfo> _gPlayerInfo = new();
    #endregion

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _gGameRulesProxy = null;
        });
        
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _gPlayerInfo.Clear();
            _gGameRulesProxy = null;
        });
        
        #region OnClientConnected
        RegisterListener<Listeners.OnClientConnected>(playerSlot =>
        {
            var finalSlot = (uint)playerSlot + 1;
            
            if (_gPlayerInfo.ContainsKey(finalSlot))
                return;
            
            _gPlayerInfo.Add(finalSlot, new PlayerInfo
            {
                Angles = new QAngle(),
                Origin = new Vector(),
                SpecWarningCount = 0,
                SpecAfkTime = 0,
                WarningCount = 0
            });
        });
        
        RegisterListener<Listeners.OnClientDisconnectPost>(playerSlot =>
        {
            _gPlayerInfo.Remove((uint)playerSlot + 1);
        });
        #endregion
        #region hotReload
        if (hotReload)
        {
            AddTimer(1.0f, () =>
            {
                var players = Utilities.GetPlayers().Where(x => x is { IsBot: false, Connected: PlayerConnectedState.PlayerConnected });

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
            });
        }
        #endregion
        AddCommandListener("spec_mode", OnCommandListener);
        AddCommandListener("spec_next", OnCommandListener);
    }

    private HookResult OnCommandListener(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid)
            return HookResult.Continue;
        
        var i = player.Index;
        if (!_gPlayerInfo.TryGetValue(i, out var data))
            return HookResult.Continue;
        
        data.SpecAfkTime = 0;
        data.SpecWarningCount = 0;
        data.WarningCount = 0;
        
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;
            
        var i = player.Index;

        if (!_gPlayerInfo.TryGetValue(i, out var value))
            return HookResult.Continue;
            
        value.SpecAfkTime = 0;
        value.SpecWarningCount = 0;
        value.WarningCount = 0;
                
        if(@event.Team != 1)
            value.MovedByPlugin = false;
        
        return HookResult.Continue;
    }
    
    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        AddTimer(0.2f, () =>
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE)
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
    }
    
    private void AfkTimer_Callback()
    {
        if (_gGameRulesProxy == null)
        {
            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();

            _gGameRulesProxy = gameRulesProxy?.GameRules;

            if (_gGameRulesProxy == null)
                return;
        }

        if (_gGameRulesProxy.FreezePeriod)
            return;

        string? sFormat = null; 
        
        var players = Utilities.GetPlayers().Where(x => x is { IsBot: false, Connected: PlayerConnectedState.PlayerConnected }).ToList();
        var playersCount = players.Count;
        
        foreach (var player in players)
        {
            var i = player.Index;
            
            if (!_gPlayerInfo.TryGetValue(i, out var data) || player.ControllingBot)
                continue;
            
            #region AFK Time
            if (Config.Warnings != 0 && player is { LifeState: (byte)LifeState_t.LIFE_ALIVE, TeamNum: 2 or 3 })
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
                    data.Origin.X == origin.X && data.Origin.Y == origin.Y)
                {
                    if (data.WarningCount == Config.Warnings)
                    {
                        data.WarningCount = 0;

                        switch (Config.Punishment)
                        {
                            case 0:
                                sFormat = Config.ChatKillMessage.Replace("{playername}", player.PlayerName).Replace("{teamcolor}",
                                    GetTeamColor((CsTeam)player.TeamNum).ToString());

                                playerPawn?.CommitSuicide(false, true);
                                break;
                            case 1:
                                sFormat = Config.ChatMoveMessage?.Replace("{playername}", player.PlayerName).Replace("{teamcolor}",
                                    GetTeamColor((CsTeam)player.TeamNum).ToString());

                                playerPawn?.CommitSuicide(false, true);
                                player.ChangeTeam(CsTeam.Spectator);
                                break;
                            case 2:
                                sFormat = Config.ChatKickMessage?.Replace("{playername}", player.PlayerName).Replace("{teamcolor}",
                                        GetTeamColor((CsTeam)player.TeamNum).ToString());

                                Server.ExecuteCommand($"kickid {player.UserId}");
                                break;
                        }
                        
                        Server.PrintToChatAll(sFormat ?? string.Empty);
                        continue;
                    }

                    sFormat = Config.Punishment switch
                    {
                        0 => Config.ChatWarningKillMessage,
                        1 => Config.ChatWarningMoveMessage,
                        2 => Config.ChatWarningKickMessage,
                        _ => sFormat
                    };

                    sFormat = sFormat?.Replace("{playername}", player.PlayerName).Replace("{time}", $"{((Config.Warnings * Config.Timer) - (_gPlayerInfo[i].WarningCount * Config.Timer)):F1}");
                    
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

            if (Config.SpecKickPlayerAfterXWarnings != 0
                && player.TeamNum == 1
                && playersCount >= Config.SpecKickMinPlayers)
            {
                if(Config.SpecKickOnlyMovedByPlugin && !data.MovedByPlugin || Config.SpecSkipFlag != string.Empty && AdminManager.PlayerHasPermissions(player, Config.SpecSkipFlag))
                    continue;
                
                data.SpecAfkTime += Config.Timer;

                if (!(data.SpecAfkTime >= Config.SpecWarnPlayerEveryXSeconds))
                    continue;
                
                if (data.SpecWarningCount == Config.SpecKickPlayerAfterXWarnings)
                {
                    sFormat = Config.ChatKickMessage?.Replace("{playername}", player.PlayerName).Replace("{teamcolor}",
                        GetTeamColor((CsTeam)player.TeamNum).ToString());

                    Server.PrintToChatAll(sFormat ?? string.Empty);
                    Server.ExecuteCommand($"kickid {player.UserId}");

                    data.SpecWarningCount = 0;
                    data.SpecAfkTime = 0; // reset counter
                    continue;
                }

                sFormat = Config.ChatWarningKickMessage?.Replace("{time}",
                    $"{Config.SpecKickPlayerAfterXWarnings * Config.SpecWarnPlayerEveryXSeconds - data.SpecWarningCount * Config.SpecWarnPlayerEveryXSeconds:F1}");

                player.PrintToChat(sFormat ?? string.Empty);
                data.SpecWarningCount++;
                data.SpecAfkTime = 0; // reset counter
            }
            #endregion
        }
    }
}