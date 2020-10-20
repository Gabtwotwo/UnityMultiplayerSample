using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;
using System.Collections.Generic;
using System.Collections;
using TMPro;

public class NetworkClient : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;

    public GameObject playerPrefab;
    private List<GameObject> playerList;
    public string myID;
    
    GameObject findPlayer(string id)
    {
        foreach(GameObject player in playerList)
        {
            if(player.GetComponentInChildren<TMP_Text>().text == id)
            return player;
        }
        return null;
    }

    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP,serverPort);
        m_Connection = m_Driver.Connect(endpoint);

        playerList = new List<GameObject>();
    }
    
    void SendToServer(string message){
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void OnConnect(){
        Debug.Log("We are now connected to the server");

        StartCoroutine(SendHandshake());
        StartCoroutine(SendPlayer());
        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = m_Connection.InternalId.ToString();
        // SendToServer(JsonUtility.ToJson(m));
    }

    IEnumerator SendHandshake()
    {
        while(1 == 1)
        {
            yield return new WaitForSeconds(2);
            HandshakeMsg message = new HandshakeMsg();
            message.player.id = m_Connection.InternalId.ToString();
            SendToServer(JsonUtility.ToJson(message));
        }
    }

    IEnumerator SendPlayer()
    {
        while(1 == 1){
            PlayerUpdateMsg message = new PlayerUpdateMsg();
            GameObject playerObject = findPlayer(myID);

            if(playerObject != null)
            {
                message.player.id = myID;
                message.player.cubeColor = playerObject.GetComponent<MeshRenderer>().material.color;
                message.player.cubPos = playerObject.transform.position;

                SendToServer(JsonUtility.ToJson(message));
            }

            yield return new WaitForSeconds(0.0333333f);
        }
    }

    void OnData(DataStreamReader stream){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);
        Debug.Log("Got this header: " + header.cmd);
        switch(header.cmd){
            case Commands.HANDSHAKE:
            HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
            Debug.Log("Handshake message received!");
            myID = hsMsg.player.id;
            break;
            case Commands.PLAYER_UPDATE:
            PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
            Debug.Log("Player update message received!");
            break;
            case Commands.SERVER_UPDATE:
            ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
            Debug.Log("Server update message received!");
            break;
            case Commands.PLAYER_JOINED:
            PlayerUpdateMsg pjMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
            Debug.Log("PlayerJoined!, new player id: " + pjMsg.player.id);
            CreatePlayer(pjMsg.player);
            break;
            case Commands.PLAYER_LEFT:
            PlayerUpdateMsg plMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
            Debug.Log("Player left, ID: " + plMsg.player.id);
            DestroyPlayer(plMsg.player);
            break;
            default:
            Debug.Log("Unrecognized message received!");
            break;
        }
    }


    private void CreatePlayer(NetworkObjects.NetworkPlayer _player)
    {
        GameObject newPlayer = Instantiate(playerPrefab, _player.cubPos, Quaternion.identity);


        newPlayer.GetComponentInChildren<TMP_Text>().text = _player.id;
        newPlayer.GetComponent<MeshRenderer>().material.color = new Color(_player.cubeColor.r, _player.cubeColor.g, _player.cubeColor.b);
        if(newPlayer.GetComponentInChildren<TMP_Text>().text == myID)
        {
            newPlayer.AddComponent<PlayerMovement>();
            Debug.Log("Created Player");
        }

        playerList.Add(newPlayer);
    }

    private void UpdatePlayers(List<NetworkObjects.NetworkPlayer> players)
    {
        foreach(var player in players)
        {
            GameObject playerObject = findPlayer(player.id);
            if(playerObject != null)
            {
                playerObject.transform.position = player.cubPos;
            }
        }
    }

    private void DestroyPlayer(NetworkObjects.NetworkPlayer _player)
    {
        GameObject player = findPlayer(_player.id);

        if(player != null)
        {
            playerList.Remove(player);

            Destroy(player);
        }
    }



    void Disconnect(){
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect(){
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
    }   

 


    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;
        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                OnConnect();
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                OnData(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                OnDisconnect();
            }

            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }
    }
}