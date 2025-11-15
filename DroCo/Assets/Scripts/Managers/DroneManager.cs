using System.Collections;
using System.Collections.Generic;
using Esri.ArcGISMapsSDK.Components;
using UnityEngine;
using System.Collections.Concurrent;
using UnityEngine.UI;

// DroneManager.cs
// Edited by Jakub Valeš
// Date: 14.5.2025
// Changes:
    // Added attributes: DroneMarks, DroneMarkPrefab
    // Added methods: AddDroneMark, HandleReceivedDronePosition
    // Changes in methods:
        // AddDrone: connectiing drone to ProximityObjectsUI and DroneFOVVisualizer
        // HandleReceivedDroneData: calling method to Update distance to icon


public class DroneManager : Singleton<DroneManager> {

    [SerializeField]
    private bool UseBuffer = false;

    public IDictionary<string, Drone> Drones = new Dictionary<string, Drone>();

    public GameObject DronePrefab;
    public IDictionary<string, DroneMark> DroneMarks = new Dictionary<string, DroneMark>();
    public GameObject DroneMarkPrefab;
    public Transform Scene3DView;


    public void HandleReceivedDroneList(DroneStaticData[] droneStaticDatas) {
        List<string> dronesToKeep = new List<string>();
        List<string> dronesToRemove = new List<string>();

        foreach (DroneStaticData dsd in droneStaticDatas) {
            if (Drones.ContainsKey(dsd.client_id)) {
                Drones[dsd.client_id].StaticData = dsd;
            } else {
                AddDrone(dsd);
            }

            dronesToKeep.Add(dsd.client_id);
        }

        foreach (KeyValuePair<string, Drone> drone in Drones) {
            if (!dronesToKeep.Contains(drone.Key)) {
                dronesToRemove.Add(drone.Key);
            }
        }

        foreach (string droneId in dronesToRemove) {
            Drones.TryGetValue(droneId, out Drone droneToBeRemoved);
            Drones.Remove(droneId);
            Destroy(droneToBeRemoved.gameObject);
        }
    }

    public void HandleReceivedDroneData(DroneFlightData flightData) {
        if (Drones.ContainsKey(flightData.client_id)) {
            GameManager.Instance.CenterMap(flightData);

            if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Pilot) {
                IconManager.Instance.UpdateDistaceToIcon(flightData.gps.latitude, flightData.gps.longitude, flightData.altitude); // Update distance to icon
            }

            if (UseBuffer) {
                Drones[flightData.client_id].DeliverNewFlightData(flightData);
            } else {
                Drones[flightData.client_id].UpdateDroneFlightData(flightData);
            }
        } else { //prisla data s neznamym drone ID -> pozadame server o novy seznam dronu
            if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Client) {
                WebSocketClient.Instance.SendDroneListRequest();
            }
        }
    }

    public void HandleReceivedVehicleData(DroneVehicleData vehicleData) {
        if (Drones.ContainsKey(vehicleData.client_id)) {
            Drones[vehicleData.client_id].UpdateDroneVehicleData(vehicleData);
        } else { //prisla data s neznamym drone ID -> pozadame server o novy seznam dronu
            if (GameManager.Instance.CurrentAppMode == GameManager.AppMode.Client) {
                WebSocketClient.Instance.SendDroneListRequest();
            }
        }
    }

    public void AddDrone(DroneStaticData dsd) {
        Debug.Log("adding new drone with id: " + dsd.client_id);

        GameObject newDroneGameObj = Instantiate(DronePrefab, Scene3DView);
        Drone newDrone = newDroneGameObj.GetComponent<Drone>();
        newDrone.InitDrone(dsd);
        Drones.Add(dsd.client_id, newDrone);

        // Find the ProximityObjectsUI and DroneFOVVisualizer components in the scene and set the tracked object
        // Only for pilot
        if(GameManager.Instance.CurrentAppMode == GameManager.AppMode.Pilot) {
            ProximityObjectsUI proximityUI = FindObjectOfType<ProximityObjectsUI>();
            if (proximityUI != null) {
                proximityUI.SetTrackedObject(newDroneGameObj.transform.GetChild(0).gameObject);
            } else {
                Debug.LogError("Proximity UI not found!");
            }
            
            DroneFOVVisualizer droneFOVVisualizer = FindObjectOfType<DroneFOVVisualizer>();
            if (droneFOVVisualizer != null) {
                droneFOVVisualizer.SetTrackedObject(newDroneGameObj.transform.GetChild(1).gameObject);
            } else {
                Debug.LogError("Drone FOV Visualizer not found!");
            }
        }

        // Init drone's UI
        newDrone.DroneListItem = UIManager.Instance.MainScreen.UnitList.SpawnListItemDrone(dsd, newDrone);
        RenderTexture renderTexture = new RenderTexture(1280, 720, 24);
        newDrone.TPVCamera.targetTexture = renderTexture;
        newDrone.DroneListItem.InitCameraViewTexture(renderTexture);
    }

    public void SetDroneModelsActive(bool active) {
        foreach (KeyValuePair<string, Drone> drone in Drones) {
            drone.Value.DroneModel.gameObject.SetActive(active);
        }
    }

    public void DestroyDroneAll() {
        foreach (KeyValuePair<string, Drone> drone in Drones) {
            DestroyDrone(drone.Value);
        }
        Drones.Clear();
    }

    public void DestroyDrone(Drone drone) {
        Destroy(drone.gameObject);
    }

    // Dronemark logic, adding new one to the scene and handling new postion, if the drone mark already exists
    public void AddDroneMark(string client_id) {
        if (DroneMarks.ContainsKey(client_id) || Drones.ContainsKey(client_id)) {
            Debug.LogWarning("DroneMark with id: " + client_id + " already exists!");
            return;
        }

        Debug.Log("adding new droneMARK with id: " + client_id);
        GameObject newDroneMarkObj = Instantiate(DroneMarkPrefab, Scene3DView);

        DroneMark newDroneMark = newDroneMarkObj.GetComponent<DroneMark>();
        newDroneMark.InitDroneMark(client_id);
        DroneMarks.Add(client_id, newDroneMark);
    }

    public void HandleReceivedDronePosition(string client_id, double latitude, double longitude, double altitude) {
        if(Drones.ContainsKey(client_id))
            return;
        if (DroneMarks.ContainsKey(client_id)) {
            DroneMarks[client_id].UpdateDroneMark(latitude, longitude, altitude);
        } else {
            AddDroneMark(client_id);
            DroneMarks[client_id].UpdateDroneMark(latitude, longitude, altitude);
        }
    }
}
