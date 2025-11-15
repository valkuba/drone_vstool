using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NativeWebSocket;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;

// WebSocketClient.cs
// Edited by Jakub Valeš
// Date: 14.5.2025
// Changes:
// Added attributes: Cmd_icon, cmd_instr, AnotherDroneInfo, AreaData, CornerData, AreaCellData, sendingDataToCom, ticksBetweenSends, lastSendTimeTicks, pendingMessage, successfulSends, totalSendAttempts, connectionDrops, networkQuality, lastNetworkCheckTicks, lastPingTime
// Added methods: AssessNetworkQuality, DirectSend, EnqueueFlightDataToCommander, SendClickInfo, SendDeleteMsgIcons, SendHighlightIconCommand, SendHighlightAreaCommand, SendDeleteInstrCommand, SendAreaCellActivate, SendDronInitToCommander, CheckPilotConnectedToCommander, HandleReceivedDronePosition, HandleReceivedAreaData
// Changes in methods:
    // Update - completely changed
    // HandleReceivedData - added new message types for anotations
    // GetWSURI - also redone a lot but the logic is the same
    // OnConnected - added pilot mode




public class WebSocketClient : Singleton<WebSocketClient> {
    /// <summary>
    /// Drone Server URI
    /// </summary>
    private string APIDomainWS = "";
    /// <summary>
    /// Websocket context
    /// </summary>
    private WebSocket websocket;

    private string ClientID;
    private bool handshake_done = false;

    private bool droneRequestSent = false;

    [Serializable]
    public class Response<T> {
        public string type;
        public T data;
    }

    [Serializable]
    private class HandshakeResponseData {
        public string client_id;    // missmatch 
        public int rtmp_port;
    }

    // Recieved JSON mesages
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
    private class cmd_instr {
        public string iconName;
    }

    [Serializable]
    private class AnotherDroneInfo {
        public string client_id;
        public double latitude;
        public double longitude;
        public double altitude;
    }

    [Serializable]
    private class AreaData {
        // Coordinates of all 4 corners
        public List<CornerData> corners;
        public string clientID;
        public int areaID;
    }

    [Serializable]
    private class CornerData {
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

    public bool sendingDataToCom = false; // was new drone added to the server?
    private long ticksBetweenSends = 5000000; // 40ms in ticks (10,000,000 ticks = 1 second)
    // variables for better sending rate control from pilot to commader (if all frames were sent the application would lag a lot)
    private long lastSendTimeTicks = 0;
    private volatile string pendingMessage = null;
    private int successfulSends = 0;
    private int totalSendAttempts = 0;
    private int connectionDrops = 0;

    // Network quality assessment
    private enum NetworkQuality {
        Good, Fair, Poor
    }
    private NetworkQuality networkQuality = NetworkQuality.Good;
    private long lastNetworkCheckTicks = 0;
    private long lastPingTime = 0;

    private void Update() {
        // Process WebSocket messages
        if (websocket != null && websocket.State == WebSocketState.Open) {
            websocket.DispatchMessageQueue();

            long currentTicks = DateTime.UtcNow.Ticks;
            // Calculate time since last successful send
            long elapsedTicks = currentTicks - lastSendTimeTicks;

            // Adjust send rate based on network conditions
            if (networkQuality == NetworkQuality.Good) {
                ticksBetweenSends = 2000000; // 25 FPS (40ms)
            } else if (networkQuality == NetworkQuality.Fair) {
                ticksBetweenSends = 4666667; // 15 FPS (66.7ms)
            } else {
                ticksBetweenSends = 10000000; // 10 FPS (100ms)
            }

            if (pendingMessage != null && elapsedTicks >= ticksBetweenSends) {
                string messageToSend = pendingMessage;
                pendingMessage = null;

                // Send the message
                if (!string.IsNullOrEmpty(messageToSend)) {
                    DirectSend(messageToSend);
                    lastSendTimeTicks = currentTicks;

                    // Track successful sends for network quality assessment
                    successfulSends++;
                    totalSendAttempts++;
                }
            }

            // Periodically assess network quality
            if (currentTicks - lastNetworkCheckTicks >= 50000000) { // Every 5 seconds
                AssessNetworkQuality();
                lastNetworkCheckTicks = currentTicks;
            }
        }
    }


    private void AssessNetworkQuality() {
        // Calculate success rate
        float successRate = totalSendAttempts > 0 ? (float) successfulSends / totalSendAttempts : 1.0f;

        // Reset counters
        successfulSends = 0;
        totalSendAttempts = 0;

        // Check ping time if available
        long pingTime = lastPingTime;

        // Simple quality assessment logic
        if (successRate > 0.95f && pingTime < 500) {
            networkQuality = NetworkQuality.Good;
        } else if (successRate > 0.85f && pingTime < 1000) {
            networkQuality = NetworkQuality.Fair;
        } else {
            networkQuality = NetworkQuality.Poor;
        }
    }


    private void DirectSend(string msg) {
        if (websocket != null && websocket.State == WebSocketState.Open) {
            try {
                // Use SendTextAsync with a timeout
                Task sendTask = websocket.SendText(msg);
            } catch (Exception ex) {
                Debug.LogError("WebSocket send error:" + ex.Message);
                totalSendAttempts++;
                connectionDrops++;
            }
        }
    }

    // Thread-safe version that works from any thread
    public void EnqueueFlightDataToCommander(/*WebSocketServerBehavior.Message<DroneFlightData>*/ string flightData) {
        if (!sendingDataToCom || string.IsNullOrEmpty(flightData))
            return;

        //Parse the JSON string to a JObject
        JObject jsonObject = JObject.Parse(flightData);

        //Change the client id recieved to the client id of this client
        jsonObject["data"]["client_id"] = ClientID;

        string newJson = jsonObject.ToString();
        pendingMessage = newJson;
    }

    // Send data to server - this is called from the main thread
    public void SendToServer(string msg) {
        // Control messages - send directly
        if (msg.Contains("\"type\":\"data_broadcast")) {
            pendingMessage = msg;
        } else {
            // Data messages - just update the pending message
            DirectSend(msg);
        }
    }
    public async void ConnectToServer(string domain, int port) {
        Debug.Log("Starting client");
        ClosePreviousConnection();

        try {
            APIDomainWS = GetWSURI(domain, port);
            websocket = new WebSocket(APIDomainWS);

            websocket.OnOpen += OnConnected;
            websocket.OnError += OnError;
            websocket.OnClose += OnClose;
            websocket.OnMessage += HandleReceivedData;

            await websocket.Connect();
        } catch (UriFormatException ex) {
            Debug.LogError(ex);
        }
    }

    public void Disconnect() {
        Debug.Log("Disconnecting client");
        if (websocket != null && websocket.State == WebSocketState.Open) {
            websocket.CancelConnection();
            websocket = null;
        }
    }

    private void ClosePreviousConnection() {
        if (websocket != null && websocket.State == WebSocketState.Open) {
            websocket.CancelConnection();
        }
    }

    /* Legacy SendtoServer with async await (lagging)
    public async void SendToServer(string msg) {
        if (websocket != null) {
            try {
                await websocket.SendText(msg);
            } catch (WebSocketException ex){
                Debug.LogError(ex);
            }
        }
    }*/

    public void SendDroneListRequest() {
        if (!droneRequestSent) {
            Debug.Log("Sending drone list request.");
            SendToServer("{\"type\":\"drone_list\"}");
            droneRequestSent = true;
            StartCoroutine(RequestTimeout());
        }
    }

    /*// Send new icon was created - not used anymore
    public void SendClickInfo(double latitude, double longitude, double altitude) {
        Response<Cmd_icon> cmd_icon = new Response<Cmd_icon> {
            data = new Cmd_icon()
        };
        cmd_icon.type = "cmd_icon";
        cmd_icon.data.latitude = latitude;
        cmd_icon.data.longitude = longitude;
        cmd_icon.data.altitude = altitude;
        //string msg = $"{{\"type\":\"cmd_icon\",\"data\":{{\"latitude\":\"{latitude}\",\"longitude\":\"{longitude}\",\"altitude\":\"{altitude}\"}}}}";
        string cmd_iconJson = JsonUtility.ToJson(cmd_icon);
        SendToServer(cmd_iconJson);
    }*/

    // Pilot delets an icon via the Instruction was succesfully done, so send the informatio to the commander
    public void SendDeleteMsgIcons(int iconID) {
        Response<Cmd_icon> cmd_icon = new Response<Cmd_icon> {
            data = new Cmd_icon()
        };
        cmd_icon.type = "cmd_delete_icons";
        cmd_icon.data.iconID = iconID;

        string data = JsonUtility.ToJson(cmd_icon);
        SendToServer(data);
    }

    // Pilot highlight icon based on the instrucion, so send the information to the commander
    public void SendHighlightIconCommand(int areaId) {
        Response<Cmd_icon> cmd_icon = new Response<Cmd_icon> {
            data = new Cmd_icon()
        };

        cmd_icon.type = "cmd_highlight_icon";
        cmd_icon.data.iconID = areaId;
        cmd_icon.data.clientID = ClientID;

        string data = JsonUtility.ToJson(cmd_icon);
        SendToServer(data);
    }

    public void SendHighlightAreaCommand(int areaId) {
        Response<Cmd_icon> cmd_icon = new Response<Cmd_icon> {
            data = new Cmd_icon()
        };

        cmd_icon.type = "cmd_highlight_area";
        cmd_icon.data.iconID = areaId;
        cmd_icon.data.clientID = ClientID;

        string data = JsonUtility.ToJson(cmd_icon);
        SendToServer(data);
    }

    // Instrucion was completed, send info to commander    
    public void SendDeleteInstrCommand() {
        Response<Cmd_icon> cmd_icon = new Response<Cmd_icon> {
            data = new Cmd_icon()
        };

        cmd_icon.type = "cmd_delete_instr";
        cmd_icon.data.clientID = ClientID;

        string data = JsonUtility.ToJson(cmd_icon);
        SendToServer(data);
    }

    // Drone activated an cell while searching the area, send the information to the commander
    public void SendAreaCellActivate(int areaID, int GridX, int GridY) {
        Response<AreaCellData> area_cell_data = new Response<AreaCellData> {
            data = new AreaCellData()
        };

        area_cell_data.type = "cmd_area_cell";
        area_cell_data.data.areaID = areaID;
        area_cell_data.data.GridX = GridX;
        area_cell_data.data.GridY = GridY;
        area_cell_data.data.clientID = ClientID;

        string data = JsonUtility.ToJson(area_cell_data);
        SendToServer(data);
    }

    // Send to commander that new drone was added to the scene so he can visualize it too
    public void SendDronInitToCommander() {
        if (!handshake_done)
            return;

        var Drones = DroneManager.Instance.Drones.FirstOrDefault();

        Response<DroneStaticData> flightDataNew = new Response<DroneStaticData> {
            type = "add_drone",
            data = new DroneStaticData()
        };
        flightDataNew.data.client_id = ClientID;    // id klienta, ne dronu 
        flightDataNew.data.drone_name = Drones.Value.StaticData.drone_name;
        flightDataNew.data.serial = Drones.Value.StaticData.serial;

        Debug.Log(ClientID);
        string flightDataNewJson = JsonUtility.ToJson(flightDataNew);
        SendToServer(flightDataNewJson);
        sendingDataToCom = true;
    }

    // Check if the pilot is connected to the commander and if so, send init Drone message and start re-sending flight data
    public void CheckPilotConnectedToCommander() {
        if (handshake_done) {
            //Debug.Log(DroneManager.Instance.Drones.Count);
            if (DroneManager.Instance.Drones.Count == 1 && !sendingDataToCom) {
                SendDronInitToCommander();
            }
        }
    }

    private IEnumerator RequestTimeout() {
        yield return new WaitForSeconds(10f);
        droneRequestSent = false;
    }

    public void SendCarDetectionRequest(string clientID, bool run = true) {
        Debug.Log("{\"type\":\"vehicle_detection_set\", \"data\":{\"drone_stream_id\":\"" + clientID + "\", \"state\":" + run.ToString().ToLower() + "}}");
        SendToServer("{\"type\":\"vehicle_detection_set\", \"data\":{\"drone_stream_id\":\"" + clientID + "\", \"state\":" + run.ToString().ToLower() + "}}");
    }

    private void HandleReceivedData(byte[] message) {
        string msgstr = Encoding.Default.GetString(message);
        Response<string> msg = JsonUtility.FromJson<Response<string>>(msgstr);

        if (!handshake_done && msg.type == "hello_resp") {
            Response<HandshakeResponseData> hr = JsonUtility.FromJson<Response<HandshakeResponseData>>(msgstr);
            if (hr != null) {
                ClientID = hr.data.client_id;
                GameManager.Instance.RTMPPort = hr.data.rtmp_port;
                handshake_done = true;
                Debug.Log("Handshake successful.");
                GameManager.Instance.HandleHandshakeDone();

                // If the drone is already added while connecting to server
                // send the init drone message to the server and start re-sending flight data
                if (DroneManager.Instance.Drones.Count == 1 && !sendingDataToCom)
                    SendDronInitToCommander();

            }
        } else if (handshake_done && msg.type == "drone_list_resp") {
            Response<DroneStaticData[]> dsdr = JsonUtility.FromJson<Response<DroneStaticData[]>>(msgstr);
            Debug.Log(msgstr);
            DroneManager.Instance.HandleReceivedDroneList(dsdr.data);
            droneRequestSent = false;
        } else if (handshake_done && msg.type == "data_broadcast") {
            Response<DroneFlightData> dsfdr = JsonUtility.FromJson<Response<DroneFlightData>>(msgstr);
            DroneManager.Instance.HandleReceivedDroneData(dsfdr.data);

        } else if (handshake_done && msg.type == "vehicle_detection_rects") {
            Response<DroneVehicleData> dsvdr = JsonUtility.FromJson<Response<DroneVehicleData>>(msgstr);
            //Debug.Log(msgstr);
            DroneManager.Instance.HandleReceivedVehicleData(dsvdr.data);

        } else if (msg.type == "cmd_icon") {
            //Debug.Log("cmd_icon message received");
            Response<Cmd_icon> parsed = JsonUtility.FromJson<Response<Cmd_icon>>(msgstr);
            IconManager.Instance.callIconSpawn(parsed.data.latitude, parsed.data.longitude, parsed.data.altitude, parsed.data.iconName, parsed.data.iconID, "");

        } else if (msg.type == "cmd_delete_icons") {
            //Debug.Log("Delete Icons message received");
            Response<Cmd_icon> parsed = JsonUtility.FromJson<Response<Cmd_icon>>(msgstr);
            IconManager.Instance.callDeleteIcons(parsed.data.iconID);

        } else if (msg.type == "add_drone_mark") {
            Response<AnotherDroneInfo> parsed = JsonUtility.FromJson<Response<AnotherDroneInfo>>(msgstr);
            //Debug.Log("Add drone mark message received " + parsed.data.client_id);
            DroneManager.Instance.AddDroneMark(parsed.data.client_id);

        } else if (msg.type == "another_drone_position") {
            //Debug.Log("Another drone position message received");
            Response<AnotherDroneInfo> parsed = JsonUtility.FromJson<Response<AnotherDroneInfo>>(msgstr);
            if (parsed.data.client_id == ClientID) // own drone position - ignore
            {
                Debug.Log("Own drone position - ignoring");
                return;
            }
            StartCoroutine(HandleReceivedDronePosition(parsed.data.client_id, parsed.data.longitude, parsed.data.latitude, parsed.data.altitude));

        } else if (msg.type == "cmd_instr") {
            Response<cmd_instr> parsed = JsonUtility.FromJson<Response<cmd_instr>>(msgstr);
            //Debug.Log("cmd_instr message received - " + parsed.data.iconName);
            PilotUIManager.Instance.DisplayIcon(parsed.data.iconName);

        } else if (msg.type == "cmd_highlight_icon") {
            //Debug.Log("cmd_highlight_icon message received");
            Response<Cmd_icon> parsed = JsonUtility.FromJson<Response<Cmd_icon>>(msgstr);
            IconManager.Instance.HighlightIcon(parsed.data.iconID, parsed.data.clientID);

        } else if (msg.type == "cmd_instr_clear") {
            //Debug.Log("delete instr icon message received");
            PilotUIManager.Instance.DeleteInstr();

        } else if (msg.type == "cmd_area") {
            //Debug.Log("cmd_area message received");
            Response<AreaData> parsed = JsonUtility.FromJson<Response<AreaData>>(msgstr);
            StartCoroutine(HandleReceivedAreaData(parsed.data));

        } else if (msg.type == "cmd_highlight_area") {
            //Debug.Log("cmd_highlight_area message received");
            Response<Cmd_icon> parsed = JsonUtility.FromJson<Response<Cmd_icon>>(msgstr);
            IconManager.Instance.HighlightArea(parsed.data.iconID, parsed.data.clientID);

        } else if (msg.type == "delete_areas") {
            //Debug.Log("Delete AREAS message received");
            Response<Cmd_icon> parsed = JsonUtility.FromJson<Response<Cmd_icon>>(msgstr);
            IconManager.Instance.callDeleteArea(parsed.data.iconID);

        } else if (!handshake_done && msg.type == "data_broadcast") {
        } else {
            Debug.LogError("Unknown data received! " + msgstr);
        }
    }

    // Handle drone mark position (so not the pilots drone but some other one)
    private IEnumerator HandleReceivedDronePosition(string client_id, double longitude, double latitude, double altitude) {
        DroneManager.Instance.HandleReceivedDronePosition(client_id, longitude, latitude, altitude);
        yield return null;
    }

    // Create a new area based on sent corners from the server
    private IEnumerator HandleReceivedAreaData(AreaData areaData) {
        List<(double latitude, double longitude, double altitude)> AllCorners = new List<(double latitude, double longitude, double altitude)>();
        foreach (var corner in areaData.corners) {
            AllCorners.Add(((double) corner.latitude, (double) corner.longitude, (double) corner.altitude));
        }
        IconManager.Instance.HandleReceivedAreaData(AllCorners, areaData.areaID, areaData.clientID);

        yield return null;
    }

    private void OnClose(WebSocketCloseCode closeCode) {
        Debug.Log("Connection closed!");
        handshake_done = false;
        GameManager.Instance.HandleConnectionFailed();
    }

    private void OnError(string errorMsg) {
        Debug.LogError(errorMsg);
        handshake_done = false;
        GameManager.Instance.HandleConnectionFailed();
    }

    private void OnConnected() {
        Debug.Log("Connected - sending handshake");
        if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Pilot)
            SendToServer("{\"type\":\"hello\",\"data\":{\"ctype\":2}}");
        else
            SendToServer("{\"type\":\"hello\",\"data\":{\"ctype\":1}}");
    }


    /// Create websocket URI from domain name and port
    public string GetWSURI(string domain, int port) {
        // Clean the domain input - remove any whitespace, etc.
        domain = domain.Trim();

        // Handle ngrok URLs specially
        if (domain.Contains("ngrok")) {
            // If the domain has a protocol, use it as is
            if (domain.StartsWith("ws://") || domain.StartsWith("wss://")) {
                Debug.Log("Using provided ngrok WebSocket URL: " + domain);
                return domain;
            }

            // For ngrok domains, default to secure WebSocket if not specified
            Debug.Log("Creating ngrok WS URI: " + "wss://" + domain);
            return "wss://" + domain;
        }

        // For regular IPs or domains, use standard WebSocket with port
        Debug.Log("Creating standard WS URI: " + "ws://" + domain);
        return "ws://" + domain;
    }

    private async void OnApplicationQuit() {
        await websocket?.Close();
    }

}
