# [CS2] AFK Manager
A simple AFK Manager plugin for CS2.
This is not a fully tested version!

# Features:
Config File located in **/addons/counterstrikesharp/configs/plugins/AFKManager** with Settings:
  - Chat Prefix (Prefix in Chat)
  - Warnings (How Many Warnings Should Be Issued Before Moving Player to Spectator, 0 - to disable)
  - Punishment options (0 - kill | 1 - kill + move to spectator | 2 - kick)
  - All messages can be configured through the configuration file.
  - Timer (Adjust the timer interval)
  - SpecWarnPlayerEveryXSeconds ( issue a warning every x seconds )
  - SpecKickPlayerAfterXWarnings ( kick player after x warnings issued, 0 - to disable )
  - SpecKickMinPlayers (minimum number of players to kick).
  - SkipFlag ( Skip players with that flag during AFK verification )
  - SpecSkipFlag ( Skip SPEC players with that flag during AFK verification )
  - SpecKickOnlyMovedByPlugin ( Only check SPEC players that were moved by AFK Manager )
  - Retake ( Only check CT players )

# Requirements:
[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) v110 or higher

# Credits:
[K4ryuu](https://github.com/K4ryuu) - Helping me with config file and other things.

xstage - Helping with the "changeTeam" function in order to properly move players to SPEC.
