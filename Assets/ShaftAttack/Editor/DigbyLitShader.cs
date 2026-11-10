#if UNITY_EDITOR
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ShaftAttack.EditorTools
{
    /// <summary>
    /// Makes the shader Digby and his pickaxe use: "ShaftAttack/Digby Lit" = Unity's own URP Lit shader with
    /// one change, NEAR LIGHTS ARE CAPPED.
    ///
    /// Why: in first person the player's headlamp sits a hand's width from his own arms, knees and pickaxe.
    /// A lamp that reaches across a cave is dozens of times too bright at that range (brightness goes with
    /// 1 / distance squared), so everything it touched went flat white: the hand he throws forward in a dash,
    /// the pickaxe handle while digging, his knee in a slide.
    /// With this shader no point or spot light can put more than LightCap on him, however close it is. He is
    /// lit as if the lamp were about 2 m away: bright, but with his colours and shading intact. Lights that
    /// are further away than that, and the sun, light him exactly as URP Lit does.
    ///
    /// How: it is not a hand-written copy. The text of the installed URP package's Lit.shader is read and a few
    /// lines are added to its forward pass that wrap URP's GetAdditionalLight. So it follows whatever URP
    /// version the project has; after a URP upgrade run Shaft Attack > Rebuild Digby to make it again.
    /// If anything about URP's files is not as expected, nothing is written and plain URP Lit is used.
    /// </summary>
    internal static class DigbyLitShader
    {
        public const string ShaderName = "ShaftAttack/Digby Lit";
        private const string Urp = "Packages/com.unity.render-pipelines.universal/";
        private const string UrpLit = "Universal Render Pipeline/Lit";

        /// <summary>Most light (colour x intensity x falloff) one point or spot light may put on him.
        /// The player's headlamp (intensity 6) reaches this at about 1.8 m. Raise it for brighter arms.</summary>
        private const string LightCap = "1.8";

        /// <summary>The shader, written to <paramref name="assetPath"/> if it is missing or out of date.
        /// Falls back to URP Lit (and says why) when it can't be made.</summary>
        public static Shader Get(string assetPath)
        {
            Shader urp = Shader.Find(UrpLit);
            try
            {
                string why;
                string text = Make(out why);
                if (text == null)
                {
                    Debug.LogWarning("[Digby] his shader was not made (" + why + "); using URP Lit");
                    return urp;
                }
                string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
                if (!File.Exists(full) || File.ReadAllText(full) != text)
                {
                    File.WriteAllText(full, text);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                }
                Shader made = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (made == null || ShaderUtil.ShaderHasError(made))
                {
                    Debug.LogWarning("[Digby] " + assetPath + " did not compile; using URP Lit");
                    return urp;
                }
                return made;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Digby] his shader was not made (" + e.Message + "); using URP Lit");
                return urp;
            }
        }

        private static string Make(out string why)
        {
            string version, unused;
            string lit = Read(Urp + "Shaders/Lit.shader", out version);
            string lights = Read(Urp + "ShaderLibrary/RealtimeLights.hlsl", out unused);
            if (lit == null || lights == null) { why = "URP's Lit.shader not found"; return null; }
            lit = lit.Replace("\r\n", "\n");
            lights = lights.Replace("\r\n", "\n");

            // every form of URP's GetAdditionalLight(...) in this version gets a capped twin
            StringBuilder twins = new StringBuilder();
            int found = 0;
            foreach (Match m in Regex.Matches(lights, @"^Light\s+GetAdditionalLight\s*\(([^)]*)\)", RegexOptions.Multiline))
            {
                string pars = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " ");
                StringBuilder names = new StringBuilder();
                foreach (string p in pars.Split(','))
                {
                    string decl = p.Trim();
                    if (names.Length > 0) names.Append(", ");
                    names.Append(decl.Substring(decl.LastIndexOf(' ') + 1));
                }
                twins.Append("            Light DigbyAdditionalLight(").Append(pars).Append(") { return DigbyCapLight(GetAdditionalLight(")
                     .Append(names).Append(")); }\n");
                found++;
            }
            if (found == 0) { why = "GetAdditionalLight not found in URP's RealtimeLights.hlsl"; return null; }

            const string title = "Shader \"" + UrpLit + "\"";
            const string include = "#include \"" + Urp + "Shaders/LitForwardPass.hlsl\"";
            if (Count(lit, title) != 1 || Count(lit, include) != 1)
            {
                why = "URP's Lit.shader is laid out differently than expected";
                return null;
            }

            string cap =
                "// ---- Shaft Attack: near lights are capped (written by DigbyLitShader.cs) ----\n" +
                "            #include \"" + Urp + "ShaderLibrary/RealtimeLights.hlsl\"\n" +
                "            Light DigbyCapLight(Light light)\n" +
                "            {\n" +
                "                half peak = max(light.color.r, max(light.color.g, light.color.b)) * light.distanceAttenuation;\n" +
                "                if (peak > " + LightCap + ") light.distanceAttenuation *= " + LightCap + " / peak;\n" +
                "                return light;\n" +
                "            }\n" +
                twins +
                "            #define GetAdditionalLight DigbyAdditionalLight\n" +
                "            // ----\n" +
                "            ";
            string header =
                "// MADE BY A SCRIPT - do not edit. Shaft Attack > Rebuild Digby writes this file again.\n" +
                "// Unity's URP Lit shader (" + version + ") with near lights capped: see Assets/ShaftAttack/Editor/DigbyLitShader.cs\n";
            why = null;
            return header + lit.Replace(title, "Shader \"" + ShaderName + "\"").Replace(include, cap + include);
        }

        private static string Read(string virtualPath, out string version)
        {
            version = "URP";
            UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(virtualPath);
            string real = Path.GetFullPath(virtualPath);
            if (info != null)
            {
                version = info.name + " " + info.version;
                real = Path.Combine(info.resolvedPath, virtualPath.Substring(("Packages/" + info.name + "/").Length));
            }
            return File.Exists(real) ? File.ReadAllText(real) : null;
        }

        private static int Count(string text, string what)
        {
            int n = 0;
            for (int i = text.IndexOf(what, System.StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(what, i + what.Length, System.StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
#endif
