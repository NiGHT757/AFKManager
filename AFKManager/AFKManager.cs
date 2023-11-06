using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using System.Threading;

namespace AFKManager;
public class AFKManager : BasePlugin
{
    public override string ModuleAuthor => "NiGHT & K4ryuu";
    public override string ModuleName => "AFK Manager";
    public override string ModuleVersion => "0.0.2";
    public static string Directory = string.Empty;
    public static string[] sPunishment = { $"{ChatColors.Grey}killed{ChatColors.Default}", $"a{ChatColors.Grey} SPECTATOR{ChatColors.Default}", $"{ChatColors.Grey}kicked{ChatColors.Default}" };
    private CCSGameRules? g_GameRulesProxy = null;

    // create a class that store player AbsOrigin
    private class PlayerAbsOrigin
    {
        public QAngle Angles { get; set; }
        public Vector Origin { get; set; }
        public int WarningCount { get; set; }
        public CCSPlayerController BotController { get; set; }
    }
    private PlayerAbsOrigin[] g_PlayerAbsOrigin = new PlayerAbsOrigin[Server.MaxPlayers];
    public override void Load(bool hotReload)
    {
        Directory = ModuleDirectory;
        new CFG().CheckConfig(ModuleDirectory);

        if (CFG.config.Punishment < 0 || CFG.config.Punishment > 2)
        {
            CFG.config.Punishment = 1;
            Console.WriteLine("AFK Manager: Punishment value is invalid, setting to default value (1).");
        }

        AddTimer(5.0f, AfkTimer_Callback, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);

        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            g_GameRulesProxy = null;
        });
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

        for (int i = 0; i < Server.MaxPlayers; i++)
        {
            CCSPlayerController player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(i));
            if (!player.IsValid || player.IsBot || player.ControllingBot || !player.PawnIsAlive || player.TeamNum < 2 || player.TeamNum > 3)
            {
                continue;
            }
      
            var playerPawn = player.PlayerPawn.Value;
            var playerFlags = player.Pawn.Value.Flags;

            if((playerFlags & ((uint)PlayerFlags.FL_ONGROUND | (uint)PlayerFlags.FL_FROZEN)) != (uint)PlayerFlags.FL_ONGROUND)
            {
                continue;
            }

            QAngle angles = playerPawn.EyeAngles;
            Vector origin = player.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;

            if (g_PlayerAbsOrigin[i] == null)
            {
                g_PlayerAbsOrigin[i] = new PlayerAbsOrigin
                {
                    Angles = new QAngle(),
                    Origin = new Vector(),
                    WarningCount = 0
                };
                continue;
            }

            if (g_PlayerAbsOrigin[i].Angles.X == angles.X && g_PlayerAbsOrigin[i].Angles.Y == angles.Y &&
                    g_PlayerAbsOrigin[i].Origin.X == origin.X && g_PlayerAbsOrigin[i].Origin.Y == origin.Y)
            {
                if (g_PlayerAbsOrigin[i].WarningCount == CFG.config.Warnings)
                {
                    switch(CFG.config.Punishment)
                    {
                        case 0:
                            playerPawn.CommitSuicide(false, true);
                            Server.PrintToChatAll($"{CFG.config.ChatPrefix} {player.PlayerName} was killed for being AFK.");
                            break;
                        case 1:
                            playerPawn.CommitSuicide(false, true);
                            var changeTeam = VirtualFunction.CreateVoid<IntPtr, CsTeam>(player.Handle, CFG.config.Offset);
                            changeTeam(player.Handle, CsTeam.Spectator);
                            Server.PrintToChatAll($"{CFG.config.ChatPrefix} {player.PlayerName} was moved to SPEC being AFK.");
                            break;
                        case 2:
                            Server.ExecuteCommand($"kickid {player.UserId}");
                            Server.PrintToChatAll($"{CFG.config.ChatPrefix} {player.PlayerName} was kicked for being AFK.");
                            break;
                    }
                    continue;
                }

                player.PrintToChat($"{CFG.config.ChatPrefix} You're{ChatColors.LightRed} Idle/ AFK{ChatColors.Default}. Move or you'll be {sPunishment[CFG.config.Punishment]} in {ChatColors.Darkred}{(CFG.config.Warnings * 5) - (g_PlayerAbsOrigin[i].WarningCount * 5)}{ChatColors.Default} seconds.");
                g_PlayerAbsOrigin[i].WarningCount++;
            }
            else
            {
                g_PlayerAbsOrigin[i].WarningCount = 0;
            }

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
        }
    }
}