using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json.Serialization;

namespace AFKManager;

public class AFKManagerConfig : BasePluginConfig
{
    public float AfkMarkTime { get; set; } = 20.0f;
    public float AfkKickTime { get; set; } = 10.0f;
    public int AfkTransferC4AfterWarnings { get; set; } = 1;
    public bool AfkTransferC4OnlyFromBuyZone { get; set; } = true;
    public float SpecWarnInterval { get; set; } = 30.0f;
    public float SpecKickTime { get; set; } = 10.0f;
    public int SpecKickMinPlayers { get; set; } = 1;
    public bool SpecKickOnlyMovedByPlugin { get; set; } = false;
    public List<string> SpecSkipFlag { get; set; } = [..new[] { "@css/root", "@css/ban" }];
    public List<string> AfkSkipFlag { get; set; } = [..new[] { "@css/root", "@css/ban" }];
    public List<string> AntiCampSkipFlag { get; set; } = [..new[] { "@css/root", "@css/ban" }];
    public string PlaySoundName { get; set; } = "";
    public bool SkipWarmup { get; set; } = true;
    public float AntiCampRadius { get; set; } = 130.0f;
    public int AntiCampPunishment { get; set; } = 1;
    public int AntiCampSlapDamage { get; set; } = 0;
    public float AntiCampMarkTime { get; set; } = 15.0f;
    public float AntiCampPunishTime { get; set; } = 10.0f;
    public bool AntiCampPunishmentAnnounce { get; set; } = false;
    public bool AntiCampSkipBombPlanted { get; set; } = true;
    public int AntiCampSkipTeam { get; set; } = 3;
    public float Timer { get; set; } = 5.0f;
    [JsonPropertyName("ConfigVersion")]
    public override int Version { get; set; } = 2;
}

public class AFKManager : BasePlugin, IPluginConfig<AFKManagerConfig>
{
    #region definitions
    public override string ModuleAuthor => "NiGHT & K4ryuu (forked by Глеб Хлебов)";
    public override string ModuleName => "AFK Manager";
    public override string ModuleVersion => "0.4.0";

    public required AFKManagerConfig Config { get; set; } = new();
    private CCSGameRules? _gGameRulesProxy;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _checkTimer;
    
    public void OnConfigParsed(AFKManagerConfig config)
    {
        if(Config.Version != config.Version)
        {
            Console.WriteLine($"{ModuleName}: Config version mismatch. Update Config.");
        }

        Config = config;

        if(Config.Timer < 0.1f)
        {                 
            Config.Timer = 5.0f;
            Console.WriteLine($"{ModuleName}: Timer value is invalid, setting to default value (5.0).");
        }

        if (Config.AfkKickTime < 0)
            Config.AfkKickTime = 0;

        if (Config.AfkKickTime > 0 && Config.AfkMarkTime < Config.Timer)
        {
            Config.AfkMarkTime = Config.Timer;
            Console.WriteLine($"{ModuleName}: AfkMarkTime is less than Timer, AfkMarkTime will be forced to {Config.Timer}");
        }

        if (Config.AntiCampPunishment is < 0 or > 1)
        {
            Config.AntiCampPunishment = 1;
            Console.WriteLine($"{ModuleName}: AntiCampPunishment value is invalid, setting to default value (1).");
        }

        if (Config.AntiCampSlapDamage < 0)
            Config.AntiCampSlapDamage = 0;

        if (Config.AntiCampPunishTime < 0)
            Config.AntiCampPunishTime = 0;

        if (Config.AntiCampPunishTime > 0 && Config.AntiCampMarkTime < Config.Timer)
        {
            Config.AntiCampMarkTime = Config.Timer;
            Console.WriteLine($"{ModuleName}: AntiCampMarkTime is less than Timer, AntiCampMarkTime will be forced to {Config.Timer}");
        }

        if (Config.SpecKickTime < 0)
            Config.SpecKickTime = 0;

        if (Config.SpecKickTime > 0 && Config.SpecWarnInterval < Config.Timer)
        {
            Config.SpecWarnInterval = Config.Timer;
            Console.WriteLine($"{ModuleName}: SpecWarnInterval is less than Timer, SpecWarnInterval will be forced to {Config.Timer}");
        }
        
        StartCheckTimer();
    }

    public override void Unload(bool hotReload)
    {
        _checkTimer?.Kill();
        _checkTimer = null;
        _gPlayerInfo.Clear();
        _gGameRulesProxy = null;
    }

    private void StartCheckTimer()
    {
        _checkTimer?.Kill();
        _checkTimer = AddTimer(Config.Timer, AfkTimer_Callback, TimerFlags.REPEAT);
    }
    
    private class PlayerInfo
    {
        public QAngle? Angles { get; set; }
        public Vector? Origin { get; set; }
        public float AfkTime { get; set; }
        public bool AfkMarked { get; set; }
        public bool SpecMarked { get; set; }
        public float SpecAfkTime { get; set; }
        public bool MovedByPlugin { get; set; }
        public float AntiCampTime { get; set; }
        public bool AntiCampMarked { get; set; }
        public Vector? AntiCampAnchor { get; set; }
    }
    
    private readonly Dictionary<uint, PlayerInfo> _gPlayerInfo = new();
    #endregion

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            Server.NextFrame(RefreshGameRules);
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
        Server.NextFrame(() =>
        {
            RefreshRuntimeState();
            StartCheckTimer();
        });

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
        data.SpecMarked = false;
        data.AfkTime = 0;
        data.AfkMarked = false;
        
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
        value.SpecMarked = false;
        value.AfkTime = 0;
        value.AfkMarked = false;
                
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
            data.SpecMarked = false;
            data.MovedByPlugin = false;
            data.AfkTime = 0;
            data.AfkMarked = false;
            data.AntiCampMarked = false;
            data.AntiCampTime = 0;
            data.AntiCampAnchor = null;
        }, TimerFlags.STOP_ON_MAPCHANGE);
            
        return HookResult.Continue;
    }
    
    private void AfkTimer_Callback()
    {
        if (_gGameRulesProxy == null || _gGameRulesProxy.FreezePeriod || (Config.SkipWarmup && _gGameRulesProxy.WarmupPeriod))
            return;

        var players = Utilities.GetPlayers().Where(x => x is { IsBot: false, Connected: PlayerConnectedState.Connected }).ToList();
        var playersCount = players.Count;
        
        foreach (var player in players)
        {
            if (player.ControllingBot)
                continue;

            if (!_gPlayerInfo.TryGetValue(player.Index, out var data))
            {
                _gPlayerInfo[player.Index] = CapturePlayerInfo(player);
                continue;
            }
            
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
                var skipAfk = HasSkipFlag(player, Config.AfkSkipFlag);
                var stationary = data.Angles.X == angles.X && data.Angles.Y == angles.Y
                                 && data.Origin.X == origin.X && data.Origin.Y == origin.Y;
                var trackingAfk = Config.AfkKickTime > 0 && !skipAfk && stationary;

                if (trackingAfk)
                {
                    data.AfkTime += Config.Timer;

                    if (!data.AfkMarked && data.AfkTime >= Config.AfkMarkTime)
                    {
                        data.AfkMarked = true;

                        if (Config.AfkTransferC4AfterWarnings != 0 && player.TeamNum == 2 && HasC4(player))
                        {
                            if (!Config.AfkTransferC4OnlyFromBuyZone || playerPawn.InBuyZone)
                            {
                                CCSPlayer_WeaponServices weaponService =
                                    new(player.PlayerPawn?.Value?.WeaponServices?.Handle ?? nint.Zero);
                                CBasePlayerWeapon weapon =
                                    new(weaponService.MyWeapons.FirstOrDefault(w =>
                                            w.IsValid && w.Value != null && w.Value.DesignerName == "weapon_c4")?.Value
                                        ?.Handle ?? nint.Zero);

                                if (weapon.IsValid)
                                {
                                    var nearestPlayer = FindNearestPlayer(player);
                                    if (nearestPlayer != null)
                                    {
                                        RemoveC4(weaponService, weapon);
                                        nearestPlayer.GiveNamedItem("weapon_c4");
                                        Server.PrintToChatAll(ReplaceVars(player,
                                            Localizer["ChatBombTransfer"].Value
                                                .Replace("{targetPlayerName}", nearestPlayer.PlayerName)));
                                    }
                                }
                            }
                        }

                        player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningKickMessage"].Value, Config.AfkMarkTime + Config.AfkKickTime - data.AfkTime));

                        if (!string.IsNullOrEmpty(Config.PlaySoundName))
                            player.ExecuteClientCommand($"play {Config.PlaySoundName}");
                    }
                    else if (data.AfkMarked && data.AfkTime >= Config.AfkMarkTime + Config.AfkKickTime)
                    {
                        Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKickMessage"].Value));
                        Server.ExecuteCommand($"kickid {player.UserId}");

                        data.AfkMarked = false;
                        data.AfkTime = 0;
                        continue;
                    }
                }
                else
                {
                    data.AfkTime = 0;
                    data.AfkMarked = false;
                }
                /*  ------------------------------------------->  <-------------------------------------------  */
                if (trackingAfk)
                {
                    data.AntiCampMarked = false;
                    data.AntiCampTime = 0;
                    data.AntiCampAnchor = null;
                }
                else if (Config.AntiCampPunishTime > 0
                         && !(Config.AntiCampSkipBombPlanted && _gGameRulesProxy.BombPlanted)
                         && !HasSkipFlag(player, Config.AntiCampSkipFlag)
                         && player.TeamNum != Config.AntiCampSkipTeam)
                {
                    if (data.AntiCampAnchor == null && data.Origin != null)
                        data.AntiCampAnchor = new Vector(data.Origin.X, data.Origin.Y, data.Origin.Z);

                    if (data.AntiCampAnchor != null && origin != null && CalculateDistance(data.AntiCampAnchor, origin) < Config.AntiCampRadius)
                    {
                        data.AntiCampTime += Config.Timer;

                        if (!data.AntiCampMarked && data.AntiCampTime >= Config.AntiCampMarkTime)
                        {
                            data.AntiCampMarked = true;
                            var warningKey = Config.AntiCampPunishment == 0
                                ? "AntiCampSlayWarningMessage"
                                : "AntiCampSlapWarningMessage";
                            player.PrintToChat(ReplaceVars(player, Localizer[warningKey].Value, Config.AntiCampMarkTime + Config.AntiCampPunishTime - data.AntiCampTime));

                            if (!string.IsNullOrEmpty(Config.PlaySoundName))
                                player.ExecuteClientCommand($"play {Config.PlaySoundName}");
                        }
                        else if (data.AntiCampMarked && data.AntiCampTime >= Config.AntiCampMarkTime + Config.AntiCampPunishTime)
                        {
                            if (Config.AntiCampPunishment == 0)
                            {
                                if (Config.AntiCampPunishmentAnnounce)
                                    Server.PrintToChatAll(ReplaceVars(player, Localizer["AntiCampSlayMessage"].Value));

                                playerPawn?.CommitSuicide(false, true);
                            }
                            else
                            {
                                if (Config.AntiCampPunishmentAnnounce)
                                    Server.PrintToChatAll(ReplaceVars(player, Localizer["AntiCampSlapMessage"].Value));

                                Slap(playerPawn, Config.AntiCampSlapDamage);
                            }

                            data.AntiCampMarked = false;
                            data.AntiCampTime = 0;
                            data.AntiCampAnchor = null;
                            continue;
                        }
                    }
                    else
                    {
                        data.AntiCampMarked = false;
                        data.AntiCampTime = 0;
                        data.AntiCampAnchor = null;
                    }
                }
                
                data.Angles = new QAngle(angles?.X, angles?.Y, angles?.Z);
                data.Origin = new Vector(origin?.X, origin?.Y, origin?.Z);
                
                continue;
            }
            
            #endregion
            #region SPEC Time

            if (Config.SpecKickTime > 0
                && player.TeamNum == 1
                && playersCount >= Config.SpecKickMinPlayers)
            {
                if((Config.SpecKickOnlyMovedByPlugin && !data.MovedByPlugin) || HasSkipFlag(player, Config.SpecSkipFlag))
                    continue;
                
                data.SpecAfkTime += Config.Timer;

                if (!data.SpecMarked && data.SpecAfkTime >= Config.SpecWarnInterval)
                {
                    data.SpecMarked = true;
                    player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningKickMessage"].Value, Config.SpecWarnInterval + Config.SpecKickTime - data.SpecAfkTime));
                }
                else if (data.SpecMarked && data.SpecAfkTime >= Config.SpecWarnInterval + Config.SpecKickTime)
                {
                    Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKickMessage"].Value));
                    Server.ExecuteCommand($"kickid {player.UserId}");

                    data.SpecMarked = false;
                    data.SpecAfkTime = 0;
                }
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
                      .Replace("{timeAmount}", $"{Math.Ceiling(timeAmount):0}")
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
            if (target.Index == player.Index || outData.AfkMarked)
                continue;
            
            var distance = CalculateDistance(player.PlayerPawn?.Value?.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(), target.PlayerPawn?.Value?.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector());
            if (nearestPlayer != null && !(distance < firstDistance))
                continue;
            
            nearestPlayer = target;
            firstDistance = distance;
        }
        return nearestPlayer;
    }
            
    // True if the player has any one of the entries: "@domain/flag" is a permission flag, "#domain/group" an admin group.
    private static bool HasSkipFlag(CCSPlayerController player, List<string> entries)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            var value = entry.Trim();
            var match = value[0] == '#'
                ? AdminManager.PlayerInGroup(player, value)
                : AdminManager.PlayerHasPermissions(player, value);

            if (match)
                return true;
        }

        return false;
    }

    private static bool HasC4(CCSPlayerController player)
    {
        var weaponServices = player.PlayerPawn?.Value?.WeaponServices;
        if (weaponServices == null)
            return false;
        
        var matchedWeapon = weaponServices.MyWeapons.FirstOrDefault(w => w.IsValid && w.Value != null && w.Value.DesignerName == "weapon_c4");
        return matchedWeapon?.IsValid == true;
    }

    private void RefreshRuntimeState()
    {
        _gPlayerInfo.Clear();
        RefreshGameRules();

        foreach (var player in Utilities.GetPlayers())
        {
            if (player is not { IsBot: false, IsValid: true, Connected: PlayerConnectedState.Connected })
                continue;

            _gPlayerInfo[player.Index] = CapturePlayerInfo(player);
        }
    }

    private void RefreshGameRules()
    {
        _gGameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()?.GameRules;
    }

    private static PlayerInfo CapturePlayerInfo(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        var angles = pawn?.EyeAngles;
        var origin = pawn?.CBodyComponent?.SceneNode?.AbsOrigin;

        return new PlayerInfo
        {
            Angles = new QAngle(angles?.X, angles?.Y, angles?.Z),
            Origin = new Vector(origin?.X, origin?.Y, origin?.Z)
        };
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