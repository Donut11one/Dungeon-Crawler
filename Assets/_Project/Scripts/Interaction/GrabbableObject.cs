using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked grabbable physics object (barrels, junk, loot).
/// Players can pick these up and throw them at enemies or allies.
/// Server-authoritative physics with client prediction.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(ImpactDamage))]
public class GrabbableObject : NetworkBehaviour
{
    [Header("Throw Settings")]
    [SerializeField] private float throwForceMultiplier = 20f;

    public NetworkVariable<bool> IsGrabbed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<ulong> GrabberClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Rigidbody rb;
    private Collider col;
    private Transform grabberHoldPoint;

    /// <summary>The Rigidbody on this object.</summary>
    public Rigidbody Rb => rb;

    /// <summary>The Collider on this object.</summary>
    public Collider Col => col;

    /// <summary>Throw force multiplier configured on this object.</summary>
    public float ThrowForce => throwForceMultiplier;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    private void Update()
    {
        // On server: keep the grabbed object positioned with the grabber
        if (IsServer && IsGrabbed.Value && GrabberClientId.Value != ulong.MaxValue)
        {
            UpdateGrabberHoldPosition();
        }
    }

    private void UpdateGrabberHoldPosition()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (grabberHoldPoint == null)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(GrabberClientId.Value, out NetworkClient client))
            {
                if (client.PlayerObject != null)
                {
                    GrabThrow grabThrow = client.PlayerObject.GetComponent<GrabThrow>();
                    if (grabThrow != null && grabThrow.HoldPoint != null)
                    {
                        grabberHoldPoint = grabThrow.HoldPoint;
                    }
                    else
                    {
                        grabberHoldPoint = client.PlayerObject.transform;
                    }
                }
            }
        }

        if (grabberHoldPoint != null)
        {
            transform.position = grabberHoldPoint.position;
            transform.rotation = grabberHoldPoint.rotation;
        }
    }

    /// <summary>
    /// Request the server to grab this object for a specific client.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void GrabServerRpc(ulong clientId)
    {
        if (IsGrabbed.Value)
        {
            return;
        }

        IsGrabbed.Value = true;
        GrabberClientId.Value = clientId;
        grabberHoldPoint = null;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        SetPhysicsStateClientRpc(true);
    }

    /// <summary>
    /// Request the server to release/throw this object from an origin with a velocity vector.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReleaseServerRpc(Vector3 releasePosition, Vector3 throwVelocity)
    {
        IsGrabbed.Value = false;
        GrabberClientId.Value = ulong.MaxValue;
        grabberHoldPoint = null;

        transform.position = releasePosition;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = throwVelocity;
            rb.angularVelocity = Random.insideUnitSphere * 4f;
        }

        SyncReleaseClientRpc(releasePosition, throwVelocity);
    }

    /// <summary>
    /// Fallback release without explicit position.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReleaseServerRpc(Vector3 throwForce)
    {
        ReleaseServerRpc(transform.position, throwForce);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SetPhysicsStateClientRpc(bool grabbed)
    {
        if (rb != null)
        {
            rb.isKinematic = grabbed;
            rb.interpolation = grabbed
                ? RigidbodyInterpolation.None
                : RigidbodyInterpolation.Interpolate;

            if (grabbed)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncReleaseClientRpc(Vector3 releasePosition, Vector3 throwVelocity)
    {
        transform.position = releasePosition;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = throwVelocity;
            rb.angularVelocity = Random.insideUnitSphere * 4f;
        }
    }
}
