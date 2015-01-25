using System;
using System.Collections.Generic;
using System.Reflection;
using System.Timers;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace CTG
{
    [ApiVersion(1, 16)]
    public class CTG : TerrariaPlugin
    {
        public static readonly List<Player> CTGplayer = new List<Player>();
        private static readonly List<string> TeamColors = new List<string> { "white", "red", "green", "blue", "yellow" };
        private static Timer initial;
        private static CTGConfig Config { get; set; }
        public static Vector2 bluespawn;
        public static Vector2 redspawn;
        public static Vector2 border;

        public static bool match;
        public static bool pause;
        public static bool PrepPhase;
        public static bool teamLock;

        public static DateTime LastCheck = DateTime.UtcNow;
        public static int timerElapsed;

        public override Version Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }
        public override string Author
        {
            get { return "popstarfreas"; }
        }
        public override string Name
        {
            get { return "CTG"; }
        }

        public override string Description
        {
            get { return "Allows you to create controlled CTG matches"; }
        }

        public override void Initialize()
        {
            ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
            ServerApi.Hooks.NetSendBytes.Register(this, OnSendBytes);
            ServerApi.Hooks.NetGetData.Register(this, GetData);
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            ServerApi.Hooks.ServerChat.Register(this, OnChat);
            GetDataHandlers.InitGetDataHandler();

            Config = new CTGConfig();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
                ServerApi.Hooks.NetSendBytes.Deregister(this, OnSendBytes);
                ServerApi.Hooks.NetGetData.Deregister(this, GetData);
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
            }
            base.Dispose(disposing);
        }

        public CTG(Main game)
            : base(game)
        {
            Order = 1;
        }

        private static void OnInitialize(EventArgs e)
        {
            Commands.ChatCommands.Add(new Command("", Join, "join", "j"));
            Commands.ChatCommands.Add(new Command("ctg.admin", SpawnSet, "spawnset", "ss"));
            Commands.ChatCommands.Add(new Command("ctg.admin", BorderSet, "borderset", "bs"));
            Commands.ChatCommands.Add(new Command("ctg.admin", Match, "match", "m"));
            Commands.ChatCommands.Add(new Command("ctg.admin", reloadConfig, "ctgreload", "ctgr"));
            Commands.ChatCommands.Add(new Command("ctg.admin", lockTeams, "lockteams", ""));
            Commands.ChatCommands.Add(new Command("ctg.admin", addTime, "addtime", ""));
            teamLock = false;

            SetUpConfig();
        }

        #region GreetLeave

        private static void OnGreetPlayer(GreetPlayerEventArgs args)
        {
            // Possible to set Medium Core with SSC?
            // such as: TSPlayer.Players[player.Index].TPlayer.difficulty?
            lock (CTGplayer)
            {
                CTGplayer.Add(new Player(args.Who));
                Tools.GetPlayerByIndex(args.Who).team = 1;
                TShock.Players[args.Who].SetTeam(1);
            }
        }

        private static void OnLeave(LeaveEventArgs args)
        {
            lock (CTGplayer)
                CTGplayer.RemoveAll(plr => plr.Index == args.Who);
        }

        #endregion

        private void OnChat(ServerChatEventArgs args)
        {
            if (args.Text.StartsWith("/"))
            {

                if (args.Text.Substring(0, 1) == "/" && args.Text.Length < 2)
                {
                    args.Handled = true;
                    return;
                }

                if (args.Text.Substring(0, 2) == "/p")
                {
                    TSPlayer.All.SendMessage("(Public) <" + Tools.GetPlayerByIndex(args.Who).PlayerName + ">: " + args.Text.Substring(3), Color.White);
                    Console.WriteLine("(Public) <" + Tools.GetPlayerByIndex(args.Who).PlayerName + ">: " + args.Text.Substring(3), Color.White);
                    args.Handled = true;
                    return;
                }
            }
            else
            {
                var team = Tools.GetPlayerByIndex(args.Who).team;
                var teamName = team == 1 ? "Red Team" : "Blue Team";
                var color = team == 3 ? Color.LightBlue : Color.MediumVioletRed;
                foreach (var player in CTGplayer)
                {
                    if (player.team == team && player.Index != args.Who)
                    {
                        TShock.Players[player.Index].SendMessage("(" + teamName + ") <" + Tools.GetPlayerByIndex(args.Who).PlayerName + ">: " + args.Text, color);
                    }
                }
                TShock.Players[args.Who].SendMessage("(" + teamName + ") <" + Tools.GetPlayerByIndex(args.Who).PlayerName + ">: " + args.Text, color);
                Console.WriteLine("(" + teamName + ") <" + Tools.GetPlayerByIndex(args.Who).PlayerName + ">: " + args.Text, color);
                args.Handled = true;
                return;
            }
        }

        void OnSendBytes(SendBytesEventArgs e)
        {
            bool build = (pause || !match); // (pause || !match)
            switch (e.Buffer[2])
            {
                case 23:
                    NPC npc = Main.npc[BitConverter.ToInt16(e.Buffer, 3)];
                    if (!npc.friendly)
                    {
                        Buffer.BlockCopy(BitConverter.GetBytes(build ? 0f : npc.position.X), 0, e.Buffer, 5, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(build ? 0f : npc.position.Y), 0, e.Buffer, 9, 4);
                    }
                    break;
                case 27:
                    short id = BitConverter.ToInt16(e.Buffer, 3);
                    int owner = e.Buffer[27];
                    Projectile proj = Main.projectile[TShock.Utils.SearchProjectile(id, owner)];
                    if (!proj.friendly)
                        Buffer.BlockCopy(BitConverter.GetBytes((short)(build ? 0 : proj.type)), 0, e.Buffer, 28, 2);
                    break;
            }
        }

        #region OnUpdate

        private static void OnUpdate(EventArgs e)
        {

            // This check will make sure it doesn't run too often as it is not needed
            if ((DateTime.UtcNow - LastCheck).TotalMilliseconds >= 50)
            {
                LastCheck = DateTime.UtcNow;

                lock (CTGplayer)
                {
                    foreach (var player in CTGplayer)
                    {
                        // For the beginning of the game, players should not be able to do anything;
                        // hence the disable.
                        if (pause)
                            TShock.Players[player.Index].Disable();
                        else if (!match && !TShock.Players[player.Index].Group.HasPermission("ctg.admin"))
                            TShock.Players[player.Index].Disable();

                        // Border Checks to force players to keep in their own area until PrepPhase is over
                        if (player.Dead)
                        {
                            player.Dead = false;
                            player.TSPlayer.Teleport(player.spawn.X, player.spawn.Y);
                        }
                        else
                        {
                            if (player.team == 1 && PrepPhase)
                            {
                                if (border.X > redspawn.X)
                                {
                                    if (Main.player[player.Index].position.X > border.X - 16)
                                    {
                                        TShock.Players[player.Index].Teleport(border.X - 32, border.Y);
                                    }
                                }
                                else
                                {
                                    if (Main.player[player.Index].position.X < border.X + 16)
                                    {
                                        TShock.Players[player.Index].Teleport(border.X + 32, border.Y);
                                    }
                                }
                            }
                            if (player.team == 3 && PrepPhase)
                            {
                                if (border.X > bluespawn.X)
                                {
                                    if (Main.player[player.Index].position.X > border.X - 16)
                                    {
                                        TShock.Players[player.Index].Teleport(border.X - 32, border.Y);
                                    }
                                }
                                else
                                {
                                    if (Main.player[player.Index].position.X < border.X + 16)
                                    {
                                        TShock.Players[player.Index].Teleport(border.X + 32, border.Y);
                                    }
                                }
                            }
                        }

                        // If the player tries to switch teams manually
                        if (TShock.Players[player.Index].Team != player.team)
                        {
                            TShock.Players[player.Index].SetTeam(player.team);
                            NetMessage.SendData((int)PacketTypes.PlayerTeam, -1, -1, "", player.Index);
                            TShock.Players[player.Index].SendData(PacketTypes.PlayerTeam, "", player.Index);
                            NetMessage.SendData((int)PacketTypes.PlayerTeam, -1, -1, "", player.Index);
                        }

                        // If the player tries to disable pvp manually
                        if (Main.player[player.Index].hostile == false && match && !PrepPhase)
                        {
                            Main.player[player.Index].hostile = true;
                            NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, "", player.Index, 0f, 0f, 0f);
                        }
                        else if (Main.player[player.Index].hostile == true && !match) // enabling pvp manually while the match isn't running
                        {
                            Main.player[player.Index].hostile = false;
                            NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, "", player.Index, 0f, 0f, 0f);
                        }
                    }
                }
            }

            foreach (var deadPlayer in CTGplayer)
            {
                if (deadPlayer.TSPlayer.Dead)
                {
                    deadPlayer.TSPlayer.RespawnTimer = 0;
                    var player = Tools.GetPlayerByIndex(deadPlayer.Index);
                    Main.player[player.Index].dead = false;
                    deadPlayer.TSPlayer.Dead = false;
                    deadPlayer.TSPlayer.Spawn();
                    deadPlayer.Dead = true;
                }
            }
        }

        #endregion

        #region Config

        private static void SetUpConfig()
        {
            var configPath = Path.Combine(TShock.SavePath, "CTGtoggle.json");
            (Config = CTGConfig.Read(configPath)).Write(configPath);
        }

        private static void reloadConfig(CommandArgs args)
        {
            var configPath = Path.Combine(TShock.SavePath, "CTGtoggle.json");
            Config = CTGConfig.Read(configPath);
            args.Player.SendMessage("Config Reloaded!", Color.Green);
        }

        #endregion

        #region Commands

        private static void addTime(CommandArgs args)
        {
            if (!match)
            {
                args.Player.SendErrorMessage("The match needs to be running first.");
                return;
            }

            if (!PrepPhase)
            {
                args.Player.SendErrorMessage("Prep-Phase is already over!");
                return;
            }

            if (args.Parameters.Count != 1)
            {
                args.Player.SendErrorMessage("Incorrect syntax. Use /addtime [minutes].");
            }

            var minutes = Convert.ToInt32(args.Parameters[0]);
            timerElapsed = timerElapsed - minutes * 60;
            TSPlayer.All.SendMessage(Tools.GetPlayerByIndex(args.Player.Index).PlayerName + " extended the Prep-Phase by " + minutes + " minutes!", Color.Aqua);
        }

        private static void Join(CommandArgs args)
        {
            if ((match && teamLock) || (!match && teamLock))
            {
                args.Player.SendMessage("Teams are locked!", Color.Aqua);
                return;
            }

            if (args.Parameters.Count != 1)
            {
                args.Player.SendErrorMessage("Incorrect syntax. Use /join [red/blue]");
                return;
            }

            if (args.Parameters[0].ToLower() == "red" || args.Parameters[0].ToLower() == "r")
            {
                args.Player.SendSuccessMessage("You have joined the Red Team");
                args.Player.SetTeam(1);
                Tools.GetPlayerByIndex(args.Player.Index).team = 1;
                NetMessage.SendData((int)PacketTypes.PlayerTeam, -1, -1, "", args.Player.Index);
                args.Player.SendData(PacketTypes.PlayerTeam, "", args.Player.Index);
                return;
            }

            if (args.Parameters[0].ToLower() == "blue" || args.Parameters[0].ToLower() == "b")
            {
                args.Player.SendSuccessMessage("You have joined the Blue Team");
                args.Player.SetTeam(3);
                Tools.GetPlayerByIndex(args.Player.Index).team = 3;
                NetMessage.SendData((int)PacketTypes.PlayerTeam, -1, -1, "", args.Player.Index);
                args.Player.SendData(PacketTypes.PlayerTeam, "", args.Player.Index);
                return;
            }

            args.Player.SendErrorMessage("You can only join Blue (b) or Red (r)");
        }

        #region GameSetup
        private static void BorderSet(CommandArgs args)
        {
            border.X = Main.player[args.Player.Index].position.X;
            border.Y = Main.player[args.Player.Index].position.Y - 5;
            args.Player.SendSuccessMessage("Border set at your position.");
            return;
        }

        private static void SpawnSet(CommandArgs args)
        {
            if (args.Parameters.Count != 1)
            {
                args.Player.SendErrorMessage("Incorrect syntax. Use /spawnset [blue/red]");
                return;
            }
            if (args.Parameters[0].ToLower() == "red" || args.Parameters[0].ToLower() == "r")
            {
                args.Player.SendSuccessMessage("You have set the Red Teams spawn");
                redspawn = Main.player[args.Player.Index].position;
                return;
            }

            if (args.Parameters[0].ToLower() == "blue" || args.Parameters[0].ToLower() == "b")
            {
                args.Player.SendSuccessMessage("You have set the Blue Teams spawn");
                bluespawn = Main.player[args.Player.Index].position;
                return;
            }

            args.Player.SendErrorMessage("You can only set Blue (b) or Red (r) spawn");
        }
        #endregion

        private static void Match(CommandArgs args)
        {
            if (args.Parameters.Count != 1)
            {
                args.Player.SendErrorMessage("Incorrect syntax. Use /match [start/end/pause/check]");
                return;
            }

            if (args.Parameters[0].ToLower() == "start" || args.Parameters[0].ToLower() == "s")
            {
                if (match)
                {
                    args.Player.SendErrorMessage("The Match is already in progress");
                    return;
                }

                TSPlayer.All.SendMessage("The CTG Match has been started", Color.Aqua);
                PrepPhase = true;
                timerElapsed = 0;
                if (Config.PrepPhase == 0) Config.PrepPhase = 1;
                initial = new Timer(1000);
                initial.Enabled = true;
                initial.Elapsed += DisablePrepPhase;
                match = true;

                lock (CTGplayer)
                {
                    foreach (var player in CTGplayer)
                    {
                        TShock.Players[player.Index].Teleport(player.spawn.X, player.spawn.Y);
                    }
                }
                return;
            }

            if (args.Parameters[0].ToLower() == "end" || args.Parameters[0].ToLower() == "e")
            {
                if (!match)
                {
                    args.Player.SendErrorMessage("The Match is not in progress");
                    return;
                }

                PrepPhase = false;
                initial.Enabled = false;
                initial.Dispose();

                TSPlayer.All.SendMessage("The CTG Match has been terminated", Color.Aqua);
                match = false;
                return;
            }

            if (args.Parameters[0].ToLower() == "pause" || args.Parameters[0].ToLower() == "p")
            {
                if (pause)
                {
                    TSPlayer.All.SendMessage("The CTG Match has been unpaused", Color.Aqua);
                    pause = false;
                }
                else
                {
                    TSPlayer.All.SendMessage("The CTG Match has been paused", Color.Aqua);
                    pause = true;
                }
                return;
            }

            if (args.Parameters[0].ToLower() == "check" || args.Parameters[0].ToLower() == "c")
            {
                if (match)
                {
                    args.Player.SendSuccessMessage("The Match is in progress");
                }
                else
                {
                    args.Player.SendSuccessMessage("The Match is not in progress");
                }
                return;
            }

            args.Player.SendErrorMessage("You can only Start/End/Pause/Check the match");
        }
        #endregion

        private static void lockTeams(CommandArgs args)
        {
            var state = !teamLock ? "Locked" : "Unlocked";
            args.Player.SendMessage("Teams have been " + state, Color.Aqua);
            teamLock = !teamLock;
            return;
        }

        private static void DisablePrepPhase(object sender, ElapsedEventArgs args)
        {
            if (!pause)
            {
                timerElapsed++;
            }

            if (timerElapsed < Config.PrepPhase)
            {
                int min = (Config.PrepPhase - timerElapsed) / 60;
                int seconds = (Config.PrepPhase - timerElapsed) % 60;
                var timeRemaining = String.Format("{0} Minutes, {1} Seconds", min, seconds);
                foreach (var ply in CTGplayer)
                {
                    ply.TSPlayer.SendData(PacketTypes.Status, String.Format("Prep-Phase Time Remaining: \n{0}\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n", timeRemaining), -1);
                }
            }
            else
            {
                foreach (var ply in CTGplayer)
                {
                    ply.TSPlayer.SendData(PacketTypes.Status, String.Format("Prep-Phase is over\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n"), -1);
                }
            }

            if (timerElapsed >= Config.PrepPhase)
            {
                PrepPhase = false;
                initial.Enabled = false;
                initial.Dispose();
                TSPlayer.All.SendMessage("PrepPhase is now over!", Color.Aqua);
            }
        }


        private static void GetData(GetDataEventArgs args)
        {
            var type = args.MsgID;
            var player = TShock.Players[args.Msg.whoAmI];

            if (player == null)
            {
                args.Handled = true;
                return;
            }

            if (!player.ConnectionAlive)
            {
                args.Handled = true;
                return;
            }

            using (var data = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
            {
                try
                {
                    if (GetDataHandlers.HandlerGetData(type, player, data))
                        args.Handled = true;
                }
                catch (Exception ex)
                {
                    Log.ConsoleError(ex.ToString());
                }
            }
        }

        #region Tools
        public class Tools
        {
            public static Player GetPlayerByIndex(int index)
            {
                return CTG.CTGplayer.FirstOrDefault(player => player.Index == index);
            }
        }
        #endregion

        #region Config
        public class CTGConfig
        {
            public int PrepPhase = 10;

            public void Write(string path)
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }

            public static CTGConfig Read(string path)
            {
                if (!File.Exists(path))
                    return new CTGConfig();
                return JsonConvert.DeserializeObject<CTGConfig>(File.ReadAllText(path));
            }
        }
        #endregion
    }
}
