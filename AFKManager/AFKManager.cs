using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using System.Net.Sockets;

namespace AFKManager;
public class AFKManager : BasePlugin
{
    #region definitions
    public override string ModuleAuthor => "NiGHT & K4ryuu";
    public override string ModuleName => "AFK Manager";
    public override string ModuleVersion => "0.0.6";
    public static string Directory = string.Empty;
    private CCSGameRules? g_GameRulesProxy = null;

    // create a class that store player AbsOrigin
    private class PlayerAbsOrigin
    {
        public QAngle Angles { get; set; }
        public Vector Origin { get; set; }
        public int WarningCount { get; set; }
        public int SpecWarningCount { get; set; }
        public Whitelist? Whitelisted { get; set; }
        public float fAfkTime { get; set; }
    }

    private PlayerAbsOrigin[] g_PlayerAbsOrigin = new PlayerAbsOrigin[Server.MaxPlayers + 1];
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
            int finalSlot = playerSlot + 1;
            CCSPlayerController player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(finalSlot));
            if(player == null || player.UserId < 0)
                return;

            if (g_PlayerAbsOrigin[finalSlot] == null)
            {
                g_PlayerAbsOrigin[finalSlot] = new PlayerAbsOrigin
                {
                    Angles = new QAngle(),
                    Origin = new Vector(),
                    Whitelisted = new Whitelist(),
                    SpecWarningCount = 0,
                    fAfkTime = 0,
                    WarningCount = 0
                };
            }

            if (CFG.config.WhiteListUsers.ContainsKey(player.SteamID))
            {
                g_PlayerAbsOrigin[finalSlot].Whitelisted = CFG.config.WhiteListUsers[player.SteamID];
            }
        });
        #endregion
        #region hotReload
        if (hotReload)
        {
            AddTimer(1.0f, () =>
            {
                if(CFG.config == null)
                    return;

                for (int i = 1; i <= Server.MaxPlayers; i++)
                {
                    CCSPlayerController player = Utilities.GetPlayerFromIndex(i);
                    if (!player.IsValid || player.IsBot || player.UserId < 0)
                        continue;

                    if (g_PlayerAbsOrigin[i] == null)
                    {
                        g_PlayerAbsOrigin[i] = new PlayerAbsOrigin
                        {
                            Angles = new QAngle(),
                            Origin = new Vector(),
                            Whitelisted = new Whitelist(),
                            SpecWarningCount = 0,
                            fAfkTime = 0,
                            WarningCount = 0
                        };
                    }
                    if(CFG.config.WhiteListUsers.ContainsKey(player.SteamID))
                    {
                        g_PlayerAbsOrigin[i].Whitelisted = CFG.config.WhiteListUsers[player.SteamID];
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

        if (g_GameRulesProxy.GamePaused || g_GameRulesProxy.FreezePeriod || g_GameRulesProxy.WarmupPeriod)
            return;

        string sFormat = null; 

        for (int i = 1; i <= Server.MaxPlayers; i++)
        {
            CCSPlayerController player = Utilities.GetPlayerFromIndex(i);
            if (g_PlayerAbsOrigin[i] == null || !player.IsValid || player.IsBot || player.ControllingBot)
                continue;

            #region AFK Time
            if (CFG.config.Warnings != 0 && !g_PlayerAbsOrigin[i].Whitelisted.SkipAFK && player.PawnIsAlive && (player.TeamNum == 2 || player.TeamNum == 3))
            {
                g_PlayerAbsOrigin[i].SpecWarningCount = 0;
                g_PlayerAbsOrigin[i].fAfkTime = 0;

                var playerPawn = player.PlayerPawn.Value;
                var playerFlags = player.Pawn.Value.Flags;

                if ((playerFlags & ((uint)PlayerFlags.FL_ONGROUND | (uint)PlayerFlags.FL_FROZEN)) != (uint)PlayerFlags.FL_ONGROUND)
                {
                    continue;
                }

                QAngle angles = playerPawn.EyeAngles;
                Vector origin = player.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;

                if (g_PlayerAbsOrigin[i].Angles.X == angles.X && g_PlayerAbsOrigin[i].Angles.Y == angles.Y &&
                        g_PlayerAbsOrigin[i].Origin.X == origin.X && g_PlayerAbsOrigin[i].Origin.Y == origin.Y)
                {
                    if (g_PlayerAbsOrigin[i].WarningCount == CFG.config.Warnings)
                    {
                        g_PlayerAbsOrigin[i].WarningCount = 0;

                        switch (CFG.config.Punishment)
                        {
                            case 0:
                                sFormat = CFG.config.ChatKillMessage;
                                sFormat = sFormat.Replace("{chatprefix}", CFG.config.ChatPrefix);
                                sFormat = sFormat.Replace("{playername}", player.PlayerName);

                                playerPawn.CommitSuicide(false, true);
                                Server.PrintToChatAll(sFormat);
                                break;
                            case 1:
                                sFormat = CFG.config.ChatMoveMessage;
                                sFormat = sFormat.Replace("{chatprefix}", CFG.config.ChatPrefix);
                                sFormat = sFormat.Replace("{playername}", player.PlayerName);

                                playerPawn.CommitSuicide(false, true);
                                var changeTeam = VirtualFunction.CreateVoid<IntPtr, CsTeam>(player.Handle, CFG.config.Offset);
                                changeTeam(player.Handle, CsTeam.Spectator);

                                Server.PrintToChatAll(sFormat);
                                break;
                            case 2:
                                sFormat = CFG.config.ChatKickMessage;
                                sFormat = sFormat.Replace("{chatprefix}", CFG.config.ChatPrefix);
                                sFormat = sFormat.Replace("{playername}", player.PlayerName);

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
                            sFormat = sFormat.Replace("{chatprefix}", CFG.config.ChatPrefix);
                            sFormat = sFormat.Replace("{playername}", player.PlayerName);
                            sFormat = sFormat.Replace("{time}", $"{(((float)CFG.config.Warnings * CFG.config.Timer) - ((float)g_PlayerAbsOrigin[i].WarningCount * CFG.config.Timer)):F1}");

                            player.PrintToChat(sFormat);
                            break;
                        case 1:
                            sFormat = CFG.config.ChatWarningMoveMessage;
                            sFormat = sFormat.Replace("{chatprefix}", CFG.config.ChatPrefix);
                            sFormat = sFormat.Replace("{playername}", player.PlayerName);
                            sFormat = sFormat.Replace("{time}", $"{(((float)CFG.config.Warnings * CFG.config.Timer) - ((float)g_PlayerAbsOrigin[i].WarningCount * CFG.config.Timer)):F1}");

                            player.PrintToChat(sFormat);
                            break;
                        case 2:
                            sFormat = CFG.config.ChatWarningKickMessage;
                            sFormat = sFormat.Replace("{chatprefix}", CFG.config.ChatPrefix);
                            sFormat = sFormat.Replace("{playername}", player.PlayerName);
                            sFormat = sFormat.Replace("{time}", $"{(((float)CFG.config.Warnings * CFG.config.Timer) - ((float)g_PlayerAbsOrigin[i].WarningCount * CFG.config.Timer)):F1}");

                            player.PrintToChat(sFormat);
                            break;
                    }
                    g_PlayerAbsOrigin[i].WarningCount++;
                }
                else
                    g_PlayerAbsOrigin[i].WarningCount = 0;

                g_PlayerAbsOrigin[i].Angles = new QAngle(
                    x: angles.X,
                    y: angles.Y,
                    z: angles.Z
                );

                g_PlayerAbsOrigin[i].Origin = new Vector(
                    x: origin.X,
                    y: origin.Y,
                    z: origin.Z
                );

                continue;
            }
            #endregion
            #region SPEC Time
            if (CFG.config.SpecKickPlayerAfterXWarnings != 0 && !g_PlayerAbsOrigin[i].Whitelisted.SkipSPEC && player.TeamNum == 1)
            {
                g_PlayerAbsOrigin[i].fAfkTime += CFG.config.Timer;

                if (g_PlayerAbsOrigin[i].fAfkTime >= CFG.config.SpecWarnPlayerEveryXSeconds) // for example, warn player every 20 seconds ( add +1 warn every 20 sec )
                {
                    if (g_PlayerAbsOrigin[i].SpecWarningCount == CFG.config.SpecKickPlayerAfterXWarnings)
                    {
                        sFormat = CFG.config.ChatKickMessage;
                        sFormat = sFormat.Replace("{chatprefix}", CFG.config.ChatPrefix);
                        sFormat = sFormat.Replace("{playername}", player.PlayerName);
                        Server.PrintToChatAll(sFormat);

                        Server.ExecuteCommand($"kickid {player.UserId}");

                        g_PlayerAbsOrigin[i].SpecWarningCount = 0;
                        g_PlayerAbsOrigin[i].fAfkTime = 0; // reset counter
                        continue;
                    }

                    sFormat = CFG.config.ChatWarningKickMessage;
                    sFormat = sFormat.Replace("{chatprefix}", CFG.config.ChatPrefix);
                    sFormat = sFormat.Replace("{time}", $"{ (((float)CFG.config.SpecKickPlayerAfterXWarnings * CFG.config.SpecWarnPlayerEveryXSeconds) - ((float)g_PlayerAbsOrigin[i].SpecWarningCount * CFG.config.SpecWarnPlayerEveryXSeconds)):F1}");
                    player.PrintToChat(sFormat);
                    g_PlayerAbsOrigin[i].SpecWarningCount++;
                    g_PlayerAbsOrigin[i].fAfkTime = 0; // reset counter
                }
            }

            #endregion
        }
    }
}