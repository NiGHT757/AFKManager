# [CS2] AFK Manager

[![Downloads](https://img.shields.io/github/downloads/NiGHT757/AFKManager/total.svg)](https://github.com/NiGHT757/AFKManager/releases)
[![License](https://img.shields.io/github/license/NiGHT757/AFKManager.svg)](https://github.com/NiGHT757/AFKManager/blob/main/LICENSE)

AFK Manager plugin for CS2 based on [player-checker by sazonische from CS:GO](https://github.com/sazonische/player-checker/blob/master/addons/sourcemod/scripting/player_checker.sp)

# Features:

Config File located in **/addons/counterstrikesharp/configs/plugins/AFKManager** with Settings:

- AfkMarkTime: Seconds without moving before the player is marked AFK and warned.
- AfkKickTime: Seconds after the AFK warning before the player is kicked (set to 0 to disable AFK).
- AfkTransferC4AfterWarnings: Transfer the C4 when the player is marked AFK (0 = disabled).
- AfkTransferC4OnlyFromBuyZone: C4 can only be transferred if the AFK player is in the Buy Zone. (default = true)
- SpecWarnInterval: Seconds in spectator before the player is warned.
- SpecKickTime: Seconds after the spectator warning before the player is kicked (set to 0 to disable).
- SpecKickMinPlayers: Minimum number of players required to kick.
- SpecKickOnlyMovedByPlugin: Only check players in spectator mode who were moved by AFK Manager.
- SpecSkipFlag: Skip players in spectator mode who have any of these flags (`@css/ban`) or groups (`#css/vip`) during AFK verification.
- AfkSkipFlag: Skip players who have any of these flags (`@css/ban`) or groups (`#css/vip`) during AFK verification.
- AntiCampSkipFlag: Skip players who have any of these flags (`@css/ban`) or groups (`#css/vip`) during AntiCamp verification.
- PlaySoundName: Play a sound after a warning is issued (leave empty to disable).
- SkipWarmup: Skip checks during warmup.
- AntiCampRadius: Distance check in units.
- AntiCampPunishment: Punishment for camping (0 - slay, 1 - slap).
- AntiCampSlapDamage: Damage dealt by slap (set to 0 for no damage).
- AntiCampMarkTime: Seconds inside the radius before the player is marked as camping and warned.
- AntiCampPunishTime: Seconds after the camping warning before slay or slap (set to 0 to disable AntiCamp).
- AntiCampPunishmentAnnounce: Announce the AntiCamp punishment to the whole server (set to false to disable).
- AntiCampSkipBombPlanted: Skip camping checks if the bomb is planted (set to true to enable).
- AntiCampSkipTeam: Skip camping checks for a specific team (2 - Terrorists, 3 - Counter-Terrorists).
- Timer: Adjust the timer for player checks.

# Requirements:

[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)

# Credits:

[K4ryuu](https://github.com/K4ryuu) - Helping with config file and other things before CSS's config implementation.

xstage - Helping with the "changeTeam" function before CSS version ~90 in order to properly move players to SPEC.

sazonische - [player-checker](https://github.com/sazonische/player-checker/blob/master/addons/sourcemod/scripting/player_checker.sp)

B3none - Plugin ideas.
