using UnityEngine;

/// <summary>
/// High-velocity physical pneumatic nail projectile.
/// Flew from the Pneumatic Nailgun.
/// Travels ballistically; damages enemies or physically embeds into walls and props on impact.
/// </summary>
public class NailProjectile : MonoBehaviour
{
    [Header("Projectile Specs")]
    [SerializeField] private float speed = 75f;
    [SerializeField] private float maxLifetime = 6f;
    [SerializeField] private float embedDepth = 0.12f;

    private Vector3 velocity;
    private int damage;
    private float knockback;
    private ulong shooterClientId;
    private bool isEmbedded;
    private float lifetimeTimer;
    private GameObject visualMesh;
    private TrailRenderer trail;

    public void Initialize(Vector3 direction, int dmg, float kb, ulong shooterId)
    {
        velocity = direction.normalized * speed;
        damage = dmg;
        knockback = kb;
        shooterClientId = shooterId;
        transform.forward = direction;

        SetupVisuals();
    }

    private void SetupVisuals()
    {
        // 1. Procedural steel nail model (slender metallic cylinder + flat nail head)
        visualMesh = new GameObject("NailMesh");
        visualMesh.transform.parent = transform;
        visualMesh.transform.localPosition = Vector3.zero;
        visualMesh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // Nail shaft
        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.name = "Shaft";
        shaft.transform.parent = visualMesh.transform;
        shaft.transform.localPosition = new Vector3(0f, 0f, 0f);
        shaft.transform.localScale = new Vector3(0.024f, 0.14f, 0.024f);
        shaft.transform.localRotation = Quaternion.identity;

        Collider shaftCol = shaft.GetComponent<Collider>();
        if (shaftCol != null)
        {
            Destroy(shaftCol);
        }

        Renderer shaftRend = shaft.GetComponent<Renderer>();
        if (shaftRend != null)
        {
            shaftRend.material = MaterialHelper.CreateLitMaterial(shaftRend, new Color(0.85f, 0.88f, 0.92f));
        }

        // Nail head
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        head.name = "Head";
        head.transform.parent = visualMesh.transform;
        head.transform.localPosition = new Vector3(0f, -0.14f, 0f);
        head.transform.localScale = new Vector3(0.065f, 0.015f, 0.065f);
        head.transform.localRotation = Quaternion.identity;

        Collider headCol = head.GetComponent<Collider>();
        if (headCol != null)
        {
            Destroy(headCol);
        }

        Renderer headRend = head.GetComponent<Renderer>();
        if (headRend != null)
        {
            headRend.material = MaterialHelper.CreateLitMaterial(headRend, new Color(0.95f, 0.95f, 0.95f));
        }

        // 2. High-speed pneumatic tracer trail
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.08f;
        trail.startWidth = 0.035f;
        trail.endWidth = 0.005f;
        trail.material = MaterialHelper.CreateLitMaterial(trail, new Color(0.3f, 0.9f, 1f, 0.85f));
        trail.minVertexDistance = 0.1f;
    }

    private void Update()
    {
        if (isEmbedded)
        {
            return;
        }

        lifetimeTimer += Time.deltaTime;
        if (lifetimeTimer >= maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Continuous collision detection along flight vector
        float stepDist = speed * Time.deltaTime;
        Vector3 moveStep = velocity.normalized * stepDist;
        Ray ray = new Ray(transform.position, velocity.normalized);

        if (Physics.Raycast(ray, out RaycastHit hit, stepDist, ~0, QueryTriggerInteraction.Ignore))
        {
            // Ignore collision with shooter
            if (hit.collider != null && hit.collider.transform.root != null)
            {
                var shooterNet = hit.collider.GetComponentInParent<Unity.Netcode.NetworkObject>();
                if (shooterNet != null && shooterNet.OwnerClientId == shooterClientId)
                {
                    transform.position += moveStep;
                    return;
                }
            }

            HandleHit(hit);
        }
        else
        {
            transform.position += moveStep;
        }
    }

    private void HandleHit(RaycastHit hit)
    {
        isEmbedded = true;

        if (trail != null)
        {
            trail.autodestruct = false;
        }

        // 1. Check if hit entity is damageable or a network object
        IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
        Unity.Netcode.NetworkObject netObj = hit.collider.GetComponentInParent<Unity.Netcode.NetworkObject>();

        if (netObj != null || damageable != null)
        {
            // Position nail at impact
            transform.position = hit.point;

            // Apply damage via server-side or RPC hook
            if (damageable != null)
            {
                damageable.TakeDamage(damage, velocity.normalized * knockback, isHeavyHit: false);
            }

            Rigidbody hitRb = hit.collider.GetComponentInParent<Rigidbody>();
            if (hitRb != null)
            {
                hitRb.AddForce(velocity.normalized * (knockback * 16f), ForceMode.Impulse);
            }

            SpawnImpactSparks(hit.point, hit.normal, new Color(1f, 0.35f, 0.1f));
            Destroy(gameObject, 0.15f);
            return;
        }

        // 2. Environment hit: Embed firmly into wall / door / floor / physical prop
        transform.position = hit.point + velocity.normalized * embedDepth;
        transform.forward = velocity.normalized;
        transform.parent = hit.transform; // Move with hit prop or door!

        Rigidbody propRb = hit.collider.GetComponentInParent<Rigidbody>();
        if (propRb != null)
        {
            propRb.AddForce(velocity.normalized * (knockback * 12f), ForceMode.Impulse);
        }

        SpawnImpactSparks(hit.point, hit.normal, new Color(0.9f, 0.85f, 0.3f));

        // Play metallic thwack impact
        PlayThwackSound(hit.point);

        // Embedded nail persists in world for 25 seconds before fading
        Destroy(gameObject, 25f);
    }

    private void SpawnImpactSparks(Vector3 pos, Vector3 normal, Color sparkColor)
    {
        GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        spark.transform.position = pos + normal * 0.05f;
        spark.transform.localScale = Vector3.one * 0.08f;

        Collider c = spark.GetComponent<Collider>();
        if (c != null)
        {
            Destroy(c);
        }

        Renderer r = spark.GetComponent<Renderer>();
        if (r != null)
        {
            r.material = MaterialHelper.CreateLitMaterial(r, sparkColor);
        }

        Destroy(spark, 0.06f);
    }

    private void PlayThwackSound(Vector3 pos)
    {
        // Synthesize short metallic impact sound
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.maxDistance = 22f;
        source.volume = 0.75f;
        source.clip = ProceduralWeaponAudio.GetNailImpactClip();
        source.Play();
    }
}
