using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using Terraria;
using TShockAPI;

namespace CTG
{
    public class Player
    {
        public string PvPType = "";
        public int Index;
        public TSPlayer TSPlayer { get { return TShock.Players[Index]; } }
        public string PlayerName { get { return Main.player[Index].name; } }
        public Vector2 spawn { get { return getSpawn(); } }
        public int team;
        public bool Dead;
        public Player killingPlayer;
        public Timer respawn;


        public Player(int index)
        {
            Index = index;
        }

        public Vector2 getSpawn()
        {
            if (team == 3) return CTG.bluespawn;
            return CTG.redspawn;
        }

        public void PlayerRespawned(object sender, ElapsedEventArgs args)
        {
            var player = CTG.Tools.GetPlayerByIndex(Index);
            player.respawn.Enabled = false;
            player.respawn.Dispose();
            player.Dead = false;
        }
    }
}
