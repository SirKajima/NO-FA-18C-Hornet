using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Dev-only (Assets/Editor, never shipped). Adds Multi-Missile (kingwixly) weapons to the FA18C and F14B
// hardpoint sets with Blueprinter OpAddWeaponToHardpoint assets. String-keyed soft dependency:
// without Multi-Missile installed, Blueprinter only logs "Weapon ... not found" and skips.
public static class MMCompat
{
    struct Row { public string ac, key; public int[] hps; public Row(string a, string k, int[] h) { ac = a; key = k; hps = h; } }
    static readonly Row[] Rows = {
        new Row("FA18C", "aim_9x_sidewinder_wingtip", new[] { 1 }),
        new Row("FA18C", "aim_9m_sidewinder_wingtip", new[] { 1 }),
        new Row("FA18C", "aim_9z_wingtip", new[] { 1 }),
        new Row("FA18C", "iris_t_wingtip", new[] { 1 }),
        new Row("FA18C", "Aim_120_wingtip", new[] { 1 }),
        new Row("FA18C", "aim_9x_sidewinder_single", new[] { 2, 3, 4, 5 }),
        new Row("FA18C", "aim_9m_sidewinder_single_exterior", new[] { 2, 3, 4, 5 }),
        new Row("FA18C", "aim_9z_single", new[] { 2, 3, 4, 5 }),
        new Row("FA18C", "irist_single_e", new[] { 2, 3, 4, 5 }),
        new Row("FA18C", "k_30_single", new[] { 2, 3, 4, 5 }),
        new Row("FA18C", "Aim_120_Single", new[] { 2, 3, 4, 5 }),
        new Row("FA18C", "METEOR_Single", new[] { 2, 3, 4, 5 }),
        new Row("FA18C", "r_27er_single", new[] { 2, 3, 4, 5 }),
        new Row("FA18C", "cuda_htk_single_ext", new[] { 2, 3, 4, 5 }),
        new Row("FA18C", "aim_9x_sidewinder_double", new[] { 2, 3, 5 }),
        new Row("FA18C", "aim_9m_sidewinder_double", new[] { 2, 3, 5 }),
        new Row("FA18C", "aim_9z_double", new[] { 2, 3, 5 }),
        new Row("FA18C", "irist_double_e", new[] { 2, 3, 5 }),
        new Row("FA18C", "k_30_double_exterior", new[] { 2, 3, 5 }),
        new Row("FA18C", "Aim_120_Double_Exterior", new[] { 2, 3, 5 }),
        new Row("FA18C", "METEOR_Double", new[] { 2, 3, 5 }),
        new Row("FA18C", "r_27er_double_exterior", new[] { 2, 3, 5 }),
        new Row("FA18C", "cuda_htk_double_exterior", new[] { 2, 3, 5 }),
        new Row("FA18C", "aim_9x_sidewinder_triple", new[] { 2, 3, 5 }),
        new Row("FA18C", "aim_9m_sidewinder_triple", new[] { 2, 3, 5 }),
        new Row("FA18C", "aim_9z_triple", new[] { 2, 3, 5 }),
        new Row("FA18C", "irist_triple", new[] { 2, 3, 5 }),
        new Row("FA18C", "cuda_htk_quad_external", new[] { 2, 3, 5 }),
        new Row("FA18C", "dani_largebomb500_jdam_single", new[] { 2, 3, 5 }),
        new Row("FA18C", "dani_largebomb500_jdam_double", new[] { 2, 3, 5 }),
        new Row("FA18C", "dani_largebomb500_jdam_triple", new[] { 2, 3, 5 }),
        new Row("FA18C", "dani_bigbomb1k_jdam_single", new[] { 2, 3, 5 }),
        new Row("FA18C", "dani_bigbomb1k_jdam_double", new[] { 2, 3, 5 }),
        new Row("FA18C", "dani_hugebomb2k_jdam_single", new[] { 2, 3, 5 }),
        new Row("FA18C", "gbu39_sdb_quad", new[] { 2, 3, 5 }),
        new Row("FA18C", "SDB2_Quad", new[] { 2, 3, 5 }),
        new Row("FA18C", "agm_84h_k_slam_er_single_ext", new[] { 2, 3, 5 }),
        new Row("FA18C", "agm_84h_k_slam_er_double_external", new[] { 2, 3, 5 }),
        new Row("FA18C", "storm_shadow_single", new[] { 2, 3, 5 }),
        new Row("FA18C", "storm_shadow_double_external", new[] { 2, 3, 5 }),
        new Row("FA18C", "brimstone_triple", new[] { 2, 3, 5 }),
        new Row("FA18C", "exocet_am39_block3c_single", new[] { 2, 3, 5 }),
        new Row("FA18C", "atmaca_al_double_external", new[] { 2, 3, 5 }),
        new Row("FA18C", "zircon_3m22_single_external", new[] { 2, 3, 5 }),
        new Row("FA18C", "zircon_3m22_double_external", new[] { 2, 3, 5 }),
        new Row("FA18C", "agm_84h_k_slam_er_centraline", new[] { 5 }),
        new Row("F14B", "aim_9x_sidewinder_single", new[] { 1, 2, 3, 4 }),
        new Row("F14B", "aim_9m_sidewinder_single_exterior", new[] { 1, 2, 3, 4 }),
        new Row("F14B", "aim_9z_single", new[] { 1, 2, 3, 4 }),
        new Row("F14B", "irist_single_e", new[] { 1, 2, 3, 4 }),
        new Row("F14B", "k_30_single", new[] { 1, 2, 3, 4 }),
        new Row("F14B", "Aim_120_Single", new[] { 1, 2, 3, 4 }),
        new Row("F14B", "METEOR_Single", new[] { 1, 2, 3, 4 }),
        new Row("F14B", "r_27er_single", new[] { 1, 2, 3, 4 }),
        new Row("F14B", "cuda_htk_single_ext", new[] { 1, 2, 3, 4 }),
        new Row("F14B", "aim_9x_sidewinder_double", new[] { 2, 3, 4 }),
        new Row("F14B", "aim_9m_sidewinder_double", new[] { 2, 3, 4 }),
        new Row("F14B", "aim_9z_double", new[] { 2, 3, 4 }),
        new Row("F14B", "irist_double_e", new[] { 2, 3, 4 }),
        new Row("F14B", "k_30_double_exterior", new[] { 2, 3, 4 }),
        new Row("F14B", "Aim_120_Double_Exterior", new[] { 2, 3, 4 }),
        new Row("F14B", "METEOR_Double", new[] { 2, 3, 4 }),
        new Row("F14B", "r_27er_double_exterior", new[] { 2, 3, 4 }),
        new Row("F14B", "cuda_htk_double_exterior", new[] { 2, 3, 4 }),
        new Row("F14B", "aim_9x_sidewinder_triple", new[] { 2, 3, 4 }),
        new Row("F14B", "aim_9m_sidewinder_triple", new[] { 2, 3, 4 }),
        new Row("F14B", "aim_9z_triple", new[] { 2, 3, 4 }),
        new Row("F14B", "irist_triple", new[] { 2, 3, 4 }),
        new Row("F14B", "cuda_htk_quad_external", new[] { 2, 3, 4 }),
        new Row("F14B", "dani_largebomb500_jdam_single", new[] { 3, 4 }),
        new Row("F14B", "dani_largebomb500_jdam_double", new[] { 3, 4 }),
        new Row("F14B", "dani_largebomb500_jdam_triple", new[] { 3, 4 }),
        new Row("F14B", "dani_bigbomb1k_jdam_single", new[] { 3, 4 }),
        new Row("F14B", "dani_bigbomb1k_jdam_double", new[] { 3, 4 }),
        new Row("F14B", "dani_hugebomb2k_jdam_single", new[] { 3, 4 }),
        new Row("F14B", "gbu39_sdb_quad", new[] { 3, 4 }),
        new Row("F14B", "SDB2_Quad", new[] { 3, 4 }),
    };
    const string ModsDir = "Assets/Blueprinter/Mods/";
    static void L(string s) { F14Bridge.Log("[MM] " + s); }

    static Type OpType()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] ts; try { ts = a.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { ts = e.Types.Where(x => x != null).ToArray(); } catch { continue; }
            foreach (var t in ts)
                if (t.Name == "OpAddWeaponToHardpoint" && typeof(ScriptableObject).IsAssignableFrom(t) && !t.IsAbstract) return t;
        }
        return null;
    }

    public static void Probe()
    {
        var t = OpType();
        L("op type: " + (t == null ? "NOT FOUND" : t.AssemblyQualifiedName));
        var hang = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ModsDir + "FA18C/OpAddAircraftToHangars.asset");
        L("hangar op type: " + (hang == null ? "null" : hang.GetType().AssemblyQualifiedName));
        if (t == null) return;
        var o = ScriptableObject.CreateInstance(t);
        var so = new SerializedObject(o);
        var it = so.GetIterator(); bool enter = true;
        while (it.Next(enter)) { enter = true; L("  field " + it.propertyPath + " : " + it.propertyType + " " + it.type); }
        UnityEngine.Object.DestroyImmediate(o);
    }

    public static void Create()
    {
        var t = OpType();
        if (t == null) { L("ABORT: op type not found"); return; }
        int made = 0;
        foreach (var ac in Rows.Select(r => r.ac).Distinct())
        {
            // remove any earlier MM_ ops for this mod
            foreach (var g in AssetDatabase.FindAssets("MM_", new[] { ModsDir + ac }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileName(p).StartsWith("MM_")) AssetDatabase.DeleteAsset(p);
            }
            foreach (var r in Rows.Where(x => x.ac == ac))
            {
                var o = ScriptableObject.CreateInstance(t);
                var so = new SerializedObject(o);
                var wk = so.FindProperty("weaponJsonKey");
                var arr = so.FindProperty("aircraft");
                if (wk == null || arr == null) { L("ABORT: fields weaponJsonKey/aircraft missing"); UnityEngine.Object.DestroyImmediate(o); return; }
                wk.stringValue = r.key;
                arr.arraySize = 1;
                var el = arr.GetArrayElementAtIndex(0);
                el.FindPropertyRelative("aircraftJsonKey").stringValue = ac;
                var hp = el.FindPropertyRelative("hardpointIndices");
                hp.arraySize = r.hps.Length;
                for (int i = 0; i < r.hps.Length; i++) hp.GetArrayElementAtIndex(i).intValue = r.hps[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(o, ModsDir + ac + "/MM_" + r.key + ".asset");
                made++;
            }
        }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        L("created " + made + " ops");
    }

    static void Build(string key, string name, string ver)
    {
        File.WriteAllText(ModsDir + key + "/modinfo.json", "{\n    \"displayName\": \"" + name + "\",\n    \"version\": \"" + ver + "\"\n}");
        AssetDatabase.Refresh();
        var bt = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Blueprinter.ModBuilder")).FirstOrDefault(x => x != null);
        var mi = bt.GetMethod("Build", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        L("building " + key + " " + ver);
        mi.Invoke(null, new object[] { key, name, ver, "E:/Project/NOModding/Builds" });
        L("built " + key + " " + ver);
    }
    public static void BuildFA18C() { Build("FA18C", "FA-18C Hornet", "0.6.4"); }
    public static void BuildF14B() { Build("F14B", "F-14B Tomcat", "0.1.9"); }
}
