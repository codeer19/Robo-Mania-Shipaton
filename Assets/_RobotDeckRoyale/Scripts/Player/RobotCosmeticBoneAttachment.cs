using UnityEngine;

/// <summary>Keeps generated cosmetic optics owned by their disposable cosmetic root while following the head.</summary>
[DefaultExecutionOrder(225)]
public class RobotCosmeticBoneAttachment : MonoBehaviour
{
    private Transform bone;
    private RobotExpressionVisual expression;
    private Vector3 offset;
    private Quaternion rotation;
    private Transform[] optics;
    private Vector3[] scales;

    public void Bind(Transform target, RobotExpressionVisual source)
    {
        bone = target; expression = source;
        offset = bone.InverseTransformPoint(transform.position);
        rotation = Quaternion.Inverse(bone.rotation) * transform.rotation;
        optics = new Transform[transform.childCount]; scales = new Vector3[optics.Length];
        for (int i = 0; i < optics.Length; i++)
        {
            var child = transform.GetChild(i);
            if (child.name == "OpticBacking") continue;
            optics[i] = child; scales[i] = child.localScale;
        }
    }
    private void LateUpdate()
    {
        if (bone == null) return;
        transform.SetPositionAndRotation(bone.TransformPoint(offset), bone.rotation * rotation);
        float openness = expression != null ? expression.EyeOpenness : 1f;
        for (int i = 0; i < optics.Length; i++)
            if (optics[i] != null) optics[i].localScale = new Vector3(scales[i].x, scales[i].y * openness, scales[i].z);
    }
}
