using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using LibAtem.Commands;
using LibAtem.Util;
using log4net;
using log4net.Config;

namespace AtemProxy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            var log = LogManager.GetLogger(typeof(Program));
            log.Info("Starting");

            var currentStateCommands = new CommandQueue();
            var unknownCommandId = 1;

            var upstream = new UpstreamConnection("10.42.13.95");
            
            var server = new AtemServer(currentStateCommands);
            server.OnReceive += (sender, commands) =>
            {
                // TODO
            };

            string deviceName = "Test Proxy";
            server.StartAnnounce(deviceName, deviceName.GetHashCode().ToString());

            upstream.OnConnection += (sender) =>
            {
                log.DebugFormat("Connected to atem");
                // TODO - state
                server.AcceptConnections();
            };
            upstream.OnDisconnection += (sender) =>
            {
                log.DebugFormat("Lost connection to atem");
                server.RejectConnections();
                currentStateCommands.Clear();
            };
            upstream.OnReceive += (sender, commands) =>
            {
                foreach (var cmd in commands)
                {
                    if (cmd.Item1 != null)
                    {
                        currentStateCommands.Set(new CommandQueueKey(cmd.Item1), cmd.Item2);
                    }
                    else
                    {
                        currentStateCommands.Set(unknownCommandId++, cmd.Item2);
                    }
                }
            };
            
            upstream.Start();
            server.StartReceive();
            server.StartPingTimer();
            
            
            //var server = new ProxyServer("10.42.13.95");

            /*
            var client = new AtemClient("10.42.13.95");
            client.Connect();
            client.OnConnection += (sender) =>
            {
                client.SendCommand(new FairlightMixerSourceSetCommand()
                {
                    Mask = FairlightMixerSourceSetCommand.MaskFlags.Gain,
                    Index = AudioSource.Mic1,
                    SourceId = -256,
                    Gain = -10
                });
                Console.WriteLine("Sent");
            };

            while(true){}
            */
            
            Console.WriteLine("Press Ctrl+C to terminate...");
            
            AutoResetEvent waitHandle = new AutoResetEvent(false);
            // Handle Control+C or Control+Break
            Console.CancelKeyPress += (o, e) =>
            {
                Console.WriteLine("Exit");

                // Allow the manin thread to continue and exit...
                waitHandle.Set();
            };

            // Wait
            waitHandle.WaitOne();
            
            // Force the exit
            System.Environment.Exit(0);
        }
    }
    
    public class CommandQueue
    {
        private readonly Dictionary<object, byte[]> _dict = new Dictionary<object, byte[]>();
        private readonly List<object> _keys = new List<object>();

        public void Clear()
        {
            lock (_dict)
            {
                _dict.Clear();
                _keys.Clear();
            }
        }

        public List<byte[]> Values()
        {
            lock (_dict)
            {
                return _keys.Select(k => _dict[k]).ToList();
            }
        }

        public void Set(object key, byte[] value)
        {
            lock (_dict)
            {
                if (_dict.TryGetValue(key, out var tmpval))
                {
                    _dict[key] = value;
                }
                else
                {
                    _dict.Add(key, value);
                    _keys.Add(key);
                }
            }
        }

    }
}