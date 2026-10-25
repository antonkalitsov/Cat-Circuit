using UnityEngine;

namespace CatCircuit
{
    /// <summary>Turns the object (around Y only) so it always faces the camera – for signs and labels.</summary>
    public class Billboard : MonoBehaviour
    {
        private Camera cam;

        private void LateUpdate()
        {
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            if (cam == null) return;
            Vector3 dir = transform.position - cam.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
