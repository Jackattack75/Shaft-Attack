using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Digby's pickaxe. It comes in with his model (PickaxeMesh, on the "Pickaxe" bone in his right fist) and hangs
    /// on his back (the "PickaxeBack" bone) until he digs: DigbyAnimDriver calls SetInHand when a swing starts and
    /// when he hangs it back over his shoulder. Sits next to DigbyAnimDriver on the body's root.
    /// </summary>
    [DisallowMultipleComponent]
    public class DigbyPickaxe : MonoBehaviour
    {
        [Tooltip("The pickaxe object. Found by name (PickaxeMesh) if left empty.")]
        public Transform pickaxe;
        [Tooltip("Bone in his right fist. Found by name (Pickaxe) if left empty.")]
        public Transform handBone;
        [Tooltip("Bone on his back where it is stowed. Found by name (PickaxeBack) if left empty.")]
        public Transform backBone;
        [Tooltip("Start with it in his hand instead of on his back.")]
        public bool startInHand = false;

        public bool InHand { get; private set; }

        private Vector3 localPosition;
        private Quaternion localRotation;
        private Vector3 localScale;
        private bool ready;

        private void Awake()
        {
            if (pickaxe == null) pickaxe = Find(transform, "PickaxeMesh");
            if (handBone == null) handBone = Find(transform, "Pickaxe");
            if (backBone == null) backBone = Find(transform, "PickaxeBack");
            ready = pickaxe != null && handBone != null && backBone != null;
            if (!ready) return;
            localPosition = pickaxe.localPosition;       // how it sits on its bone; the same on either bone
            localRotation = pickaxe.localRotation;
            localScale = pickaxe.localScale;
            Put(startInHand);
        }

        /// <summary>True = in his right fist, false = on his back.</summary>
        public void SetInHand(bool hand)
        {
            if (ready && hand != InHand) Put(hand);
        }

        private void Put(bool hand)
        {
            pickaxe.SetParent(hand ? handBone : backBone, false);
            pickaxe.localPosition = localPosition;
            pickaxe.localRotation = localRotation;
            pickaxe.localScale = localScale;
            InHand = hand;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = Find(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
