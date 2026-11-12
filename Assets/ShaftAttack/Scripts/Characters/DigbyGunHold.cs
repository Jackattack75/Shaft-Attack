using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShaftAttack
{
    /// <summary>
    /// Digby's body holding the gun you have out (what other players see; you see it in the third-person test view and
    /// in your shadow). The gun sits in his right fist (the Pickaxe grip bone). Each frame, after the animation:
    ///   one-handed gun   right arm stretched out toward where you aim
    ///   two-handed gun   held in front of his chest, chest turned a little right, left hand on the Foregrip
    /// (two-bone IK on top of the running / jumping animation, so the legs keep doing their thing), the trigger finger
    /// squeezes with each shot and the gun kicks back. During a bomb throw the left hand lets go.
    /// In first person the arms holding the gun are folded out of your view (the gun view has its own arms) and the
    /// gun only casts a shadow.
    /// Sits on DigbyBody next to DigbyAnimDriver (Shaft Attack > Rebuild Weapons adds it).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]              // after DigbyAnimDriver's lean, before FirstPersonBody moves the body
    public class DigbyGunHold : MonoBehaviour
    {
        [Header("One-handed (metres from his right shoulder)")]
        [Tooltip("How far out in front the gun is held.")]
        public float oneHandOut = 0.62f;
        [Tooltip("Down / toward the middle of his body.")]
        public float oneHandDown = 0.1f;
        public float oneHandIn = 0.08f;

        [Header("Two-handed (metres from the middle of his shoulders)")]
        [Tooltip("How far in front of his shoulders the right hand holds it. His arms are short: keep it close.")]
        public float twoHandFront = 0.27f;
        [Tooltip("How far below his shoulders (high, like aiming: lower and his arms can't reach round his chest).")]
        public float twoHandBelow = 0.15f;
        [Tooltip("To his right of the middle.")]
        public float twoHandRightOf = 0f;
        [Tooltip("If his left hand can't reach the Foregrip it slides back along the gun, at most this far (metres): on a " +
                 "rifle that's the magazine.")]
        public float leftSlideBack = 0.16f;

        [Header("Feel")]
        [Tooltip("Seconds to bring the gun up / down when switching.")]
        public float raiseTime = 0.2f;

        private Loadout loadout;
        private PlayerGun shooter;
        private DigbyAnimDriver driver;
        private DigbyPickaxe pickaxe;
        private FirstPersonBody body;
        private Transform frame;
        private Transform chest;
        private Transform gripBone;
        private ArmChain right, left;
        private readonly Dictionary<GunDefinition, GunModel> models = new Dictionary<GunDefinition, GunModel>();
        private GunModel held;
        private GunDefinition heldGun;
        private float weight;
        private float leftWeight;
        private float kickBack, kickUp;
        private bool wasThird = true;

        private void Awake()
        {
            loadout = GetComponentInParent<Loadout>();
            shooter = GetComponentInParent<PlayerGun>();
            driver = GetComponent<DigbyAnimDriver>();
            pickaxe = GetComponent<DigbyPickaxe>();
            body = GetComponent<FirstPersonBody>();
            frame = transform.parent != null ? transform.parent : transform;
            chest = ArmChain.Deep(transform, "Chest");
            gripBone = ArmChain.Deep(transform, "Pickaxe");
            right = ArmChain.Find(transform, "Right", "Pickaxe");
            left = ArmChain.Find(transform, "Left", "LeftGrip");
            if (right != null) right.CaptureRest();          // still the rest pose: the Animator hasn't run yet
            if (left != null) left.CaptureRest();
        }

        private void OnEnable()
        {
            if (shooter != null) shooter.OnShot += Shot;
        }

        private void OnDisable()
        {
            if (shooter != null) shooter.OnShot -= Shot;
        }

        private void Shot(GunDefinition gun)
        {
            kickBack = Mathf.Min(kickBack + gun.viewKickBack, gun.viewKickBack * 2f);
            kickUp = Mathf.Min(kickUp + gun.viewKickUp, gun.viewKickUp * 2f);
            if (held != null && heldGun == gun && (body == null || body.IsThirdPerson)) held.Flash();   // first person: the gun view flashes
        }

        private GunModel ModelFor(GunDefinition gun)
        {
            GunModel m;
            if (models.TryGetValue(gun, out m) && m != null) return m;
            if (gun.model == null || gripBone == null) return null;
            GameObject go = Instantiate(gun.model, gripBone, false);
            go.name = gun.displayName;
            m = go.GetComponent<GunModel>();
            if (m == null) m = go.AddComponent<GunModel>();
            m.Init(gameObject.layer, ShadowCastingMode.On, true, shooter != null ? shooter.flashMaterial : null);
            m.SetVisible(false);
            models[gun] = m;
            return m;
        }

        private void LateUpdate()
        {
            if (loadout == null || right == null) return;
            float dt = Time.deltaTime;
            GunDefinition gun = loadout.CurrentGun;
            bool third = body == null || body.IsThirdPerson;

            // the gun in his hand: the new one as soon as you switch, the old one stays while it's lowered
            GunDefinition want = gun != null ? gun : (weight > 0f ? heldGun : null);
            GunModel m = want != null ? ModelFor(want) : null;
            if (m != held)
            {
                if (held != null) held.SetVisible(false);
                held = m;
                heldGun = want;
                if (held != null) held.SetVisible(true);
                wasThird = !third;                           // force the shadow setting below
            }
            weight = Mathf.MoveTowards(weight, gun != null ? 1f : 0f, dt / Mathf.Max(0.01f, raiseTime));
            if (driver != null) driver.SetGunHold(gun == null ? 0 : gun.hands == GunDefinition.Hands.Two ? 2 : 1);
            if (gun != null && pickaxe != null && pickaxe.InHand) pickaxe.SetInHand(false);   // switched mid-dig: back it goes
            if (held == null || weight <= 0f)
            {
                if (held != null) held.SetVisible(false);
                held = null;
                heldGun = null;
                if (body != null) body.SetArmsHidden(false, false);
                return;
            }
            if (third != wasThird)
            {
                held.SetShadows(third ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly, true);
                wasThird = third;
            }

            // ---- where the gun should be
            Transform aim = shooter != null ? shooter.aim : null;
            Vector3 fwd = aim != null ? aim.forward : frame.forward;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, fwd).normalized;
            Vector3 side = Vector3.Cross(up, fwd);                          // his right
            float s = transform.lossyScale.x;
            kickBack = Mathf.Lerp(kickBack, 0f, 1f - Mathf.Exp(-14f * dt));
            kickUp = Mathf.Lerp(kickUp, 0f, 1f - Mathf.Exp(-12f * dt));
            bool twoHands = heldGun.hands == GunDefinition.Hands.Two && held.foregrip != null;
            Vector3 gripPos;
            if (twoHands && left != null)
            {
                Vector3 mid = 0.5f * (right.upper.position + left.upper.position);
                gripPos = mid + fwd * twoHandFront * s - up * twoHandBelow * s + side * twoHandRightOf * s;
            }
            else
                gripPos = right.upper.position + fwd * oneHandOut * s - up * oneHandDown * s - side * oneHandIn * s;
            gripPos -= fwd * kickBack * s * 1.5f;
            Quaternion gunRot = Quaternion.AngleAxis(-kickUp, side) * Quaternion.LookRotation(fwd, up);

            // put the gun there and note where its Grip and Foregrip end up, then the arms reach for them. (Note them
            // first: the gun hangs on his right hand, so it moves when the right arm does.)
            held.transform.rotation = held.RootFor(gunRot);
            held.transform.position += gripPos - held.grip.position;
            bool hasFore = held.foregrip != null;
            Vector3 forePos = hasFore ? held.foregrip.position : Vector3.zero;
            Quaternion foreRot = hasFore ? GunModel.HandOn(held.foregrip) : Quaternion.identity;
            Vector3 foreBack = hasFore ? -held.foregrip.forward : -fwd;
            Vector3 hp;
            Quaternion hr;
            right.HandForGrip(held.grip.position, GunModel.HandOn(held.grip), out hp, out hr);
            Vector3 pole = side * 0.6f - up + -fwd * 0.3f;
            float e = weight * weight * (3f - 2f * weight);
            right.Solve(hp, hr, pole, e);
            float trig = shooter != null ? shooter.TriggerAmount : 0f;
            Vector2 finger = Vector2.Lerp(heldGun.fingerHold, heldGun.fingerPull, trig);
            right.SetFinger(finger.x, finger.y, e);
            held.SetTrigger(trig, heldGun.triggerTravel);

            bool throwing = driver != null && driver.ThrowActive;
            leftWeight = Mathf.MoveTowards(leftWeight, twoHands && !throwing ? 1f : 0f, dt / 0.1f);
            if (left != null && leftWeight > 0f && hasFore)
            {
                Vector3 lp;
                Quaternion lr;
                left.HandForGrip(forePos, foreRot, out lp, out lr);
                // his arms are short: if he can't reach the front grip, the hand slides back along the gun
                Vector3 back = foreBack;
                float reach = left.Reach * 0.98f;
                float slide = 0f, max = leftSlideBack * s;
                for (int i = 0; i < 8 && Vector3.Distance(left.upper.position, lp + back * slide) > reach && slide < max; i++)
                    slide = Mathf.Min(max, slide + max / 6f);
                lp += back * slide;
                left.Solve(lp, lr, -side * 0.6f - up - fwd * 0.2f, leftWeight * e);
            }

            // the gun goes exactly where his fist ended up
            if (gripBone != null) held.PlaceByGrip(gripBone.position, gripBone.rotation * GunModel.ToHand);

            if (body != null) body.SetArmsHidden(!third, !third && twoHands && !throwing);
            if (third && shooter != null) shooter.SetMuzzle(held.muzzle, null);
        }
    }
}
