using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Dedicated ranged combat controller.
/// Provides instant responsive trigger shooting, physical nail projectiles,
/// buckshot spreads, magazine ammo management, tactical reloading, and procedural weapon audio.
/// Decoupled from melee attack slow-downs and melee punch animations.
/// </summary>
public class PlayerRangedCombat : NetworkBehaviour
{
    [Header("Ammo Capacities")]
    [SerializeField] private int nailgunMaxMag = 12;
    [SerializeField] private int shotgunMaxMag = 4;
    [SerializeField] private float nailgunReloadTime = 1.35f;
    [SerializeField] private float shotgunReloadTime = 1.75f;

    [Header("Hit Detection Layer Mask")]
    [SerializeField] private LayerMask hitMask = ~0;

    private int nailgunAmmo = 12;
    private int shotgunAmmo = 4;

    private float fireTimer;
    private float reloadTimer;
    private float currentReloadDuration;
    private bool isReloading;

    private PlayerInventory inventory;
    private PlayerController playerController;
    private FirstPersonViewmodel viewmodel;
    private Camera playerCam;
    private AudioSource audioSource;
    private float cameraRecoilOffset = 0f;

    public bool IsHoldingRangedWeapon
    {
        get
        {
            if (inventory == null)
            {
                return false;
            }
            ItemData active = inventory.GetSelectedItem();
            return active != null && active.Type == ItemType.RangedWeapon;
        }
    }

    public int CurrentAmmo
    {
        get
        {
            if (inventory == null)
            {
                return 0;
            }
            ItemData active = inventory.GetSelectedItem();
            if (active == null || active.Type != ItemType.RangedWeapon)
            {
                return 0;
            }

            return active.ItemId == "shotgun" ? shotgunAmmo : nailgunAmmo;
        }
    }

    public int MaxAmmo
    {
        get
        {
            if (inventory == null)
            {
                return 0;
            }
            ItemData active = inventory.GetSelectedItem();
            if (active == null || active.Type != ItemType.RangedWeapon)
            {
                return 0;
            }

            return active.ItemId == "shotgun" ? shotgunMaxMag : nailgunMaxMag;
        }
    }

    public bool IsReloading
    {
        get
        {
            return isReloading;
        }
    }

    public float ReloadProgress
    {
        get
        {
            if (!isReloading || currentReloadDuration <= 0f)
            {
                return 1f;
            }
            return Mathf.Clamp01(1f - (reloadTimer / currentReloadDuration));
        }
    }

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        playerController = GetComponent<PlayerController>();
        viewmodel = GetComponent<FirstPersonViewmodel>();
        playerCam = GetComponentInChildren<Camera>();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 0f; // 2D local player weapon audio
        audioSource.volume = 0.85f;

        if (playerCam != null)
        {
            playerCam.transform.localRotation = Quaternion.identity;
        }
    }

    private void OnDisable()
    {
        cameraRecoilOffset = 0f;
        if (playerCam != null)
        {
            playerCam.transform.localRotation = Quaternion.identity;
        }
    }

    public override void OnNetworkSpawn()
    {
        nailgunAmmo = nailgunMaxMag;
        shotgunAmmo = shotgunMaxMag;
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (playerCam == null)
        {
            playerCam = GetComponentInChildren<Camera>();
        }

        if (viewmodel == null)
        {
            viewmodel = GetComponent<FirstPersonViewmodel>();
        }

        // Smooth spring recovery for camera recoil offset
        if (cameraRecoilOffset > 0.001f)
        {
            cameraRecoilOffset = Mathf.Lerp(cameraRecoilOffset, 0f, Time.deltaTime * 18f);
            if (cameraRecoilOffset < 0.001f)
            {
                cameraRecoilOffset = 0f;
            }

            if (playerCam != null)
            {
                playerCam.transform.localRotation = Quaternion.Euler(-cameraRecoilOffset, 0f, 0f);
            }
        }
        else if (playerCam != null && playerCam.transform.localRotation != Quaternion.identity)
        {
            playerCam.transform.localRotation = Quaternion.identity;
        }

        // Update fire cooldown timer
        if (fireTimer > 0f)
        {
            fireTimer -= Time.deltaTime;
        }

        // Update reload timer
        if (isReloading)
        {
            reloadTimer -= Time.deltaTime;
            if (reloadTimer <= 0f)
            {
                CompleteReload();
            }
        }

        if (playerController != null && !playerController.CanControl)
        {
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        if (!IsHoldingRangedWeapon)
        {
            if (isReloading)
            {
                isReloading = false;
            }
            return;
        }

        ItemData activeWeapon = inventory.GetSelectedItem();
        if (activeWeapon == null)
        {
            return;
        }

        // [R] Key: Tactical Reload
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            if (!isReloading && CurrentAmmo < MaxAmmo)
            {
                StartReload(activeWeapon);
                return;
            }
        }

        // Left Click: Shoot Ranged Weapon
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (fireTimer <= 0f && !isReloading)
            {
                if (CurrentAmmo <= 0)
                {
                    // Empty gun click
                    PlaySound(ProceduralWeaponAudio.GetEmptyClickClip(), 0.6f);
                    StartReload(activeWeapon);
                }
                else
                {
                    FireWeapon(activeWeapon);
                }
            }
        }
    }

    private void StartReload(ItemData weapon)
    {
        isReloading = true;
        currentReloadDuration = weapon.ItemId == "shotgun" ? shotgunReloadTime : nailgunReloadTime;
        reloadTimer = currentReloadDuration;

        PlaySound(ProceduralWeaponAudio.GetReloadClip(), 0.7f);

        if (viewmodel != null)
        {
            viewmodel.TriggerReloadAnimation(currentReloadDuration);
        }
    }

    private void CompleteReload()
    {
        isReloading = false;

        ItemData activeWeapon = inventory != null ? inventory.GetSelectedItem() : null;
        if (activeWeapon != null && activeWeapon.ItemId == "shotgun")
        {
            shotgunAmmo = shotgunMaxMag;
        }
        else
        {
            nailgunAmmo = nailgunMaxMag;
        }

        PlaySound(ProceduralWeaponAudio.GetReloadClip(), 0.5f);
    }

    private void FireWeapon(ItemData weapon)
    {
        // 1. Deduct ammo
        if (weapon.ItemId == "shotgun")
        {
            shotgunAmmo--;
            fireTimer = 1.05f; // Heavy pump cooldown
        }
        else
        {
            nailgunAmmo--;
            fireTimer = 0.35f; // Fast pneumatic trigger
        }

        // 2. Play Audio & Viewmodel Recoil
        if (weapon.ItemId == "shotgun")
        {
            PlaySound(ProceduralWeaponAudio.GetShotgunShotClip(), 0.95f);

            if (viewmodel != null)
            {
                viewmodel.TriggerRecoil(0.12f, 12f);
                viewmodel.TriggerMuzzleFlash(new Color(1f, 0.45f, 0.15f));
            }

            PlayCameraRecoil(3.5f);
            ExecuteShotgunBlast(weapon);
        }
        else // nailgun
        {
            PlaySound(ProceduralWeaponAudio.GetNailgunShotClip(), 0.85f);

            if (viewmodel != null)
            {
                viewmodel.TriggerRecoil(0.06f, 5.5f);
                viewmodel.TriggerMuzzleFlash(new Color(0.2f, 0.85f, 1f));
            }

            PlayCameraRecoil(1.2f);
            ExecuteNailgunShot(weapon);
        }
    }

    private void ExecuteNailgunShot(ItemData weapon)
    {
        if (playerCam == null)
        {
            return;
        }

        Vector3 spawnPos = playerCam.transform.position + playerCam.transform.forward * 0.25f + playerCam.transform.right * 0.16f - playerCam.transform.up * 0.10f;
        Vector3 aimDir = playerCam.transform.forward;

        // Launch physical ballistic nail projectile
        GameObject nailObj = new GameObject("Nail_Fired");
        nailObj.transform.position = spawnPos;
        nailObj.transform.forward = aimDir;

        NailProjectile nail = nailObj.AddComponent<NailProjectile>();
        nail.Initialize(aimDir, weapon.Damage, weapon.KnockbackForce, transform, this);

        // Also broadcast fired effect to other clients
        FireRangedVisualClientRpc(spawnPos, aimDir, isShotgun: false);
    }

    private void ExecuteShotgunBlast(ItemData weapon)
    {
        if (playerCam == null)
        {
            return;
        }

        Vector3 origin = playerCam.transform.position;
        Vector3 forward = playerCam.transform.forward;
        int pelletCount = 8;
        float range = 18f;

        // Group hits by entity root transform so every connected pellet contributes to the total burst damage
        Dictionary<Transform, ShotgunTargetHit> targetHits = new Dictionary<Transform, ShotgunTargetHit>();
        Dictionary<Rigidbody, Vector3> propImpulses = new Dictionary<Rigidbody, Vector3>();

        for (int p = 0; p < pelletCount; p++)
        {
            Vector2 spread = Random.insideUnitCircle * 0.068f;
            Vector3 pelletDir = (forward + playerCam.transform.right * spread.x + playerCam.transform.up * spread.y).normalized;

            Vector3 endPoint = origin + pelletDir * range;

            Ray ray = new Ray(origin, pelletDir);
            RaycastHit hit;
            bool hasHit = Physics.Raycast(ray, out hit, range, hitMask, QueryTriggerInteraction.Ignore);

            if (!hasHit)
            {
                // Fallback SphereCast with radius 0.12f catches fast-moving abnormalities and limbs
                hasHit = Physics.SphereCast(ray, 0.12f, out hit, range, hitMask, QueryTriggerInteraction.Ignore);
            }

            if (hasHit && hit.collider != null && !hit.transform.IsChildOf(transform))
            {
                endPoint = hit.point;

                Transform rootTransform = hit.collider.transform.root;
                NetworkObject netObj = hit.collider.GetComponentInParent<NetworkObject>();
                IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable == null && netObj != null)
                {
                    damageable = netObj.GetComponent<IDamageable>() ?? netObj.GetComponentInChildren<IDamageable>();
                }

                if (netObj != null || damageable != null)
                {
                    if (!targetHits.TryGetValue(rootTransform, out ShotgunTargetHit targetHit))
                    {
                        targetHit = new ShotgunTargetHit
                        {
                            NetObj = netObj,
                            Damageable = damageable,
                            Hits = 0,
                            TotalDir = Vector3.zero
                        };
                        targetHits[rootTransform] = targetHit;
                    }

                    targetHit.Hits++;
                    targetHit.TotalDir += pelletDir;
                }
                else
                {
                    Rigidbody propRb = hit.collider.GetComponentInParent<Rigidbody>();
                    if (propRb != null)
                    {
                        if (!propImpulses.ContainsKey(propRb))
                        {
                            propImpulses[propRb] = Vector3.zero;
                        }
                        propImpulses[propRb] += pelletDir;
                    }
                }
            }

            // Draw fiery buckshot tracer line
            CreateShotgunTracer(origin + playerCam.transform.forward * 0.4f + playerCam.transform.right * 0.18f - playerCam.transform.up * 0.12f, endPoint);
        }

        // Apply aggregated damage and knockback for ALL pellets that connected with each target
        foreach (var kvp in targetHits)
        {
            ShotgunTargetHit target = kvp.Value;
            int totalDamage = weapon.Damage * target.Hits;
            Vector3 avgDir = (target.TotalDir / target.Hits).normalized;
            Vector3 knockbackImpulse = avgDir * (weapon.KnockbackForce * (1f + 0.12f * (target.Hits - 1)));

            if (target.NetObj != null)
            {
                RangedAttackServerRpc(target.NetObj.NetworkObjectId, totalDamage, knockbackImpulse, isHeavyHit: true);
            }
            else if (target.Damageable != null)
            {
                target.Damageable.TakeDamage(totalDamage, knockbackImpulse, isHeavyHit: true);
            }

            Rigidbody hitRb = kvp.Key.GetComponentInChildren<Rigidbody>();
            if (hitRb != null)
            {
                hitRb.AddForce(knockbackImpulse * 14f, ForceMode.Impulse);
            }

            Debug.Log($"[Shotgun] Connected {target.Hits}/{pelletCount} pellets on {kvp.Key.name} for {totalDamage} total damage!");
        }

        // Push physical props
        foreach (var kvp in propImpulses)
        {
            if (kvp.Key != null)
            {
                kvp.Key.AddForce(kvp.Value.normalized * (weapon.KnockbackForce * 16f), ForceMode.Impulse);
            }
        }

        // Broadcast blast effect to peers
        FireRangedVisualClientRpc(origin + forward * 0.4f, forward, isShotgun: true);
    }

    private class ShotgunTargetHit
    {
        public NetworkObject NetObj;
        public IDamageable Damageable;
        public int Hits;
        public Vector3 TotalDir;
    }

    private void CreateShotgunTracer(Vector3 start, Vector3 end)
    {
        GameObject tracer = new GameObject("ShotgunTracer");
        LineRenderer lr = tracer.AddComponent<LineRenderer>();
        lr.startWidth = 0.045f;
        lr.endWidth = 0.012f;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.material = MaterialHelper.CreateLitMaterial(lr, new Color(1f, 0.55f, 0.15f, 0.9f));
        Destroy(tracer, 0.065f);
    }

    private void PlayCameraRecoil(float pitchUp)
    {
        cameraRecoilOffset = Mathf.Min(cameraRecoilOffset + pitchUp, 4.5f);
        if (playerCam != null)
        {
            playerCam.transform.localRotation = Quaternion.Euler(-cameraRecoilOffset, 0f, 0f);
        }
    }

    private void PlaySound(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip, volume);
        }
    }

    [Rpc(SendTo.NotOwner)]
    private void FireRangedVisualClientRpc(Vector3 origin, Vector3 direction, bool isShotgun)
    {
        // Plays third-person muzzle flash and sound for other players in the lobby/dungeon
        AudioClip clip = isShotgun ? ProceduralWeaponAudio.GetShotgunShotClip() : ProceduralWeaponAudio.GetNailgunShotClip();
        AudioSource.PlayClipAtPoint(clip, origin, 0.75f);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RangedAttackServerRpc(ulong targetNetId, int damage, Vector3 knockbackImpulse, bool isHeavyHit)
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out NetworkObject targetNetObj))
        {
            float dist = Vector3.Distance(transform.position, targetNetObj.transform.position);
            if (dist > 48f)
            {
                return;
            }

            IDamageable damageable = targetNetObj.GetComponent<IDamageable>();
            if (damageable == null)
            {
                damageable = targetNetObj.GetComponentInParent<IDamageable>();
            }
            if (damageable == null)
            {
                damageable = targetNetObj.GetComponentInChildren<IDamageable>();
            }

            if (damageable != null)
            {
                damageable.TakeDamage(damage, knockbackImpulse, isHeavyHit);
            }

            Rigidbody rb = targetNetObj.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(knockbackImpulse * 12f, ForceMode.Impulse);
            }
        }
    }
}
