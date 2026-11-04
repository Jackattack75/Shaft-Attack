using System.Collections;
using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Drops an object onto the ground below it once the terrain exists. The test world's ground height
    /// changes every Play (random noise offset) and its colliders are built after the scene starts, so
    /// this keeps looking for a few seconds.
    /// </summary>
    public class SnapToGround : MonoBehaviour
    {
        [Tooltip("How far above the object to start looking for ground.")]
        public float searchHeight = 40f;

        [Tooltip("Give up after this many seconds.")]
        public float maxWait = 8f;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private IEnumerator Start()
        {
            float waited = 0f;
            while (waited < maxWait)
            {
                Vector3 origin = transform.position + Vector3.up * searchHeight;
                int n = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, searchHeight * 2f, ~0,
                    QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                bool found = false;
                Vector3 point = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    Collider c = Hits[i].collider;
                    if (c.transform.IsChildOf(transform)) continue;
                    if (c.GetComponentInParent<CharacterController>() != null) continue;
                    if (Hits[i].distance < best)
                    {
                        best = Hits[i].distance;
                        point = Hits[i].point;
                        found = true;
                    }
                }
                if (found)
                {
                    transform.position = point;
                    yield break;
                }
                waited += Time.deltaTime;
                yield return null;
            }
        }
    }
}
