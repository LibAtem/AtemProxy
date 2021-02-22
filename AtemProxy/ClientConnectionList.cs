using System.Collections.Generic;
using System.Net;
using log4net;

namespace AtemProxy
{
    public class ClientConnectionList
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ClientConnectionList));

        private readonly Dictionary<EndPoint, AtemServerConnection> connections;

        public ClientConnectionList()
        {
            connections = new Dictionary<EndPoint, AtemServerConnection>();
        }

        public AtemServerConnection FindOrCreateConnection(EndPoint ep, out bool isNew)
        {
            lock (connections)
            {
                AtemServerConnection val;
                if (connections.TryGetValue(ep, out val))
                {
                    isNew = false;
                    return val;
                }

                val = new AtemServerConnection(ep, 0x8008);// TODO - make dynamic
                connections[ep] = val;
                val.OnDisconnect += RemoveTimedOut;
                
                Log.InfoFormat("New connection from {0}", ep);

                isNew = true;
                return val;
            }
        }

        public void ClearAll()
        {
            lock (connections)
            {
                connections.Clear();
            }
        }

        private void RemoveTimedOut(object sender)
        {
            var conn = sender as AtemServerConnection;
            if (conn == null)
                return;

            Log.InfoFormat("Lost connection to {0}", conn.Endpoint);

            lock (connections)
            {
                connections.Remove(conn.Endpoint);
            }
        }
        
        internal void QueuePings()
        {
            lock (connections)
            {
                var toRemove = new List<EndPoint>();
                foreach (KeyValuePair<EndPoint, AtemServerConnection> conn in connections)
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
                    connections.Remove(ep);
                }
            }
        }

    }
}