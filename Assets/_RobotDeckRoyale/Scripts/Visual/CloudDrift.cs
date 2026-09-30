using UnityEngine;

/// <summary>
/// Scrolls a cloud deck's UVs so the sky reads as moving air without
/// animating any transforms.
///
/// Motion lives in a MaterialPropertyBlock rather than on the material, so the
/// shared cloud material is never cloned into a per-renderer instance. Several
/// decks can drift at different rates while still batching together.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class CloudDrift : MonoBehaviour
{
    [Tooltip("UV units per second. Values above ~0.02 read as wind rather than drift.")]
    [SerializeField] private Vector2 driftSpeed = new Vector2(0.004f, 0.002f);
    [SerializeField] private Vector2 tiling = Vector2.one;

    private static readonly int BaseMapScaleOffset = Shader.PropertyToID("_BaseMap_ST");

    private Renderer cachedRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Vector2 offset;

    private void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
    }

    private void OnValidate()
    {
        // Several decks share one cloud material, so their differing tiling only
        // exists in the property block. Re-apply on edit so the Scene view shows
        // what the game will.
        cachedRenderer = GetComponent<Renderer>();
        propertyBlock ??= new MaterialPropertyBlock();
        Apply();
    }

    private void Update()
    {
        if (cachedRenderer == null)
        {
            cachedRenderer = GetComponent<Renderer>();
            propertyBlock ??= new MaterialPropertyBlock();
        }

        offset += driftSpeed * Time.deltaTime;

        // Keep the offset in [0,1) so it cannot lose float precision over a
        // long match.
        offset.x -= Mathf.Floor(offset.x);
        offset.y -= Mathf.Floor(offset.y);

        Apply();
    }

    private void Apply()
    {
        if (cachedRenderer == null || propertyBlock == null)
        {
            return;
        }

        cachedRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetVector(
            BaseMapScaleOffset,
            new Vector4(tiling.x, tiling.y, offset.x, offset.y));
        cachedRenderer.SetPropertyBlock(propertyBlock);
    }
}
