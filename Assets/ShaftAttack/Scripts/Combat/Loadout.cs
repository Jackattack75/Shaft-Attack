using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// What's in your hand. Slot 0 is the pickaxe (key 1), then the guns in order (keys 2, 3...). Left mouse uses
    /// what you're holding: the pickaxe digs (MinerTools), a gun shoots (PlayerGun). Bombs (right mouse / Q) work with
    /// anything. Switching takes the new item's draw time before it can be used.
    /// Sits on the Player.
    /// </summary>
    [DisallowMultipleComponent]
    public class Loadout : MonoBehaviour
    {
        [Tooltip("Key 2, key 3, ... in order. Key 1 is always the pickaxe.")]
        public GunDefinition[] guns = new GunDefinition[0];
        [Tooltip("What you start with: 0 = pickaxe, 1 = the first gun ...")]
        public int startSlot = 0;
        [Tooltip("Seconds to get the pickaxe out.")]
        public float pickaxeDrawTime = 0.15f;

        /// <summary>0 = pickaxe, 1.. = guns[slot - 1].</summary>
        public int Slot { get; private set; }
        /// <summary>The slot before the last switch (what's being put away while the new one comes out).</summary>
        public int PreviousSlot { get; private set; }
        public GunDefinition CurrentGun { get { return GunIn(Slot); } }
        public bool PickaxeOut { get { return Slot == 0; } }
        public int SlotCount { get { return 1 + (guns != null ? guns.Length : 0); } }
        /// <summary>0 right after switching, 1 when the new item is ready to use.</summary>
        public float DrawProgress
        {
            get
            {
                float t = DrawTimeOf(Slot);
                return t <= 0f ? 1f : Mathf.Clamp01((Time.time - switchedAt) / t);
            }
        }
        public bool Ready { get { return DrawProgress >= 1f; } }

        /// <summary>(new slot, old slot)</summary>
        public System.Action<int, int> OnSwitched;

        private PlayerLook look;
        private float switchedAt = -10f;

        private void Awake()
        {
            look = GetComponent<PlayerLook>();
            Slot = Mathf.Clamp(startSlot, 0, SlotCount - 1);
            PreviousSlot = Slot;
        }

        private void Update()
        {
            if (look != null && !look.CursorLocked) return;
            int k = GameInput.SlotPressedThisFrame;
            if (k >= 0 && k < SlotCount) Select(k);
        }

        public GunDefinition GunIn(int slot)
        {
            if (slot <= 0 || guns == null || slot > guns.Length) return null;
            return guns[slot - 1];
        }

        public string NameOf(int slot)
        {
            if (slot == 0) return "Pickaxe";
            GunDefinition g = GunIn(slot);
            return g != null ? g.displayName : "-";
        }

        public void Select(int slot)
        {
            if (slot == Slot || slot < 0 || slot >= SlotCount) return;
            if (slot > 0 && GunIn(slot) == null) return;
            PreviousSlot = Slot;
            Slot = slot;
            switchedAt = Time.time;
            if (OnSwitched != null) OnSwitched(Slot, PreviousSlot);
        }

        private float DrawTimeOf(int slot)
        {
            GunDefinition g = GunIn(slot);
            return g != null ? g.drawTime : pickaxeDrawTime;
        }
    }
}
