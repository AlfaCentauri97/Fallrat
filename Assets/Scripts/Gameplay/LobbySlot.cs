using TMPro;
using UnityEngine;

/// <summary>
/// Lobby slot UI controller (occupied state + label).
/// </summary>
    
public sealed class LobbySlot : MonoBehaviour
{
    [SerializeField] Canvas canvas;
    [SerializeField] TextMeshProUGUI label;

    public void SetOccupied(bool occupied, bool isLocalPlayer)
    {
        if (canvas)
            canvas.gameObject.SetActive(occupied);

        if (!occupied || !label) return;

        label.text = isLocalPlayer ? "YOU" : "PLAYER";
    }

    public void Clear()
    {
        if (canvas)
            canvas.gameObject.SetActive(false);
    }
}