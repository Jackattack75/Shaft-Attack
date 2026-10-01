using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShaftAttack
{
    /// <summary>
    /// One chunk of SmoothTerrain, meshed with Surface Nets:
    ///
    ///  1. For every cell the surface passes through, place ONE vertex at the average of the
    ///     points where the surface crosses that cell's edges.
    ///  2. For every grid edge the surface crosses, join the four cells around that edge with a quad.
    ///
    /// Rock layers: every vertex is tagged with the layer it sits in, and each quad goes into that
    /// layer's submesh. A chunk only creates submeshes for the layers it actually contains.
    ///
    /// TWO MESHES:
    ///   mesh        - shared vertices. Always the physics collider (fewest vertices = fastest bake).
    ///   renderMesh  - what you see when hard edges are on. Every quad gets its own four vertices,
    ///                 so each corner can carry its own normal. At each corner the normal is the
    ///                 average of only those neighbouring quads that face within smoothingAngle of
    ///                 this one ("auto smooth", as Blender calls it). Gentle curves blend smoothly;
    ///                 where fracture faces meet at a sharp angle the shading breaks into a crisp
    ///                 edge - which is most of what makes a crater read as broken rock.
    ///
    /// With smoothingAngle at 180 the render mesh is skipped and the shared mesh is drawn directly,
    /// exactly as before.
    ///
    /// Rebuilding is two steps so colliders can be baked in parallel:
    ///   BuildMesh()      - main thread, fills both meshes
    ///   (SmoothTerrain bakes every changed collider on worker threads)
    ///   ApplyCollider()  - hands over the pre-baked collider
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class TerrainChunk : MonoBehaviour
    {
        /// <summary>
        /// Skips the slow "cook for faster simulation" pass - terrain changes constantly, so a
        /// fast bake matters more than a marginally faster collision query.
        /// </summary>
        public const MeshColliderCookingOptions CookingOptions =
            MeshColliderCookingOptions.UseFastMidphase |
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices;

        public int CX { get; private set; }
        public int CY { get; private set; }
        public int CZ { get; private set; }
        public Vector3 Center { get; private set; }

        /// <summary>The shared-vertex mesh the collider is baked from.</summary>
        public Mesh Mesh { get { return mesh; } }

        private SmoothTerrain terrain;
        private Mesh mesh;
        private Mesh renderMesh;
        private bool hardEdges;
        private float hardEdgeCos;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private Vector3 originLocal;          // this chunk's corner, in terrain-local metres

        private Material[] materialSlots;
        private int lastLayerMask = -1;

        // ---- shared scratch. Chunks rebuild one at a time on the main thread, so this is safe.
        private static readonly List<Vector3> Verts = new List<Vector3>(4096);
        private static readonly List<Vector3> Normals = new List<Vector3>(4096);
        private static readonly List<int> VertLayer = new List<int>(4096);
        private static List<int>[] LayerTris;
        private static readonly float[] Corner = new float[8];
        private static readonly int[] CornerStep = new int[8];
        private static float[] cache;
        private static int[] vertexIndex;
        private static int cachedSize = -1;

        // Hard-edge scratch: per quad, its four corners, the slots its six indices use, its
        // normal and layer; per vertex, the quads touching it (compressed-row lists).
        private static int[] quadCorner = new int[0];
        private static byte[] quadSlot = new byte[0];
        private static Vector3[] quadNormal = new Vector3[0];
        private static int[] quadLayer = new int[0];
        private static int[] incidentStart = new int[0];
        private static int[] incidentFill = new int[0];
        private static int[] incidentQuads = new int[0];
        private static readonly List<Vector3> RenderVerts = new List<Vector3>(16384);
        private static readonly List<Vector3> RenderNormals = new List<Vector3>(16384);
        private static List<int>[] RenderTris;

        // Corner c of a cell sits at (c & 1, (c >> 1) & 1, (c >> 2) & 1).
        private static readonly Vector3[] CornerOffset =
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0),
            new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(0, 1, 1), new Vector3(1, 1, 1)
        };

        // The 12 cell edges, as corner pairs: four along X, four along Y, four along Z.
        private static readonly int[] EdgeA = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        private static readonly int[] EdgeB = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };

        public void Init(SmoothTerrain owner, int cx, int cy, int cz)
        {
            terrain = owner;
            CX = cx; CY = cy; CZ = cz;

            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
            meshCollider.cookingOptions = CookingOptions;

            mesh = new Mesh();
            mesh.name = "TerrainChunk " + cx + "_" + cy + "_" + cz;
            mesh.MarkDynamic();

            hardEdges = owner.smoothingAngle < 179f;
            hardEdgeCos = Mathf.Cos(Mathf.Clamp(owner.smoothingAngle, 0f, 180f) * Mathf.Deg2Rad);

            if (hardEdges)
            {
                renderMesh = new Mesh();
                renderMesh.name = "TerrainChunk " + cx + "_" + cy + "_" + cz + " (render)";
                renderMesh.MarkDynamic();
                GetComponent<MeshFilter>().sharedMesh = renderMesh;
            }
            else
            {
                GetComponent<MeshFilter>().sharedMesh = mesh;
            }

            float size = owner.cellsPerChunk * owner.VoxelSize;
            originLocal = new Vector3(cx, cy, cz) * size;
            Center = transform.position + Vector3.one * (size * 0.5f);
        }

        /// <summary>
        /// Rebuilds the meshes. Returns true if there's geometry whose collider needs baking;
        /// the caller bakes it and then calls ApplyCollider().
        /// </summary>
        public bool BuildMesh()
        {
            int n = terrain.cellsPerChunk;
            int c = n + 2;   // samples cached per axis: [origin-1, origin+n]
            int r = n + 1;   // cells per axis in pass 1: [origin-1, origin+n)
            float vs = terrain.VoxelSize;
            int ox = CX * n, oy = CY * n, oz = CZ * n;
            int layerCount = terrain.LayerCount;

            EnsureScratch(c, r, layerCount);

            bool anySolid, anyAir;
            terrain.CopySamples(cache, ox - 1, oy - 1, oz - 1, c, out anySolid, out anyAir);

            if (!anySolid || !anyAir)
            {
                ClearMesh();
                return false;
            }

            Verts.Clear();
            Normals.Clear();
            VertLayer.Clear();
            for (int i = 0; i < layerCount; i++) LayerTris[i].Clear();

            int cc = c * c;

            // ---- Pass 1: one vertex per cell the surface passes through ----
            for (int lz = 0; lz < r; lz++)
            for (int ly = 0; ly < r; ly++)
            for (int lx = 0; lx < r; lx++)
            {
                int li = lx + r * (ly + r * lz);
                int baseIndex = lx + c * ly + cc * lz;

                int mask = 0;
                for (int k = 0; k < 8; k++)
                {
                    float v = cache[baseIndex + CornerStep[k]];
                    Corner[k] = v;
                    if (v > 0f) mask |= 1 << k;
                }

                if (mask == 0 || mask == 255)
                {
                    vertexIndex[li] = -1;
                    continue;
                }

                Vector3 sum = Vector3.zero;
                int count = 0;
                for (int e = 0; e < 12; e++)
                {
                    int a = EdgeA[e], b = EdgeB[e];
                    if (((mask >> a) & 1) == ((mask >> b) & 1)) continue;

                    float da = Corner[a], db = Corner[b];
                    float t = da / (da - db);
                    sum += Vector3.Lerp(CornerOffset[a], CornerOffset[b], t);
                    count++;
                }

                Vector3 inCell = sum / count;
                Vector3 localPos = new Vector3(lx - 1 + inCell.x, ly - 1 + inCell.y, lz - 1 + inCell.z) * vs;

                // Normal points from rock toward air: the negative density gradient.
                float gX = (Corner[1] - Corner[0]) + (Corner[3] - Corner[2]) + (Corner[5] - Corner[4]) + (Corner[7] - Corner[6]);
                float gY = (Corner[2] - Corner[0]) + (Corner[3] - Corner[1]) + (Corner[6] - Corner[4]) + (Corner[7] - Corner[5]);
                float gZ = (Corner[4] - Corner[0]) + (Corner[5] - Corner[1]) + (Corner[6] - Corner[2]) + (Corner[7] - Corner[3]);
                Vector3 normal = new Vector3(-gX, -gY, -gZ);
                normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;

                int layer = terrain.LayerAt(originLocal.x + localPos.x,
                                            originLocal.y + localPos.y,
                                            originLocal.z + localPos.z);
                if (layer < 0) layer = 0;
                else if (layer >= layerCount) layer = layerCount - 1;

                vertexIndex[li] = Verts.Count;
                Verts.Add(localPos);
                Normals.Add(normal);
                VertLayer.Add(layer);
            }

            // ---- Pass 2: one quad per grid edge the surface crosses ----
            // Each chunk owns the edges that START inside it, so no edge is ever emitted twice.
            for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int s = (x + 1) + c * (y + 1) + cc * (z + 1);
                bool solid = cache[s] > 0f;
                int lx = x + 1, ly = y + 1, lz = z + 1;

                if (solid != (cache[s + 1] > 0f))
                    EmitQuad(V(lx, ly - 1, lz - 1, r), V(lx, ly, lz - 1, r), V(lx, ly, lz, r), V(lx, ly - 1, lz, r),
                             solid ? Vector3.right : Vector3.left);

                if (solid != (cache[s + c] > 0f))
                    EmitQuad(V(lx - 1, ly, lz - 1, r), V(lx, ly, lz - 1, r), V(lx, ly, lz, r), V(lx - 1, ly, lz, r),
                             solid ? Vector3.up : Vector3.down);

                if (solid != (cache[s + cc] > 0f))
                    EmitQuad(V(lx - 1, ly - 1, lz, r), V(lx, ly - 1, lz, r), V(lx, ly, lz, r), V(lx - 1, ly, lz, r),
                             solid ? Vector3.forward : Vector3.back);
            }

            int usedMask = 0;
            int used = 0;
            for (int i = 0; i < layerCount; i++)
            {
                if (LayerTris[i].Count == 0) continue;
                usedMask |= 1 << i;
                used++;
            }

            if (used == 0)
            {
                ClearMesh();
                return false;
            }

            // ---- The shared mesh: always the collider, and what's drawn when hard edges are off.
            mesh.Clear();
            mesh.indexFormat = Verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Verts);
            if (!hardEdges) mesh.SetNormals(Normals);   // the collider has no use for normals
            mesh.subMeshCount = used;

            int sub = 0;
            for (int i = 0; i < layerCount; i++)
            {
                if (LayerTris[i].Count == 0) continue;
                mesh.SetTriangles(LayerTris[i], sub, true);
                sub++;
            }

            if (hardEdges) BuildRenderMesh(layerCount, used);

            // The renderer's material array only changes when the set of layers present changes.
            if (usedMask != lastLayerMask)
            {
                materialSlots = new Material[used];
                Material[] source = terrain.LayerMaterials;
                sub = 0;
                for (int i = 0; i < layerCount; i++)
                {
                    if ((usedMask & (1 << i)) == 0) continue;
                    materialSlots[sub++] = source[i];
                }
                meshRenderer.sharedMaterials = materialSlots;
                lastLayerMask = usedMask;
            }

            meshRenderer.enabled = true;
            return true;
        }

        /// <summary>
        /// Builds the drawn mesh with hard edges where faces meet sharply. EmitQuad always writes a
        /// quad as exactly six indices, so every six entries in a layer list are one quad - that's
        /// what lets the quads be recovered here without storing anything extra while meshing.
        /// </summary>
        private void BuildRenderMesh(int layerCount, int used)
        {
            int quads = 0;
            for (int L = 0; L < layerCount; L++) quads += LayerTris[L].Count / 6;
            int verts = Verts.Count;
            EnsureHardEdgeScratch(quads, verts, layerCount);

            // ---- Gather each quad: its four corners, where its six indices point, its normal.
            int q = 0;
            for (int L = 0; L < layerCount; L++)
            {
                List<int> src = LayerTris[L];
                for (int k = 0; k + 5 < src.Count; k += 6, q++)
                {
                    int i0 = src[k], i1 = src[k + 1], i2 = src[k + 2];
                    int j0 = src[k + 3], j1 = src[k + 4], j2 = src[k + 5];

                    // The second triangle shares two corners with the first; the odd one out is
                    // the quad's fourth corner.
                    int fourth = !IsOneOf(j0, i0, i1, i2) ? j0 : !IsOneOf(j1, i0, i1, i2) ? j1 : j2;

                    int qc = q * 4;
                    quadCorner[qc] = i0;
                    quadCorner[qc + 1] = i1;
                    quadCorner[qc + 2] = i2;
                    quadCorner[qc + 3] = fourth;

                    int qs = q * 6;
                    quadSlot[qs] = 0;
                    quadSlot[qs + 1] = 1;
                    quadSlot[qs + 2] = 2;
                    quadSlot[qs + 3] = SlotOf(j0, i0, i1, i2);
                    quadSlot[qs + 4] = SlotOf(j1, i0, i1, i2);
                    quadSlot[qs + 5] = SlotOf(j2, i0, i1, i2);

                    // Sum of both triangles' normals - the winding already faces them out of the rock.
                    Vector3 nq = Vector3.Cross(Verts[i1] - Verts[i0], Verts[i2] - Verts[i0]) +
                                 Vector3.Cross(Verts[j1] - Verts[j0], Verts[j2] - Verts[j0]);
                    float mag = nq.magnitude;
                    quadNormal[q] = mag > 1e-8f ? nq / mag : Normals[i0];
                    quadLayer[q] = L;
                }
            }

            // ---- Which quads touch each vertex (counted, prefix-summed, then filled).
            System.Array.Clear(incidentStart, 0, verts + 1);
            for (int i = 0; i < quads * 4; i++) incidentStart[quadCorner[i] + 1]++;
            for (int v = 0; v < verts; v++) incidentStart[v + 1] += incidentStart[v];
            System.Array.Copy(incidentStart, incidentFill, verts);
            for (int i = 0; i < quads * 4; i++) incidentQuads[incidentFill[quadCorner[i]]++] = i >> 2;

            // ---- Emit four vertices per quad, each corner smoothed only across similar faces.
            RenderVerts.Clear();
            RenderNormals.Clear();
            for (int L = 0; L < layerCount; L++) RenderTris[L].Clear();

            float cosLimit = hardEdgeCos;
            for (q = 0; q < quads; q++)
            {
                Vector3 nq = quadNormal[q];
                int first = RenderVerts.Count;

                for (int corner = 0; corner < 4; corner++)
                {
                    int v = quadCorner[q * 4 + corner];
                    float ax = 0f, ay = 0f, az = 0f;

                    for (int t = incidentStart[v]; t < incidentStart[v + 1]; t++)
                    {
                        Vector3 nj = quadNormal[incidentQuads[t]];
                        if (nj.x * nq.x + nj.y * nq.y + nj.z * nq.z >= cosLimit)
                        {
                            ax += nj.x; ay += nj.y; az += nj.z;
                        }
                    }

                    float len = Mathf.Sqrt(ax * ax + ay * ay + az * az);
                    RenderVerts.Add(Verts[v]);
                    RenderNormals.Add(len > 1e-8f ? new Vector3(ax / len, ay / len, az / len) : nq);
                }

                List<int> dst = RenderTris[quadLayer[q]];
                int qs = q * 6;
                for (int k = 0; k < 6; k++) dst.Add(first + quadSlot[qs + k]);
            }

            renderMesh.Clear();
            renderMesh.indexFormat = RenderVerts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            renderMesh.SetVertices(RenderVerts);
            renderMesh.SetNormals(RenderNormals);
            renderMesh.subMeshCount = used;

            int sub = 0;
            for (int L = 0; L < layerCount; L++)
            {
                if (RenderTris[L].Count == 0) continue;
                renderMesh.SetTriangles(RenderTris[L], sub, true);
                sub++;
            }
        }

        private static bool IsOneOf(int j, int a, int b, int c)
        {
            return j == a || j == b || j == c;
        }

        private static byte SlotOf(int j, int a, int b, int c)
        {
            return (byte)(j == a ? 0 : j == b ? 1 : j == c ? 2 : 3);
        }

        /// <summary>Called after the collider data has been pre-baked on a worker thread.</summary>
        public void ApplyCollider()
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }

        private static void EnsureScratch(int c, int r, int layerCount)
        {
            if (LayerTris == null || LayerTris.Length < layerCount)
            {
                LayerTris = new List<int>[layerCount];
                for (int i = 0; i < layerCount; i++) LayerTris[i] = new List<int>(8192);
            }

            if (cachedSize == c) return;

            cache = new float[c * c * c];
            vertexIndex = new int[r * r * r];
            for (int k = 0; k < 8; k++)
                CornerStep[k] = (k & 1) + c * ((k >> 1) & 1) + c * c * ((k >> 2) & 1);
            cachedSize = c;
        }

        private static void EnsureHardEdgeScratch(int quads, int verts, int layerCount)
        {
            if (quadNormal.Length < quads)
            {
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(quads, 1024));
                quadCorner = new int[cap * 4];
                quadSlot = new byte[cap * 6];
                quadNormal = new Vector3[cap];
                quadLayer = new int[cap];
                incidentQuads = new int[cap * 4];
            }

            if (incidentStart.Length < verts + 1)
            {
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(verts + 1, 1024));
                incidentStart = new int[cap];
                incidentFill = new int[cap];
            }

            if (RenderTris == null || RenderTris.Length < layerCount)
            {
                RenderTris = new List<int>[layerCount];
                for (int i = 0; i < layerCount; i++) RenderTris[i] = new List<int>(8192);
            }
        }

        private static int V(int lx, int ly, int lz, int r)
        {
            return vertexIndex[lx + r * (ly + r * lz)];
        }

        /// <summary>
        /// Adds a quad to its layer's list as exactly six indices (BuildRenderMesh relies on that),
        /// flipping its winding if needed so it faces out of the rock.
        /// </summary>
        private static void EmitQuad(int a, int b, int c, int d, Vector3 outward)
        {
            if (a < 0 || b < 0 || c < 0 || d < 0) return;

            List<int> tris = LayerTris[VertLayer[a]];

            // The cross of the two diagonals is a stable quad normal even when a triangle is thin.
            Vector3 n = Vector3.Cross(Verts[c] - Verts[a], Verts[d] - Verts[b]);

            if (Vector3.Dot(n, outward) >= 0f)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
            else
            {
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(a); tris.Add(d); tris.Add(c);
            }
        }

        private void ClearMesh()
        {
            mesh.Clear();
            if (renderMesh != null) renderMesh.Clear();
            meshRenderer.enabled = false;
            meshCollider.sharedMesh = null;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (renderMesh != null) Destroy(renderMesh);
        }
    }
}
