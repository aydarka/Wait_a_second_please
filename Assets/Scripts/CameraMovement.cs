using UnityEngine;

[DisallowMultipleComponent]
public sealed class CameraMovement : MonoBehaviour
{
    [SerializeField, Min(0f)] private float speed = 6f;

    public float Speed => isActiveAndEnabled ? Mathf.Max(0f, speed) : 0f;

    private void Update()
    {
        transform.position += Vector3.forward * (Speed * Time.deltaTime);
    }
}
