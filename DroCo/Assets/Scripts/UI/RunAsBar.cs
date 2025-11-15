using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// RunAsBar.cs
// Edited by Jakub Valeš
// Date: 14.5.2025
// Changes:
    // Changes in methods:
        // OnDropdownChanged: added pilot mode

public class RunAsBar : MonoBehaviour {

    [SerializeField]
    private Image outline;

    public void OnPointerEnter() {
        outline.color = Color.white;
    }

    public void OnPointerExit() {
        outline.color = Color.grey;
    }

    public void OnDropdownChanged(TMP_Dropdown dropdown) {
        GameManager.AppMode mode;
        switch (dropdown.value) {
            case 0:
                mode = GameManager.AppMode.Client;
                break;
            case 1:
                mode = GameManager.AppMode.Server;
                break;
            case 2:
                mode = GameManager.AppMode.Pilot;
                break;
            default:
                mode = GameManager.AppMode.Client;
                break;
        }

        GameManager.Instance.ChangeAppMode(mode);
    }

}
