using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebSocketLogProxy
{
    internal class ButtplugIOMultiplexerProxy : WebSocketBehavior
    {
        //[{"RequestServerInfo":{"Id":1,"ClientName":"Buttplug Playground","MessageVersion":2}}]
        //[{"ServerInfo":{"Id":1,"MessageVersion":2,"MaxPingTime":0,"ServerName":"Intiface Server"}}]
        private static WebSocket _ws;
        private static bool _serverInfoReceived = false;
        private static string _serverInfoMessage = null;
        
        private static long _currentServerId = 1;
        private static ConcurrentDictionary<long, (ButtplugIOMultiplexerProxy, long)> ActiveIDLookup = new ConcurrentDictionary<long, (ButtplugIOMultiplexerProxy,long)>();
        private static ButtplugIOMultiplexerProxy OneInstance;
        static ButtplugIOMultiplexerProxy()
        {
            _ws = new WebSocket("ws://localhost:12346");
            _ws.OnMessage += (sender, args) =>
            {
                if (!_serverInfoReceived)
                {
                    Console.WriteLine("Received ServerInfo : " + args.Data);
                    _serverInfoMessage = args.Data;
                    _serverInfoReceived = true;
                    return;
                }
                var rss = JArray.Parse(args.Data);
                Console.WriteLine(">    " + rss.ToString(Formatting.None));
                ButtplugIOMultiplexerProxy target = null;
                foreach (var entry in rss)
                {
                    var id = GetIdInMessage(entry);
                    if (id == 0)
                    {
                        OneInstance?.Sessions?.Broadcast(args.Data);
                        return;
                    }
                    
                    if (ActiveIDLookup.TryRemove(id, out var resolved))
                    {
                        target = resolved.Item1;
                        SetIdInMessage(entry, resolved.Item2);
                        
                    }
                    else
                    {
                        return; // no id found
                    }
                }
                if(target== null)
                    return;
                Console.WriteLine(">    " + rss.ToString(Formatting.None));
                target.Send(rss.ToString(Formatting.None));
            };

            _ws.Connect();
            _ws.Send("[{\"RequestServerInfo\":{\"Id\":1,\"ClientName\":\"Multiplexer Proxy\",\"MessageVersion\":2}}]");
            
        }

        public ButtplugIOMultiplexerProxy()
        {
            if(OneInstance==null)
                OneInstance = this;
        }
        

        protected override void OnMessage(MessageEventArgs e)
        {
            var rss = JArray.Parse(e.Data);

            Console.WriteLine("<    " + rss.ToString(Formatting.None));
            foreach (var entry in rss)
            {
                var id = GetIdInMessage(entry);
                if (id == 1)
                {
                    Send(_serverInfoMessage);
                    return;
                }

                var newId =TranslateId(id);
                SetIdInMessage(entry, newId);
            }
            Console.WriteLine("<    " + rss.ToString(Formatting.None));
            _ws.Send(rss.ToString(Formatting.None));
        }

        private static long GetIdInMessage(JToken entry)
        {
            foreach (var props in ((JObject)entry).Properties())
            {
                var id = Convert.ToInt64(((JObject)props.Value)["Id"].ToString());
                
                return id;
            }

            return -1;
        }

        private long TranslateId(long id)
        {
            
            var newId = Interlocked.Increment(ref _currentServerId);
            ActiveIDLookup[newId] = (this, id);
            return newId;
        }

        private static void SetIdInMessage(JToken entry, long id)
        {
            foreach (var props in ((JObject)entry).Properties())
            {
                ((JObject)props.Value)["Id"] = id;
            }
            
        }
    }
}