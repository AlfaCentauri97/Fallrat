using Unity.Cinemachine;
using UnityEngine;
using Project.Core;

public sealed class CameraMgr : SingletonMonoBehaviour<CameraMgr>
{
    [SerializeField] CinemachineCamera gameCam;
    [SerializeField] CinemachineCamera endCam;

    void Start()
    {
        SetGameCamera();
    }

    public void SetGameCamera()
    {
        if (!gameCam || !endCam) return;

        gameCam.gameObject.SetActive(true);
        endCam.gameObject.SetActive(false);
    }

    public void SetEndCamera()
    {
        if (!gameCam || !endCam) return;

        gameCam.gameObject.SetActive(false);
        endCam.gameObject.SetActive(true);
    }

    public void ToggleCamera()
    {
        if (!gameCam || !endCam) return;

        bool gameActive = gameCam.gameObject.activeSelf;
        gameCam.gameObject.SetActive(!gameActive);
        endCam.gameObject.SetActive(gameActive);
    }
}