using System; using System.IO; using System.Linq; using System.Text; using System.Collections.Generic; using UnityEditor; using UnityEngine;
// Dev-only geometry measurements for the extended-update builds (never shipped).
public static class EXMeasure
{
    const string D = EXTools.D;
    static Transform Find(GameObject go, string name) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    static List<Vector3> WorldVerts(GameObject go, Func<Transform, bool> pick)
    {
        var l = new List<Vector3>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || !pick(mf.transform)) continue;
            var r = mf.GetComponent<Renderer>(); if (r == null || !r.enabled) continue;
            Vector3[] vs; try { vs = mf.sharedMesh.vertices; } catch { continue; }
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in vs) l.Add(m.MultiplyPoint3x4(v));
        }
        return l;
    }
    static GameObject MountPrefab(string mountName)
    {
        var p = AssetDatabase.FindAssets(mountName).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == mountName + "_PLACEHOLDER" && x.EndsWith(".asset"));
        if (p == null) return null;
        var so = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(p)); var pp = so.FindProperty("prefab");
        return pp != null ? pp.objectReferenceValue as GameObject : null;
    }
    public static void Stations()
    {
        var sb = new StringBuilder();
        var c = new Vector3(0, 1000, 0);
        var h = EXTools.Inst(EXTools.FA18, c);
        try
        {
            var st = Find(h, "weaponbayMount_L");
            var skin = WorldVerts(h, t => t.name.StartsWith("FA18C_Skin")).Select(v => st.InverseTransformPoint(v)).ToList();
            sb.AppendLine("station L local pos " + st.localPosition.ToString("F3") + " euler " + st.localEulerAngles.ToString("F1"));
            // skin histogram in the station frame: lateral |x|<0.10, per z bin, u.y values from -0.6..0.4
            for (float z = -2.6f; z <= 2.6f; z += 0.1f)
            {
                var sel = skin.Where(u => Mathf.Abs(u.x) < 0.10f && Mathf.Abs(u.z - z) < 0.05f && u.y > -0.6f && u.y < 0.4f).Select(u => u.y).OrderBy(y => y).ToList();
                var hist = new int[10]; foreach (var y in sel) hist[Mathf.Clamp((int)((y + 0.6f) / 0.1f), 0, 9)]++;
                sb.AppendLine("skin z=" + z.ToString("F2") + " n=" + sel.Count + (sel.Count > 0 ? " min=" + sel[0].ToString("F3") + " p10=" + sel[sel.Count / 10].ToString("F3") : "") + " hist(-0.6..0.4 by .1)=" + string.Join(",", hist));
            }
            foreach (var mn in new[] { "AAM1_single", "AAM2_single", "AAM4_single", "ARM2_single" })
            {
                var pf = MountPrefab(mn); if (pf == null) { sb.AppendLine(mn + " not found"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf); inst.transform.SetParent(st, false); inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
                var mv = WorldVerts(inst, t => true).Select(v => st.InverseTransformPoint(v)).ToList();
                float z0 = mv.Min(u => u.z), z1 = mv.Max(u => u.z);
                sb.AppendLine(mn + " z " + z0.ToString("F3") + ".." + z1.ToString("F3") + " y " + mv.Min(u => u.y).ToString("F3") + ".." + mv.Max(u => u.y).ToString("F3"));
                // body: central slice |x|<0.015 -> top/bottom per z
                for (float z = z0; z <= z1; z += 0.25f)
                {
                    var s = mv.Where(u => Mathf.Abs(u.x) < 0.015f && Mathf.Abs(u.z - z) < 0.06f).ToList();
                    var s2 = mv.Where(u => Mathf.Abs(u.z - z) < 0.06f).ToList();
                    if (s.Count == 0) continue;
                    sb.AppendLine("   " + mn + " z=" + z.ToString("F2") + " centre-slice y " + s.Min(u => u.y).ToString("F3") + ".." + s.Max(u => u.y).ToString("F3") + "  all x " + s2.Min(u => u.x).ToString("F3") + ".." + s2.Max(u => u.x).ToString("F3"));
                }
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(h); }
        File.WriteAllText(D + "ex_stations.txt", sb.ToString());
        Debug.Log("[F14] EX stations done");
    }
}
