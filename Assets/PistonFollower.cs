using UnityEngine;

public class PistonFollower : MonoBehaviour
{

    public Transform target;
    public Vector3 rotationOffset = new Vector3(90f, 0f, 0f);

    void LateUpdate()
    {
        if (target == null) return;
        transform.LookAt(target.position);
        transform.Rotate(rotationOffset, Space.Self);
    }
}
