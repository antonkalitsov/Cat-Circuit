using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Makes an NPC turn (yaw only, smoothly) to look at the cat when it comes close or talks to it,
    /// and slowly turn back to where it was looking before. Added automatically by CatNPC / GuideNPC.
    /// <see cref="faceLocal"/> = the direction the model's FACE points in the NPC's own space
    /// (the village cat models look along −X – checked with editor screenshots; the guide is built facing +Z).
    /// </summary>
    public class NpcTurner : MonoBehaviour
    {
        public Vector3 faceLocal = Vector3.left;
        public float lookRange = 4.5f;
        public float turnSpeed = 240f;      // degrees per second towards the player
        public float returnSpeed = 60f;     // degrees per second back to the original pose

        private Quaternion restRotation;
        private Transform player;
        private float nextSearch;

        /// <summary>Optional: the NPC tells us when it is in a conversation (always look then).</summary>
        public System.Func<bool> isTalking;

        private void Start() => restRotation = transform.rotation;

        private void Update()
        {
            if (player == null)
            {
                if (Time.time < nextSearch) return;
                nextSearch = Time.time + 1f;
                PlayerCarry p = FindAnyObjectByType<PlayerCarry>();
                if (p == null) return;
                player = p.transform;
            }

            Vector3 to = player.position - transform.position;
            to.y = 0f;
            bool look = to.sqrMagnitude < lookRange * lookRange || (isTalking != null && isTalking());

            Quaternion target = restRotation;
            if (look && to.sqrMagnitude > 0.01f)
            {
                // Rotate so that the face direction (not transform.forward) points at the player.
                Vector3 faceNow = transform.rotation * faceLocal;
                faceNow.y = 0f;
                if (faceNow.sqrMagnitude > 0.0001f)
                    target = Quaternion.AngleAxis(Vector3.SignedAngle(faceNow, to, Vector3.up), Vector3.up) * transform.rotation;
            }

            float speed = look ? turnSpeed : returnSpeed;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, speed * Time.deltaTime);
        }
    }
}
