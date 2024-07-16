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
    public List<string> AntiCampSkipFlag { get; set; } = [..new[] { "@css/root", "@css/ban" }];
    public int Warnings { get; set; } = 3;
    public int Punishment { get; set; } = 1;
    public string PlaySoundName { get; set; } = "ui/panorama/popup_reveal_01";
    public bool SkipWarmup { get; set; } = false;
    
    public float AntiCampRadius { get; set; } = 130.0f;
    public int AntiCampPunishment { get; set; } = 1;
    public int AntiCampSlapDamage { get; set; } = 0;
    public float AntiCampWarnPlayerEveryXSeconds { get; set; } = 10.0f;
    public int AntiCampPunishPlayerAfterXWarnings { get; set; } = 3;
    public bool AntiCampSkipBombPlanted { get; set; } = true;
    public int AntiCampSkipTeam { get; set; } = 3;
    public float Timer { get; set; } = 5.0f;
}

public class AFKManager : BasePlugin, IPluginConfig<AFKManagerConfig>
{
    #region definitions
    public override string ModuleAuthor => "NiGHT & K4ryuu (forked by Глеб Хлебов)";
    public override string ModuleName => "AFK Manager";
    public override string ModuleVersion => "0.2.1";
    
    public required AFKManagerConfig Config { get; set; }
    private CCSGameRules? _gGameRulesProxy;
    
    public void OnConfigParsed(AFKManagerConfig config)
    {
        Config = config;

        if (Config.Punishment is < 0 or > 2)
        {
            Config.Punishment = 1;
            Console.WriteLine($"{ModuleName}: Punishment value is invalid, setting to default value (1).");
        }

        if(Config.Timer < 0.1f)
        {                 
            Config.Timer = 5.0f;
            Console.WriteLine($"{ModuleName}: Timer value is invalid, setting to default value (5.0).");
        }

        if (Config.SpecWarnPlayerEveryXSeconds < Config.Timer)
        {
            Config.SpecWarnPlayerEveryXSeconds = Config.Timer;
            Console.WriteLine($"{ModuleName}: The value of SpecWarnPlayerEveryXSeconds is less than the value of Timer, SpecWarnPlayerEveryXSeconds will be forced to {Config.Timer}");
        }
        
        if(Config.AntiCampWarnPlayerEveryXSeconds < Config.Timer)
        {
            Config.AntiCampWarnPlayerEveryXSeconds = Config.Timer;
            Console.WriteLine($"{ModuleName}: The value of AntiCampWarnPlayerEveryXSeconds is less than the value of Timer, AntiCampWarnPlayerEveryXSeconds will be forced to {Config.Timer}");
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
        public float AntiCampTime { get; set; }
        public int AntiCampWarningCount { get; set; }
    }
    
    private readonly Dictionary<uint, PlayerInfo> _gPlayerInfo = new();
    #endregion

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            Server.NextFrame(() =>
            {
                _gGameRulesProxy =
                    Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules ??
                    throw new Exception("Failed to find game rules proxy entity.");
            });
            
        });
        
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _gPlayerInfo.Clear();
        });
        
        #region OnClientConnected
        RegisterListener<Listeners.OnClientConnected>(playerSlot =>
        {
            var finalSlot = (uint)playerSlot + 1;
            
            if (_gPlayerInfo.ContainsKey(finalSlot))
                return;
            
            _gPlayerInfo.Add(finalSlot, new PlayerInfo {
                Angles = new QAngle(),
                Origin = new Vector()
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
                            Origin = new Vector()
                        });
                    }
                }
                
                _gGameRulesProxy =
                    Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules ??
                        throw new Exception("Failed to find game rules proxy entity on hotReload.");
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
            data.AntiCampWarningCount = 0;
            data.AntiCampTime = 0;
        }, TimerFlags.STOP_ON_MAPCHANGE);
            
        return HookResult.Continue;
    }
    
    private void AfkTimer_Callback()
    {
        if (_gGameRulesProxy == null || _gGameRulesProxy.FreezePeriod || (Config.SkipWarmup && _gGameRulesProxy.WarmupPeriod))
            return;

        var players = Utilities.GetPlayers().Where(x => x is { IsBot: false, Connected: PlayerConnectedState.PlayerConnected }).ToList();
        var playersCount = players.Count;
        
        foreach (var player in players)
        {
            if (player.ControllingBot || !_gPlayerInfo.TryGetValue(player.Index, out var data))
                continue;
            
            #region AFK Time
            if (Config.Warnings != 0 && player is { LifeState: (byte)LifeState_t.LIFE_ALIVE, Team: CsTeam.Terrorist or CsTeam.CounterTerrorist })
            {
                var playerPawn = player.PlayerPawn.Value;
                var playerFlags = player.Pawn.Value!.Flags;

                if ((playerFlags & ((uint)PlayerFlags.FL_ONGROUND | (uint)PlayerFlags.FL_FROZEN)) != (uint)PlayerFlags.FL_ONGROUND)
                    continue;
                
                var angles = playerPawn?.EyeAngles;
                var origin = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;

                if (!(Config.SkipFlag.Count >= 1 && AdminManager.PlayerHasPermissions(player, Config.SkipFlag.ToArray()))
                    && data.Angles.X == angles.X && data.Angles.Y == angles.Y
                    && data.Origin.X == origin.X && data.Origin.Y == origin.Y)
                {
                    if (data.WarningCount == Config.Warnings)
                    {
                        data.WarningCount = 0;

                        switch (Config.Punishment)
                        {
                            case 0:
                                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKillMessage"].Value));
                                playerPawn?.CommitSuicide(false, true);
                                
                                break;
                            case 1:
                                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatMoveMessage"].Value));
                                playerPawn?.CommitSuicide(false, true);
                                player.ChangeTeam(CsTeam.Spectator);
                                
                                break;
                            case 2:
                                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKickMessage"].Value));
                                Server.ExecuteCommand($"kickid {player.UserId}");
                                
                                break;
                        }
                        continue;
                    }
                    
                    switch (Config.Punishment)
                    {
                        case 0:
                            player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningKillMessage"].Value, Config.Warnings * Config.Timer - data.WarningCount * Config.Timer));
                        break;

                        case 1:
                            player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningMoveMessage"].Value, Config.Warnings * Config.Timer - data.WarningCount * Config.Timer));
                            break;

                        case 2:
                            player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningKickMessage"].Value, Config.Warnings * Config.Timer - data.WarningCount * Config.Timer));
                            break;
                    }

                    if (!string.IsNullOrEmpty(Config.PlaySoundName))
                        player.ExecuteClientCommand($"play {Config.PlaySoundName}");
                    
                    data.WarningCount++;
                }
                else
                    data.WarningCount = 0;

                if (data.WarningCount == 0 && Config.AntiCampPunishPlayerAfterXWarnings != 0
                                           && !(Config.AntiCampSkipBombPlanted && _gGameRulesProxy.BombPlanted)
                                           && !(Config.AntiCampSkipFlag.Count >= 1 && AdminManager.PlayerHasPermissions(player, Config.AntiCampSkipFlag.ToArray()))
                                           && player.TeamNum != Config.AntiCampSkipTeam)
                {
                    if (CalculateDistance(data.Origin, origin) < Config.AntiCampRadius)
                    {
                        data.AntiCampTime += Config.Timer;

                        if (!(data.AntiCampTime >= Config.AntiCampWarnPlayerEveryXSeconds))
                            continue;

                        if (data.AntiCampWarningCount == Config.AntiCampPunishPlayerAfterXWarnings)
                        {
                            switch (Config.AntiCampPunishment)
                            {
                                case 0:
                                    Server.PrintToChatAll(ReplaceVars(player, Localizer["AntiCampSlayMessage"].Value));

                                    playerPawn.CommitSuicide(false, true);
                                    break;
                                case 1:
                                    Server.PrintToChatAll(ReplaceVars(player, Localizer["AntiCampSlapMessage"].Value));

                                    Slap(playerPawn, Config.AntiCampSlapDamage);
                                    break;
                            }
                            
                            data.AntiCampWarningCount = 0;
                            data.AntiCampTime = 0;
                        }
                        else
                        {
                            switch (Config.AntiCampPunishment)
                            {
                                case 0:
                                    player.PrintToChat(ReplaceVars(player, Localizer["AntiCampSlayWarningMessage"].Value, Config.AntiCampPunishPlayerAfterXWarnings * Config.AntiCampWarnPlayerEveryXSeconds - data.AntiCampWarningCount * Config.AntiCampWarnPlayerEveryXSeconds));
                                    break;
                                case 1:
                                    player.PrintToChat(ReplaceVars(player, Localizer["AntiCampSlapWarningMessage"].Value, Config.AntiCampPunishPlayerAfterXWarnings * Config.AntiCampWarnPlayerEveryXSeconds - data.AntiCampWarningCount * Config.AntiCampWarnPlayerEveryXSeconds));
                                    break;
                            }
                            
                            if (!string.IsNullOrEmpty(Config.PlaySoundName))
                                player.ExecuteClientCommand($"play {Config.PlaySoundName}");
                            
                            data.AntiCampWarningCount++;
                        }
                    }
                    else
                    {
                        data.AntiCampWarningCount = 0;
                        data.AntiCampTime = 0;
                    }
                }
                
                data.Angles = new QAngle(angles?.X, angles?.Y, angles?.Z);
                data.Origin = new Vector(origin?.X, origin?.Y, origin?.Z);
                
                continue;
            }
            
            #endregion
            #region SPEC Time

            if (Config.SpecKickPlayerAfterXWarnings != 0
                && player.TeamNum == 1
                && playersCount >= Config.SpecKickMinPlayers)
            {
                if((Config.SpecKickOnlyMovedByPlugin && !data.MovedByPlugin) || (Config.SpecSkipFlag.Count >= 1 && AdminManager.PlayerHasPermissions(player, Config.SpecSkipFlag.ToArray())))
                    continue;
                
                data.SpecAfkTime += Config.Timer;

                if (!(data.SpecAfkTime >= Config.SpecWarnPlayerEveryXSeconds))
                    continue;
                
                if (data.SpecWarningCount == Config.SpecKickPlayerAfterXWarnings)
                {
                    Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKickMessage"].Value));
                    Server.ExecuteCommand($"kickid {player.UserId}");

                    data.SpecWarningCount = 0;
                    data.SpecAfkTime = 0;
                    continue;
                }

                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatWarningKickMessage"].Value, Config.SpecKickPlayerAfterXWarnings * Config.SpecWarnPlayerEveryXSeconds - data.SpecWarningCount * Config.SpecWarnPlayerEveryXSeconds));
                data.SpecWarningCount++;
                data.SpecAfkTime = 0;
            }
            #endregion
        }
    }
    
    private static float CalculateDistance(Vector point1, Vector point2)
    {
        var dx = point2.X - point1.X;
        var dy = point2.Y - point1.Y;
        var dz = point2.Z - point1.Z;

        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
    
    private static string GetTeamColor(CsTeam team)
    {
        return team switch
        {
            CsTeam.Spectator => ChatColors.Grey.ToString(),
            CsTeam.Terrorist => ChatColors.Red.ToString(),
            CsTeam.CounterTerrorist => ChatColors.Blue.ToString(),
            _ => ChatColors.Default.ToString()
        };
    }
    
    private string ReplaceVars(CCSPlayerController player, string message, float timeAmount = 0.0f)
    {
        return Localizer["ChatPrefix"] + message.Replace("{playerName}", player.PlayerName)
                      .Replace("{teamColor}", GetTeamColor(player.Team))
                      .Replace("{weaponName}", player.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value?.DesignerName ?? "Unknown")
                      .Replace("{timeAmount}", $"{timeAmount:F1}")
                      .Replace("{slapAmount}", Config.AntiCampSlapDamage.ToString())
                      .Replace("{zoneName}", player.PlayerPawn?.Value?.LastPlaceName ?? "Unknown");
    }
    
    private static void Slap(CBasePlayerPawn? pawn, int damage = 0)
    {
        if (pawn == null || pawn.Health <= 0)
            return;

        pawn.Health -= damage;

        if (pawn.Health <= 0)
        {
            pawn.CommitSuicide(true, true);
            return;
        }
        
        Random random = new();
        Vector vel = new(pawn.AbsVelocity.X, pawn.AbsVelocity.Y, pawn.AbsVelocity.Z);

        vel.X += (random.Next(180) + 50) * (random.Next(2) == 1 ? -1 : 1);
        vel.Y += (random.Next(180) + 50) * (random.Next(2) == 1 ? -1 : 1);
        vel.Z += random.Next(200) + 100;

        pawn.Teleport(pawn.AbsOrigin, pawn.AbsRotation, vel);
    }
}