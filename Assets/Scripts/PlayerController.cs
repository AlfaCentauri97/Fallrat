using Unity.Netcode;
using UnityEngine;

public sealed class NetworkPlayerController : NetworkBehaviour
{
    [Header("Move Base")]
    [SerializeField] float maxSpeedX = 7f;
    [SerializeField] float maxSpeedY = 7f;
    [SerializeField] float accelX = 35f;
    [SerializeField] float accelY = 35f;

    [Header("Move Scaling")]
    [SerializeField] float maxSpeedXMax = 12f;
    [SerializeField] float maxSpeedYMax = 12f;
    [SerializeField] float accelXMax = 60f;
    [SerializeField] float accelYMax = 60f;
    [SerializeField] float rampDuration = 90f;
    [SerializeField] AnimationCurve ramp;

    [Header("Bounds")]
    [SerializeField] Vector2 center = Vector2.zero;
    [SerializeField] float radius = 3f;
    [SerializeField] float edgePushIn = 0.03f;
    [SerializeField] float edgeExitBoost = 8f;

    [Header("Steering")]
    [SerializeField] float turnSmoothing = 14f;
    [SerializeField] float rollAmount = 18f;

    [Header("Animation")]
    [SerializeField] Animator animator;
    [SerializeField] string jumpTriggerName = "Jump";
    [SerializeField] string deathTriggerName = "Death";

    Rigidbody rb;

    Vector2 inputOwner;
    Vector2 inputServer;

    float currentRollOwner;  // tylko do “feel” na ownerze
    float rollServer;        // autorytatywny roll na serwerze (replikowany przez NetworkTransform)

    bool isDead;
    bool deathRequested;

    float elapsed;
    float curMaxSpeedX, curMaxSpeedY;
    float curAccelX, curAccelY;

    int jumpTrig;
    int deathTrig;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb)
        {
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezePositionZ |
                             RigidbodyConstraints.FreezeRotationX |
                             RigidbodyConstraints.FreezeRotationY;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        if (!animator) animator = GetComponentInChildren<Animator>();

        if (ramp == null || ramp.length == 0)
            ramp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        curMaxSpeedX = maxSpeedX;
        curMaxSpeedY = maxSpeedY;
        curAccelX = accelX;
        curAccelY = accelY;

        jumpTrig = Animator.StringToHash(jumpTriggerName);
        deathTrig = Animator.StringToHash(deathTriggerName);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner && CameraMgr.Instance)
            CameraMgr.Instance.SetGameCamera();
    }

    void Update()
    {
        if (isDead) return;

        if (!IsOwner) return;

        // input local
        inputOwner = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        inputOwner = Vector2.ClampMagnitude(inputOwner, 1f);

        // roll local (responsywność)
        float targetRoll = -inputOwner.x * rollAmount;
        currentRollOwner = Mathf.Lerp(currentRollOwner, targetRoll, 1f - Mathf.Exp(-turnSmoothing * Time.deltaTime));

        // opcjonalnie: owner może podglądać roll od razu
        // (serwer i tak go zaraz nadpisze)
        transform.localRotation = Quaternion.Euler(0f, 0f, currentRollOwner);

        // wyślij input + roll do serwera
        SubmitInputServerRpc(inputOwner, currentRollOwner);

        if (Input.GetKeyDown(KeyCode.Space))
            JumpServerRpc();
    }

    void FixedUpdate()
    {
        if (!IsServer || isDead) return;
        if (!rb) return;

        elapsed += Time.fixedDeltaTime;

        float t = rampDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / rampDuration);
        float k = Mathf.Clamp01(ramp.Evaluate(t));

        curMaxSpeedX = Mathf.Lerp(maxSpeedX, maxSpeedXMax, k);
        curMaxSpeedY = Mathf.Lerp(maxSpeedY, maxSpeedYMax, k);
        curAccelX = Mathf.Lerp(accelX, accelXMax, k);
        curAccelY = Mathf.Lerp(accelY, accelYMax, k);

        // ruch (server authoritative)
        Vector3 v3 = rb.linearVelocity;

        float targetVX = inputServer.x * curMaxSpeedX;
        float targetVY = inputServer.y * curMaxSpeedY;

        v3.x = MoveTowards(v3.x, targetVX, curAccelX * Time.fixedDeltaTime);
        v3.y = MoveTowards(v3.y, targetVY, curAccelY * Time.fixedDeltaTime);

        rb.linearVelocity = v3;

        // bounds
        Vector3 p3 = rb.position;
        Vector2 p = new Vector2(p3.x, p3.y);
        Vector2 offset = p - center;

        float r = Mathf.Max(0.0001f, radius);
        float r2 = r * r;

        if (offset.sqrMagnitude > r2)
        {
            Vector2 normal = offset.normalized;

            Vector2 clamped = center + normal * r;
            clamped -= normal * edgePushIn;
            rb.position = new Vector3(clamped.x, clamped.y, p3.z);

            Vector3 vel = rb.linearVelocity;
            Vector2 velXY = new Vector2(vel.x, vel.y);

            float radial = Vector2.Dot(velXY, normal);
            if (radial > 0f) velXY -= radial * normal;

            float inwardInput = Mathf.Clamp01(-Vector2.Dot(inputServer, normal));
            if (inwardInput > 0f) velXY += (-normal) * (edgeExitBoost * inwardInput);

            rb.linearVelocity = new Vector3(velXY.x, velXY.y, vel.z);
        }

        // ✅ rotacja ustawiana na serwerze (NetworkTransform ją zreplikuje)
        transform.localRotation = Quaternion.Euler(0f, 0f, rollServer);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsOwner || isDead) return;

        if (other.CompareTag("Occluder"))
            RequestDeath();
    }

    void RequestDeath()
    {
        if (deathRequested) return;
        deathRequested = true;
        DieServerRpc();
    }

    [ServerRpc]
    void DieServerRpc()
    {
        if (isDead) return;
        isDead = true;

        inputServer = Vector2.zero;
        rollServer = 0f;

        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // animacja śmierci dla wszystkich (NetworkAnimator + trigger)
        if (animator) animator.SetTrigger(deathTrig);

        // kamera + UI tylko lokalnie dla ownera
        var targets = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
        PlayLocalDeathClientRpc(targets);

        if (GameManager.Instance)
            GameManager.Instance.ServerMoveToEndPoint(this);
    }

    [ClientRpc]
    void PlayLocalDeathClientRpc(ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        if (CameraMgr.Instance)
            CameraMgr.Instance.SetEndCamera();

        if (GameManager.Instance)
            GameManager.Instance.LocalPlayerDied();
    }

    // ✅ teraz serwer dostaje input + roll
    [ServerRpc]
    void SubmitInputServerRpc(Vector2 input, float roll)
    {
        if (isDead) return;

        inputServer = input;
        rollServer = Mathf.Clamp(roll, -rollAmount, rollAmount);
    }

    [ServerRpc]
    void JumpServerRpc()
    {
        if (isDead) return;

        if (animator) animator.SetTrigger(jumpTrig);
    }

    public void ServerTeleportTo(Vector3 pos, Quaternion rot)
    {
        if (!IsServer) return;

        rollServer = 0f;

        if (rb)
        {
            rb.position = pos;
            rb.rotation = rot;
        }
        else
        {
            transform.SetPositionAndRotation(pos, rot);
        }
    }

    static float MoveTowards(float current, float target, float maxDelta)
    {
        if (current < target) return Mathf.Min(current + maxDelta, target);
        if (current > target) return Mathf.Max(current - maxDelta, target);
        return current;
    }

    public void SoundEffect()
    {
        AudioManager.Instance.PlayHitEffect("Splat", 1f);
    }
}
