using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Helper for the FA-18C Hornet mod. Lives outside the mod folder so it is never shipped.
//  1. Hides FBX sub-meshes that must not always show (centerline tank + pylon, AIM launcher rails).
//  2. Splits each tyre out of the Hornet gear meshes and parents it to the game's wheel transform
//     (LandingGear spins wheels about local X) ; the nose strut goes under 'unsprung' so it steers.
public static class FA18CFixups
{
    const string PrefabPath = "Assets/Blueprinter/Mods/FA18C/FA18C_FlyingPrefab.prefab";
    const string MeshDir = "Assets/Blueprinter/Mods/FA18C/GearMeshes";
    static readonly string[] Hide = { "tank", "tank_pyl", "pyl_aa", "pyl_aa_1", "pyl_aa_2" };

    [MenuItem("Tools/FA-18C/Apply Model Fixups")]
    public static void Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            var model = all.FirstOrDefault(t => t.name == "FA-18C Visual Model");
            if (model == null) { Debug.LogError("[FA18C] 'FA-18C Visual Model' not found in prefab"); return; }
            int hidden = 0;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (System.Array.IndexOf(Hide, t.name) < 0) continue;
                var r = t.GetComponent<MeshRenderer>();
                if (r != null) { r.enabled = false; hidden++; }
            }
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Blueprinter/Mods/FA18C", "GearMeshes");
            // main gears: single tyre 0.28 m wide, r 0.40 ; nose: twin tyres 0.18 m wide either side, r 0.27
            SplitTyre(root, "FA18C_GearVisual_L", "wheel_L", "gear_L_unsprung", false, new Vector3(-1.34f, -1.972f, -2.21f), 0.42f, 0f, 0.18f, false);
            SplitTyre(root, "FA18C_GearVisual_R", "wheel_R", "gear_R_unsprung", false, new Vector3(1.34f, -1.972f, -2.21f), 0.42f, 0f, 0.18f, false);
            SplitTyre(root, "FA18C_GearVisual_F", "wheel_F", "gear_F_unsprung", true, new Vector3(-0.004f, -2.029f, 2.985f), 0.30f, 0.05f, 0.34f, true);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[FA18C] Hid " + hidden + " model renderers; tyres split and wired to wheels. Prefab saved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SplitTyre(GameObject root, string visName, string wheelName, string unsprungName, bool strutUnderUnsprung,
                          Vector3 center, float radius, float minDx, float maxDx, bool perTriangleFallback)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        Transform vis = all.FirstOrDefault(t => t.name == visName);
        Transform wheel = all.FirstOrDefault(t => t.name == wheelName);
        Transform unsprung = all.FirstOrDefault(t => t.name == unsprungName);
        if (vis == null || wheel == null || unsprung == null) { Debug.LogError("[FA18C] missing " + visName + "/" + wheelName); return; }
        if (all.Any(t => t.name == "FA18C_Tyre_" + visName)) { Debug.Log("[FA18C] " + visName + " already split"); return; }

        var mf = vis.GetComponent<MeshFilter>();
        var mr = vis.GetComponent<MeshRenderer>();
        var src = mf.sharedMesh;
        var l2w = vis.localToWorldMatrix;
        var verts = src.vertices;
        var world = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++) world[i] = l2w.MultiplyPoint3x4(verts[i]);

        System.Func<Vector3, bool> inTyre = p =>
        {
            float dx = Mathf.Abs(p.x - center.x);
            float d = new Vector2(p.y - center.y, p.z - center.z).magnitude;
            return dx >= minDx && dx <= maxDx && d <= radius;
        };

        // group triangles into connected pieces (vertices welded by position so UV seams don't split a piece);
        // a piece spins with the wheel only if every vertex lies inside the tyre cylinder
        var key = new Dictionary<Vector3Int, int>();
        var weld = new int[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var k = Vector3Int.RoundToInt(verts[i] * 10000f);
            if (!key.TryGetValue(k, out weld[i])) { weld[i] = key.Count; key[k] = weld[i]; }
        }
        var uf = new int[key.Count];
        for (int i = 0; i < uf.Length; i++) uf[i] = i;
        System.Func<int, int> find = null;
        find = x => { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; };
        var subTris = new int[src.subMeshCount][];
        for (int s = 0; s < src.subMeshCount; s++)
        {
            subTris[s] = src.GetTriangles(s);
            var t3 = subTris[s];
            for (int i = 0; i < t3.Length; i += 3)
            {
                int a0 = find(weld[t3[i]]), a1 = find(weld[t3[i + 1]]), a2 = find(weld[t3[i + 2]]);
                uf[a1] = a0; uf[find(a2)] = a0;
            }
        }
        var outside = new HashSet<int>();
        var pieces = new HashSet<int>();
        for (int s = 0; s < src.subMeshCount; s++)
            foreach (var vi in subTris[s])
            {
                int r0 = find(weld[vi]); pieces.Add(r0);
                if (!inTyre(world[vi])) outside.Add(r0);
            }

        var strut = Object.Instantiate(src); strut.name = src.name + "_strut";
        var tyre = Object.Instantiate(src); tyre.name = src.name + "_tyre";
        int nTyre = 0, nStrut = 0;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            var tris = subTris[s];
            var a = new List<int>(); var b = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                bool t = !outside.Contains(find(weld[tris[i]]))
                    || (perTriangleFallback && inTyre(world[tris[i]]) && inTyre(world[tris[i + 1]]) && inTyre(world[tris[i + 2]]));
                var dst = t ? b : a;
                dst.Add(tris[i]); dst.Add(tris[i + 1]); dst.Add(tris[i + 2]);
            }
            strut.SetTriangles(a, s); tyre.SetTriangles(b, s);
            nTyre += b.Count / 3; nStrut += a.Count / 3;
        }
        Debug.Log("[FA18C] " + visName + ": " + pieces.Count + " pieces, " + (pieces.Count - outside.Count) + " inside tyre");
        strut.RecalculateBounds(); tyre.RecalculateBounds();
        string sp = MeshDir + "/" + visName + "_strut.asset", tp = MeshDir + "/" + visName + "_tyre.asset";
        AssetDatabase.DeleteAsset(sp); AssetDatabase.DeleteAsset(tp);
        AssetDatabase.CreateAsset(strut, sp); AssetDatabase.CreateAsset(tyre, tp);
        mf.sharedMesh = strut;

        wheel.position = center;
        wheel.rotation = Quaternion.identity;
        var go = new GameObject("FA18C_Tyre_" + visName);
        go.layer = vis.gameObject.layer;
        go.transform.SetParent(vis, false);
        go.AddComponent<MeshFilter>().sharedMesh = tyre;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mr.sharedMaterials;
        r.shadowCastingMode = mr.shadowCastingMode;
        go.transform.SetParent(wheel, true);
        if (strutUnderUnsprung) vis.SetParent(unsprung, true);
        Debug.Log("[FA18C] " + visName + ": " + nTyre + " tyre tris, " + nStrut + " strut tris");
    }
}
