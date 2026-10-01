#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ShaftAttack.EditorTools
{
    /// <summary>
    /// Turns off Unity's asynchronous shader compilation, once.
    ///
    /// With it on, the Editor draws objects in a cyan placeholder colour while their shader is
    /// still compiling - which is why the terrain flashed light blue at startup and the bomb was
    /// blue for its first few throws. It is purely an Editor display thing and never happens in
    /// a build, but it makes testing confusing.
    ///
    /// With it off, the Editor waits for the shader instead (a brief hitch the first time).
    /// Toggle it yourself any time under Edit > Project Settings > Editor > Shader Compilation,
    /// or with the menu items below.
    /// </summary>
    [InitializeOnLoad]
    public static class ShaftAttackEditorSetup
    {
        private const string AppliedKey = "ShaftAttack.AsyncShaderCompilationHandled";

        static ShaftAttackEditorSetup()
        {
            // Only ever act once, so it stays your setting after this.
            if (SessionStateSafeGet()) return;

            if (EditorSettings.asyncShaderCompilation)
            {
                EditorSettings.asyncShaderCompilation = false;
                Debug.Log("[Shaft Attack] Turned off asynchronous shader compilation so meshes " +
                          "stop flashing cyan while shaders compile. Edit > Project Settings > " +
                          "Editor > Shader Compilation to change it back.");
            }

            EditorPrefs.SetBool(AppliedKey, true);
        }

        private static bool SessionStateSafeGet()
        {
            return EditorPrefs.GetBool(AppliedKey, false);
        }

        [MenuItem("Shaft Attack/Shaders/Disable Async Compilation (no cyan flashes)")]
        private static void Disable()
        {
            EditorSettings.asyncShaderCompilation = false;
            Debug.Log("[Shaft Attack] Asynchronous shader compilation OFF.");
        }

        [MenuItem("Shaft Attack/Shaders/Enable Async Compilation (Unity default)")]
        private static void Enable()
        {
            EditorSettings.asyncShaderCompilation = true;
            Debug.Log("[Shaft Attack] Asynchronous shader compilation ON.");
        }
    }
}
#endif
