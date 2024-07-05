# [CS2] AFK Manager
[![Downloads](https://img.shields.io/github/downloads/NiGHT757/AFKManager/total.svg)](https://github.com/NiGHT757/AFKManager/releases)
[![License](https://img.shields.io/github/license/NiGHT757/AFKManager.svg)](https://github.com/NiGHT757/AFKManager/blob/main/LICENSE)

A simple AFK Manager plugin for CS2 based on [player-checker by sazonische from CS:GO](https://github.com/sazonische/player-checker/blob/master/addons/sourcemod/scripting/player_checker.sp)

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
[K4ryuu](https://github.com/K4ryuu) - Helping with config file and other things before CSS's config implementation.

xstage - Helping with the "changeTeam" function before CSS version ~90 in order to properly move players to SPEC.

sazonische - [player-checker](https://github.com/sazonische/player-checker/blob/master/addons/sourcemod/scripting/player_checker.sp)

B3none - Plugin ideas.
