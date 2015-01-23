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
        public static Vector2 bluespawn, redspawn;
        public static int border;
        public static bool match, pause, PrepPhase, teamLock;
        public static DateTime LastCheck = DateTime.UtcNow;

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
            teamLock = false;

            SetUpConfig();
        }

        #region GreetLeave

        private static void OnGreetPlayer(GreetPlayerEventArgs args)
        {
            lock (CTGplayer)
                CTGplayer.Add(new Player(args.Who));
        }

        private static void OnLeave(LeaveEventArgs args)
        {
            lock (CTGplayer)
                CTGplayer.RemoveAll(plr => plr.Index == args.Who);
        }

        #endregion

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
            if ((DateTime.UtcNow - LastCheck).TotalMilliseconds >= 500)
            {
                LastCheck = DateTime.UtcNow;

                lock (CTGplayer)
                {
                    foreach (var player in CTGplayer)
                    {

                        if (pause)
                            TShock.Players[player.Index].Disable();
                        else if (!match && !TShock.Players[player.Index].Group.HasPermission("ctg.admin"))
                            TShock.Players[player.Index].Disable();

                        if (player.team == 1 && PrepPhase)
                        {
                            if (border > redspawn.X)
                            {
                                if (Main.player[player.Index].position.X > border)
                                    TShock.Players[player.Index].Teleport(border - 5, Main.player[player.Index].position.Y);
                            }
                            else
                            {
                                if (Main.player[player.Index].position.X < border)
                                    TShock.Players[player.Index].Teleport(border + 5, Main.player[player.Index].position.Y);
                            }
                        }

                        if (player.team == 3 && PrepPhase)
                        {
                            if (border > bluespawn.X)
                            {
                                if (Main.player[player.Index].position.X > border)
                                {
                                    TShock.Players[player.Index].Teleport(border - 2, Main.player[player.Index].position.Y);
                                    TShock.Players[player.Index].TPlayer.velocity.X = -5;
                                }
                            }
                            else
                            {
                                if (Main.player[player.Index].position.X < border)
                                {
                                    TShock.Players[player.Index].Teleport(border + 2, Main.player[player.Index].position.Y);
                                    TShock.Players[player.Index].TPlayer.velocity.X = 5;
                                }
                            }
                        }

                        if (TShock.Players[player.Index].Team != player.team)
                        {
                            TShock.Players[player.Index].SetTeam(player.team);
                        }

                        if (Main.player[player.Index].hostile == false && match)
                        {
                            Main.player[player.Index].hostile = true;
                            NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, "", player.Index, 0f, 0f, 0f);
                        }
                        else if (Main.player[player.Index].hostile == true && !match)
                        {
                            Main.player[player.Index].hostile = false;
                            NetMessage.SendData((int)PacketTypes.TogglePvp, -1, -1, "", player.Index, 0f, 0f, 0f);
                        }
                    }
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
        }

        #endregion

        #region Join

        private static void Join(CommandArgs args)
        {
            if (match || teamLock)
            {
                args.Player.SendMessage("Teams are locked while the match is running!");
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
                return;
            }

            if (args.Parameters[0].ToLower() == "blue" || args.Parameters[0].ToLower() == "b")
            {
                args.Player.SendSuccessMessage("You have joined the Blue Team");
                args.Player.SetTeam(3);
                Tools.GetPlayerByIndex(args.Player.Index).team = 3;
                return;
            }

            args.Player.SendErrorMessage("You can only join Blue (b) or Red (r)");
            return;
        }

        #endregion

        #region GameSetup
        private static void BorderSet(CommandArgs args)
        {
            border = (int)Main.player[args.Player.Index].position.X;
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
            return;
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

                TShock.Utils.Broadcast("The CTG Match has been started");
                PrepPhase = true;
                if (Config.PrepPhase == 0) Config.PrepPhase = 1;
                initial = new Timer(Config.PrepPhase * 1000);
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

                TShock.Utils.Broadcast("The CTG Match has been terminated");
                match = false;
                return;
            }

            if (args.Parameters[0].ToLower() == "pause" || args.Parameters[0].ToLower() == "p")
            {
                if (pause)
                {
                    TShock.Utils.Broadcast("The CTG Match has been unpaused");
                    pause = false;
                }
                else
                {
                    TShock.Utils.Broadcast("The CTG Match has been paused");
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
            return;
        }

        private static void DisablePrepPhase(object sender, ElapsedEventArgs args)
        {
            PrepPhase = false;
            initial.Enabled = false;
            initial.Dispose();
            TShock.Utils.Broadcast("PrepPhase is now over!");
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


            if (type == PacketTypes.PlayerKillMe)
            {
                var ply = Tools.GetPlayerByIndex(args.Msg.whoAmI);
                if (ply == null)
                {
                    args.Handled = true;
                    return;
                }
                player.Spawn(0, 0);
                player.Teleport(ply.spawn.X, ply.spawn.Y);
                args.Handled = true;
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
