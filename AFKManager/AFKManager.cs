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
    public float SpecWarnPlayerEveryXSeconds { get; set; } = 20.0f;
    public int SpecKickPlayerAfterXWarnings { get; set; } = 5;
    public int SpecKickMinPlayers { get; set; } = 5;
    public bool SpecKickOnlyMovedByPlugin { get; set; } = false;
    public List<string> SpecSkipFlag { get; set; } = [..new[] { "@css/root", "@css/ban" }];
    public List<string> SkipFlag { get; set; } = [..new[] { "@css/root", "@css/ban" }];
    public int Warnings { get; set; } = 3;
    public int Punishment { get; set; } = 1;
    public bool Retake { get; set; } = false;
    public bool PlaySound { get; set; } = true;
    public float Timer { get; set; } = 5.0f;
}

public class AFKManager : BasePlugin, IPluginConfig<AFKManagerConfig>
{
    #region definitions
    public override string ModuleAuthor => "NiGHT & K4ryuu";
    public override string ModuleName => "AFK Manager";
    public override string ModuleVersion => "0.1.1";
    
    public required AFKManagerConfig Config { get; set; }
    private CCSGameRules? _gGameRulesProxy;

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
                
                if(Config.SkipFlag.Count >= 1 && AdminManager.PlayerHasPermissions(player, Config.SkipFlag.ToArray()))
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
                                sFormat = Localizer["ChatKickMessage"].Value.Replace("{playername}", player.PlayerName).Replace("{teamcolor}",
                                    GetTeamColor((CsTeam)player.TeamNum).ToString());

                                playerPawn?.CommitSuicide(false, true);
                                break;
                            case 1:
                                sFormat = Localizer["ChatMoveMessage"].Value.Replace("{playername}", player.PlayerName).Replace("{teamcolor}",
                                    GetTeamColor((CsTeam)player.TeamNum).ToString());

                                playerPawn?.CommitSuicide(false, true);
                                player.ChangeTeam(CsTeam.Spectator);
                                break;
                            case 2:
                                sFormat = Localizer["ChatKickMessage"].Value.Replace("{playername}", player.PlayerName).Replace("{teamcolor}",
                                        GetTeamColor((CsTeam)player.TeamNum).ToString());

                                Server.ExecuteCommand($"kickid {player.UserId}");
                                break;
                        }
                        
                        Server.PrintToChatAll(Localizer["ChatPrefix"] + sFormat);
                        continue;
                    }

                    sFormat = Config.Punishment switch
                    {
                        0 => Localizer["ChatWarningKillMessage"].Value,
                        1 => Localizer["ChatWarningMoveMessage"].Value,
                        2 => Localizer["ChatWarningKickMessage"].Value,
                        _ => sFormat
                    };

                    sFormat = sFormat?.Replace("{playername}", player.PlayerName).Replace("{time}", $"{((Config.Warnings * Config.Timer) - (_gPlayerInfo[i].WarningCount * Config.Timer)):F1}");
                    
                    player.PrintToChat(Localizer["ChatPrefix"] + sFormat);
                    
                    if (Config.PlaySound)
                        player.ExecuteClientCommand("play player/damage1");
                    
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
                if(Config.SpecKickOnlyMovedByPlugin && !data.MovedByPlugin || Config.SpecSkipFlag.Count >= 1 && AdminManager.PlayerHasPermissions(player, Config.SpecSkipFlag.ToArray()))
                    continue;
                
                data.SpecAfkTime += Config.Timer;

                if (!(data.SpecAfkTime >= Config.SpecWarnPlayerEveryXSeconds))
                    continue;
                
                if (data.SpecWarningCount == Config.SpecKickPlayerAfterXWarnings)
                {
                    sFormat = Localizer["ChatKickMessage"].Value.Replace("{playername}", player.PlayerName).Replace("{teamcolor}",
                        GetTeamColor((CsTeam)player.TeamNum).ToString());

                    Server.PrintToChatAll(Localizer["ChatPrefix"] + sFormat);
                    Server.ExecuteCommand($"kickid {player.UserId}");

                    data.SpecWarningCount = 0;
                    data.SpecAfkTime = 0; // reset counter
                    continue;
                }

                sFormat = Localizer["ChatWarningKickMessage"].Value.Replace("{time}",
                    $"{Config.SpecKickPlayerAfterXWarnings * Config.SpecWarnPlayerEveryXSeconds - data.SpecWarningCount * Config.SpecWarnPlayerEveryXSeconds:F1}");

                player.PrintToChat(Localizer["ChatPrefix"] + sFormat);
                data.SpecWarningCount++;
                data.SpecAfkTime = 0; // reset counter
            }
            #endregion
        }
    }
}