using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// Paints a miner's team colour onto the model's team slot (material slot 1 - Digby's helmet) with a
    /// MaterialPropertyBlock, so all four players share the same two materials. The helmet's texture is
    /// baked white (with its dirt and scratches), so this colour is exactly what you see.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class MinerTeamColour : MonoBehaviour
    {
        [Tooltip("This miner's team colour (the helmet).")]
        public Color teamColour = new Color(0.965f, 0.675f, 0.071f);

        [Tooltip("Material slot that takes the team colour.")]
        public int teamSlot = 1;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock block;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        /// <summary>Call after changing teamColour from code.</summary>
        public void Apply()
        {
            if (block == null) block = new MaterialPropertyBlock();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))     // the player has two (FirstPersonBody)
            {
                if (teamSlot < 0 || teamSlot >= r.sharedMaterials.Length) continue;
                r.GetPropertyBlock(block, teamSlot);
                block.SetColor(BaseColorId, teamColour);
                r.SetPropertyBlock(block, teamSlot);
            }
        }
    }
}
