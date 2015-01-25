using System;
using System.Collections.Generic;
using Terraria;
using System.Linq;
using System.IO;
using TShockAPI;
using TerrariaApi.Server;
using System.IO.Streams;

namespace CTG
{
    internal delegate bool GetDataHandlerDelegate(GetDataHandlerArgs args);
    internal class GetDataHandlerArgs : EventArgs
    {
        public TSPlayer Player { get; private set; }
        public MemoryStream Data { get; private set; }

        public GetDataHandlerArgs(TSPlayer player, MemoryStream data)
        {
            Player = player;
            Data = data;
        }
    }
    internal static class GetDataHandlers
    {
        private static Dictionary<PacketTypes, GetDataHandlerDelegate> _getDataHandlerDelegates;

        public static void InitGetDataHandler()
        {
            _getDataHandlerDelegates = new Dictionary<PacketTypes, GetDataHandlerDelegate>
            {
                {PacketTypes.PlayerKillMe, HandlePlayerKillMe},             
                {PacketTypes.PlayerDamage, HandlePlayerDamage},
            };
        }

        public static bool HandlerGetData(PacketTypes type, TSPlayer player, MemoryStream data)
        {
            GetDataHandlerDelegate handler;
            if (_getDataHandlerDelegates.TryGetValue(type, out handler))
            {
                try
                {
                    return handler(new GetDataHandlerArgs(player, data));
                }
                catch (Exception ex)
                {
                    Log.Error(ex.ToString());
                }
            }
            return false;
        }

        private static bool HandlePlayerKillMe(GetDataHandlerArgs args)
        {
            var index = args.Player.Index;
            var playerId = (byte)args.Data.ReadByte();
            var hitDirection = (byte)args.Data.ReadByte();
            var damage = args.Data.ReadInt16();
            var pvp = args.Data.ReadBoolean();
            var player = CTG.Tools.GetPlayerByIndex(playerId);
            var text = args.Data.ReadString();
            var tPlayer = TShock.Players[player.Index];

            tPlayer.Dead = false;
            tPlayer.Teleport(player.spawn.X, player.spawn.Y);
            if (pvp)
            {
                var messages = new string[] { " was slain by ", " was murdered by ", " was brutally bashed by ", " was royally smashed by " };
                Random rnd = new Random();
                foreach(var ply in CTG.CTGplayer)
                {
                    if (ply.team == player.team)
                        ply.TSPlayer.SendMessage(player.PlayerName + messages[rnd.Next(0, 5)] + player.killingPlayer.PlayerName, Color.Magenta);
                    else
                        ply.TSPlayer.SendMessage(player.PlayerName + messages[rnd.Next(0, 5)] + player.killingPlayer.PlayerName, Color.Indigo);
                }

                //tPlayer.SendData(PacketTypes.PlayerSpawnSelf, "", player.Index);
                return true;
            }

            return false;
        }

        private static bool HandlePlayerDamage(GetDataHandlerArgs args)
        {
            var index = args.Player.Index;
            var playerId = (byte)args.Data.ReadByte();
            var hitDirection = (byte)args.Data.ReadByte();
            var damage = args.Data.ReadInt16();
            var text = args.Data.ReadString();
            var player = CTG.Tools.GetPlayerByIndex(playerId);
            var pvp = args.Data.ReadBoolean();
            var crit = args.Data.ReadBoolean();
            var ply = Main.player[playerId];
            var hitDamage = Main.CalculateDamage(damage, ply.statDefense);

            if (index != playerId)
            {
                hitDamage = hitDamage > ply.statLife ? ply.statLife : hitDamage;
                player.killingPlayer = CTG.Tools.GetPlayerByIndex(index);
            }
            else
            {
                player.killingPlayer = null;
            }

            return false;
        }
    }
}