using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using System.Net;
using System.Net.Sockets;
using System.Web.Script.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using RabbitMQ.Client;
using MySql.Data.MySqlClient;
using Mono.Data.Sqlite;

namespace Phase
{
    public class PhaseMessage
    {
        public string message { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
    }

    [ApiVersion(2, 1)]
    public class Phase : TerrariaPlugin
    {
        public static Config Config = new Config();
        public static IDbConnection db;
        private object syncLock = new object();
        public static Rabbit RMQ;

        public override string Author
        {
            get
            {
                return "popstarfreas";
            }
        }

        public override string Description
        {
            get
            {
                return "Interacts with Phase";
            }
        }

        public override string Name
        {
            get
            {
                return "Phase";
            }
        }

        public override Version Version
        {
            get
            {
                return new Version(1, 0, 0);
            }
        }

        public Phase(Main game) : base(game)
        {
            Order = 1;
        }

        public override void Initialize()
        {
            ServerApi.Hooks.ServerJoin.Register(this, GreetPlayer);
            ServerApi.Hooks.ServerLeave.Register(this, LeavePlayer);
            ServerApi.Hooks.ServerChat.Register(this, OnChat);
            ServerApi.Hooks.GamePostInitialize.Register(this, PostInit);
            Commands.ChatCommands.Add(new Command("phase.reload", Reload, "phasereload"));

            string path = Path.Combine(TShock.SavePath, "phase.json");
            if (File.Exists(path))
                Config = Config.Read(path);
            Config.Write(path);

            Database();
        }

        private void PostInit(EventArgs args)
        {
            var jsonSerializer = new JavaScriptSerializer();
            SetupRabbit();
        }

        void SetupRabbit()
        {
            if (RMQ != null)
            {
                RMQ.Dispose();
            }

            RMQ = new Rabbit(Config.hostName, Config.username, Config.password, Config.vhost);
            RMQ.NewMessage += PhaseMessage;

            JObject json = new JObject();
            json.Add("token", Config.token);
            json.Add("type", "started");
            //sub.PublishAsync("phase_server", json.ToString());
            //sub.PublishAsync("phase_relay_" + Config.relayName, json.ToString());

            if (RMQ != null)
                RMQ.Publish(json.ToString());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.ServerJoin.Deregister(this, GreetPlayer);
                ServerApi.Hooks.ServerLeave.Deregister(this, LeavePlayer);
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
                base.Dispose(disposing);
            }
        }

        private void Database()
        {
            if (TShock.Config.StorageType == "sqlite")
            {
                string sql = Path.Combine(TShock.SavePath, "tshock.sqlite");
                db = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
            }
            else
            {
                try
                {
                    db = new MySqlConnection();
                    db.ConnectionString =
                        String.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                                                         TShock.Config.MySqlHost.Split(':')[0],
                                                         TShock.Config.MySqlHost.Split(':')[1],
                                                         TShock.Config.MySqlDbName,
                                                         TShock.Config.MySqlUsername,
                                                         TShock.Config.MySqlPassword
                            );
                }
                catch (MySqlException ex)
                {
                    TShock.Log.Error(ex.ToString());
                    throw new Exception("MySql not setup correctly");
                }
            }
        }

        internal async Task<QueryResult> QueryReader(string query, params object[] args)
        {
            return await Task.Run(() =>
            {
                try
                {
                    lock (syncLock)
                    {
                        return db.QueryReader(query, args);
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.Error(ex.ToString());
                    return null;
                }
            });
        }
        private void PhaseMessage(object sender, Message m)
        {
            dynamic message = JObject.Parse(m.content);
            //string[] message = new JavaScriptSerializer().Deserialize<string[]>(messageIncoming);
            //Console.WriteLine("[PhaseMessage] {Type: " + ((string)message.type) + "}");
            string token = message.token;
            if (token == Config.token)
            {
                string type = message.type;
                switch (type)
                {
                    case "command":
                        HandlePhaseCommand(message);
                        break;
                    case "chat":
                        TSPlayer.All.SendMessage((string)message.content, (byte)message.R, (byte)message.G, (byte)message.B);
                        break;
                    case "login":
                        HandlePasswordRequest((string)message.username);
                        break;
                }
            }
        }

        private void HandlePhaseCommand(dynamic message)
        {
            string commandName = message.commandName;
            string sender = message.sender;
            int discID = message.discID;
            string userName = message.commandUserName;
            switch(commandName)
            {
                case "kick":
                    {
                        string kickType = message.kickType;
                        string reason = message.reason;
                        string name = "";
                        TSPlayer player = null;
                        if (kickType == "playerName")
                        {
                            string playerName = message.playerName;
                            name = playerName;
                            player = TShock.Players.FirstOrDefault(p => p != null && p.Name == playerName);
                        }
                        else
                        if (kickType == "accountName")
                        {
                            string accountName = message.accountName;
                            name = accountName;
                            player = TShock.Players.FirstOrDefault(p => p != null && p.User != null && p.User.Name == accountName);
                        }

                        JObject json = new JObject();
                        json.Add("type", "commandResponse");
                        json.Add("sender", sender);
                        json.Add("discID", discID);
                        if (player != null)
                        {
                            TShock.Utils.ForceKick(player, reason);
                            json.Add("state", "success");
                            json.Add("responseMessage", $"Successfully kicked player \"{name}\"");
                        }
                        else
                        {
                            json.Add("state", "failure");
                            json.Add("responseMessage", "No such player \"" + name + "\"");
                        }
                        RMQ.Publish(json.ToString());
                    }
                    break;
                case "mute":
                    {
                        string muteType = message.muteType;
                        string reason = message.reason;
                        bool remove = message.remove;
                        string name = "";
                        TSPlayer player = null;
                        if (muteType == "playerName")
                        {
                            string playerName = message.playerName;
                            name = playerName;
                            player = TShock.Players.FirstOrDefault(p => p != null && p.Name == playerName);
                        }
                        else
                        if (muteType == "accountName")
                        {
                            string accountName = message.accountName;
                            name = accountName;
                            player = TShock.Players.FirstOrDefault(p => p != null && p.User != null && p.User.Name == accountName);
                        }

                        JObject json = new JObject();
                        json.Add("type", "commandResponse");
                        json.Add("sender", sender);
                        json.Add("discID", discID);
                        if (player != null)
                        {
                            if (!remove)
                            {
                                player.mute = true;
                                TSPlayer.All.SendInfoMessage("{0} has been muted by {1} for \"{2}\".", player.Name, userName, reason);

                                if (player.User != null)
                                    json.Add("responseMessage", "Successfully muted player \"" + player.User.Name + "\"");
                                else
                                    json.Add("responseMessage", "Successfully muted player \"" + player.Name + "\"");
                            } else
                            {
                                player.mute = false;
                                TSPlayer.All.SendInfoMessage("{0} has been unmuted by {1}", player.Name, userName);

                                if (player.User != null)
                                    json.Add("responseMessage", "Successfully unmuted player \"" + player.User.Name + "\"");
                                else
                                    json.Add("responseMessage", "Successfully unmuted player \"" + player.Name + "\"");
                            }

                            json.Add("state", "success");
                        }
                        else
                        {
                            json.Add("state", "failure");
                            json.Add("responseMessage", "No such player \"" + name + "\"");
                        }
                        RMQ.Publish(json.ToString());
                    }
                    break;
                case "ban":
                    {
                        // message {"banType", "reason", "remove", "offline", "playerName"||"accountName"||"playerIP", "expire"}
                        string banType = message.banType;
                        string reason = message.reason;
                        bool remove = message.remove;
                        bool offlineBan = message.offline;
                        string name = "";
                        string ip = "";
                        bool expire = message.expire != null;
                        int time = 0;
                        TSPlayer player = null;
                        JObject json = new JObject();

                        if (expire)
                        {
                            if (!TShock.Utils.TryParseTime((string)message.expire, out time))
                            {
                                json.Add("responseMessage", "Invalid time string! Proper format: _d_h_m_s, with at least one time specifier.");
                                json.Add("state", "failure");
                                RMQ.Publish(json.ToString());
                                return;
                            }
                        }

                        if (banType == "playerName")
                        {
                            string playerName = message.playerName;
                            name = playerName;
                            player = TShock.Players.FirstOrDefault(p => p != null && p.Name == playerName);
                        }
                        else
                        if (banType == "accountName")
                        {
                            string accountName = message.accountName;
                            name = accountName;
                            player = TShock.Players.FirstOrDefault(p => p != null && p.User != null && p.User.Name == accountName);
                        } else if (banType == "playerIP")
                        {
                            ip = (string)message.playerIP;
                        }

                        json.Add("type", "commandResponse");
                        json.Add("sender", sender);
                        json.Add("discID", discID);
                        if (player != null)
                        {
                            if (!remove)
                            {
                                bool passed = false;
                                if (expire)
                                {
                                    passed = TShock.Bans.AddBan(player.IP, player.Name, player.UUID, reason, false, userName, DateTime.UtcNow.AddSeconds(time).ToString("s"));
                                } else
                                {
                                    passed = TShock.Bans.AddBan(player.IP, player.Name, player.UUID, reason, false, userName);
                                }

                                if (passed)
                                {
                                    json.Add("state", "success");
                                    json.Add("responseMessage", $"Successfully banned player \"{name}\"");
                                } else
                                {
                                    json.Add("responseMessage", "Something went wrong :/");
                                    json.Add("state", "failure");
                                }
                            }
                            else
                            {
                                Ban ban = TShock.Bans.GetBanByName(name, true);
                                if (ban != null)
                                {
                                    if (TShock.Bans.RemoveBan(ban.Name, true))
                                    {
                                        json.Add("responseMessage", String.Format("Unbanned {0} ({1}).", ban.Name, ban.IP));
                                        json.Add("state", "success");
                                    }
                                    else
                                    {
                                        json.Add("responseMessage", String.Format("Failed to unban {0} ({1}), check logs.", ban.Name, ban.IP));
                                        json.Add("state", "failure");
                                    }
                                }
                                else
                                {
                                    json.Add("responseMessage", String.Format("No bans for {0} exist.", name));
                                    json.Add("state", "failure");
                                }
                            }
                        } else if (banType == "playerIP")
                        {
                            if (!remove)
                            {
                                bool passed = false;
                                if (expire)
                                {
                                    passed = TShock.Bans.AddBan(ip, "", "", reason, false, userName, DateTime.UtcNow.AddSeconds(time).ToString("s"));
                                }
                                else
                                {
                                    passed = TShock.Bans.AddBan(ip, "", "", reason, false, userName);
                                }

                                if (passed)
                                {
                                    TShock.Bans.AddBan(ip, "", "", reason, false, userName);
                                    json.Add("responseMessage", String.Format("Banned IP {0}.", ip));
                                    json.Add("state", "success");
                                }
                                else
                                {
                                    json.Add("responseMessage", "Something went wrong :/");
                                    json.Add("state", "failure");
                                }
                            } else
                            {
                                Ban ban = TShock.Bans.GetBanByIp(ip);
                                if (ban != null)
                                {
                                    if (TShock.Bans.RemoveBan(ip, false))
                                    {
                                        json.Add("responseMessage", String.Format("Unbanned {0} ({1}).", ban.Name, ban.IP));
                                        json.Add("state", "success");
                                    }
                                    else
                                    {
                                        json.Add("responseMessage", String.Format("Failed to unban {0} ({1}), check logs.", ban.Name, ban.IP));
                                        json.Add("state", "failure");
                                    }
                                }
                                else
                                {
                                    json.Add("responseMessage", String.Format("No IP bans for {0} exist.", ip));
                                    json.Add("state", "failure");
                                }
                            }
                        } else if (offlineBan)
                        {
                            User user = TShock.Users.GetUserByName(name);
                            if (user != null) {
                                var knownIps = JsonConvert.DeserializeObject<List<string>>(user.KnownIps);
                                bool passed = TShock.Bans.AddBan(knownIps.Last(), user.Name, user.UUID, reason, false, userName);

                                if (passed)
                                {
                                    json.Add("responseMessage", String.Format("Banned {0} ({1}).", user.Name, knownIps.Last()));
                                    json.Add("state", "success");
                                }
                                else
                                {
                                    json.Add("responseMessage", "Something went wrong :/");
                                    json.Add("state", "failure");
                                }
                            } else
                            {
                                json.Add("state", "failure");
                                json.Add("responseMessage", "No such user \"" + name + "\"");
                            }
                        }
                        else
                        {
                            json.Add("state", "failure");
                            json.Add("responseMessage", "No such player \"" + name + "\"");
                        }
                        RMQ.Publish(json.ToString());
                    }
                    break;
            }
        }

        private async void HandlePasswordRequest(string username)
        {
            var jsonSerializer = new JavaScriptSerializer();
            string information = await getUserInformation(username);
            //Console.WriteLine("Pushing: " + information);
            if (RMQ != null)
                RMQ.Publish(information);
            //await sub.PublishAsync("phase_loginResponse", jsonSerializer.Serialize(information));
        }

        private async Task<string> getUserInformation(string username)
        {
            var json = new JObject();
            var jsonDetails = new JObject();
            using (var reader = await QueryReader("SELECT ID, Username, Password FROM users WHERE Username = @0", username))
            {
                if (reader.Read()) {
                    jsonDetails.Add("state", "success");
                    jsonDetails.Add("username", reader.Get<string>("Username"));
                    jsonDetails.Add("password", reader.Get<string>("Password"));
                    jsonDetails.Add("ID", Convert.ToString(reader.Get<int>("ID")));
                    jsonDetails.Add("token", Convert.ToString(Config.token));

                    json.Add("type", "loginResponse");
                    json.Add("details", jsonDetails);
                    return json.ToString();
                }
            }

            jsonDetails.Add("state", "failure");
            jsonDetails.Add("username", username);
            jsonDetails.Add("token", Convert.ToString(Config.token));
            json.Add("type", "loginResponse");
            json.Add("details", jsonDetails);
            return json.ToString();
        }


        private void OnChat(ServerChatEventArgs args)
        {
            if (!args.Handled)
            {
                if ((args.Text.StartsWith(TShock.Config.CommandSpecifier) || args.Text.StartsWith(TShock.Config.CommandSilentSpecifier)) && args.Text.Length > 1)
                    return;

                if (args.Who > 255 || args.Who < 0 || TShock.Players[args.Who] == null)
                    return;

                // Remove those stupid color tags that people like to use
                string newText = args.Text;

                var player = TShock.Players[args.Who];
                if (player == null)
                {
                    args.Handled = false;
                    return;
                }

                if (!TShock.Players[args.Who].Group.HasPermission("chat.colortags"))
                {
                    string pattern = @"\[c(.*?)\:(?<message>.*?)\]";
                    string replacement = "${message}";
                    Regex rgx = new Regex(pattern);
                    newText = rgx.Replace(newText, replacement);
                }

                if (!player.mute && player.Group.HasPermission("tshock.canchat"))
                {
                    // Send to Phase
                    JObject json = new JObject();
                    json.Add("token", Config.token);
                    json.Add("type", "player_chat");
                    json.Add("name", TShock.Players[args.Who].Name);
                    json.Add("prefix", TShock.Players[args.Who].Group.Prefix);
                    json.Add("suffix", TShock.Players[args.Who].Group.Suffix);
                    json.Add("message", newText);
                    json.Add("R", Convert.ToString(TShock.Players[args.Who].Group.R));
                    json.Add("G", Convert.ToString(TShock.Players[args.Who].Group.G));
                    json.Add("B", Convert.ToString(TShock.Players[args.Who].Group.B));
                    json.Add("ip", TShock.Players[args.Who].IP);
                    json.Add("id", Convert.ToString(TShock.Players[args.Who].User != null ? TShock.Players[args.Who].User.ID : -1));
                    json.Add("accountName", TShock.Players[args.Who].User != null ? TShock.Players[args.Who].User.Name : "");

                    if (RMQ != null)
                        RMQ.Publish(json.ToString());
                }
            }
        }

        private void LeavePlayer(LeaveEventArgs args)
        {
            if (TShock.Players[args.Who] != null)
            {
                JObject json = new JObject();
                json.Add("token", Config.token);
                json.Add("type", "player_leave");
                json.Add("name", TShock.Players[args.Who].Name);
                json.Add("ip", TShock.Players[args.Who].IP);
                if (RMQ != null)
                    RMQ.Publish(json.ToString());
            }
        }

        private void GreetPlayer(JoinEventArgs args)
        {
            if (TShock.Players[args.Who] != null && !args.Handled)
            {
                JObject json = new JObject();
                json.Add("token", Config.token);
                json.Add("type", "player_join");
                json.Add("name", TShock.Players[args.Who].Name);
                json.Add("ip", TShock.Players[args.Who].IP);
                if (RMQ != null)
                    RMQ.Publish(json.ToString());
            }
        }

        void Reload(CommandArgs e)
        {

            string path = Path.Combine(TShock.SavePath, "phase.json");
            if (File.Exists(path))
                Config = Config.Read(path);
            Config.Write(path);

            SetupRabbit();

            e.Player.SendSuccessMessage("Reloaded phase config.");
        }
    }
}

