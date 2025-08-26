using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace AFKManager;

public class AFKManagerConfig : BasePluginConfig
{
    public int AfkPunishAfterWarnings { get; set; } = 3;
    public int AfkPunishment { get; set; } = 1;
    public float AfkWarnInterval { get; set; } = 5.0f;
    public int AfkTransferC4AfterWarnings { get; set; } = 1;
    public bool AfkTransferC4OnlyFromBuyZone { get; set; } = true;
    public float SpecWarnInterval { get; set; } = 20.0f;
    public int SpecKickAfterWarnings { get; set; } = 5;
    public int SpecKickMinPlayers { get; set; } = 5;
    public bool SpecKickOnlyMovedByPlugin { get; set; } = false;
    public List<string> SpecSkipFlag { get; set; } = [..new[] { "@css/root", "@css/ban" }];
    public List<string> AfkSkipFlag { get; set; } = [..new[] { "@css/root", "@css/ban" }];
    public List<string> AntiCampSkipFlag { get; set; } = [..new[] { "@css/root", "@css/ban" }];
    public string PlaySoundName { get; set; } = "ui/panorama/popup_reveal_01";
    public bool SkipWarmup { get; set; } = false;
    public float AntiCampRadius { get; set; } = 130.0f;
    public int AntiCampPunishment { get; set; } = 1;
    public int AntiCampSlapDamage { get; set; } = 0;
    public float AntiCampWarnInterval { get; set; } = 10.0f;
    public int AntiCampPunishAfterWarnings { get; set; } = 3;
    public bool AntiCampSkipBombPlanted { get; set; } = true;
    public int AntiCampSkipTeam { get; set; } = 3;
    public float Timer { get; set; } = 5.0f;
}

public class AFKManager : BasePlugin, IPluginConfig<AFKManagerConfig>
{
    #region definitions
    public override string ModuleAuthor => "NiGHT & K4ryuu (forked by Глеб Хлебов)";
    public override string ModuleName => "AFK Manager";
    public override string ModuleVersion => "0.2.7";
    
    public required AFKManagerConfig Config { get; set; }
    private CCSGameRules? _gGameRulesProxy;
    
    public void OnConfigParsed(AFKManagerConfig config)
    {
        Config = config;

        if (Config.AfkPunishment is < 0 or > 2)
        {
            Config.AfkPunishment = 1;
            Console.WriteLine($"{ModuleName}: AFKPunishment value is invalid, setting to default value (1).");
        }

        if(Config.Timer < 0.1f)
        {                 
            Config.Timer = 5.0f;
            Console.WriteLine($"{ModuleName}: Timer value is invalid, setting to default value (5.0).");
        }

        if (Config.SpecWarnInterval < Config.Timer)
        {
            Config.SpecWarnInterval = Config.Timer;
            Console.WriteLine($"{ModuleName}: The value of SpecWarnInterval is less than the value of Timer, SpecWarnInterval will be forced to {Config.Timer}");
        }
        
        if(Config.AntiCampWarnInterval < Config.Timer)
        {
            Config.AntiCampWarnInterval = Config.Timer;
            Console.WriteLine($"{ModuleName}: The value of AntiCampWarnInterval is less than the value of Timer, AntiCampWarnInterval will be forced to {Config.Timer}");
        }
        
        if (Config.AntiCampPunishment is < 0 or > 1)
        {
            Config.AfkPunishment = 1;
            Console.WriteLine($"{ModuleName}: AntiCampPunishment value is invalid, setting to default value (1).");
        }
        
        AddTimer(Config.Timer, AfkTimer_Callback, TimerFlags.REPEAT);
    }
    
    private class PlayerInfo
    {
        public QAngle? Angles { get; set; }
        public Vector? Origin { get; set; }
        public float AfkTime { get; set; }
        public int AfkWarningCount { get; set; }
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
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        #endregion
        AddCommandListener("spec_mode", OnCommandListener);
        AddCommandListener("spec_next", OnCommandListener);
    }

    private HookResult OnCommandListener(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid)
            return HookResult.Continue;
        
        if (!_gPlayerInfo.TryGetValue(player.Index, out var data))
            return HookResult.Continue;
        
        data.SpecAfkTime = 0;
        data.SpecWarningCount = 0;
        data.AfkWarningCount = 0;
        
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        if (!_gPlayerInfo.TryGetValue(player.Index, out var value))
            return HookResult.Continue;
        
        value.SpecAfkTime = 0;
        value.SpecWarningCount = 0;
        value.AfkWarningCount = 0;
                
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
                
            if(!_gPlayerInfo.TryGetValue(player.Index, out var data))
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
            if (player is { LifeState: (byte)LifeState_t.LIFE_ALIVE, Team: CsTeam.Terrorist or CsTeam.CounterTerrorist })
            {
                var playerPawn = player.PlayerPawn.Value;
                var playerFlags = player.Pawn.Value!.Flags;

                if ((playerFlags & ((uint)PlayerFlags.FL_ONGROUND | (uint)PlayerFlags.FL_FROZEN)) != (uint)PlayerFlags.FL_ONGROUND)
                    continue;
                
                var angles = playerPawn?.EyeAngles;
                var origin = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
                
                /*  ------------------------------------------->  <-------------------------------------------  */
                if (Config.AfkPunishAfterWarnings != 0
                    && !(Config.AfkSkipFlag.Count >= 1 && AdminManager.PlayerHasPermissions(player, Config.AfkSkipFlag.ToArray()))
                    && data.Angles.X == angles.X && data.Angles.Y == angles.Y
                    && data.Origin.X == origin.X && data.Origin.Y == origin.Y)
                {
                    data.AfkTime += Config.Timer;
                    
                    if (data.AfkTime < Config.AfkWarnInterval)
                        continue;
                    
                    //transfer c4 to nearest player
                    if (Config.AfkTransferC4AfterWarnings != 0 && player.TeamNum == 2 && Config.AfkTransferC4AfterWarnings == data.AfkWarningCount &&
                        HasC4(player))
                    {
                        if(Config.AfkTransferC4OnlyFromBuyZone && !playerPawn.InBuyZone)
                            continue;
                        
                        CCSPlayer_WeaponServices weaponService = new (player.PlayerPawn?.Value?.WeaponServices?.Handle ?? nint.Zero);
                        CBasePlayerWeapon weapon = new(weaponService.MyWeapons.FirstOrDefault(w => w.IsValid && w.Value != null && w.Value.DesignerName == "weapon_c4")?.Value?.Handle ?? nint.Zero);
                        if(!weapon.IsValid)
                            continue;
                        
                        var nearestPlayer = FindNearestPlayer(player);
                        if (nearestPlayer != null)
                        {
                            RemoveC4(weaponService,  weapon);
                            nearestPlayer.GiveNamedItem("weapon_c4");
                            Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatBombTransfer"].Value.Replace("{targetPlayerName}", nearestPlayer.PlayerName)));
                        }
                    }
                    
                    if (data.AfkWarningCount == Config.AfkPunishAfterWarnings)
                    {
                        switch (Config.AfkPunishment)
                        {
                            case 0:
                                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKillMessage"].Value));
                                playerPawn?.CommitSuicide(false, true);
                                
                                break;
                            case 1:
                                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatMoveMessage"].Value));
                                playerPawn?.CommitSuicide(false, true);
                                player.ChangeTeam(CsTeam.Spectator);
                                data.MovedByPlugin = true;
                                
                                break;
                            case 2:
                                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKickMessage"].Value));
                                Server.ExecuteCommand($"kickid {player.UserId}");
                                
                                break;
                        }
                        
                        data.AfkWarningCount = 0;
                        data.AfkTime = 0;
                        
                        continue;
                    }
                    
                    switch (Config.AfkPunishment)
                    {
                        case 0:
                            player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningKillMessage"].Value, Config.AfkPunishAfterWarnings * Config.AfkWarnInterval - data.AfkWarningCount * Config.AfkWarnInterval));
                        break;

                        case 1:
                            player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningMoveMessage"].Value, Config.AfkPunishAfterWarnings * Config.AfkWarnInterval - data.AfkWarningCount * Config.AfkWarnInterval));
                            break;

                        case 2:
                            player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningKickMessage"].Value, Config.AfkPunishAfterWarnings * Config.AfkWarnInterval - data.AfkWarningCount * Config.AfkWarnInterval));
                            break;
                    }

                    if (!string.IsNullOrEmpty(Config.PlaySoundName))
                        player.ExecuteClientCommand($"play {Config.PlaySoundName}");
                    
                    data.AfkTime = 0;
                    data.AfkWarningCount++;
                }
                else
                {
                    data.AfkTime = 0;
                    data.AfkWarningCount = 0;
                }
                /*  ------------------------------------------->  <-------------------------------------------  */
                if (data.AfkWarningCount == 0 && Config.AntiCampPunishAfterWarnings != 0
                                           && !(Config.AntiCampSkipBombPlanted && _gGameRulesProxy.BombPlanted)
                                           && !(Config.AntiCampSkipFlag.Count >= 1 && AdminManager.PlayerHasPermissions(player, Config.AntiCampSkipFlag.ToArray()))
                                           && player.TeamNum != Config.AntiCampSkipTeam)
                {
                    if (CalculateDistance(data.Origin, origin) < Config.AntiCampRadius)
                    {
                        data.AntiCampTime += Config.Timer;

                        if (data.AntiCampTime < Config.AntiCampWarnInterval)
                            continue;

                        if (data.AntiCampWarningCount == Config.AntiCampPunishAfterWarnings)
                        {
                            switch (Config.AntiCampPunishment)
                            {
                                case 0:
                                    Server.PrintToChatAll(ReplaceVars(player, Localizer["AntiCampSlayMessage"].Value));

                                    playerPawn?.CommitSuicide(false, true);
                                    break;
                                case 1:
                                    Server.PrintToChatAll(ReplaceVars(player, Localizer["AntiCampSlapMessage"].Value));
                                
                                    Slap(playerPawn, Config.AntiCampSlapDamage);
                                    break;
                            }
                            
                            data.AntiCampWarningCount = 0;
                            data.AntiCampTime = 0;

                            continue;
                        }

                        switch (Config.AntiCampPunishment)
                        {
                            case 0:
                                player.PrintToChat(ReplaceVars(player, Localizer["AntiCampSlayWarningMessage"].Value, Config.AntiCampPunishAfterWarnings * Config.AntiCampWarnInterval - data.AntiCampWarningCount * Config.AntiCampWarnInterval));
                                break;
                            case 1:
                                player.PrintToChat(ReplaceVars(player, Localizer["AntiCampSlapWarningMessage"].Value, Config.AntiCampPunishAfterWarnings * Config.AntiCampWarnInterval - data.AntiCampWarningCount * Config.AntiCampWarnInterval));
                                break;
                        }
                            
                        if (!string.IsNullOrEmpty(Config.PlaySoundName))
                            player.ExecuteClientCommand($"play {Config.PlaySoundName}");
                            
                        data.AntiCampWarningCount++;
                        data.AntiCampTime = 0;
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

            if (Config.SpecKickAfterWarnings != 0
                && player.TeamNum == 1
                && playersCount >= Config.SpecKickMinPlayers)
            {
                if((Config.SpecKickOnlyMovedByPlugin && !data.MovedByPlugin) || (Config.SpecSkipFlag.Count >= 1 && AdminManager.PlayerHasPermissions(player, Config.SpecSkipFlag.ToArray())))
                    continue;
                
                data.SpecAfkTime += Config.Timer;

                if (!(data.SpecAfkTime >= Config.SpecWarnInterval))
                    continue;
                
                if (data.SpecWarningCount == Config.SpecKickAfterWarnings)
                {
                    Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKickMessage"].Value));
                    Server.ExecuteCommand($"kickid {player.UserId}");

                    data.SpecWarningCount = 0;
                    data.SpecAfkTime = 0;
                    
                    continue;
                }

                player.PrintToChat( ReplaceVars(player, Localizer["ChatWarningKickMessage"].Value, Config.SpecKickAfterWarnings * Config.SpecWarnInterval - data.SpecWarningCount * Config.SpecWarnInterval));
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

    private static void RemoveC4(CCSPlayer_WeaponServices services ,CBasePlayerWeapon weapon)
    {
        Guard.IsValidEntity(weapon);
        VirtualFunction.CreateVoid<nint, CBasePlayerWeapon, Vector?, Vector?>(services.Handle, 24)(services.Handle, weapon, null, null);
        
        weapon.AddEntityIOEvent("Kill", weapon, delay: 0.2f);
    }
    
    private CCSPlayerController? FindNearestPlayer(CCSPlayerController player)
    {
        CCSPlayerController? nearestPlayer = null;
        var firstDistance = 0.0f;

        var players = Utilities.GetPlayers().Where(x => x is {TeamNum: 2, LifeState: (byte)LifeState_t.LIFE_ALIVE, PawnIsAlive: true }).ToList();
        foreach (var target in players)
        {
            var outData = _gPlayerInfo[target.Index];
            if (target.Index == player.Index || outData.AfkWarningCount > 0)
                continue;
            
            var distance = CalculateDistance(player.PlayerPawn?.Value?.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(), target.PlayerPawn?.Value?.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector());
            if (nearestPlayer != null && !(distance < firstDistance))
                continue;
            
            nearestPlayer = target;
            firstDistance = distance;
        }
        return nearestPlayer;
    }
            
    private static bool HasC4(CCSPlayerController player)
    {
        var weaponServices = player.PlayerPawn?.Value?.WeaponServices;
        if (weaponServices == null)
            return false;
        
        var matchedWeapon = weaponServices.MyWeapons.FirstOrDefault(w => w.IsValid && w.Value != null && w.Value.DesignerName == "weapon_c4");
        return matchedWeapon?.IsValid == true;
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