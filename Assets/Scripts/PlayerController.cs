using UnityEngine;

public sealed class PlayerController : MonoBehaviour
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

    Rigidbody rb;
    Vector2 input;
    float currentRoll;
    bool isDead;

    float elapsed;
    float curMaxSpeedX, curMaxSpeedY;
    float curAccelX, curAccelY;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezePositionZ |
                         RigidbodyConstraints.FreezeRotationX |
                         RigidbodyConstraints.FreezeRotationY;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (!animator)
            animator = GetComponentInChildren<Animator>();

        if (ramp == null || ramp.length == 0)
            ramp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        curMaxSpeedX = maxSpeedX;
        curMaxSpeedY = maxSpeedY;
        curAccelX = accelX;
        curAccelY = accelY;
    }

    void Update()
    {
        if (isDead) return;

        input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        input = Vector2.ClampMagnitude(input, 1f);

        float targetRoll = -input.x * rollAmount;
        currentRoll = Mathf.Lerp(currentRoll, targetRoll, 1f - Mathf.Exp(-turnSmoothing * Time.deltaTime));
        transform.localRotation = Quaternion.Euler(0f, 0f, currentRoll);

        if (Input.GetKeyDown(KeyCode.Space))
            PlayJump();
    }

    void FixedUpdate()
    {
        if (isDead) return;

        elapsed += Time.fixedDeltaTime;

        float t = rampDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / rampDuration);
        float k = Mathf.Clamp01(ramp.Evaluate(t));

        curMaxSpeedX = Mathf.Lerp(maxSpeedX, maxSpeedXMax, k);
        curMaxSpeedY = Mathf.Lerp(maxSpeedY, maxSpeedYMax, k);
        curAccelX = Mathf.Lerp(accelX, accelXMax, k);
        curAccelY = Mathf.Lerp(accelY, accelYMax, k);

        Vector3 v3 = rb.linearVelocity;

        float targetVX = input.x * curMaxSpeedX;
        float targetVY = input.y * curMaxSpeedY;

        v3.x = MoveTowards(v3.x, targetVX, curAccelX * Time.fixedDeltaTime);
        v3.y = MoveTowards(v3.y, targetVY, curAccelY * Time.fixedDeltaTime);

        rb.linearVelocity = v3;

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

            float inwardInput = Mathf.Clamp01(-Vector2.Dot(input, normal));
            if (inwardInput > 0f) velXY += (-normal) * (edgeExitBoost * inwardInput);

            rb.linearVelocity = new Vector3(velXY.x, velXY.y, vel.z);
        }
    }

    static float MoveTowards(float current, float target, float maxDelta)
    {
        if (current < target) return Mathf.Min(current + maxDelta, target);
        if (current > target) return Mathf.Max(current - maxDelta, target);
        return current;
    }

    public void PlayDeath()
    {
        if (animator)
            animator.Play("DeathPlayer", 0, 0f);
    }

    public void PlayJump()
    {
        if (!animator) return;
        animator.Play("JumpPlayer", 0, 0f);
    }

    void OnTriggerEnter(Collider other)
    {
        if (isDead) return;

        if (other.CompareTag("Occluder"))
        {
            isDead = true;

            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;

            GameManager.Instance.PlayerDied(this);
        }
    }
}
