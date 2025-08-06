using System;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace work.ctrl3d
{
    public class TransportServer : MonoBehaviour
    {
        [SerializeField] private NetworkFamily networkFamily = NetworkFamily.Ipv4;
        [SerializeField] private string address = "0.0.0.0";
        [SerializeField] private ushort port = 7777;

        private NetworkDriver _driver;
        private NativeList<NetworkConnection> _connections;

        protected event Action<NetworkConnection> OnConnected;
        protected event Action<NetworkConnection> OnDisconnected;
        protected event Action<byte[], NetworkConnection> OnDataReceived;

        protected string Address
        {
            get => address;
            set => address = value;
        }

        protected ushort Port
        {
            get => port;
            set => port = value;
        }

        protected virtual void Listen()
        {
            if (_driver.IsCreated)
            {
                Debug.LogWarning($"Server is already running...");
                return;
            }

            _driver = NetworkDriver.Create();
            _connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

            var endpoint = NetworkEndpoint.Parse(address, port, networkFamily);
            if (_driver.Bind(endpoint) != 0)
            {
                Debug.LogError($"Failed to bind to port {port}");
                return;
            }

            _driver.Listen();
        }

        protected virtual void Close()
        {
            if (!_driver.IsCreated) return;

            _driver.Dispose();
            _connections.Dispose();
        }

        private void Update()
        {
            if (!_driver.IsCreated) return;

            _driver.ScheduleUpdate().Complete();

            CleanUpConnections();
            AcceptNewConnections();

            for (var i = 0; i < _connections.Length; i++)
            {
                NetworkEvent.Type cmd;
                while ((cmd = _driver.PopEventForConnection(_connections[i], out var stream)) !=
                       NetworkEvent.Type.Empty)
                {
                    switch (cmd)
                    {
                        case NetworkEvent.Type.Data:
                        {
                            var length = stream.Length;

                            var bytes = new NativeArray<byte>(length, Allocator.Temp);
                            stream.ReadBytes(bytes);
                            OnDataReceived?.Invoke(bytes.ToArray(), _connections[i]);
                            bytes.Dispose();
                            break;
                        }

                        case NetworkEvent.Type.Disconnect:
                        {
                            OnDisconnected?.Invoke(_connections[i]);
                            _connections[i] = default;
                            break;
                        }
                    }
                }
            }
        }

        private void AcceptNewConnections()
        {
            NetworkConnection connection;
            while ((connection = _driver.Accept()) != default)
            {
                _connections.Add(connection);
                var remoteEndpoint = _driver.GetRemoteEndpoint(connection);

                Debug.Log("Accepted a connection. :" + remoteEndpoint);

                OnConnected?.Invoke(connection);
            }
        }

        private void CleanUpConnections()
        {
            for (var i = 0; i < _connections.Length; i++)
            {
                if (!_connections[i].IsCreated)
                {
                    _connections.RemoveAtSwapBack(i);
                    i--;
                }
            }
        }

        public void SendBytes(NetworkConnection connection, byte[] data)
        {
            _driver.BeginSend(connection, out var writer);
            writer.WriteBytes(data);
            _driver.EndSend(writer);
        }

        public void BroadcastBytes(byte[] data)
        {
            foreach (var t in _connections)
            {
                SendBytes(t, data);
            }
        }

        private void OnDestroy()
        {
            Close();
        }
    }
}