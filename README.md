Capture-The-Gem
===============

A plugin for tShock that aids CTG

Permissions: ctg.admin
 
Commands: /join (or /j), /spawnset (or /ss), /borderset (or /bs), /match (or /m), /p, /addtime, /lockteams
 
This plugin is intended for use on a server wherein all players will participate or observe. Players can join a team by using "/join (color)". While the match is not running, all players will be unable to enable pvp. When set to a team, players will also be unable to manually switch teams and when the match is running, players will not be able to switch teams at all if "/lockteam" is set to lock the teams (disabling the use of /j).

The border set command is intended for admins to set the center point wherein players may not cross until the time set in the config has passed. This is the initial "prep phase".

The spawn set command is intended to set the spawns for each team. This means that players do not need to set bed spawns and when the match is started, all players are teleported to their appropriate spawns.

A match can be paused using "/match pause" as well as unpaused by using the same command. Pausing a match will freeze players and block npc updates from being sent to the client so that they do not get hurt (thanks to MarioE's buildmode plugin of which I used some of the code from).

The chat is also modified. Players normal chat will be sent only to their team and must use "/p" to chat to everyone. This makes chatting to your team much easier.

Future Plans:
 Automatic Team Assignment
 Automatic detection when the gem is taken from a chest and when both gems are in the team's own chests
