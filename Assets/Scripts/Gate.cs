using System.Collections.Generic;
using UnityEngine;
using Project.Pooling;

public sealed class Gate : MonoBehaviour, IPoolable
{
    [SerializeField] List<GameObject> traps = new();
    [SerializeField] int forcedIndex = -1;

    int activeIndex = -1;

    public int TrapCount => traps != null ? traps.Count : 0;

    public void OnSpawned()
    {
        activeIndex = -1;

        if (forcedIndex >= 0)
        {
            SetTrapIndex(forcedIndex);
        }
        else
        {
            DisableAll();
        }
    }

    public void OnDespawned()
    {
        activeIndex = -1;
        DisableAll();
    }

    public void SetTrapIndex(int index)
    {
        int count = TrapCount;
        if (count <= 0) return;

        index = Mathf.Clamp(index, 0, count - 1);
        if (index == activeIndex) return;

        for (int i = 0; i < count; i++)
        {
            var go = traps[i];
            if (go) go.SetActive(i == index);
        }

        activeIndex = index;
    }

    void DisableAll()
    {
        int count = TrapCount;
        for (int i = 0; i < count; i++)
        {
            var go = traps[i];
            if (go) go.SetActive(false);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        UIManager.Instance.AddScore();
        AudioManager.Instance.PlayHitEffect("Woosh", 0.15f);
        Debug.Log("gate hit");
    }
}