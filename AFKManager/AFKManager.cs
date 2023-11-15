using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;

namespace AFKManager;

[MinimumApiVersion(52)]
public class AFKManager : BasePlugin
{
    #region definitions
    public override string ModuleAuthor => "NiGHT & K4ryuu";
    public override string ModuleName => "AFK Manager";
    public override string ModuleVersion => "0.0.8";
    public static string Directory = string.Empty;
    private CCSGameRules? g_GameRulesProxy = null;
    
    private class playerInfo
    {
        public QAngle Angles { get; set; }
        public Vector Origin { get; set; }
        public int WarningCount { get; set; }
        public int SpecWarningCount { get; set; }
        public float SpecAfkTime { get; set; }
        public bool MovedByPlugin { get; set; }
    }
    
    private Dictionary<uint, playerInfo> g_PlayerInfo = new Dictionary<uint, playerInfo>();
    
    #endregion
    public override void Load(bool hotReload)
    {
        Directory = ModuleDirectory;
        new CFG().CheckConfig(ModuleDirectory);

        AddTimer(CFG.config.Timer, AfkTimer_Callback, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);

        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            g_GameRulesProxy = null;
        });
        
        #region OnClientConnected
        RegisterListener<Listeners.OnClientConnected>(playerSlot =>
        {
            uint finalSlot = (uint)playerSlot + 1;
            CCSPlayerController player = new CCSPlayerController(NativeAPI.GetEntityFromIndex((int)finalSlot));
            if(player == null || player.UserId < 0)
                return;

            if (!g_PlayerInfo.ContainsKey(finalSlot))
            {
                g_PlayerInfo.Add(finalSlot, new playerInfo
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
            var player = Utilities.GetPlayerFromSlot(playerSlot);
            if (!player.IsValid || player.IsBot)
                return;
            
            uint i = player.EntityIndex!.Value.Value;

            if (g_PlayerInfo.ContainsKey(i))
                g_PlayerInfo.Remove(i);
        });
        
        RegisterEventHandler<EventPlayerTeam>((@event, info) =>
        {
            var player = @event.Userid;
            if (!player.IsValid || player.IsBot)
                return HookResult.Continue;
            
            uint i = player.EntityIndex!.Value.Value;
            if (g_PlayerInfo.ContainsKey(i))
            {
                g_PlayerInfo[i].SpecAfkTime = 0;
                g_PlayerInfo[i].SpecWarningCount = 0;
                g_PlayerInfo[i].WarningCount = 0;
                
                if(@event.Team != 1)
                    g_PlayerInfo[i].MovedByPlugin = false;
            }
            return HookResult.Continue;
        });
        
        RegisterEventHandler<EventPlayerSpawned>((@event, info) =>
        {
            var player = @event.Userid;
            if(!player.IsValid || player.IsBot || !player.PawnIsAlive)
                return HookResult.Continue;

            AddTimer(0.2f, () =>
            {
                // wait 0.2 sec to get correct values (angles, origin)
                uint i = player.EntityIndex!.Value.Value;
                if(!g_PlayerInfo.ContainsKey(i))
                    return;
                
                var angles = player.PlayerPawn.Value.EyeAngles;
                var origin = player.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;
                
                g_PlayerInfo[i].Angles = new QAngle(
                    x: angles.X,
                    y: angles.Y,
                    z: angles.Z
                );
                
                g_PlayerInfo[i].Origin = new Vector(
                    x: origin.X,
                    y: origin.Y,
                    z: origin.Z
                );

                g_PlayerInfo[i].SpecAfkTime = 0;
                g_PlayerInfo[i].SpecWarningCount = 0;
                g_PlayerInfo[i].MovedByPlugin = false;
            });
            
            return HookResult.Continue;
        });
        #endregion
        #region hotReload
        if (hotReload)
        {
            AddTimer(1.0f, () =>
            {
                if(CFG.config == null)
                    return;
                
                var players = Utilities.GetPlayers().Where(x => !x.IsBot);

                foreach (var player in players)
                {
                    uint i = player.EntityIndex!.Value.Value;
                    
                    if (!g_PlayerInfo.ContainsKey(i))
                    {
                        g_PlayerInfo.Add(i, new playerInfo
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
    }
    
    private void AfkTimer_Callback()
    {
        if (g_GameRulesProxy == null)
        {
            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();

            g_GameRulesProxy = gameRulesProxy.GameRules!;

            if (g_GameRulesProxy == null)
                return;
        }

        if (g_GameRulesProxy.FreezePeriod)
            return;

        string sFormat = null; 
        
        var players = Utilities.GetPlayers().Where(x => !x.IsBot);
        var playersCount = players.Count();
        
        foreach (var player in players)
        {
            uint i = player.EntityIndex!.Value.Value;
            
            if (!g_PlayerInfo.ContainsKey(i) || player.ControllingBot)
                continue;
            
            #region AFK Time
            if (CFG.config.Warnings != 0 
                && player.PawnIsAlive 
                && (player.TeamNum == 2 || player.TeamNum == 3))
            {
                if(CFG.config.SkipFlag != string.Empty && AdminManager.PlayerHasPermissions(player, CFG.config.SkipFlag))
                    continue;
                
                var playerPawn = player.PlayerPawn.Value;
                var playerFlags = player.Pawn.Value.Flags;

                if ((playerFlags & ((uint)PlayerFlags.FL_ONGROUND | (uint)PlayerFlags.FL_FROZEN)) != (uint)PlayerFlags.FL_ONGROUND)
                    continue;

                QAngle angles = playerPawn.EyeAngles;
                Vector origin = player.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;
                
                if (g_PlayerInfo[i].Angles.X == angles.X && g_PlayerInfo[i].Angles.Y == angles.Y &&
                    g_PlayerInfo[i].Origin.X == origin.X && g_PlayerInfo[i].Origin.Y == origin.Y)
                {
                    if (g_PlayerInfo[i].WarningCount == CFG.config.Warnings)
                    {
                        g_PlayerInfo[i].WarningCount = 0;

                        switch (CFG.config.Punishment)
                        {
                            case 0:
                                sFormat = CFG.config.ChatKillMessage.Replace("{chatprefix}", CFG.config.ChatPrefix).Replace("{playername}", player.PlayerName);

                                playerPawn.CommitSuicide(false, true);
                                Server.PrintToChatAll(sFormat);
                                break;
                            case 1:
                                sFormat = CFG.config.ChatMoveMessage.Replace("{chatprefix}", CFG.config.ChatPrefix).Replace("{playername}", player.PlayerName);

                                playerPawn.CommitSuicide(false, true);
                                player.ChangeTeam(CsTeam.Spectator);

                                Server.PrintToChatAll(sFormat);
                                break;
                            case 2:
                                sFormat = CFG.config.ChatKickMessage.Replace("{chatprefix}", CFG.config.ChatPrefix).Replace("{playername}", player.PlayerName);

                                Server.ExecuteCommand($"kickid {player.UserId}");
                                Server.PrintToChatAll(sFormat);
                                break;
                        }

                        continue;
                    }

                    switch (CFG.config.Punishment)
                    {
                        case 0:
                            sFormat = CFG.config.ChatWarningKillMessage;
                            break;
                        case 1:
                            sFormat = CFG.config.ChatWarningMoveMessage;
                            break;
                        case 2:
                            sFormat = CFG.config.ChatWarningKickMessage;
                            break;
                    }
                    
                    sFormat = sFormat.Replace("{chatprefix}", CFG.config.ChatPrefix).Replace("{playername}", player.PlayerName).Replace("{time}", $"{(((float)CFG.config.Warnings * CFG.config.Timer) - ((float)g_PlayerInfo[i].WarningCount * CFG.config.Timer)):F1}");
                    
                    player.PrintToChat(sFormat);
                    g_PlayerInfo[i].WarningCount++;
                }
                else
                    g_PlayerInfo[i].WarningCount = 0;

                g_PlayerInfo[i].Angles = new QAngle(
                    x: angles.X,
                    y: angles.Y,
                    z: angles.Z
                );

                g_PlayerInfo[i].Origin = new Vector(
                    x: origin.X,
                    y: origin.Y,
                    z: origin.Z
                );

                continue;
            }
            #endregion
            #region SPEC Time
            
            if (CFG.config.SpecKickPlayerAfterXWarnings != 0 
                && player.TeamNum == 1 
                && playersCount >= CFG.config.SpecKickMinPlayers)
            {
                if (CFG.config.SpecKickOnlyMovedByPlugin && !g_PlayerInfo[i].MovedByPlugin)
                    continue;

                if (CFG.config.SpecSkipFlag != string.Empty && AdminManager.PlayerHasPermissions(player, CFG.config.SpecSkipFlag))
                    continue;
                
                g_PlayerInfo[i].SpecAfkTime += CFG.config.Timer;

                if (g_PlayerInfo[i].SpecAfkTime >= CFG.config.SpecWarnPlayerEveryXSeconds) // for example, warn player every 20 seconds ( add +1 warn every 20 sec )
                {
                    if (g_PlayerInfo[i].SpecWarningCount == CFG.config.SpecKickPlayerAfterXWarnings)
                    {
                        sFormat = CFG.config.ChatKickMessage.Replace("{chatprefix}", CFG.config.ChatPrefix).Replace("{playername}", player.PlayerName);
                        
                        Server.PrintToChatAll(sFormat);
                        Server.ExecuteCommand($"kickid {player.UserId}");

                        g_PlayerInfo[i].SpecWarningCount = 0;
                        g_PlayerInfo[i].SpecAfkTime = 0; // reset counter
                        continue;
                    }

                    sFormat = CFG.config.ChatWarningKickMessage.Replace("{chatprefix}", CFG.config.ChatPrefix)
                        .Replace("{time}", $"{ (((float)CFG.config.SpecKickPlayerAfterXWarnings * CFG.config.SpecWarnPlayerEveryXSeconds) - ((float)g_PlayerInfo[i].SpecWarningCount * CFG.config.SpecWarnPlayerEveryXSeconds)):F1}");
                    
                    player.PrintToChat(sFormat);
                    g_PlayerInfo[i].SpecWarningCount++;
                    g_PlayerInfo[i].SpecAfkTime = 0; // reset counter
                }
            }

            #endregion
        }
    }
}