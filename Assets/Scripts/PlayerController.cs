using UnityEngine;

public sealed class PlayerController : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] float maxSpeedX = 7f;
    [SerializeField] float maxSpeedY = 7f;
    [SerializeField] float accelX = 35f;
    [SerializeField] float accelY = 35f;

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
    }

    void Update()
    {
        if (isDead) return;

        input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        input = Vector2.ClampMagnitude(input, 1f);

        float targetRoll = -input.x * rollAmount;
        currentRoll = Mathf.Lerp(currentRoll, targetRoll, 1f - Mathf.Exp(-turnSmoothing * Time.deltaTime));
        transform.localRotation = Quaternion.Euler(0f, 0f, currentRoll);
    }

    void FixedUpdate()
    {
        if (isDead) return;

        Vector3 v3 = rb.linearVelocity;

        float targetVX = input.x * maxSpeedX;
        float targetVY = input.y * maxSpeedY;

        v3.x = MoveTowards(v3.x, targetVX, accelX * Time.fixedDeltaTime);
        v3.y = MoveTowards(v3.y, targetVY, accelY * Time.fixedDeltaTime);

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
    // COLLISION
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
