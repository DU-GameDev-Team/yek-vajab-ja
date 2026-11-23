using UnityEngine;

/// <summary>Keeps the manually editable art board visible in Edit Mode only.</summary>
[DisallowMultipleComponent]
public sealed class ArtEditPreviewGuard : MonoBehaviour
{
    private void Awake()
    {
        if (Application.isPlaying)
            gameObject.SetActive(false);
    }
}
