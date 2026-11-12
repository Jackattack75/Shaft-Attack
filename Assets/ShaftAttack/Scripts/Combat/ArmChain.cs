using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// One of Digby's arms, posed from code: two-bone IK (upper arm, forearm) that puts his hand where a gun needs it,
    /// plus the half-joint helper bones (they normally follow by a Blender constraint, which Unity doesn't have) and the
    /// trigger finger. Used for the first-person arms (GunViewmodel) and for holding guns in third person
    /// (DigbyGunHold).
    /// Call CaptureRest once while the skeleton is still in its rest pose (Awake / Start, before the Animator runs).
    /// </summary>
    public class ArmChain
    {
        public Transform upper, lower, hand, index1, index2, halfElbow, halfShoulder, grip;

        private Quaternion upperRest, lowerRest, handRest, index1Rest, index2Rest, halfElbowRest, halfShoulderRest;
        private Vector3 curl1 = Vector3.right, curl2 = Vector3.right;      // local axes: + angle = curl toward the palm
        private bool ready;

        /// <summary>side = "Right" or "Left"; gripName = the bone in that fist ("Pickaxe" right, "LeftGrip" left).</summary>
        public static ArmChain Find(Transform root, string side, string gripName)
        {
            ArmChain a = new ArmChain();
            string k = side.Substring(0, 1);
            a.upper = Deep(root, side + "UpperArm");
            a.lower = Deep(root, side + "LowerArm");
            a.hand = Deep(root, side + "Hand");
            a.index1 = Deep(root, side + "Index1");
            a.index2 = Deep(root, side + "Index2");
            a.halfElbow = Deep(root, "Half_Elbow." + k);
            a.halfShoulder = Deep(root, "Half_Shoulder." + k);
            a.grip = gripName != null ? Deep(root, gripName) : null;
            return a.upper != null && a.lower != null && a.hand != null ? a : null;
        }

        public bool HasFinger { get { return index1 != null && index2 != null; } }
        public float Reach { get { return Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position); } }

        public void CaptureRest()
        {
            upperRest = upper.localRotation;
            lowerRest = lower.localRotation;
            handRest = hand.localRotation;
            if (halfElbow != null) halfElbowRest = halfElbow.localRotation;
            if (halfShoulder != null) halfShoulderRest = halfShoulder.localRotation;
            if (HasFinger)
            {
                index1Rest = index1.localRotation;
                index2Rest = index2.localRotation;
                // which way is "curl"? The way that brings the fingertip toward the palm (the grip point is in the palm)
                Vector3 palm = grip != null ? grip.position : hand.position;
                curl1 = CurlAxis(index1, index2, palm);
                curl2 = CurlAxis(index2, null, palm);
            }
            ready = true;
        }

        private static Vector3 Tip(Transform finger, Transform child)
        {
            if (child != null) return child.position + (child.position - finger.position).normalized * 0.04f * finger.lossyScale.x;
            return finger.position + finger.up * 0.05f * finger.lossyScale.x;
        }

        private static Vector3 CurlAxis(Transform finger, Transform child, Vector3 palm)
        {
            Quaternion keep = finger.localRotation;
            finger.localRotation = keep * Quaternion.AngleAxis(20f, Vector3.right);
            float plus = Vector3.Distance(Tip(finger, child), palm);
            finger.localRotation = keep * Quaternion.AngleAxis(-20f, Vector3.right);
            float minus = Vector3.Distance(Tip(finger, child), palm);
            finger.localRotation = keep;
            return plus < minus ? Vector3.right : Vector3.left;
        }

        /// <summary>Put the arm back in its rest pose (no Animator on it).</summary>
        public void ResetPose()
        {
            if (!ready) return;
            upper.localRotation = upperRest;
            lower.localRotation = lowerRest;
            hand.localRotation = handRest;
            if (HasFinger)
            {
                index1.localRotation = index1Rest;
                index2.localRotation = index2Rest;
            }
            UpdateHelpers();
        }

        /// <summary>Where the hand bone has to be so its grip bone ends up at (gripPos, gripRot).</summary>
        public void HandForGrip(Vector3 gripPos, Quaternion gripRot, out Vector3 handPos, out Quaternion handRot)
        {
            if (grip == null)
            {
                handPos = gripPos;
                handRot = gripRot;
                return;
            }
            handRot = gripRot * Quaternion.Inverse(grip.localRotation);
            handPos = gripPos - handRot * Vector3.Scale(grip.localPosition, hand.lossyScale);
        }

        /// <summary>
        /// Two-bone IK: wrist to handPos (as close as the arm reaches), hand turned to handRot, the elbow bending toward
        /// `pole` (a direction). weight 0..1 blends from whatever pose the arm has now (e.g. the Animator's) to the IK.
        /// </summary>
        public void Solve(Vector3 handPos, Quaternion handRot, Vector3 pole, float weight)
        {
            if (weight <= 0f) return;
            Quaternion u0 = upper.localRotation, l0 = lower.localRotation, h0 = hand.localRotation;

            Vector3 a = upper.position;
            float lab = Vector3.Distance(a, lower.position);
            float lbc = Vector3.Distance(lower.position, hand.position);
            Vector3 at = handPos - a;
            float lat = Mathf.Clamp(at.magnitude, 0.001f, (lab + lbc) * 0.999f);
            Vector3 dir = at.sqrMagnitude > 1e-8f ? at.normalized : upper.up;
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir);
            bend.Normalize();
            float cosA = Mathf.Clamp((lab * lab + lat * lat - lbc * lbc) / (2f * lab * lat), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = a + (dir * cosA + bend * sinA) * lab;
            Vector3 wrist = a + dir * lat;

            // upper arm: aim at the elbow, then roll it about its own length so the forearm bends toward the wrist
            // (an explicit roll: FromToRotation picks any axis when the bend has to flip right round, which threw
            // the arm up in the air)
            upper.rotation = FromTo(lower.position - a, elbow - a, bend) * upper.rotation;
            Vector3 axis = (elbow - a).normalized;
            Vector3 nowBend = Vector3.ProjectOnPlane(hand.position - a, axis);
            Vector3 wantBend = Vector3.ProjectOnPlane(wrist - a, axis);
            if (nowBend.sqrMagnitude > 1e-8f && wantBend.sqrMagnitude > 1e-8f)
                upper.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(nowBend, wantBend, axis), axis) * upper.rotation;
            // forearm: aim at the wrist
            lower.rotation = FromTo(hand.position - lower.position, wrist - lower.position, bend) * lower.rotation;
            hand.rotation = handRot;

            if (weight < 1f)
            {
                upper.localRotation = Quaternion.Slerp(u0, upper.localRotation, weight);
                lower.localRotation = Quaternion.Slerp(l0, lower.localRotation, weight);
                hand.localRotation = Quaternion.Slerp(h0, hand.localRotation, weight);
            }
            UpdateHelpers();
        }

        /// <summary>FromToRotation that turns about a sensible axis even when the two directions are opposite.</summary>
        private static Quaternion FromTo(Vector3 from, Vector3 to, Vector3 hint)
        {
            if (Vector3.Angle(from, to) < 179f) return Quaternion.FromToRotation(from, to);
            Vector3 axis = Vector3.Cross(from, hint);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(from, Vector3.up);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(from, Vector3.right);
            return Quaternion.AngleAxis(180f, axis.normalized);
        }

        /// <summary>The half-joint helpers turn half as far as the joint they sit on (as the Blender rig does).</summary>
        public void UpdateHelpers()
        {
            if (!ready) return;
            if (halfElbow != null)
                halfElbow.localRotation = halfElbowRest * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(lowerRest) * lower.localRotation, 0.5f);
            if (halfShoulder != null)
                halfShoulder.localRotation = halfShoulderRest * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(upperRest) * upper.localRotation, 0.5f);
        }

        /// <summary>Trigger finger: knuckle and middle joint in degrees from the modelled shape (+ straighter, - curled).
        /// weight blends from the current pose.</summary>
        public void SetFinger(float knuckle, float middle, float weight)
        {
            if (!ready || !HasFinger || weight <= 0f) return;
            Quaternion a = index1Rest * Quaternion.AngleAxis(-knuckle, curl1);
            Quaternion b = index2Rest * Quaternion.AngleAxis(-middle, curl2);
            index1.localRotation = weight >= 1f ? a : Quaternion.Slerp(index1.localRotation, a, weight);
            index2.localRotation = weight >= 1f ? b : Quaternion.Slerp(index2.localRotation, b, weight);
        }

        public static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = Deep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
