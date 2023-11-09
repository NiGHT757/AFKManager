# [CS2] AFK Manager
A simple AFK Manager plugin for CS2.

This is not a fully tested version!

# Features:
Config File with Settings:
  - Chat Prefix (Prefix in Chat)
  - Warnings (How Many Warnings Should Be Issued Before Moving Player to Spectator)
  - Punishment options (0 - kill | 1 - kill + move to spectator | 2 - kick)
  - All messages can be configured through the configuration file.
  - Whitelist players (only in steamid64 format) to skip them during AFK verification.
  - Timer (Adjust the timer interval)
  - SpecWarnPlayerEveryXSeconds ( issue a warning every x seconds )
  - SpecKickPlayerAfterXWarnings ( kick player after x warnings issued )
  - Offset (This Is the "CCSPlayerController_ChangeTeam" Offset)

# Requirements:
[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) 

# Credits:
[K4ryuu](https://github.com/K4ryuu) - Helping me with config file and other things.

xstage - Helping with the "changeTeam" function in order to properly move players to SPEC.
