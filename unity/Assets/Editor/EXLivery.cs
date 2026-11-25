using System; using System.IO; using System.Linq; using System.Text; using System.Collections.Generic; using UnityEditor; using UnityEngine;
// Dev-only: livery paint kit helpers (never shipped). Reads the prefabs, writes reports/OBJ to F14Bridge/livery.
public static class EXLivery
{
    const string OUT = "E:/Project/NOModding/F14Bridge/livery/";
    static readonly string[] Prefabs = { EXTools.F14, EXTools.FA18 };

    static Mesh GetMesh(Renderer r) { if (r is SkinnedMeshRenderer smr) return smr.sharedMesh; var mf = r.GetComponent<MeshFilter>(); return mf != null ? mf.sharedMesh : null; }
    static bool IsLivery(Material m) => m != null && m.shader != null && m.shader.name.ToLower().Contains("livery");

    public static void Report()
    {
        Directory.CreateDirectory(OUT);
        foreach (var path in Prefabs)
        {
            var sb = new StringBuilder();
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var mats = new Dictionary<Material, int>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = GetMesh(r);
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    var ms = r.sharedMaterials;
                    sb.Append(Path(r.transform, root.transform)).Append(" | ").Append(r.GetType().Name).Append(" | en=").Append(r.enabled && r.gameObject.activeInHierarchy)
                      .Append(" | mesh=").Append(mesh ? mesh.name + " v" + mesh.vertexCount + " sub" + mesh.subMeshCount : "null").Append(" | ");
                    for (int i = 0; i < ms.Length; i++)
                    {
                        var m = ms[i]; sb.Append(i).Append(':').Append(m ? m.name : "null").Append(IsLivery(m) ? "*L" : "").Append(' ');
                        if (m != null) { mats.TryGetValue(m, out int c); mats[m] = c + 1; }
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("==== MATERIALS");
                foreach (var kv in mats.OrderByDescending(k => k.Value))
                {
                    var m = kv.Key; string tex = "";
                    foreach (var prop in new[] { "_Livery", "_BaseMap", "_MainTex" })
                        if (m.HasProperty(prop) && m.GetTexture(prop) != null) { var t = m.GetTexture(prop); tex += prop + "=" + AssetDatabase.GetAssetPath(t) + " " + t.width + "x" + t.height + " ; "; }
                    sb.AppendLine(m.name + " | " + AssetDatabase.GetAssetPath(m) + " | shader=" + m.shader.name + " | uses=" + kv.Value + " | " + tex);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            File.WriteAllText(OUT + System.IO.Path.GetFileNameWithoutExtension(path) + "_report.txt", sb.ToString());
        }
        // livery assets in the mod folders
        var sb2 = new StringBuilder();
        foreach (var g in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/Blueprinter/Mods/F14B", "Assets/Blueprinter/Mods/FA18C" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g); var o = AssetDatabase.LoadMainAssetAtPath(p);
            if (o == null || !o.GetType().Name.Contains("Livery")) continue;
            var so = new SerializedObject(o); var it = so.GetIterator(); sb2.Append(p).Append(" : ");
            while (it.NextVisible(true)) { if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue) sb2.Append(it.name + "=" + AssetDatabase.GetAssetPath(it.objectReferenceValue) + " "); else if (it.propertyType == SerializedPropertyType.Float) sb2.Append(it.name + "=" + it.floatValue + " "); }
            sb2.AppendLine();
        }
        File.WriteAllText(OUT + "livery_assets.txt", sb2.ToString());
        Debug.Log("[F14] EX livery report done");
    }

    static string Path(Transform t, Transform root) { var s = t.name; while (t.parent != null && t != root) { t = t.parent; s = t.name + "/" + s; } return s; }

    // ---------- OBJ export (prefab-root space, converted to right-handed by negating X) ----------
    // extraMats: null = livery materials only; otherwise every material EXCEPT the names in extraMats (skip list).
    public static void ExportObj(string prefabPath, string objFile, HashSet<string> extraMats, HashSet<string> skipPaths)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var sb = new StringBuilder(); sb.AppendLine("# exported from " + prefabPath + " (Unity space, X negated)");
        int vBase = 1; int objs = 0; long tris = 0;
        var mtl = new HashSet<string>();
        try
        {
            root.transform.position = Vector3.zero; root.transform.rotation = Quaternion.identity; root.transform.localScale = Vector3.one;
            var w2r = root.transform.worldToLocalMatrix;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var rp = Path(r.transform, root.transform);
                if (skipPaths != null && skipPaths.Any(s => rp.Contains(s))) continue;
                Mesh mesh = GetMesh(r);
                if (mesh == null) continue;
                var ms = r.sharedMaterials;
                var subs = new List<int>();
                for (int i = 0; i < mesh.subMeshCount && i < ms.Length; i++)
                    if (ms[i] != null && (IsLivery(ms[i]) || (extraMats != null && !extraMats.Contains(ms[i].name)))) subs.Add(i);
                if (subs.Count == 0) continue;
                var m = r.transform.localToWorldMatrix; var M = w2r * m; var N = M.inverse.transpose;
                var vs = mesh.vertices; var ns = mesh.normals; var uvs = mesh.uv;
                bool flip = M.determinant < 0;
                sb.AppendLine("o " + r.name + "_" + objs);
                foreach (var v in vs) { var p = M.MultiplyPoint3x4(v); sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "v {0:F5} {1:F5} {2:F5}\n", -p.x, p.y, p.z); }
                for (int i = 0; i < vs.Length; i++) { var u = uvs.Length == vs.Length ? uvs[i] : Vector2.zero; sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "vt {0:F6} {1:F6}\n", u.x, u.y); }
                for (int i = 0; i < vs.Length; i++) { var n = ns.Length == vs.Length ? N.MultiplyVector(ns[i]).normalized : Vector3.up; sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "vn {0:F4} {1:F4} {2:F4}\n", -n.x, n.y, n.z); }
                foreach (var si in subs)
                {
                    string mn = IsLivery(ms[si]) ? "Paintable_Livery" : "Ref_" + System.Text.RegularExpressions.Regex.Replace(ms[si].name, "[^A-Za-z0-9_]", "_"); mtl.Add(mn);
                    sb.AppendLine("usemtl " + mn);
                    var t = mesh.GetTriangles(si);
                    for (int k = 0; k < t.Length; k += 3)
                    {
                        int a = t[k] + vBase, b = t[k + 1] + vBase, c = t[k + 2] + vBase;
                        // X mirror keeps Unity winding facing the wrong way for OBJ: swap (unless the transform already mirrors)
                        if (!flip) { var tmp = b; b = c; c = tmp; }
                        sb.AppendFormat("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n", a, b, c); tris++;
                    }
                }
                vBase += vs.Length; objs++;
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Directory.CreateDirectory(OUT);
        File.WriteAllText(OUT + objFile, sb.ToString());
        Debug.Log("[F14] EX obj " + objFile + " objects=" + objs + " tris=" + tris + " mats=" + string.Join(",", mtl));
    }

    static readonly HashSet<string> Interior = new HashSet<string> { "pilot", "Seats", "hotas", "tacScreenDisplay", "fighter1_cockpit_interior", "FastBomber1_cockpit 1", "Afterburner", "Fighter1_nozzle_glow", "glass_int", "fighter1_canopy_int", "FastBomber1_glass_int", "CockpitWarningLight", "light_red_off", "light_green_off", "LOD", "Mirror" };
    public static void ExportAll()
    {
        ExportObj(EXTools.F14, "F14B_paintable.obj", null, null);
        ExportObj(EXTools.F14, "F14B_full.obj", Interior, null);
        ExportObj(EXTools.FA18, "FA18C_paintable.obj", null, null);
        ExportObj(EXTools.FA18, "FA18C_full.obj", Interior, null);
        CopyTex("Assets/Blueprinter/Mods/F14B/Model/Textures/F14B_8bc3cf9c_base.png", "F14B_livery_base");
        CopyTex("Assets/Blueprinter/Mods/FA18C/Model040/Textures/FA18C_2dc7f697_base.jpg", "FA18C_livery_base");
        CopyTex("Assets/Blueprinter/Mods/FA18C/Model040/Textures/FA18C_2dc7f697_mf203.jpg", "FA18C_livery_mf203");
    }
    public static void CopyTex(string assetPath, string outName)
    {
        var full = System.IO.Path.GetFullPath(assetPath);
        File.Copy(full, OUT + outName + System.IO.Path.GetExtension(full), true);
        Debug.Log("[F14] EX copied " + assetPath);
    }
}
