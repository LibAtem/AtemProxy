using System;
using System.Collections.Generic;
using System.Net;
using LibAtem.Net;
using log4net;

namespace AtemProxy
{
    public class ClientConnectionList
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ClientConnectionList));

        private readonly Dictionary<EndPoint, AtemServerConnection> _connections =
            new Dictionary<EndPoint, AtemServerConnection>();
        private readonly HashSet<AtemServerConnection> _subscribedToAudio = new HashSet<AtemServerConnection>();
        
        public event AtemConnection.PacketHandler OnReceive;
        
        public ClientConnectionList()
        {
        }

        public AtemServerConnection FindOrCreateConnection(EndPoint ep, out bool isNew)
        {
            lock (_connections)
            {
                AtemServerConnection val;
                if (_connections.TryGetValue(ep, out val))
                {
                    isNew = false;
                    return val;
                }

                val = new AtemServerConnection(ep, 0x8008);// TODO - make dynamic
                _connections[ep] = val;
                val.OnDisconnect += RemoveTimedOut;

                val.OnReceivePacket += OnReceive;
                
                Log.InfoFormat("New connection from {0}", ep);

                isNew = true;
                return val;
            }
        }

        public void ClearAll()
        {
            lock (_connections)
            {
                _connections.Clear();
            }

            lock (_subscribedToAudio)
            {
                _subscribedToAudio.Clear();
            }
        }

        private void RemoveTimedOut(object sender)
        {
            var conn = sender as AtemServerConnection;
            if (conn == null)
                return;

            Log.InfoFormat("Lost connection to {0}", conn.Endpoint);

            lock (_connections)
            {
                _connections.Remove(conn.Endpoint);
            }

            lock (_subscribedToAudio)
            {
                // var initSize = _subscribedToAudio.Count;
                _subscribedToAudio.Remove(conn);
            }
        }
        
        internal void QueuePings()
        {
            lock (_connections)
            {
                var toRemove = new List<EndPoint>();
                foreach (KeyValuePair<EndPoint, AtemServerConnection> conn in _connections)
                {
                    if (conn.Value.HasTimedOut)
                    {
                        toRemove.Add(conn.Key);
                        continue;
                    }

                    if (conn.Value.IsOpened)
                    {
                        conn.Value.QueuePing();
                    }
                }

                foreach (var ep in toRemove)
                {
                    Log.InfoFormat("Lost connection to {0}", ep);
                    _connections.Remove(ep);
                }
            }
        }

        public void Broadcast(IReadOnlyList<OutboundMessage> messages)
        {
            lock (_connections)
            {
                foreach (var conn in _connections)
                {
                    if (!conn.Value.HasTimedOut)
                    {
                        foreach (var msg in messages)
                        {
                            conn.Value.QueueMessage(msg);
                        }
                    }
                }
            }
        }

        public void BroadcastAudioLevels(IReadOnlyList<OutboundMessage> messages)
        {
            lock (_subscribedToAudio)
            {
                foreach (var conn in _subscribedToAudio)
                {
                    if (!conn.HasTimedOut)
                    {
                        foreach (var msg in messages)
                        {
                            conn.QueueMessage(msg);
                        }
                    }
                }
            }
        }

        public bool SubscribeAudio(object sender, bool subscribed)
        {
            if (sender is AtemServerConnection {HasTimedOut: false} sender2)
            {
                lock (_subscribedToAudio)
                {
                    if (subscribed)
                    {
                        var before = _subscribedToAudio.Count;
                        _subscribedToAudio.Add(sender2);
                        return before == 0;
                    }
                    else
                    {
                        var before = _subscribedToAudio.Count;
                        _subscribedToAudio.Remove(sender2);
                        return before != 0 && _subscribedToAudio.Count == 0;
                    }
                }
            }
            else
            {
                return false;
            }
        }

    }
}