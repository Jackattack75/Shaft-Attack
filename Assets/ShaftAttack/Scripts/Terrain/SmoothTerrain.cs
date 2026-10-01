using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace ShaftAttack
{
    /// <summary>
    /// One band of rock. Layers are listed top to bottom and get darker as they go down.
    /// </summary>
    [System.Serializable]
    public class RockLayer
    {
        public string name = "Rock";

        [Tooltip("World height this layer's TOP sits at. Ignored on the first layer, which reaches the sky. " +
                 "These must descend down the list.")]
        public float topHeight = 24f;

        [Tooltip("Colour of the rock. Deeper layers should be darker.")]
        public Color color = new Color(0.48f, 0.37f, 0.25f);

        [Tooltip("How tough this rock is. Nothing reads it yet - it's here so the pickaxe, guns and " +
                 "ammo payout can all hang off one number later.")]
        public float hardness = 1f;

        [Tooltip("Optional material override, used exactly as-is, instead of the generated one.")]
        public Material material;

        [Tooltip("Hand-painted texture for this layer, projected onto the rock in world space. Left " +
                 "empty, it's filled in from ShaftAttack/Textures/Rock_<name>.png, or the layer uses " +
                 "its flat colour if there isn't one.")]
        public Texture2D texture;

        [Range(0f, 1f)]
        [Tooltip("Horizontal sediment bands painted from world height, so they line up across a whole " +
                 "wall like real strata. 1 on Sandstone, 0 elsewhere.")]
        public float strata = 0f;
    }

    /// <summary>
    /// Smooth, fully destructible terrain.
    ///
    /// Under the hood it is a 3D grid of "density" samples, one every voxelSize metres:
    /// positive = rock, negative = air, and the visible surface is wherever the value crosses
    /// zero. Digging lowers the values, so every dig leaves a real hole and every dug slope is a
    /// ramp you can slide down.
    ///
    /// HOW ROCK BREAKS. A dig removes a cluster of FRACTURE FRAGMENTS, not a sphere. Each fragment
    /// is a random convex polyhedron - a handful of flat planes intersected together, randomly
    /// turned, unevenly spaced and squashed into a slab, because rock breaks in flakes, not dice.
    /// A bomb breaks out a few big fragments and knocks a ring of small chips off the rim, which
    /// is what serrates the edge; a pickaxe bite is one or two small fragments.
    ///
    /// Why planes and not noise: the mesher smooths away anything finer than a few samples, so
    /// noise-based roughness mostly vanishes. A PLANE is the one shape it reproduces exactly at
    /// any voxel size - the flat faces come out perfectly flat and only the edges get a slight
    /// bevel. That's why this reads as broken rock at 0.5 m voxels where noise never could.
    /// TerrainChunk's hard edges (smoothingAngle) are the other half: they keep the creases
    /// between fracture faces crisp.
    ///
    /// Everything about a blast's shape is derived from its POSITION, not a random number. The
    /// same spot always breaks the same way and different spots break differently, so a
    /// destructible edit can be networked as nothing more than (position, radius).
    ///
    /// After every dig, any rock left floating with nothing holding it up is removed.
    ///
    /// Performance:
    ///  - A fragment is skipped as soon as it provably can't be the nearest one to a sample, and
    ///    its plane test stops early for the same reason, so most samples touch very few planes.
    ///  - Only chunks a dig touched are rebuilt, nearest to the camera first, within a
    ///    per-frame time budget so a big blast never causes a hitch.
    ///  - Physics colliders (the expensive half) are baked on worker threads in parallel.
    /// </summary>
    [DisallowMultipleComponent]
    public class SmoothTerrain : MonoBehaviour
    {
        public static SmoothTerrain Instance { get; private set; }

        [Header("Resolution")]
        [Tooltip("Metres between samples. Fracture faces look right at 0.5 - halving this costs 8x " +
                 "the memory and time and changes the look surprisingly little.")]
        public float voxelSize = 0.5f;
        [Tooltip("Cells along each side of one chunk.")]
        public int cellsPerChunk = 16;

        [Header("World size (in chunks)")]
        public int chunksX = 8;
        public int chunksY = 5;
        public int chunksZ = 8;

        [Header("Look")]
        [Tooltip("Base material. Layer materials are tinted copies of this, so its shader is what the whole world uses.")]
        public Material material;
        [Range(0f, 180f)]
        [Tooltip("Faces that meet at a sharper angle than this get a HARD edge; gentler curves stay " +
                 "smooth. This is what keeps fracture faces reading as broken rock. 180 = everything " +
                 "smooth (the old look), 0 = every polygon faceted. Takes effect when you press Play.")]
        public float smoothingAngle = 28f;

        [Header("Rock layers (top to bottom, darkest at the bottom)")]
        public RockLayer[] layers = DefaultLayers();

        [Tooltip("How far a layer boundary wanders above and below its height, in metres. 0 = dead flat bands.")]
        public float layerWander = 1.8f;
        [Tooltip("Size of the wander. Smaller = long lazy waves, bigger = a jagged seam.")]
        public float layerNoiseScale = 0.045f;
        [Range(0f, 1f)]
        [Tooltip("Rock wants to be matte.")]
        public float rockSmoothness = 0.04f;

        [Header("Rock surface shape")]
        [Tooltip("How far the untouched rock face wanders in and out, in metres. Gentle undulation " +
                 "only - at 0.5 m voxels, fine noise gets smoothed away. Raising it also makes floors " +
                 "bumpier to slide on.")]
        public float surfaceRoughness = 0.5f;
        [Tooltip("Size of the lumps, in metres.")]
        public float rockDetailSize = 4.5f;
        [Range(1, 4)] public int rockDetailOctaves = 2;
        [Range(0f, 1f)] public float rockJaggedness = 0.65f;
        [Tooltip("Fixed seed for the rock grain, so the stone looks the same every run while you tune it.")]
        public int rockSeed = 1337;

        [Header("Test world shape")]
        [Tooltip("Ground level, in metres above the bottom of the world.")]
        public float surfaceHeight = 28f;
        public float surfaceBumpiness = 1.5f;
        public float surfaceNoiseScale = 0.06f;
        [Tooltip("Carve the big open arena under the spawn.")]
        public bool carveArena = true;
        public Vector3 arenaCenter = new Vector3(32f, 12f, 32f);
        public Vector3 arenaRadii = new Vector3(15f, 7f, 15f);

        [Header("Digging - how rock breaks")]
        [Tooltip("Nothing below this height can be dug, so the world always has a floor.")]
        public float minDigHeight = 1f;
        [Range(1, 6)]
        [Tooltip("Big fragments a blast breaks out. A pickaxe bite always uses at most 2.")]
        public int digFragments = 4;
        [Range(4, 12)]
        [Tooltip("Flat faces per fragment. Fewer = chunkier, bigger fracture faces. More = rounder.")]
        public int digFacets = 7;
        [Range(0, 10)]
        [Tooltip("Small chips knocked off around the edge of a blast. These are what serrate the rim. " +
                 "Bombs only - a pickaxe bite is too small for them to show.")]
        public int digRimChips = 6;
        [Range(0.3f, 1f)]
        [Tooltip("Size of the central fragment, as a fraction of the blast radius.")]
        public float digCoreSize = 0.8f;
        [Range(0f, 1f)]
        [Tooltip("How far the other fragments sit from the centre, as a fraction of the blast radius.")]
        public float digFragmentSpread = 0.5f;
        [Range(0.1f, 1f)] public float digFragmentMinSize = 0.42f;
        [Range(0.1f, 1f)] public float digFragmentMaxSize = 0.65f;
        [Range(0f, 0.6f)]
        [Tooltip("How uneven the faces are. 0 = neat crystal shapes, higher = lopsided fragments.")]
        public float digFaceVariation = 0.3f;
        [Range(0f, 0.8f)]
        [Tooltip("How much each fragment is flattened into a slab. Rock breaks in flakes, not dice.")]
        public float digSlabbiness = 0.4f;

        [Header("Rock textures (hand-painted, triplanar)")]
        [Tooltip("Draw each layer with its hand-painted texture. Off = flat layer colours, as before.")]
        public bool useRockTextures = true;
        [Tooltip("The triplanar rock shader. Referenced here rather than found by name so it's " +
                 "guaranteed to be included in builds. Filled in automatically in the editor.")]
        public Shader triplanarShader;
        [Tooltip("Metres of world one texture tile covers. The textures were painted for 6.")]
        public float textureTileMeters = 6f;
        [Range(1f, 16f)]
        [Tooltip("How sharply the three projections hand over. Higher = each flat face takes a single " +
                 "projection; lower = softer, blurrier blends on curved rock.")]
        public float triplanarSharpness = 6f;
        [Tooltip("Thickness of Sandstone's sediment bands, in metres.")]
        public float strataBandHeight = 0.55f;
        [Tooltip("Stops the texture visibly repeating on big walls and floors: each patch of rock " +
                 "shows its own shifted (sometimes mirrored) copy, cross-faded at soft seams.")]
        public bool breakUpTiling = true;
        [Range(0f, 1f)]
        [Tooltip("Slow light/dark and warm/cool drift across the rock, from noise that never repeats, " +
                 "so big surfaces don't look flat. 0 = off.")]
        public float largeScaleVariation = 0.35f;

        [Header("Floating rock cleanup")]
        [Tooltip("After each dig, delete any rock that's no longer connected to anything solid.")]
        public bool removeFloatingRock = true;
        [Tooltip("Rock this deep inside solid ground (metres from the nearest surface) counts as anchored. Keeps the check fast.")]
        public float anchorDepth = 3.5f;
        [Tooltip("Pieces bigger than this many samples are always treated as supported. 20000 samples = 2500 m³.")]
        public int maxFloatingPieceSamples = 20000;

        [Header("Performance")]
        [Tooltip("Milliseconds per frame spent rebuilding chunk meshes. Leftover chunks finish next frame.")]
        public float rebuildBudgetMs = 4f;

        /// <summary>(centre, radius, rock samples removed). Hook for ammo, sound, debris.</summary>
        public System.Action<Vector3, float, int> OnDug;
        /// <summary>(centre of the piece, samples removed). Hook for a crumble effect.</summary>
        public System.Action<Vector3, int> OnFloatingRockRemoved;

        // --- stats, shown on the debug HUD ---
        public float GenerationMs { get; private set; }
        public float LastFrameRebuildMs { get; private set; }
        public float AverageChunkMs { get; private set; }
        public int PendingRebuilds { get { return dirty.Count; } }
        public int FloatingPiecesRemoved { get; private set; }

        public float VoxelSize { get { return voxelSize; } }
        public int LayerCount { get { return layerMaterials != null ? layerMaterials.Length : 1; } }
        public Material[] LayerMaterials { get { return layerMaterials; } }

        private const float SolidValue = 1f;
        private const float AirValue = -1f;
        private const float ClampValue = 4f;

        /// <summary>How far either side of a surface the generation noise reaches, as a multiple of its amplitude.</summary>
        private const float NoiseWindow = 2.5f;

        // --- fracture fragments ---------------------------------------------------------------
        private const int MaxPieces = 16;
        private const int MaxFacets = 12;
        /// <summary>How far a face normal is knocked off its even spacing. Breaks up the "crystal" look.</summary>
        private const float FacetJitter = 0.35f;
        /// <summary>Every fragment is also clipped to a sphere this many times its size, so two
        /// nearly-parallel faces can never meet in a long needle.</summary>
        private const float PieceBoundScale = 1.5f;
        /// <summary>Rim chips sit on a shell at about this fraction of the blast radius.</summary>
        private const float ChipShell = 0.82f;
        private const float ChipMinSize = 0.26f;
        private const float ChipMaxSize = 0.38f;

        // Dig only ever runs on the main thread, so this scratch is safe to share.
        private static readonly Vector3[] PieceCenter = new Vector3[MaxPieces];
        private static readonly float[] PieceBound = new float[MaxPieces];
        private static readonly int[] PieceFacets = new int[MaxPieces];
        private static readonly Vector3[] PlaneNormal = new Vector3[MaxPieces * MaxFacets];
        private static readonly float[] PlaneOffset = new float[MaxPieces * MaxFacets];

        private int sx, sy, sz;
        private int floorY;
        private float[] density;
        private TerrainChunk[,,] chunks;

        // Layer boundaries: one wander offset per (x,z) column per boundary, worked out once.
        private float[] boundaryOffset;
        private float[] boundaryHeight;
        private int boundaryCount;
        private Material[] layerMaterials;
        private readonly List<Material> ownedMaterials = new List<Material>();
        private Shader rockShader;
        private bool rockShaderResolved;

        private readonly HashSet<TerrainChunk> dirty = new HashSet<TerrainChunk>();
        private readonly List<TerrainChunk> queue = new List<TerrainChunk>();
        private readonly List<TerrainChunk> bakeList = new List<TerrainChunk>();
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        private System.Comparison<TerrainChunk> byDistance;
        private Vector3 sortFrom;

        // Floating-rock flood fill scratch space.
        private int[] visitMark;
        private int[] floodQueue;
        private int markCounter;

        /// <summary>
        /// The four bands, surface to bedrock, each darker than the one above. Used both as the
        /// inspector default and as the fallback if the array ever comes back empty.
        /// </summary>
        public static RockLayer[] DefaultLayers()
        {
            return new[]
            {
                new RockLayer { name = "Topsoil",   topHeight = 9999f, hardness = 1.0f, color = new Color(0.48f, 0.37f, 0.25f) },
                new RockLayer { name = "Sandstone", topHeight = 24f,   hardness = 1.6f, color = new Color(0.35f, 0.29f, 0.22f), strata = 1f },
                new RockLayer { name = "Greystone", topHeight = 16f,   hardness = 2.4f, color = new Color(0.24f, 0.23f, 0.24f) },
                new RockLayer { name = "Deeprock",  topHeight = 8f,    hardness = 3.6f, color = new Color(0.14f, 0.14f, 0.17f) }
            };
        }

        private void Awake()
        {
            Instance = this;
            byDistance = CompareByDistance;

            sx = chunksX * cellsPerChunk + 1;
            sy = chunksY * cellsPerChunk + 1;
            sz = chunksZ * cellsPerChunk + 1;
            floorY = Mathf.CeilToInt(minDigHeight / voxelSize);
            density = new float[sx * sy * sz];

            watch.Restart();
            BuildLayers();
            Generate();
            BuildChunks();
            GenerationMs = (float)watch.Elapsed.TotalMilliseconds;
            Debug.Log("[Terrain] Built " + (chunksX * chunksY * chunksZ) + " chunks, " +
                      layerMaterials.Length + " rock layers, in " + GenerationMs.ToString("0") + " ms");
        }

        private void OnDestroy()
        {
            for (int i = 0; i < ownedMaterials.Count; i++)
                if (ownedMaterials[i] != null) Destroy(ownedMaterials[i]);
            ownedMaterials.Clear();

            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ rock layers

        /// <summary>
        /// Works out each layer's material and precomputes how far its top boundary wanders over
        /// every (x,z) column. Meshing then only does an array lookup per vertex.
        /// </summary>
        private void BuildLayers()
        {
            if (layers == null || layers.Length == 0) layers = DefaultLayers();

            int count = layers.Length;
            boundaryCount = count - 1;

            layerMaterials = new Material[count];
            for (int i = 0; i < count; i++) layerMaterials[i] = MakeLayerMaterial(layers[i]);

            boundaryHeight = new float[Mathf.Max(1, boundaryCount)];
            for (int i = 0; i < boundaryCount; i++) boundaryHeight[i] = layers[i + 1].topHeight;

            if (boundaryCount == 0) { boundaryOffset = null; return; }

            int columns = sx * sz;
            boundaryOffset = new float[boundaryCount * columns];

            if (layerWander <= 0f) return;

            for (int b = 0; b < boundaryCount; b++)
            {
                float seedX = 131.7f * (b + 1);
                float seedZ = 57.3f * (b + 1);
                int baseIndex = b * columns;

                for (int z = 0; z < sz; z++)
                for (int x = 0; x < sx; x++)
                {
                    float n = Mathf.PerlinNoise(x * voxelSize * layerNoiseScale + seedX,
                                                z * voxelSize * layerNoiseScale + seedZ);
                    boundaryOffset[baseIndex + x + sx * z] = (n - 0.5f) * 2f * layerWander;
                }
            }
        }

        /// <summary>
        /// The triplanar rock shader, or null if textures are off or it can't run on this machine.
        /// Never fatal - the layers just fall back to their flat colours.
        /// </summary>
        private Shader GetRockShader()
        {
            if (rockShaderResolved) return rockShader;
            rockShaderResolved = true;

            if (!useRockTextures) return null;

            Shader s = triplanarShader != null ? triplanarShader : Shader.Find("ShaftAttack/Triplanar Rock");
            if (s == null || !s.isSupported)
            {
                Debug.LogWarning("[Terrain] Triplanar rock shader isn't available. Using flat layer colours.");
                return null;
            }

            rockShader = s;
            return s;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Fills any empty texture slot from ShaftAttack/Textures/Rock_[LayerName].png and the shader
        /// slot from ShaftAttack/Shaders, so a fresh or older scene picks the art up with no wiring.
        /// Anything already assigned is left alone.
        /// </summary>
        private void OnValidate()
        {
            if (triplanarShader == null)
                triplanarShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/ShaftAttack/Shaders/TriplanarRock.shader");

            if (layers == null) return;
            for (int i = 0; i < layers.Length; i++)
            {
                RockLayer l = layers[i];
                if (l == null || l.texture != null || string.IsNullOrEmpty(l.name)) continue;
                l.texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ShaftAttack/Textures/Rock_" + l.name + ".png");
            }
        }
#endif

        private Material MakeLayerMaterial(RockLayer layer)
        {
            if (layer.material != null) return layer.material;

            Material m;
            Shader rock = layer.texture != null ? GetRockShader() : null;

            if (rock != null)
            {
                // The texture already carries the layer's colour, so the tint stays white.
                m = new Material(rock);
                m.name = "Rock_" + layer.name;
                m.SetTexture("_BaseMap", layer.texture);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_TileMeters", Mathf.Max(0.1f, textureTileMeters));
                m.SetFloat("_Sharpness", triplanarSharpness);
                m.SetFloat("_Smoothness", rockSmoothness);
                m.SetFloat("_Strata", layer.strata);
                m.SetFloat("_StrataHeight", Mathf.Max(0.05f, strataBandHeight));
                m.SetFloat("_AntiTile", breakUpTiling ? 1f : 0f);
                m.SetFloat("_MacroStrength", largeScaleVariation);
                ownedMaterials.Add(m);
                return m;
            }

            if (material != null)
            {
                m = new Material(material);   // same shader as the rest of the world, retinted
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                m = new Material(shader);
            }

            m.name = "Rock_" + layer.name;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", layer.color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", layer.color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", rockSmoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", rockSmoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);

            ownedMaterials.Add(m);
            return m;
        }

        /// <summary>
        /// Which layer the rock at this point belongs to, given in terrain-local metres.
        /// Deepest band wins, so the boundaries are checked from the bottom up.
        /// </summary>
        public int LayerAt(float x, float y, float z)
        {
            if (boundaryCount <= 0) return 0;

            int gx = Mathf.Clamp((int)(x / voxelSize + 0.5f), 0, sx - 1);
            int gz = Mathf.Clamp((int)(z / voxelSize + 0.5f), 0, sz - 1);
            int col = gx + sx * gz;
            int columns = sx * sz;

            for (int b = boundaryCount - 1; b >= 0; b--)
            {
                float top = boundaryHeight[b];
                if (boundaryOffset != null) top += boundaryOffset[b * columns + col];
                if (y <= top) return b + 1;
            }
            return 0;
        }

        /// <summary>How tough the rock is at a world point. Nothing uses this yet.</summary>
        public float HardnessAt(Vector3 worldPoint)
        {
            Vector3 p = transform.InverseTransformPoint(worldPoint);
            int i = LayerAt(p.x, p.y, p.z);
            return (layers != null && i < layers.Length) ? layers[i].hardness : 1f;
        }

        // ------------------------------------------------------------------ sampling

        /// <summary>Density at a sample index. Outside the world: rock to the sides and below, air above.</summary>
        public float Sample(int x, int y, int z)
        {
            if (y < 0) return SolidValue;
            if (y >= sy) return AirValue;
            if (x < 0 || z < 0 || x >= sx || z >= sz) return SolidValue;
            return density[x + sx * (y + sy * z)];
        }

        /// <summary>
        /// Copies a size^3 block of samples into dst in one go, so chunk meshing reads a small
        /// flat array instead of doing bounds checks on every lookup.
        /// </summary>
        public void CopySamples(float[] dst, int x0, int y0, int z0, int size, out bool anySolid, out bool anyAir)
        {
            anySolid = false;
            anyAir = false;

            bool fullyInside = x0 >= 0 && y0 >= 0 && z0 >= 0 &&
                               x0 + size <= sx && y0 + size <= sy && z0 + size <= sz;
            int k = 0;

            for (int z = 0; z < size; z++)
            for (int y = 0; y < size; y++)
            {
                if (fullyInside)
                {
                    int src = x0 + sx * ((y0 + y) + sy * (z0 + z));
                    for (int x = 0; x < size; x++)
                    {
                        float v = density[src + x];
                        dst[k++] = v;
                        if (v > 0f) anySolid = true; else anyAir = true;
                    }
                }
                else
                {
                    for (int x = 0; x < size; x++)
                    {
                        float v = Sample(x0 + x, y0 + y, z0 + z);
                        dst[k++] = v;
                        if (v > 0f) anySolid = true; else anyAir = true;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ generation

        private void Generate()
        {
            float seed = Random.Range(0f, 1000f);
            float minR = Mathf.Min(arenaRadii.x, Mathf.Min(arenaRadii.y, arenaRadii.z));

            float[] heights = new float[sx * sz];
            for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                float n = Mathf.PerlinNoise(x * voxelSize * surfaceNoiseScale + seed, z * voxelSize * surfaceNoiseScale + seed);
                heights[x + sx * z] = surfaceHeight + (n - 0.5f) * 2f * surfaceBumpiness;
            }

            float roughAmp = Mathf.Max(0f, surfaceRoughness);
            float roughFreq = rockDetailSize > 0.01f ? 1f / rockDetailSize : 0.3f;
            float roughReach = roughAmp * NoiseWindow;

            for (int z = 0; z < sz; z++)
            for (int y = 0; y < sy; y++)
            for (int x = 0; x < sx; x++)
            {
                float d;

                if (y == sy - 1)
                {
                    d = AirValue;   // open sky, and caps the boundary walls cleanly
                }
                else if (y == 0 || x == 0 || z == 0 || x == sx - 1 || z == sz - 1)
                {
                    d = SolidValue; // indestructible shell: floor plus perimeter walls
                }
                else
                {
                    float px = x * voxelSize, py = y * voxelSize, pz = z * voxelSize;
                    d = heights[x + sx * z] - py;

                    if (carveArena)
                    {
                        float qx = (px - arenaCenter.x) / arenaRadii.x;
                        float qy = (py - arenaCenter.y) / arenaRadii.y;
                        float qz = (pz - arenaCenter.z) / arenaRadii.z;
                        float cave = (Mathf.Sqrt(qx * qx + qy * qy + qz * qz) - 1f) * minR;
                        d = Mathf.Min(d, cave);
                    }

                    // Only near a surface can the noise move the zero crossing, so skip it
                    // elsewhere, and fade it out at the edge of that band so the shortcut is invisible.
                    if (roughAmp > 0.001f && d > -roughReach && d < roughReach)
                    {
                        float window = 1f - Mathf.Abs(d) / roughReach;
                        d += RockNoise.Rock(px, py, pz, roughFreq, rockDetailOctaves, rockJaggedness, rockSeed)
                             * roughAmp * window;
                    }
                }

                density[x + sx * (y + sy * z)] = Mathf.Clamp(d, -ClampValue, ClampValue);
            }
        }

        private void BuildChunks()
        {
            chunks = new TerrainChunk[chunksX, chunksY, chunksZ];
            float chunkWorld = cellsPerChunk * voxelSize;
            bakeList.Clear();

            for (int cz = 0; cz < chunksZ; cz++)
            for (int cy = 0; cy < chunksY; cy++)
            for (int cx = 0; cx < chunksX; cx++)
            {
                GameObject go = new GameObject("Chunk " + cx + "_" + cy + "_" + cz);
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(cx, cy, cz) * chunkWorld;

                TerrainChunk chunk = go.AddComponent<TerrainChunk>();
                chunk.Init(this, cx, cy, cz);
                if (chunk.BuildMesh()) bakeList.Add(chunk);
                chunks[cx, cy, cz] = chunk;
            }

            BakeColliders(bakeList);
        }

        // ------------------------------------------------------------------ fracture fragments

        /// <summary>Deterministic xorshift, so a blast's shape depends only on where it happened.</summary>
        private static float NextRandom(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xffffff) * (1f / 16777216f);
        }

        /// <summary>A direction spread evenly over the sphere (picking each axis at random would
        /// bunch them toward the corners of a cube).</summary>
        private static Vector3 RandomUnit(ref uint state)
        {
            float u = NextRandom(ref state) * 2f - 1f;
            float a = NextRandom(ref state) * Mathf.PI * 2f;
            float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
            return new Vector3(ring * Mathf.Cos(a), u, ring * Mathf.Sin(a));
        }

        /// <summary>A uniformly random rotation (Shoemake's method).</summary>
        private static Quaternion RandomRotation(ref uint state)
        {
            float u1 = NextRandom(ref state);
            float u2 = NextRandom(ref state) * Mathf.PI * 2f;
            float u3 = NextRandom(ref state) * Mathf.PI * 2f;
            float a = Mathf.Sqrt(1f - u1), b = Mathf.Sqrt(u1);
            return new Quaternion(a * Mathf.Sin(u2), a * Mathf.Cos(u2), b * Mathf.Sin(u3), b * Mathf.Cos(u3));
        }

        /// <summary>The i-th of n directions laid out evenly on a sphere (Fibonacci spiral). Starting
        /// every fragment from even coverage guarantees it is a closed shape before it's jittered.</summary>
        private static Vector3 FibonacciDirection(int i, int n)
        {
            float y = 1f - (i + 0.5f) * 2f / n;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float phi = i * 2.39996323f;   // the golden angle
            return new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
        }

        /// <summary>
        /// Adds one fracture fragment: a convex polyhedron of <paramref name="facets"/> planes around
        /// <paramref name="center"/>, randomly turned, unevenly spaced and optionally squashed into a slab.
        /// </summary>
        private void AddPiece(ref int count, Vector3 center, float size, int facets, float slab,
                              ref uint rng, Vector3 blastCenter, ref float maxExtent)
        {
            if (count >= MaxPieces) return;

            Quaternion turn = RandomRotation(ref rng);
            Vector3 squashAxis = RandomUnit(ref rng);
            int start = count * MaxFacets;

            for (int f = 0; f < facets; f++)
            {
                Vector3 n = turn * FibonacciDirection(f, facets) + RandomUnit(ref rng) * FacetJitter;
                n.Normalize();

                float uneven = 1f + (NextRandom(ref rng) * 2f - 1f) * digFaceVariation;
                float squash = 1f - slab * Mathf.Abs(Vector3.Dot(n, squashAxis));

                PlaneNormal[start + f] = n;
                PlaneOffset[start + f] = size * uneven * squash;
            }

            PieceCenter[count] = center;
            PieceBound[count] = size * PieceBoundScale;
            PieceFacets[count] = facets;

            float reach = (center - blastCenter).magnitude + PieceBound[count];
            if (reach > maxExtent) maxExtent = reach;
            count++;
        }

        /// <summary>
        /// Lays out every fragment this dig breaks off: a central one, a few more around it, and for
        /// bombs a ring of small chips near the rim. Returns how many, and the furthest any reaches.
        /// </summary>
        private int BuildPieces(Vector3 c, float radius, int seed, out float maxExtent)
        {
            int count = 0;
            maxExtent = 0f;

            // A pickaxe bite is small enough that too many fragments just read as mush, and the
            // player has to be able to predict where their swing lands.
            bool small = radius < 2f;

            int fragments = Mathf.Clamp(digFragments, 1, 6);
            if (small) fragments = Mathf.Min(fragments, 2);
            int facets = Mathf.Clamp(digFacets, 4, MaxFacets);
            float minSize = Mathf.Min(digFragmentMinSize, digFragmentMaxSize);
            float maxSize = Mathf.Max(digFragmentMinSize, digFragmentMaxSize);

            uint rng = (uint)seed | 1u;
            for (int k = 0; k < fragments; k++)
            {
                Vector3 center;
                float size;
                if (k == 0)
                {
                    center = c;
                    size = radius * digCoreSize;
                }
                else
                {
                    Vector3 dir = RandomUnit(ref rng);
                    float dist = radius * digFragmentSpread * (0.45f + 0.55f * NextRandom(ref rng));
                    center = c + dir * dist;
                    size = radius * Mathf.Lerp(minSize, maxSize, NextRandom(ref rng));
                }
                AddPiece(ref count, center, size, facets, digSlabbiness, ref rng, c, ref maxExtent);
            }

            // Chips come from their own stream, so changing the chip count never reshuffles the
            // big fragments.
            int chips = small ? 0 : Mathf.Clamp(digRimChips, 0, MaxPieces - count);
            uint chipRng = ((uint)seed ^ 0x5A5A5A5u) | 1u;
            int chipFacets = Mathf.Max(5, facets - 1);

            for (int k = 0; k < chips; k++)
            {
                Vector3 dir = RandomUnit(ref chipRng);
                Vector3 center = c + dir * radius * ChipShell * (0.9f + 0.2f * NextRandom(ref chipRng));
                float size = radius * Mathf.Lerp(ChipMinSize, ChipMaxSize, NextRandom(ref chipRng));
                AddPiece(ref count, center, size, chipFacets, 0f, ref chipRng, c, ref maxExtent);
            }

            return count;
        }

        // ------------------------------------------------------------------ digging

        /// <summary>
        /// Break rock out of the world, then clear anything the dig left floating. Returns how
        /// many rock samples went (later: the ammo payout).
        ///
        /// The hole is the union of the fracture fragments from BuildPieces - see the class summary.
        /// Its shape comes from the position, so it's the same on every machine.
        /// </summary>
        public int Dig(Vector3 worldCenter, float radius)
        {
            if (density == null || radius <= 0f) return 0;

            Vector3 c = transform.InverseTransformPoint(worldCenter);
            float inv = 1f / voxelSize;

            float maxExtent;
            int pieces = BuildPieces(c, radius, RockNoise.SeedFromPosition(c), out maxExtent);
            float outer = maxExtent + voxelSize;

            // Samples on the outer shell (index 0 and max) are never touched: that's the
            // indestructible boundary that stops the arena growing forever.
            int x0 = Mathf.Max(1, Mathf.FloorToInt((c.x - outer) * inv));
            int x1 = Mathf.Min(sx - 2, Mathf.CeilToInt((c.x + outer) * inv));
            int y0 = Mathf.Max(Mathf.Max(1, floorY), Mathf.FloorToInt((c.y - outer) * inv));
            int y1 = Mathf.Min(sy - 2, Mathf.CeilToInt((c.y + outer) * inv));
            int z0 = Mathf.Max(1, Mathf.FloorToInt((c.z - outer) * inv));
            int z1 = Mathf.Min(sz - 2, Mathf.CeilToInt((c.z + outer) * inv));
            if (x0 > x1 || y0 > y1 || z0 > z1) return 0;

            int removed = 0;
            bool changed = false;

            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x * voxelSize, py = y * voxelSize, pz = z * voxelSize;

                // Distance to the nearest fragment. Taking the minimum over fragments is a union -
                // the hole is everything inside ANY of them. Inside one fragment, taking the maximum
                // over its planes is an intersection - that's what makes each one a solid with flat
                // faces. A fragment's value only ever rises as planes are added, so the moment it
                // can't beat the current best it's abandoned.
                float s = float.MaxValue;
                for (int k = 0; k < pieces; k++)
                {
                    float rx = px - PieceCenter[k].x;
                    float ry = py - PieceCenter[k].y;
                    float rz = pz - PieceCenter[k].z;

                    float piece = Mathf.Sqrt(rx * rx + ry * ry + rz * rz) - PieceBound[k];
                    if (piece >= s) continue;

                    int first = k * MaxFacets;
                    int last = first + PieceFacets[k];
                    for (int f = first; f < last; f++)
                    {
                        Vector3 n = PlaneNormal[f];
                        float plane = n.x * rx + n.y * ry + n.z * rz - PlaneOffset[f];
                        if (plane > piece)
                        {
                            piece = plane;
                            if (piece >= s) break;
                        }
                    }

                    if (piece < s) s = piece;
                }

                int i = x + sx * (y + sy * z);
                float old = density[i];
                if (s < old)
                {
                    if (old > 0f && s <= 0f) removed++;
                    density[i] = Mathf.Max(s, -ClampValue);
                    changed = true;
                }
            }

            if (changed)
            {
                MarkDirty(x0, y0, z0, x1, y1, z1);
                if (removeFloatingRock) RemoveFloatingRock(x0 - 1, y0 - 1, z0 - 1, x1 + 1, y1 + 1, z1 + 1);
            }

            if (OnDug != null) OnDug(worldCenter, radius, removed);
            return removed;
        }

        // ------------------------------------------------------------------ floating rock

        /// <summary>
        /// Starting from every bit of rock around the crater, flood-fill through connected rock.
        /// If the fill reaches something anchored (the world's shell, the undiggable floor, or rock
        /// buried deep inside solid ground), that piece is supported. If it runs out of rock first,
        /// it's floating - delete it.
        /// </summary>
        private void RemoveFloatingRock(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            if (visitMark == null || visitMark.Length != density.Length)
            {
                visitMark = new int[density.Length];
                markCounter = 0;
            }
            if (floodQueue == null || floodQueue.Length < maxFloatingPieceSamples + 8)
                floodQueue = new int[maxFloatingPieceSamples + 8];

            if (markCounter > int.MaxValue - 1000000)
            {
                System.Array.Clear(visitMark, 0, visitMark.Length);
                markCounter = 0;
            }
            int digBase = markCounter + 1;

            x0 = Mathf.Max(1, x0); y0 = Mathf.Max(1, y0); z0 = Mathf.Max(1, z0);
            x1 = Mathf.Min(sx - 2, x1); y1 = Mathf.Min(sy - 2, y1); z1 = Mathf.Min(sz - 2, z1);

            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = x + sx * (y + sy * z);
                if (density[i] <= 0f) continue;
                if (visitMark[i] >= digBase) continue;

                markCounter++;
                FloodFrom(i, digBase, markCounter);
            }
        }

        private void FloodFrom(int start, int digBase, int id)
        {
            int layer = sx * sy;
            int head = 0, tail = 0;
            floodQueue[tail++] = start;
            visitMark[start] = id;

            bool supported = false;
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;

            while (head < tail && !supported)
            {
                int i = floodQueue[head++];
                int x = i % sx;
                int t = i / sx;
                int y = t % sy;
                int z = t / sy;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;

                if (IsAnchor(x, y, z, i)) { supported = true; break; }

                for (int n = 0; n < 6; n++)
                {
                    int j;
                    switch (n)
                    {
                        case 0: if (x + 1 >= sx) continue; j = i + 1; break;
                        case 1: if (x - 1 < 0) continue; j = i - 1; break;
                        case 2: if (y + 1 >= sy) continue; j = i + sx; break;
                        case 3: if (y - 1 < 0) continue; j = i - sx; break;
                        case 4: if (z + 1 >= sz) continue; j = i + layer; break;
                        default: if (z - 1 < 0) continue; j = i - layer; break;
                    }

                    if (density[j] <= 0f) continue;

                    int mark = visitMark[j];
                    if (mark == id) continue;
                    if (mark >= digBase) { supported = true; break; }
                    if (tail >= maxFloatingPieceSamples) { supported = true; break; }

                    visitMark[j] = id;
                    floodQueue[tail++] = j;
                }
            }

            if (supported) return;

            for (int k = 0; k < tail; k++) density[floodQueue[k]] = AirValue;

            MarkDirty(minX, minY, minZ, maxX, maxY, maxZ);
            FloatingPiecesRemoved++;

            if (OnFloatingRockRemoved != null)
            {
                Vector3 centre = new Vector3(minX + maxX, minY + maxY, minZ + maxZ) * (0.5f * voxelSize);
                OnFloatingRockRemoved(transform.TransformPoint(centre), tail);
            }
        }

        private bool IsAnchor(int x, int y, int z, int i)
        {
            if (x == 0 || z == 0 || y == 0 || x == sx - 1 || z == sz - 1) return true; // the shell
            if (y < floorY) return true;                                                // undiggable floor
            return density[i] >= anchorDepth;                                           // deep in solid ground
        }

        // ------------------------------------------------------------------ rebuilding

        private void MarkDirty(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            int n = cellsPerChunk;
            int cx0 = Mathf.Max(0, FloorDiv(x0 - 2, n)), cx1 = Mathf.Min(chunksX - 1, FloorDiv(x1 + 1, n));
            int cy0 = Mathf.Max(0, FloorDiv(y0 - 2, n)), cy1 = Mathf.Min(chunksY - 1, FloorDiv(y1 + 1, n));
            int cz0 = Mathf.Max(0, FloorDiv(z0 - 2, n)), cz1 = Mathf.Min(chunksZ - 1, FloorDiv(z1 + 1, n));

            for (int cz = cz0; cz <= cz1; cz++)
            for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
                dirty.Add(chunks[cx, cy, cz]);
        }

        private static int FloorDiv(int a, int b)
        {
            return a >= 0 ? a / b : -((-a + b - 1) / b);
        }

        private void LateUpdate()
        {
            if (dirty.Count == 0)
            {
                LastFrameRebuildMs = 0f;
                return;
            }

            queue.Clear();
            queue.AddRange(dirty);

            if (queue.Count > 1)
            {
                Camera cam = Camera.main;
                sortFrom = cam != null ? cam.transform.position : transform.position;
                queue.Sort(byDistance);
            }

            watch.Restart();
            bakeList.Clear();
            int built = 0;

            for (int i = 0; i < queue.Count; i++)
            {
                if (built > 0 && watch.Elapsed.TotalMilliseconds >= rebuildBudgetMs) break;

                TerrainChunk chunk = queue[i];
                dirty.Remove(chunk);
                if (chunk.BuildMesh()) bakeList.Add(chunk);
                built++;
            }

            float meshMs = (float)watch.Elapsed.TotalMilliseconds;
            BakeColliders(bakeList);

            LastFrameRebuildMs = (float)watch.Elapsed.TotalMilliseconds;
            float perChunk = meshMs / built;
            AverageChunkMs = AverageChunkMs <= 0f ? perChunk : Mathf.Lerp(AverageChunkMs, perChunk, 0.1f);
        }

        /// <summary>
        /// Baking a mesh into a physics collider is the slowest part of a rebuild. Physics.BakeMesh
        /// is safe to run off the main thread, so bake every changed chunk in parallel, then hand
        /// the finished colliders over - Unity reuses the baked data instead of baking again.
        /// </summary>
        private static void BakeColliders(List<TerrainChunk> list)
        {
            if (list.Count == 0) return;

            NativeArray<EntityId> ids = new NativeArray<EntityId>(list.Count, Allocator.TempJob);
            for (int i = 0; i < list.Count; i++) ids[i] = list[i].Mesh.GetEntityId();

            BakeJob job = new BakeJob { meshIds = ids, options = TerrainChunk.CookingOptions };
            job.Schedule(list.Count, 1).Complete();
            ids.Dispose();

            for (int i = 0; i < list.Count; i++) list[i].ApplyCollider();
        }

        private struct BakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityId> meshIds;
            public MeshColliderCookingOptions options;

            public void Execute(int index)
            {
                Physics.BakeMesh(meshIds[index], false, options);
            }
        }

        private int CompareByDistance(TerrainChunk a, TerrainChunk b)
        {
            return (a.Center - sortFrom).sqrMagnitude.CompareTo((b.Center - sortFrom).sqrMagnitude);
        }
    }
}
