using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// ListItemButton.cs
// Edited by Jakub Valeš
// Date: 14.5.2025
// Changes:
    // Added attributes: iconImage, FPVButton
    // Added methods: UpdateInstruction, OnButtonClickFPV
    // Changes in methods:
        // Awake

public class ListItemButton : MonoBehaviour {

    [SerializeField]
    private TMP_Text UnitIDText;
    [SerializeField]
    private TMP_Text UnitTypeText;
    [SerializeField]
    private TMP_Text DistanceText;
    [SerializeField]
    private TMP_Text AltitudeText;
    [SerializeField]
    private GameObject CameraView;
    [SerializeField]
    private RawImage CameraViewImage;
    [SerializeField]
    private TMP_Text DelayText;
    [SerializeField]
    private Image iconImage;
    [SerializeField]
    private Button FPVButton;

    private InteractiveObject interactiveObject;
    private Button button;
    private bool buttonSelected = false;

    private float lastClick = 0f;
    private float doubleClickInterval = 0.4f;

    private void Awake() {
        CameraView.SetActive(false);
        button = GetComponent<Button>();
        //button.onClick.AddListener(OnButtonClickFPV); // Assign the click event
    }

    public void OnClick() {

    }

    public void OnPointerEnter() {
        if (!buttonSelected) {
            CameraView.SetActive(true);
            interactiveObject.Highlight(true);
        }
    }

    public void OnPointerExit() {
        if (!buttonSelected) {
            CameraView.SetActive(false);
            interactiveObject.Highlight(false);
        }
    }

    public void OnPointerClick() {
        if ((lastClick + doubleClickInterval) > Time.time) {
            interactiveObject.FocusCamera();
            OnDeselect();
        }
        lastClick = Time.time;
    }

    public void OnSelect() {
        buttonSelected = true;
        CameraView.SetActive(true);
        interactiveObject.Highlight(true);
        interactiveObject.SetCameras();
    }

    public void OnDeselect() {
        buttonSelected = false;
        CameraView.SetActive(false);
        interactiveObject.Highlight(false);
    }

    public void InitUnitData(string unitID, string unitType, InteractiveObject intObject) {
        UnitIDText.text = unitID;
        UnitTypeText.text = unitType;
        interactiveObject = intObject;
    }

    public void UpdateHeight(double height) {
        AltitudeText.text = "H:" + height.ToString("0.00") + "m";
    }

    public void UpdateDistance(float distance) {
        DistanceText.text = "D:" + distance.ToString("0.00") + "m";
    }

    public void InitCameraViewTexture(RenderTexture texture) {
        CameraViewImage.texture = texture;
    }

    public void ChangeDelay(float delay) {
        DelayText.text = "Delay: " + delay.ToString() + " ms";
        interactiveObject.ChangeFlightDataDelay(delay);
    }

    // Show instruction for each pilot in the commander mode (in the list)
    public void UpdateInstruction(string iconName) {
        Sprite iconSprite = PilotUIManager.Instance.ChooseSprite(iconName);
        if (iconSprite == null)
        {
            // no instruction
            iconImage.sprite = iconSprite;
            iconImage.gameObject.SetActive(false);
            return;
        }
        iconImage.sprite = iconSprite;
        iconImage.gameObject.SetActive(true);
    }

    // Show FPV mode (check if its available)
    public void OnButtonClickFPV() {
        //Debug.Log("Button clicked!" + UnitIDText.text);
        if(!UIButtonManager.Instance.arevisible)
        {
            // if drone screen isnt visible, show it and then you can change view to fpv
            UIButtonManager.Instance.BtnToggleDroneScreen();
        }
        CameraManager.Instance.SwitchCameraViewCommander(UnitIDText.text);
        OnPointerExit();
    }
}
