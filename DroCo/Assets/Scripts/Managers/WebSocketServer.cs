using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using PimDeWitte.UnityMainThreadDispatcher;
using UnityEditor;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Net.NetworkInformation;

// WebSocketServer.cs
// Edited by Jakub Valeš
// Date: 14.5.2025
// Changes:
// WebSocketServerBehavior
// Added attributes: cmd_icon, AnotherDroneInfo, AreaCellData
// Added methods: UpdateDropdownCommander, CheckPilotConnectionToCommander, SendDronesPositionToClientsInit
// Changes in methods:
    // OnMessage - redone a lot, but the main logic is the same, just added new messages types support and the add drone, and data_broadcast are supporting resending to the pilots
    // DoHandshake - the add new drone is changed to support resending to the commander (as pilot server)
    // AddDrone - start sedning the drone position to the clients (if atleast 2 drones are connected)
// WebSocketServer
// Added attributes: Message<T>, Cmd_icon, AreaData, CornerData
// Added methods: SendClickInfo, SendIconToUI, ClearPilotUI, SendDeleteMsgIcon, SendDeleteMsgArea, SendHighlightIconCommand, SendHighlightAreaCommand, SendAreaData, MySendMessageToClient
// Changes in methods:
    // StartServer - changed the port to 8080 for the server and 5556 for the client
    // GetLocalIPAddress - changed the order of the IP addresses to prefer Wi-Fi over Ethernet


public class TestBehavior : WebSocketBehavior {
    protected override void OnOpen() {
        base.OnOpen();
        Debug.Log("Connection open");
    }

    protected override void OnClose(CloseEventArgs e) {
        base.OnClose(e);
        Debug.Log("Connection close: " + e.Reason);
    }

    protected override void OnError(ErrorEventArgs e) {
        base.OnError(e);
        Debug.Log("Connection error: " + e.Message + " ..exception: " + e.Exception);
    }

    protected override void OnMessage(MessageEventArgs e) {
        base.OnMessage(e);
        Debug.Log(e.Data);

        try {
            Send("hello response");
        } catch (Exception ex) {
            Debug.LogError(ex.Message);
        }
    }
}

public class WebSocketServerBehavior : WebSocketBehavior {

    [Serializable]
    public class Message<T> {
        public string type;
        public T data;
    }

    [Serializable]
    private class Hello {
        public string ctype;
        public string drone_name;
        public string serial;
    }

    [Serializable]
    private class HelloResponse {
        public string client_id;
        public string rtmp_port;
    }

    // For the json messages
    [Serializable]
    private class Cmd_icon {
        public double latitude;
        public double longitude;
        public double altitude;
        public string iconName;
        public int iconID;
        public string clientID;
    }

    [Serializable]
    private class AnotherDroneInfo {
        public string client_id;
        public double latitude;
        public double longitude;
        public double altitude;
    }

    [Serializable]
    private class AreaCellData {
        public int areaID;
        public int GridX;
        public int GridY;
        public string clientID; // probably not needed idk
    }

    private bool handshake_done = false;
    private static bool request_drones_position = false;   // if true, send drones position to clients

    protected override void OnOpen() {
        base.OnOpen();
        Debug.Log("Connection open");
    }

    protected override void OnMessage(MessageEventArgs e) {
        base.OnMessage(e);

        //Debug.Log(e.Data);
        Message<string> msg = JsonUtility.FromJson<Message<string>>(e.Data);
        if (msg.type == "hello") {
            DoHandshake(ID, JsonUtility.FromJson<Message<Hello>>(e.Data));

        } else if (handshake_done && msg.type == "add_drone") {     // New drone needs to be added to the scene
            Message<DroneFlightData> dfd = JsonUtility.FromJson<Message<DroneFlightData>>(e.Data);
            Message<Hello> droneData = JsonUtility.FromJson<Message<Hello>>(e.Data);
            DroneStaticData newDrone = new DroneStaticData {
                client_id = dfd.data.client_id,
                drone_name = droneData.data.drone_name,
                serial = droneData.data.serial
            };

            UnityMainThreadDispatcher.Instance().Enqueue(AddDrone(newDrone));
            if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Server)
                UnityMainThreadDispatcher.Instance().Enqueue(UpdateDropdownCommander());

            UnityMainThreadDispatcher.Instance().Enqueue(UpdateDropdownCommander());

        } else if (handshake_done && msg.type == "data_broadcast") {

            Message<DroneFlightData> dfd = JsonUtility.FromJson<Message<DroneFlightData>>(e.Data);

            // Always display the drone data to the screen
            UnityMainThreadDispatcher.Instance().Enqueue(UpdateDroneFlightData(dfd.data));

            // Recieve flight data from drone if the instance is pilots, send drone data to commander
            if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Pilot) { 
                WebSocketClient.Instance.EnqueueFlightDataToCommander(e.Data);
            }

            // If the instance is server, send drone data to all clients so they can display drone mark in the scene
            if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Server) {
                if (request_drones_position)
                    SendDronesPositionToClients(dfd.data);
            }

        } else if (msg.type == "cmd_delete_icons") {
            //Debug.Log("Delete Icons message received");
            Message<Cmd_icon> parsed = JsonUtility.FromJson<Message<Cmd_icon>>(e.Data);
            UnityMainThreadDispatcher.Instance().Enqueue(() => {
                IconManager.Instance.callDeleteIcons(parsed.data.iconID);
            });

        } else if (msg.type == "cmd_highlight_area"){
            //Debug.Log("cmd_highlight_icon message received");
            Message<Cmd_icon> parsed = JsonUtility.FromJson<Message<Cmd_icon>>(e.Data);
            UnityMainThreadDispatcher.Instance().Enqueue(() => {
                IconManager.Instance.HighlightArea(parsed.data.iconID);
            });

        } else if (msg.type == "cmd_delete_instr") {
            //Debug.Log("Delete Instruction message received");
            Message<Cmd_icon> parsed = JsonUtility.FromJson<Message<Cmd_icon>>(e.Data);
            UnityMainThreadDispatcher.Instance().Enqueue(() => {
                UIButtonManager.Instance.ClearPilotUICommander(parsed.data.clientID);
            });

        } else if (msg.type == "cmd_area_cell") {
            //Debug.Log("cmd_area_cell message received");
            Message<AreaCellData> parsed = JsonUtility.FromJson<Message<AreaCellData>>(e.Data);
            UnityMainThreadDispatcher.Instance().Enqueue(() => {
                IconManager.Instance.HandleAreaCell(parsed.data.areaID, parsed.data.GridX, parsed.data.GridY, parsed.data.clientID);
            });

        } else if (msg.type == "cmd_highlight_icon") {
            //Debug.Log("cmd_highlight_area message received");
            Message<Cmd_icon> parsed = JsonUtility.FromJson<Message<Cmd_icon>>(e.Data);
            UnityMainThreadDispatcher.Instance().Enqueue(() => {
                IconManager.Instance.HighlightIcon(parsed.data.iconID, parsed.data.clientID);
            });

        } else {
            Debug.LogError("Unknown data received! " + e.Data);
        }
    }

    protected override void OnClose(CloseEventArgs e) {
        base.OnClose(e);
        Debug.Log("Connection close: " + e.Reason);
        UnityMainThreadDispatcher.Instance().Enqueue(HandleClientDisconnected());

    }

    protected override void OnError(ErrorEventArgs e) {
        base.OnError(e);
        Debug.Log("Connection error: " + e.Message + " ..exception: " + e.Exception);
    }

    private void DoHandshake(string clientID, Message<Hello> droneData) {
        Message<HelloResponse> helloResponse = new Message<HelloResponse> {
            data = new HelloResponse()
        };
        helloResponse.type = "hello_resp";
        helloResponse.data.client_id = clientID;
        helloResponse.data.rtmp_port = "1935";

        string msg = JsonUtility.ToJson(helloResponse);
        Debug.Log("Sending:" + msg);

        Send(msg);

        handshake_done = true;
        UnityMainThreadDispatcher.Instance().Enqueue(HandleClientConnected());

        // If the connected client is drone sending flight data
        if (droneData.data.ctype == "0") {
            DroneStaticData newDrone = new DroneStaticData {
                client_id = clientID,
                drone_name = droneData.data.drone_name,
                serial = droneData.data.serial
            };

            UnityMainThreadDispatcher.Instance().Enqueue(AddDrone(newDrone));

            if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Server)
                UnityMainThreadDispatcher.Instance().Enqueue(UpdateDropdownCommander());

            // If the app is Pilot app connects drone, check if the commander is connected and send the data
            if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Pilot) {
                // Needs to wait after the AddDrone function is finished
                UnityMainThreadDispatcher.Instance().Enqueue(CheckPilotConnectionToCommander());
            }
        }
    }

    // Commander sends cliets the postion of another drones, so they can update its drone mark position
    public void SendDronesPositionToClients(DroneFlightData flightData) {
        Message<AnotherDroneInfo> msg = new Message<AnotherDroneInfo> {
            data = new AnotherDroneInfo()
        };
        msg.type = "another_drone_position";
        msg.data.client_id = flightData.client_id;
        msg.data.latitude = flightData.gps.latitude;
        msg.data.longitude = flightData.gps.longitude;
        msg.data.altitude = flightData.altitude;
        string data = JsonUtility.ToJson(msg);
        WebSocketServer.Instance.MySendMessageToClient(data, "");
    }

    // New drone was added to the scene, send the information to client as well so they can create dronw mark
    public void SendDronesPositionToClientsInit() {
        var Drones = DroneManager.Instance.Drones;
        foreach (string key in Drones.Keys) {
            Message<AnotherDroneInfo> msg = new Message<AnotherDroneInfo> {
                data = new AnotherDroneInfo()
            };
            msg.type = "add_drone_mark";
            //Debug.Log("Sending drone position to client: " + key);
            msg.data.client_id = key;
            msg.data.latitude = 0.0;
            msg.data.longitude = 0.0;
            msg.data.altitude = 0.0;
            string data = JsonUtility.ToJson(msg);
            Send(data);
        }
        request_drones_position = true;
    }

    private IEnumerator HandleClientConnected() {
        GameManager.Instance.HandleClientConnected();
        yield return null;
    }

    private IEnumerator HandleClientDisconnected() {
        GameManager.Instance.HandleClientDisconnected();
        yield return null;
    }

    private IEnumerator AddDrone(DroneStaticData newDrone) {
        DroneManager.Instance.AddDrone(newDrone);
        if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Server && DroneManager.Instance.Drones.Count > 1) {
            request_drones_position = true;
            //SendDronesPositionToClientsInit();
        }
        yield return null;
    }

    private IEnumerator UpdateDroneFlightData(DroneFlightData flightData) {
        DroneManager.Instance.HandleReceivedDroneData(flightData);
        yield return null;
    }

    // Add new drone to the commanders dropdown list
    private IEnumerator UpdateDropdownCommander() {
        UIButtonManager.Instance.UpdateDroneDropdown();
        yield return null;
    }
    // Check of the pilot is already connected to the commander
    private IEnumerator CheckPilotConnectionToCommander() {
        WebSocketClient.Instance.CheckPilotConnectedToCommander();
        yield return null;
    }
}

public class WebSocketServer : Singleton<WebSocketServer> {
    // JSON messages
    [Serializable]
    private class Message<T> {
        public string type;
        public T data;
    }

    [Serializable]
    private class Cmd_icon {
        public double latitude;
        public double longitude;
        public double altitude;
        public string iconName;
        public int iconID;
        public string clientID;
    }

    [Serializable]
    private class AreaData
    {
        // Coordinates of all 4 corners
        public List<CornerData> corners;
        
        // Client ID
        public string clientID;
        
        // Area ID
        public int areaID;
    }

    [Serializable]
    private class CornerData
    {
        public double latitude;
        public double longitude;
        public double altitude;
    }

    public string Address;
    public string Port;

    private WebSocketSharp.Server.WebSocketServer Server;

    private string clientID;

    public void StartServer() {
        Debug.Log("Starting server");

        try {
            if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Server) {
                Port = "8080";
            } else {
                Address = GetLocalIPAddress();
                Port = "5556";
            }
            Server = new WebSocketSharp.Server.WebSocketServer("ws://" + Address + ":" + Port);
            Server.AddWebSocketService<WebSocketServerBehavior>("/");

            //Server.AddWebSocketService<TestBehavior>("/test");

            Server.Start();
            Debug.Log("Server started on " + Address + " and port " + Port);

        } catch (Exception ex) {
            Debug.LogError(ex.Message);
            Port = (int.Parse(Port) + 1).ToString();
            StartServer();
            return;
        }

        GameManager.Instance.HandleServerRunning(GetLocalIPAddress() + ":" + Port);

    }

    private string GetLocalIPAddress() {
        try {
            // Get all network interfaces
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            string ethernetIP = null;
            string wifiIP = null;
            string otherIP = null;

            foreach (NetworkInterface ni in interfaces) {
                // Check if the network interface is up and has IP addresses
                if (ni.OperationalStatus == OperationalStatus.Up) {
                    foreach (UnicastIPAddressInformation ipInfo in ni.GetIPProperties().UnicastAddresses) {
                        // We're only interested in IPv4 addresses
                        if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork) {
                            // Check if it's Ethernet
                            if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) {
                                ethernetIP = ipInfo.Address.ToString();
                            }
                            // Check if it's Wi-Fi
                            else if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) {
                                wifiIP = ipInfo.Address.ToString();
                            }
                            // Store other network interfaces
                            else if (otherIP == null) {
                                otherIP = ipInfo.Address.ToString();
                            }
                        }
                    }
                }
            }

            // Prioritize Ethernet, then Wi-Fi, then others
            // swapped
            if (wifiIP != null) {
                Debug.Log("Using Wi-Fi IP: " + wifiIP);
                return wifiIP;
            }
            if (ethernetIP != null) {
                Debug.Log("Using Ethernet IP: " + ethernetIP);
                return ethernetIP;
            }
            if (otherIP != null) {
                Debug.Log("Using Other IP: " + otherIP);
                return otherIP;
            }

            throw new System.Exception("No valid network adapters found!");
        } catch (System.Exception e) {
            Debug.LogError("Error retrieving local IP address: " + e.Message);
            return "0.0.0.0";
        }
    }

    public void CloseServer() {
        Debug.Log("Closing server");
        if (Server != null) {
            Server.Stop();
            Server = null;
        }
    }

    private void SendMessageToClient() {
        clientID = Server.WebSocketServices["/test"].Sessions.IDs.First();
        Debug.Log("Sending test hello message to client " + clientID);
        try {
            Server.WebSocketServices["/test"].Sessions.SendTo("test hello message", clientID);
        } catch (Exception e) {
            Debug.LogError(e);
        }

    }

    private void OnApplicationQuit() {
        if (Server != null) {
            Server.Stop();
            Server = null;
        }
    }

    // New icon was created so send its location to the client so he can create its in his application
    public void SendClickInfo(double latitude, double longitude, double altitude, string iconName, int iconID, string clientID) {
        Message<Cmd_icon> cmd_icon = new Message<Cmd_icon> {
            data = new Cmd_icon()
        };
        cmd_icon.type = "cmd_icon";
        cmd_icon.data.latitude = latitude;
        cmd_icon.data.longitude = longitude;
        cmd_icon.data.altitude = altitude;
        cmd_icon.data.iconName = iconName;
        cmd_icon.data.iconID = iconID;
        cmd_icon.data.clientID = clientID;
        string data = JsonUtility.ToJson(cmd_icon);
        MySendMessageToClient(data, clientID);
    }

    // Commadner sends new instruction icon to clients
    public void SendIconToUI(string iconType, string pilotId) {
        Message<Cmd_icon> cmd_icon = new Message<Cmd_icon> {
            data = new Cmd_icon()
        };
        cmd_icon.type = "cmd_instr";
        cmd_icon.data.iconName = iconType;

        string data = JsonUtility.ToJson(cmd_icon);
        MySendMessageToClient(data, pilotId);
    }

    // Delete current instruion icon from the UI
    public void ClearPilotUI(string pilotId) {
        // Vytvoření JSON zprávy
        Message<Cmd_icon> cmd_icon = new Message<Cmd_icon> {
            data = new Cmd_icon()
        };
        cmd_icon.type = "cmd_instr_clear";

        string data = JsonUtility.ToJson(cmd_icon);
        MySendMessageToClient(data, pilotId);
    }

    // Delete icon based on iconID (-1 means all icons)
    public void SendDeleteMsgIcon(int iconID) {
        Message<Cmd_icon> cmd_icon = new Message<Cmd_icon> {
            data = new Cmd_icon()
        };
        cmd_icon.type = "cmd_delete_icons";
        cmd_icon.data.iconID = iconID;

        string data = JsonUtility.ToJson(cmd_icon);
        MySendMessageToClient(data, "");
    }

    // Delete area based on areaID 
    public void SendDeleteMsgArea(int areaID) { //dont need the id because we are orienting by the areaId
        Message<Cmd_icon> cmd_icon = new Message<Cmd_icon> {
            data = new Cmd_icon()
        };
        cmd_icon.type = "delete_areas";
        cmd_icon.data.iconID = areaID;

        string data = JsonUtility.ToJson(cmd_icon);
        MySendMessageToClient(data, "");
    }

    // Send highlight command for a specific icon
    public void SendHighlightIconCommand(int iconId, string clientId) {
        Message<Cmd_icon> cmd_icon = new Message<Cmd_icon> {
            data = new Cmd_icon()
        };

        cmd_icon.type = "cmd_highlight_icon";
        cmd_icon.data.iconID = iconId;
        cmd_icon.data.clientID = clientId;

        string data = JsonUtility.ToJson(cmd_icon);
        MySendMessageToClient(data, clientId);
    }

    public void SendHighlightAreaCommand(int iconId, string clientId) {
        Message<Cmd_icon> cmd_icon = new Message<Cmd_icon> {
            data = new Cmd_icon()
        };

        cmd_icon.type = "cmd_highlight_area";
        cmd_icon.data.iconID = iconId;
        cmd_icon.data.clientID = clientId;

        string data = JsonUtility.ToJson(cmd_icon);
        MySendMessageToClient(data, clientId);
    }


    // Send area data to a specific client
    public void SendAreaData(List<(double latitude, double longitude, double altitude)> corners, int areaId, string clientId)
    {
        Message<AreaData> areaMessage = new Message<AreaData>
        {
            data = new AreaData()
        };
        
        areaMessage.type = "cmd_area";
        areaMessage.data.areaID = areaId;
        areaMessage.data.clientID = clientId;
        
        // Add all corners
        areaMessage.data.corners = new List<CornerData>();
        foreach (var corner in corners)
        {
            areaMessage.data.corners.Add(new CornerData
            {
                latitude = corner.latitude,
                longitude = corner.longitude,
                altitude = corner.altitude
            });
        }
        
        string data = JsonUtility.ToJson(areaMessage);
        MySendMessageToClient(data, clientId);
    }


    // Send message to a specific client or all clients if clientId is null or empty
    public void MySendMessageToClient(string message, string clientId) {
        bool sendToAll = clientId == null || clientId.Trim() == string.Empty;

        foreach (var path in Server.WebSocketServices.Paths) {
            var service = Server.WebSocketServices[path];

            if (sendToAll) {
                // Send to all clietns connected
                foreach (var id in service.Sessions.IDs) {
                    //Debug.Log("Sending message to client " + id);
                    service.Sessions.SendTo(message, id);
                }
            } else {
                // if client withi this id exists send message
                if (service.Sessions.IDs.Contains(clientId)) {
                    //Debug.Log("Sending message to client " + clientId);
                    service.Sessions.SendTo(message, clientId);
                } else {
                    Debug.LogWarning("Client with ID " + clientId + " not found");
                }
            }
        }
    }
}
