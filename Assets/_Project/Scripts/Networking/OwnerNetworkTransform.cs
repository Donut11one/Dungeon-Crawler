using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Owner-authoritative NetworkTransform.
/// Allows the owning client (e.g. Player 2) to drive and replicate their own position and rotation.
/// </summary>
[DisallowMultipleComponent]
public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
