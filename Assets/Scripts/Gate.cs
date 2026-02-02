using System.Collections.Generic;
using UnityEngine;
using Project.Pooling;

public sealed class Gate : MonoBehaviour, IPoolable
{
    [SerializeField] List<GameObject> traps = new();
    [SerializeField] int forcedIndex = -1;

    int activeIndex = -1;

    public void OnSpawned()
    {
        int count = traps != null ? traps.Count : 0;
        if (count <= 0) return;

        int index = forcedIndex >= 0 ? Mathf.Clamp(forcedIndex, 0, count - 1) : Random.Range(0, count);

        if (index == activeIndex) return;

        for (int i = 0; i < count; i++)
        {
            var go = traps[i];
            if (go) go.SetActive(i == index);
        }

        activeIndex = index;
    }

    public void OnDespawned() { }

    void OnTriggerEnter(Collider other)
    {
        UIManager.Instance.AddScore();
        Debug.Log("gate hit");
    }
}