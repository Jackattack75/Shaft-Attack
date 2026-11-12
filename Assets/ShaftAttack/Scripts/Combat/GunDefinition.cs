using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Everything about one gun, as an asset (Assets/ShaftAttack/Weapons/Pistol.asset, Rifle.asset). The Loadout on the
    /// player lists the guns; PlayerGun fires the one in your hand.
    ///
    /// SWAPPING IN A NEW MODEL (e.g. from the Asset Store): make a prefab of it and drop it in `model`. Give it these
    /// empty children (names matter, they can sit anywhere under the root):
    ///   Grip      where the right fist holds it: +X to the right, +Y up the handle, +Z forward (the barrel's way)
    ///   Muzzle    the end of the barrel, +Z the way bullets go (tracers and the flash start here)
    ///   Trigger   (optional) the trigger part, turned about its X axis when you shoot
    ///   Foregrip  (two-handed guns) where the left fist holds it, same axes as Grip
    /// In the prefab the gun should point along +Z with its top up. Missing Grip: the root is used. Missing Muzzle:
    /// the front of the model. Scale the model so it fits his hand (his fists are big).
    /// </summary>
    [CreateAssetMenu(menuName = "Shaft Attack/Gun", fileName = "NewGun")]
    public class GunDefinition : ScriptableObject
    {
        public enum Hands { One, Two }

        [Header("Look")]
        public string displayName = "Gun";
        [Tooltip("The gun model (prefab). See the notes at the top of GunDefinition.cs for the children it needs.")]
        public GameObject model;
        [Tooltip("One = right hand only (the left arm stays free). Two = left hand on the Foregrip.")]
        public Hands hands = Hands.One;

        [Header("Shooting")]
        [Tooltip("Hold the button to keep firing. Off = one shot per click.")]
        public bool automatic = false;
        [Tooltip("Shots per second at most.")]
        public float fireRate = 6f;
        [Tooltip("Damage per bullet (Health starts at 100).")]
        public float damage = 25f;
        [Tooltip("Bullets reach this far (metres).")]
        public float range = 200f;

        [Header("Accuracy (degrees)")]
        [Tooltip("Random spread of every shot.")]
        public float spread = 0.35f;
        [Tooltip("Extra spread added by each shot...")]
        public float bloomPerShot = 0.3f;
        [Tooltip("...up to this much...")]
        public float maxBloom = 2f;
        [Tooltip("...which wears off at this many degrees per second.")]
        public float bloomRecovery = 8f;
        [Tooltip("Extra spread while moving at full run speed.")]
        public float moveSpread = 0.4f;
        [Tooltip("Extra spread in the air.")]
        public float airSpread = 0.6f;

        [Header("Bullet (an effect, not an object)")]
        [Tooltip("Metres per second. Fast, but you can still see the tracer fly.")]
        public float bulletSpeed = 380f;
        [ColorUsage(false, true)]
        public Color tracerColor = new Color(1f, 0.72f, 0.3f);
        public float tracerWidth = 0.035f;
        [Tooltip("Length of the streak (metres).")]
        public float tracerLength = 5f;

        [Header("Hitting rock")]
        [Tooltip("Size of the bite a bullet takes out of rock or dirt (metres). The pickaxe is 1.25, a bomb 3.6.")]
        public float digRadius = 0.55f;
        [Tooltip("How far past the surface the bite is centred.")]
        public float digInset = 0.1f;

        [Header("Kick")]
        [Tooltip("Your aim climbs this much per shot (degrees). Stays where it went, like real recoil.")]
        public float aimClimb = 0.5f;
        [Tooltip("Random sideways aim kick per shot (degrees).")]
        public float aimWander = 0.15f;
        [Tooltip("The camera nods up this much per shot and comes back (degrees).")]
        public float cameraKick = 1.2f;
        [Tooltip("The gun jumps back in your hand (metres)...")]
        public float viewKickBack = 0.05f;
        [Tooltip("...and tips up (degrees).")]
        public float viewKickUp = 6f;

        [Header("In your view (first person)")]
        [Tooltip("Where the gun's Grip sits in front of the camera: right, up, forward (metres).")]
        public Vector3 viewOffset = new Vector3(0.21f, -0.27f, 0.6f);
        [Tooltip("How the gun is turned in your view (degrees: pitch, yaw, roll). Small yaw toward the middle looks right.")]
        public Vector3 viewEuler = new Vector3(0f, -5f, 0f);
        [Tooltip("Seconds to pull it out after switching (no shooting until then).")]
        public float drawTime = 0.25f;

        [Header("Trigger finger (degrees, + straighter, - curled)")]
        [Tooltip("Index finger knuckle and middle joint while holding the gun, finger on the trigger.")]
        public Vector2 fingerHold = new Vector2(0f, 8f);
        [Tooltip("The same with the trigger pulled.")]
        public Vector2 fingerPull = new Vector2(-2f, -22f);
        [Tooltip("How far the gun's own Trigger part turns back when pulled.")]
        public float triggerTravel = 16f;
    }
}
