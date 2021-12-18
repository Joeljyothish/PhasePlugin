using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Phase
{
    public class Config
    {
        public string token = "";
        public string hostName = "t.dark-gaming.com";
        public string exchangeName = "";
        public string username = "";
        public string password = "";
        public string vhost = "phase";
        public string relayReceiveFormat = "";
        public string relayPhaseReceiveFormat = "";
        public bool logPhaseLoginRequests = false;
        public bool logPhaseMessages = false;
        public bool showOtherServerMessages = true;
        public bool sendMessagesToPhase = true;

        public void Write(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static Config Read(string path)
        {
            if (!File.Exists(path))
            {
                Config.WriteTemplates(path);
            }
            return JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
        }
        public static void WriteTemplates(string file)
        {
            var Conf = new Config();
            Conf.token = "token";
            Conf.hostName = "t.dark-gaming.com";
            Conf.exchangeName = "exchange";
            Conf.username = "username";
            Conf.password = "password";
            Conf.vhost = "phase";
            Conf.Write(file);
        }
    }
}
