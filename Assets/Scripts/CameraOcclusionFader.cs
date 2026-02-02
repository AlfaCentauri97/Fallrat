using System.Collections.Generic;
using UnityEngine;

public sealed class CameraOcclusionFader : MonoBehaviour
{
    [SerializeField] float distance = 15f;
    [SerializeField] Vector3 localOffset = Vector3.zero;
    [SerializeField] float sphereRadius = 0.25f;
    [SerializeField] LayerMask occluderMask;

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    readonly HashSet<ObstacleFade> last = new();
    readonly HashSet<ObstacleFade> now = new();
    readonly RaycastHit[] hits = new RaycastHit[32];

    Vector3 dbgFrom;
    Vector3 dbgTo;
    int dbgCount;

    void LateUpdate()
    {
        now.Clear();

        dbgFrom = transform.position;
        dbgTo = transform.TransformPoint(localOffset) + transform.forward * distance;

        Vector3 dir = dbgTo - dbgFrom;
        float dist = dir.magnitude;
        if (dist < 0.001f) return;

        dir /= dist;

        dbgCount = Physics.SphereCastNonAlloc(
            dbgFrom,
            sphereRadius,
            dir,
            hits,
            dist,
            occluderMask,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < dbgCount; i++)
        {
            var col = hits[i].collider;
            if (!col) continue;

            var fade = col.GetComponentInParent<ObstacleFade>();
            if (!fade) continue;

            fade.SetOccluded(true);
            now.Add(fade);
        }

        foreach (var f in last)
            if (!now.Contains(f))
                f.SetOccluded(false);

        last.Clear();
        foreach (var f in now) last.Add(f);
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(dbgFrom, dbgTo);
        Gizmos.DrawWireSphere(dbgTo, sphereRadius);
    }
}