using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;
using System.Net;
using System.Linq;
using UnityEngine.UIElements;
using System.Drawing;
using System.Collections.Generic;
using System.Collections;


public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    private NativeList<NetworkConnection> m_Connections;
    public List<NetworkObjects.NetworkPlayer> PlayerList;

    private NetworkObjects.NetworkPlayer getPlayer(string id)
    {
        foreach(var player in PlayerList)
        {
            if (player.id == id)
                return player;
        }
        return null;
    }


    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = serverPort;
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);
        else
            m_Driver.Listen();

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
        PlayerList = new List<NetworkObjects.NetworkPlayer>();

        StartCoroutine(SendHandshake());
        StartCoroutine(SendUpdate());
    }

    IEnumerator SendHandshake()
    {
        while (1 == 1)
        {
            for(int i = 0; i < m_Connections.Length; i++)
            {
                if (!m_Connections[i].IsCreated)
                    continue;
                HandshakeMsg message = new HandshakeMsg();
                message.player.id = m_Connections[i].InternalId.ToString();
                SendToClient(JsonUtility.ToJson(message), m_Connections[i]);
            }
            yield return new WaitForSeconds(2);
        }
    }

    IEnumerator SendUpdate()
    {
        while(1 == 1)
        {
            ServerUpdateMsg message = new ServerUpdateMsg();
            message.players = PlayerList;
            foreach(var connection in m_Connections)
            {
                if (!connection.IsCreated)
                    continue;
                SendToClient(JsonUtility.ToJson(message), connection);
            }
            yield return new WaitForSeconds(0.0333333f);
        }
    }

    void SendToClient(string message, NetworkConnection c){
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }
    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    void OnConnect(NetworkConnection c){
        m_Connections.Add(c);
        Debug.Log("Accepted a connection");

        HandshakeMsg message = new HandshakeMsg();
        message.player.id = c.InternalId.ToString();
        SendToClient(JsonUtility.ToJson(message), c);

        NetworkObjects.NetworkPlayer nPlayer = new NetworkObjects.NetworkPlayer();

        nPlayer.id = c.InternalId.ToString();
        nPlayer.cubeColor = UnityEngine.Random.ColorHSV(0f, 1f, 1f, 1f, 0f, 1f);
        nPlayer.cubPos = new Vector3(UnityEngine.Random.Range(-5, 5), UnityEngine.Random.Range(-5, 5), UnityEngine.Random.Range(0, 0));

        for(int i = 0; i < m_Connections.Length; i++)
        {
            if(m_Connections[i] != c)
            {
                PlayerUpdateMsg othermessage = new PlayerUpdateMsg();
                othermessage.cmd = Commands.PLAYER_JOINED;
                othermessage.player = nPlayer;
                SendToClient(JsonUtility.ToJson(othermessage), m_Connections[i]);
            }
        }
        PlayerList.Add(nPlayer);
        foreach(var player in PlayerList)
        {
            PlayerUpdateMsg otherothermessage = new PlayerUpdateMsg();
            otherothermessage.cmd = Commands.PLAYER_JOINED;
            otherothermessage.player = player;
            SendToClient(JsonUtility.ToJson(otherothermessage), c);
            
        }

    }

    void OnData(DataStreamReader stream, int i){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
            HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
            Debug.Log("Handshake message received!");
            break;
            case Commands.PLAYER_UPDATE:
            PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
            Debug.Log("Player update message received!");
            break;
            case Commands.SERVER_UPDATE:
            ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
            Debug.Log("Server update message received!");
            break;         
            default:
            Debug.Log("SERVER ERROR: Unrecognized message received!");
            break;
        }
    }

    void OnDisconnect(int i){
        Debug.Log("Client disconnected from server");
        m_Connections[i] = default(NetworkConnection);

        PlayerUpdateMsg message = new PlayerUpdateMsg();
        message.cmd = Commands.PLAYER_LEFT;
        message.player = getPlayer(m_Connections[i].InternalId.ToString());

        foreach(var client in m_Connections)
        {
            if(client != m_Connections[i])
            {
                SendToClient(JsonUtility.ToJson(message), client);

            }
        }
    }

    private void UpdatePlayer(NetworkObjects.NetworkPlayer _player)
    {
        var playerInList = getPlayer(_player.id);
        if(playerInList != null)
        {
            playerInList.cubeColor = _player.cubeColor;

            //Baby Bears are being positioned as players. Help, they do not want to be moved.
            playerInList.cubPos = _player.cubPos;
            Debug.Log("Updating players");
        }

    }

    void Update ()
    {
        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {

                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        // AcceptNewConnections
        NetworkConnection c = m_Driver.Accept();
        while (c  != default(NetworkConnection))
        {            
            OnConnect(c);

            // Check if there is another new connection
            c = m_Driver.Accept();
        }


        // Read Incoming Messages
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            
            NetworkEvent.Type cmd;
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            while (cmd != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    OnData(stream, i);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
    }
}