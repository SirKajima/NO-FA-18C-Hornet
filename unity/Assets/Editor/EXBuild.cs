using System; using System.IO; using System.Linq; using System.Text; using System.Collections.Generic; using UnityEditor; using UnityEngine;
// Dev-only: applies the "extended updates" changes to the F-14B and FA-18C prefabs and builds them into Builds/EXBuilds.
public static class EXBuild
{
    const string D = EXTools.D;
    const string F14 = EXTools.F14, FA18 = EXTools.FA18;
    const string F14Status = "Assets/Blueprinter/Mods/F14B/F14B_StatusDisplay.prefab";
    const string FA18Status = "Assets/Blueprinter/Mods/FA18C/FA18C_StatusDisplay.prefab";
    static readonly StringBuilder log = new StringBuilder();
    static void L(string s) { log.AppendLine(s); Debug.Log("[F14] EX " + s); }
    static void Flush(string f) { File.WriteAllText(D + f, log.ToString()); log.Clear(); }

    static Transform Find(GameObject go, string name) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    static Component Comp(GameObject go, string type) => go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == type);
    static IEnumerable<Component> Comps(GameObject root, string type) => root.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == type);
    static T Load<T>(string nameNoExt) where T : UnityEngine.Object
    {
        foreach (var g in AssetDatabase.FindAssets(nameNoExt))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (Path.GetFileNameWithoutExtension(p) != nameNoExt) continue;
            var o = AssetDatabase.LoadAssetAtPath<T>(p); if (o != null) return o;
        }
        throw new Exception("asset not found: " + nameNoExt);
    }
    static void SetF(Component c, string path, float v) { var so = new SerializedObject(c); var p = so.FindProperty(path); if (p == null) throw new Exception("no prop " + path + " on " + c.GetType().Name); p.floatValue = v; so.ApplyModifiedPropertiesWithoutUndo(); }
    static float GetF(Component c, string path) => new SerializedObject(c).FindProperty(path).floatValue;
    static void SetO(Component c, string path, UnityEngine.Object v) { var so = new SerializedObject(c); var p = so.FindProperty(path); if (p == null) throw new Exception("no prop " + path + " on " + c.GetType().Name); p.objectReferenceValue = v; so.ApplyModifiedPropertiesWithoutUndo(); }
    static UnityEngine.Object GetO(Component c, string path) => new SerializedObject(c).FindProperty(path).objectReferenceValue;
    static void SetS(Component c, string path, string v) { var so = new SerializedObject(c); var p = so.FindProperty(path); p.stringValue = v; so.ApplyModifiedPropertiesWithoutUndo(); }
    static void DeleteAt(Component c, string arrayPath, int idx)
    {
        var so = new SerializedObject(c); var a = so.FindProperty(arrayPath); int n = a.arraySize;
        var e = a.GetArrayElementAtIndex(idx); if (e.propertyType == SerializedPropertyType.ObjectReference) e.objectReferenceValue = null;
        a.DeleteArrayElementAtIndex(idx); if (a.arraySize == n) a.DeleteArrayElementAtIndex(idx);
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void AppendO(Component c, string arrayPath, UnityEngine.Object v)
    {
        var so = new SerializedObject(c); var a = so.FindProperty(arrayPath); a.arraySize++; a.GetArrayElementAtIndex(a.arraySize - 1).objectReferenceValue = v; so.ApplyModifiedPropertiesWithoutUndo();
    }
    static int ArrSize(Component c, string arrayPath) => new SerializedObject(c).FindProperty(arrayPath).arraySize;

    // remove every serialized reference to objects in `dead` from all components under root (array elements are deleted)
    static int RemoveRefs(GameObject root, HashSet<UnityEngine.Object> dead)
    {
        int n = 0;
        foreach (var c in root.GetComponentsInChildren<Component>(true))
        {
            if (c == null || dead.Contains(c) || c is Transform) continue;
            var so = new SerializedObject(c); var it = so.GetIterator();
            var setNull = new List<string>(); var del = new List<KeyValuePair<string, int>>();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null || !dead.Contains(it.objectReferenceValue)) continue;
                string p = it.propertyPath; int k = p.LastIndexOf(".Array.data[", StringComparison.Ordinal);
                if (k < 0) { setNull.Add(p); continue; }
                int e = p.IndexOf(']', k); int idx = int.Parse(p.Substring(k + 12, e - (k + 12)));
                del.Add(new KeyValuePair<string, int>(p.Substring(0, k), idx));
                L("  ref " + c.GetType().Name + "@" + c.name + " " + p);
            }
            foreach (var p in setNull) { so.FindProperty(p).objectReferenceValue = null; n++; L("  null " + c.GetType().Name + "@" + c.name + " " + p); }
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var g in del.Distinct().GroupBy(x => x.Key))
                foreach (var idx in g.Select(x => x.Value).Distinct().OrderByDescending(x => x)) { DeleteAt(c, g.Key, idx); n++; }
        }
        return n;
    }
    static void Dead(GameObject g, HashSet<UnityEngine.Object> set)
    {
        foreach (var t in g.GetComponentsInChildren<Transform>(true)) { set.Add(t.gameObject); foreach (var c in t.GetComponents<Component>()) if (c != null) set.Add(c); }
    }
    static void CullBack(string matName)
    {
        var m = Load<Material>(matName);
        L("material " + matName + " cull " + m.GetFloat("_Cull") + " -> 2");
        m.SetFloat("_Cull", 2f); EditorUtility.SetDirty(m);
    }
    static GameObject MeshGO(string name, Transform parent, Mesh mesh, Material mat, int layer)
    {
        var g = new GameObject(name); g.layer = layer; g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; return g;
    }

    // =====================================================================================  F-14B
    public static void F14B()
    {
        var root = PrefabUtility.LoadPrefabContents(F14);
        try
        {
            // ---------- 1. four game engines -> two (one per F110), same total thrust, fuel flow and IR ----------
            var e1 = Find(root, "engine1").gameObject; var e2 = Find(root, "engine2").gameObject; var e3 = Find(root, "engine3").gameObject; var e4 = Find(root, "engine4").gameObject;
            var n1 = Find(root, "nozzle1").gameObject; var n2 = Find(root, "nozzle2").gameObject; var n3 = Find(root, "nozzle3").gameObject; var n4 = Find(root, "nozzle4").gameObject;
            var t1 = Comp(e1, "Turbofan"); var t2 = Comp(e2, "Turbofan"); var t3 = Comp(e3, "Turbofan");
            var fire2 = GetO(t2, "failureMessageAudio");
            foreach (var t in new[] { t1, t3 })
            {
                SetF(t, "staticThrust", GetF(t, "staticThrust") * 2f);
                SetF(t, "fuelConsumptionMin", GetF(t, "fuelConsumptionMin") * 2f);
                SetF(t, "fuelConsumptionMax", GetF(t, "fuelConsumptionMax") * 2f);
                L("engine " + t.name + " staticThrust " + GetF(t, "staticThrust") + " fuel " + GetF(t, "fuelConsumptionMin") + "/" + GetF(t, "fuelConsumptionMax"));
            }
            SetS(t3, "failureMessage", "ENGINE 2 FIRE"); SetO(t3, "failureMessageAudio", fire2);
            foreach (var n in new[] { n1, n3 })
            {
                var jn = Comp(n, "JetNozzle");
                SetF(jn, "afterburners.Array.data[0].thrust", GetF(jn, "afterburners.Array.data[0].thrust") * 2f);
                SetF(jn, "afterburners.Array.data[0].fuelConsumption", GetF(jn, "afterburners.Array.data[0].fuelConsumption") * 2f);
                SetF(jn, "afterburners.Array.data[0].IRIntensity", GetF(jn, "afterburners.Array.data[0].IRIntensity") * 2f);
                SetF(jn, "IRMin", GetF(jn, "IRMin") * 2f); SetF(jn, "IRMax", GetF(jn, "IRMax") * 2f);
                SetF(jn, "thrustMaxVolume", GetF(jn, "thrustMaxVolume") * 1.414f);
                L("nozzle " + n.name + " AB thrust " + GetF(jn, "afterburners.Array.data[0].thrust") + " AB fuel " + GetF(jn, "afterburners.Array.data[0].fuelConsumption") + " IR " + GetF(jn, "IRMin") + "/" + GetF(jn, "IRMax"));
            }
            var dead = new HashSet<UnityEngine.Object>(); foreach (var g in new[] { e2, e4, n2, n4 }) Dead(g, dead);
            L("removed refs: " + RemoveRefs(root, dead));
            foreach (var g in new[] { e2, e4, n2, n4 }) UnityEngine.Object.DestroyImmediate(g);
            var dw = Comps(root, "Downwash").First();
            for (int i = 0; i < ArrSize(dw, "downwashSources"); i++) SetF(dw, "downwashSources.Array.data[" + i + "].maxThrust", GetF(dw, "downwashSources.Array.data[" + i + "].maxThrust") * 2f);
            L("turbofans now " + Comps(root, "Turbofan").Count() + ", nozzles " + Comps(root, "JetNozzle").Count() + ", downwash sources " + ArrSize(dw, "downwashSources"));

            // ---------- 2. afterburner: FS-20 Vortex flame + nozzle glow, sized to the F110 nozzle and set deeper ----------
            var glowMesh = Load<Mesh>("smallFighter1_afterburner_nozzle_PLACEHOLDER"); var flameMesh = Load<Mesh>("smallFighter1_afterburner_flame_PLACEHOLDER");
            var glowMat = Load<Material>("Fighter1_nozzle_glow_PLACEHOLDER"); var flameMat = Load<Material>("Afterburner_PLACEHOLDER");
            foreach (var pair in new[] { new { n = n1, k = "1", side = "L" }, new { n = n3, k = "3", side = "R" } })
            {
                var nz = pair.n.transform; var jn = Comp(pair.n, "JetNozzle");
                float ax = nz.position.x, ay = 0f;
                var oldGlow = Find(pair.n, "afterburner" + pair.k + "_nozzle_glow"); var oldFlame = Find(pair.n, "afterburner" + pair.k + "_flame");
                foreach (var o in new[] { oldGlow, oldFlame }) { o.GetComponent<MeshRenderer>().enabled = false; o.GetComponent<MeshFilter>().sharedMesh = null; }
                // glow: Vortex glow cone (r 0.52) just aft of the F110 turbine face (z -5.1), inside the duct
                var gh = new GameObject("F14_ABGlowHolder_" + pair.side); gh.layer = 14; gh.transform.SetParent(nz, false); gh.transform.position = new Vector3(ax, ay, -5.55f); gh.transform.rotation = Quaternion.identity;
                var g = MeshGO("F14_ABGlow_" + pair.side, gh.transform, glowMesh, glowMat, 14); g.transform.localScale = Vector3.one; g.transform.localPosition = -glowMesh.bounds.center;
                // flame: Vortex flame, Vortex x/y scale (same 0.52 m nozzle radius), 1.4x longer for the F110; starts 0.6 m inside the nozzle
                var fh = new GameObject("F14_ABFlameHolder_" + pair.side); fh.layer = 14; fh.transform.SetParent(nz, false);
                var fs = new Vector3(1.2f, 1.1f, 1.4f); float zStart = -6.6f; float zLocalMax = flameMesh.bounds.max.z;
                fh.transform.position = new Vector3(ax, ay, zStart - zLocalMax * fs.z); fh.transform.rotation = Quaternion.identity;
                var f = MeshGO("F14_ABFlame_" + pair.side, fh.transform, flameMesh, flameMat, 14); f.transform.localScale = fs; f.transform.localPosition = Vector3.zero;
                SetO(jn, "afterburners.Array.data[0].flameRenderer", f.GetComponent<MeshRenderer>());
                SetO(jn, "afterburners.Array.data[0].nozzleGlowRenderer", g.GetComponent<MeshRenderer>());
                SetF(jn, "afterburners.Array.data[0].flameBrightness", 40f); SetF(jn, "afterburners.Array.data[0].nozzleGlowBrightness", 40f);
                L("AB " + pair.side + " glow z " + (gh.transform.position.z + (glowMesh.bounds.min.z - glowMesh.bounds.center.z)).ToString("F2") + ".." + (gh.transform.position.z + (glowMesh.bounds.max.z - glowMesh.bounds.center.z)).ToString("F2")
                    + " flame z " + (fh.transform.position.z + flameMesh.bounds.min.z * fs.z).ToString("F2") + ".." + (fh.transform.position.z + zLocalMax * fs.z).ToString("F2"));
            }

            // ---------- 3. gear: all legs finish together (no early SetGear), nose small doors re-based so "closed" = 0 ----------
            foreach (var lg in Comps(root, "LandingGear"))
            {
                float fd = GetF(lg, "foldDegrees"), fsp = GetF(lg, "foldSpeed");
                if (Mathf.Abs(Mathf.Abs(fd) - 90f) < 0.5f)
                {
                    SetF(lg, "foldSpeed", 90f * 30f / 134.21f);
                    L("nose gear foldSpeed " + fsp + " -> " + GetF(lg, "foldSpeed") + " (" + (90f / GetF(lg, "foldSpeed")).ToString("F2") + " s, mains " + (134.21f / 30f).ToString("F2") + " s)");
                    var so = new SerializedObject(lg); var doors = so.FindProperty("gearDoors");
                    for (int i = 0; i < doors.arraySize; i++)
                    {
                        var e = doors.GetArrayElementAtIndex(i); var tr = e.FindPropertyRelative("transform").objectReferenceValue as Transform;
                        var cl = e.FindPropertyRelative("closedAngle"); var op = e.FindPropertyRelative("openAngle");
                        if (tr == null || cl.vector3Value == Vector3.zero) continue;
                        Vector3 c = cl.vector3Value, o = op.vector3Value;
                        tr.parent.localRotation = tr.parent.localRotation * Quaternion.Euler(c);
                        tr.localEulerAngles = Vector3.zero;
                        op.vector3Value = o - c; cl.vector3Value = Vector3.zero;
                        L("door " + tr.name + " closed " + c + " open " + o + " -> closed 0, open " + (o - c) + " (parent re-based)");
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // ---------- 4. gun: bullets leave from the M61 port; the plugin moves the flash/smoke to the port ----------
            var hp = Find(root, "F14_cannon"); var hp0 = hp.position;
            hp.position = new Vector3(-0.62f, -0.21f - 0.038f, 9.55f - 3.0f);
            L("cannon hardpoint " + hp0.ToString("F3") + " -> " + hp.position.ToString("F3") + " (bullets at " + (hp.position + new Vector3(0, 0.038f, 3f)).ToString("F3") + ")");

            PrefabUtility.SaveAsPrefabAsset(root, F14);
            L("F14 prefab saved");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        // ---------- status display: engine 2 lamp moves to the right nacelle, lamps 3/4 removed ----------
        var sd = PrefabUtility.LoadPrefabContents(F14Status);
        try
        {
            var s = Comps(sd, "StatusDisplay").First();
            var so = new SerializedObject(s); var fi = so.FindProperty("failureIndicators");
            var names = new List<string>(); for (int i = 0; i < fi.arraySize; i++) { var o = fi.GetArrayElementAtIndex(i).objectReferenceValue; names.Add(o ? o.name : "null"); }
            L("F14 status indicators: " + string.Join(" | ", names));
            var i2 = Find(sd, "ENGINE 2 FIRE"); var i3 = Find(sd, "ENGINE 3 FIRE"); var i4 = Find(sd, "ENGINE 4 FIRE");
            if (i2 && i3)
            {
                var r2 = i2 as RectTransform; var r3 = i3 as RectTransform;
                if (r2 && r3) { r2.anchoredPosition = r3.anchoredPosition; r2.sizeDelta = r3.sizeDelta; } else i2.localPosition = i3.localPosition;
                var dead = new HashSet<UnityEngine.Object>(); Dead(i3.gameObject, dead); if (i4) Dead(i4.gameObject, dead);
                RemoveRefs(sd, dead); UnityEngine.Object.DestroyImmediate(i3.gameObject); if (i4) UnityEngine.Object.DestroyImmediate(i4.gameObject);
                L("ENGINE 2 FIRE lamp moved to the right nacelle; lamps 3/4 removed");
                PrefabUtility.SaveAsPrefabAsset(sd, F14Status);
            }
            else L("engine lamps not found by name - status display unchanged");
        }
        finally { PrefabUtility.UnloadPrefabContents(sd); }

        // ---------- canopy glass: clear from the inside (back faces culled), unchanged from outside ----------
        CullBack("F14B_a79b8aca"); CullBack("F14B_7a37476d");
        AssetDatabase.SaveAssets();
        Flush("ex_build_f14.txt");
    }

    // =====================================================================================  FA-18C
    public static void FA18C()
    {
        var root = PrefabUtility.LoadPrefabContents(FA18);
        try
        {
            // ---------- 1. one game engine -> two (F404 left/right), same total thrust and fuel flow ----------
            var T0 = Comp(root, "Turbojet");
            var nozzle = root.transform.Find("tail/nozzle"); var N0 = Comp(nozzle.gameObject, "JetNozzle");
            var aud0 = GetO(T0, "turbineAudio") as AudioSource;
            var eR = new GameObject("FA18C_Engine_R"); eR.layer = 14; eR.transform.SetParent(root.transform, false); eR.transform.localPosition = new Vector3(0.44f, -0.336f, -3f);
            var T1 = eR.AddComponent(T0.GetType()); EditorUtility.CopySerialized(T0, T1);
            var aud1 = eR.AddComponent<AudioSource>(); EditorUtility.CopySerialized(aud0, aud1); SetO(T1, "turbineAudio", aud1);
            foreach (var t in new[] { T0, T1 })
            {
                SetF(t, "maxThrust", GetF(t, "maxThrust") * 0.5f);
                SetF(t, "fuelConsumptionMin", GetF(t, "fuelConsumptionMin") * 0.5f);
                SetF(t, "fuelConsumptionMax", GetF(t, "fuelConsumptionMax") * 0.5f);
            }
            // critical parts: [intake_L, intake_R, root, tail, fuselage] -> each engine keeps its own intake
            DeleteAt(T0, "criticalParts", 1); DeleteAt(T1, "criticalParts", 0);
            while (ArrSize(T1, "vectoringTransforms") > 0) DeleteAt(T1, "vectoringTransforms", 0);
            var c1 = Load<AudioClip>("engine1Fire_PLACEHOLDER"); var c2 = Load<AudioClip>("engine2Fire_PLACEHOLDER");
            SetS(T0, "failureMessage", "ENGINE 1 FIRE"); SetO(T0, "failureMessageAudio", c1);
            SetS(T1, "failureMessage", "ENGINE 2 FIRE"); SetO(T1, "failureMessageAudio", c2);
            // right nozzle
            var nR = new GameObject("FA18C_Nozzle_R"); nR.layer = nozzle.gameObject.layer; nR.transform.SetParent(nozzle.parent, false); nR.transform.localPosition = nozzle.localPosition; nR.transform.localRotation = nozzle.localRotation;
            var N1 = nR.AddComponent(N0.GetType()); EditorUtility.CopySerialized(N0, N1);
            var thrAud0 = GetO(N0, "thrustAudio") as AudioSource; var abAud0 = GetO(N0, "afterburners.Array.data[0].source") as AudioSource;
            var thrAud1 = nR.AddComponent<AudioSource>(); EditorUtility.CopySerialized(thrAud0, thrAud1); SetO(N1, "thrustAudio", thrAud1);
            AudioSource abAud1 = thrAud1;
            if (abAud0 != null && abAud0 != thrAud0) { abAud1 = nR.AddComponent<AudioSource>(); EditorUtility.CopySerialized(abAud0, abAud1); }
            DeleteAt(N0, "afterburners", 1); DeleteAt(N1, "afterburners", 0);
            SetO(N1, "afterburners.Array.data[0].source", abAud1);
            SetO(N1, "turbojet", T1); SetO(N0, "turbojet", T0);
            while (ArrSize(N1, "vectorTransforms") > 0) DeleteAt(N1, "vectorTransforms", 0);
            while (ArrSize(T1, "nozzles") > 0) DeleteAt(T1, "nozzles", 0); AppendO(T1, "nozzles", N1);
            var tt0 = (GetO(N0, "thrustTransform") as Transform);
            var ttL = new GameObject("thrustTransform_L").transform; ttL.SetParent(nozzle, false); ttL.localPosition = tt0.localPosition + new Vector3(-0.44f, 0, 0); ttL.localRotation = tt0.localRotation;
            var ttR = new GameObject("thrustTransform_R").transform; ttR.SetParent(nR.transform, false); ttR.localPosition = tt0.localPosition + new Vector3(0.44f, 0, 0); ttR.localRotation = tt0.localRotation;
            SetO(N0, "thrustTransform", ttL); SetO(N1, "thrustTransform", ttR);
            var haze0 = GetO(N0, "heatHaze.system") as ParticleSystem;
            if (haze0 != null)
            {
                var hz1 = UnityEngine.Object.Instantiate(haze0.gameObject, haze0.transform.parent); hz1.name = haze0.name + "_R";
                hz1.transform.localPosition = haze0.transform.localPosition + new Vector3(0.44f, 0, 0); haze0.transform.localPosition += new Vector3(-0.44f, 0, 0);
                SetO(N1, "heatHaze.system", hz1.GetComponent<ParticleSystem>());
            }
            foreach (var n in new[] { N0, N1 })
            {
                SetF(n, "IRMin", GetF(n, "IRMin") * 0.5f); SetF(n, "IRMax", GetF(n, "IRMax") * 0.5f);
                SetF(n, "thrustMaxVolume", GetF(n, "thrustMaxVolume") * 0.707f);
            }
            var ac = Comp(root, "Aircraft");
            foreach (var a in new[] { aud1, thrAud1, abAud1 }.Distinct()) AppendO(ac, "dopplerSounds", a);
            var ps = Comps(root, "PowerSupply").FirstOrDefault(); if (ps) AppendO(ps, "powerSources", eR);
            var ck = Comps(root, "Cockpit").FirstOrDefault(); if (ck) AppendO(ck, "engineSources", eR);
            var dw = Comps(root, "Downwash").FirstOrDefault();
            if (dw)
            {
                var so = new SerializedObject(dw); var a = so.FindProperty("downwashSources"); a.InsertArrayElementAtIndex(0); so.ApplyModifiedPropertiesWithoutUndo();
                SetO(dw, "downwashSources.Array.data[1].thrustSourceObject", eR); SetO(dw, "downwashSources.Array.data[1].castTransform", nR.transform);
            }
            L("FA18 engines: turbojets " + Comps(root, "Turbojet").Count() + " (" + string.Join(", ", Comps(root, "Turbojet").Select(t => t.name + " " + GetF(t, "maxThrust") + " N, fuel " + GetF(t, "fuelConsumptionMin") + "/" + GetF(t, "fuelConsumptionMax") + ", nozzles " + ArrSize(t, "nozzles") + ", crit " + ArrSize(t, "criticalParts"))) + ")");
            L("FA18 nozzles: " + string.Join(", ", Comps(root, "JetNozzle").Select(n => n.name + " AB " + ArrSize(n, "afterburners") + " thrust " + GetF(n, "afterburners.Array.data[0].thrust") + " tt " + ((Transform)GetO(n, "thrustTransform")).position.ToString("F2"))));

            // ---------- 2. afterburner deeper: glow at the turbine face, flame starts inside the nozzle ----------
            foreach (var gn in new[] { "afterburner_nozzle_glow", "afterburner_nozzle_glow_R" })
            {
                var g = Find(root, gn); var p0 = g.position; var mb = g.GetComponent<MeshFilter>().sharedMesh.bounds;
                g.localScale = Vector3.one * 0.86f; float s = g.lossyScale.z;
                g.position = new Vector3(p0.x, p0.y, -6.33f - mb.max.z * s);
                L("glow " + gn + " " + p0.ToString("F3") + " -> " + g.position.ToString("F3") + " span z " + (g.position.z + mb.min.z * s).ToString("F2") + ".." + (g.position.z + mb.max.z * s).ToString("F2"));
            }
            foreach (var fn in new[] { "FA18C_FlameScale_L", "FA18C_FlameScale_R" })
            {
                var f = Find(root, fn); var p0 = f.position; var child = f.GetChild(0); var mb = child.GetComponent<MeshFilter>().sharedMesh.bounds;
                var ls = f.localScale; ls.z = 0.77f; f.localScale = ls; float sz = child.lossyScale.z;
                f.position = new Vector3(p0.x, p0.y, -7.45f - mb.max.z * sz);
                L("flame " + fn + " " + p0.ToString("F3") + " -> " + f.position.ToString("F3") + " span z " + (f.position.z + mb.min.z * sz).ToString("F2") + ".." + (f.position.z + mb.max.z * sz).ToString("F2"));
            }

            // ---------- 3. fuselage stations 0.30 m aft (missile noses behind the intake lips), 2 cm lower (no clipping) ----------
            foreach (var sn in new[] { "weaponbayMount_L", "weaponbayMount_R" })
            {
                var st = Find(root, sn); var p0 = st.localPosition;
                st.localPosition = p0 - st.up * 0.02f + new Vector3(0, 0, -0.30f);
                L("station " + sn + " " + p0.ToString("F3") + " -> " + st.localPosition.ToString("F3"));
            }

            // ---------- 4. gear: nose leg finishes with the mains ----------
            foreach (var lg in Comps(root, "LandingGear"))
            {
                float fd = GetF(lg, "foldDegrees");
                if (Mathf.Abs(Mathf.Abs(fd) - 90f) < 0.5f) { float o = GetF(lg, "foldSpeed"); SetF(lg, "foldSpeed", 90f * 30f / 94.26f); L("nose foldSpeed " + o + " -> " + GetF(lg, "foldSpeed")); }
            }
            PrefabUtility.SaveAsPrefabAsset(root, FA18);
            L("FA18 prefab saved");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        // ---------- status display: separate engine 1 / engine 2 fire lamps ----------
        var sd = PrefabUtility.LoadPrefabContents(FA18Status);
        try
        {
            var s = Comps(sd, "StatusDisplay").First();
            var so = new SerializedObject(s); var fi = so.FindProperty("failureIndicators");
            var names = new List<string>(); for (int i = 0; i < fi.arraySize; i++) { var o = fi.GetArrayElementAtIndex(i).objectReferenceValue; names.Add(o ? o.name : "null"); }
            L("FA18 status indicators: " + string.Join(" | ", names));
            var ef = Find(sd, "ENGINE FIRE");
            if (ef && !Find(sd, "ENGINE 1 FIRE"))
            {
                var e2 = UnityEngine.Object.Instantiate(ef.gameObject, ef.parent); e2.name = "ENGINE 2 FIRE"; ef.name = "ENGINE 1 FIRE";
                var r1 = ef as RectTransform; var r2 = e2.transform as RectTransform;
                if (r1 && r2) { var w = Mathf.Max(4f, r1.rect.width * 0.35f); r1.anchoredPosition += new Vector2(-w, 0); r2.anchoredPosition = r1.anchoredPosition + new Vector2(2f * w, 0); }
                AppendO(s, "failureIndicators", e2);
                L("ENGINE FIRE lamp split into ENGINE 1 FIRE / ENGINE 2 FIRE");
                PrefabUtility.SaveAsPrefabAsset(sd, FA18Status);
            }
            else L("no single ENGINE FIRE lamp - status display unchanged");
        }
        finally { PrefabUtility.UnloadPrefabContents(sd); }

        CullBack("FA18C_58ae374e");
        AssetDatabase.SaveAssets();
        Flush("ex_build_fa18.txt");
    }


    // engine count changed: keep the electrical charge rate (charge += chargePerRPM * rpm per engine)
    public static void PowerFix()
    {
        foreach (var pair in new[] { new { p = F14, k = 2f }, new { p = FA18, k = 0.5f } })
        {
            var root = PrefabUtility.LoadPrefabContents(pair.p);
            try
            {
                var ps = Comps(root, "PowerSupply").First();
                float o = GetF(ps, "chargePerRPM"); SetF(ps, "chargePerRPM", o * pair.k);
                L(Path.GetFileName(pair.p) + " chargePerRPM " + o + " -> " + GetF(ps, "chargePerRPM") + " (power sources " + ArrSize(ps, "powerSources") + ")");
                PrefabUtility.SaveAsPrefabAsset(root, pair.p);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Flush("ex_powerfix.txt");
    }

    // =====================================================================================  verification renders
    public static void Verify()
    {
        var c = new Vector3(0, 1000, 0);
        var f = EXTools.Inst(F14, c);
        try
        {
            foreach (var r in f.GetComponentsInChildren<MeshRenderer>(true)) if (r.name.StartsWith("F14_ABFlame") || r.name.StartsWith("F14_ABGlow")) r.enabled = true;
            EXTools.Shot(new Vector3(0.25f, 0.3f, 1f).normalized, Vector3.up, 2.2f, "v_f14_ab_rear_iso.jpg", c + new Vector3(0, 0, -7.5f));
            EXTools.Shot(Vector3.forward, Vector3.up, 1.4f, "v_f14_ab_rear.jpg", c + new Vector3(0, 0, -9f));
            EXTools.Shot(Vector3.left, Vector3.up, 2.5f, "v_f14_ab_side.jpg", c + new Vector3(-1.4f, 0, -8.5f));
            // gun
            var hp = f.GetComponentsInChildren<Transform>(true).First(t => t.name == "F14_cannon");
            EXTools.Marker(f.transform, hp.TransformPoint(new Vector3(0, 0.038f, 2.85f)), new Color(1, 0.5f, 0), 0.07f);
            EXTools.Marker(f.transform, hp.TransformPoint(new Vector3(0, 0.038f, 3.0f)), Color.red, 0.05f);
            EXTools.Shot(new Vector3(0.6f, 0.1f, -0.8f).normalized, Vector3.up, 0.9f, "v_f14_gun_iso.jpg", c + new Vector3(-0.6f, -0.2f, 9.2f));
            EXTools.Shot(Vector3.right, Vector3.up, 1.2f, "v_f14_gun_side.jpg", c + new Vector3(0, -0.2f, 9.0f));
            // gear doors: up / half / down
            EXTools.Gear(f, 1f); EXTools.Shot(new Vector3(0.5f, 0.6f, -0.6f).normalized, Vector3.up, 1.6f, "v_f14_nose_up.jpg", c + new Vector3(0, -0.6f, 6.0f));
            EXTools.Gear(f, 0f); EXTools.Shot(new Vector3(0.5f, 0.6f, -0.6f).normalized, Vector3.up, 1.6f, "v_f14_nose_down.jpg", c + new Vector3(0, -0.8f, 6.0f));
            var eye = f.GetComponentsInChildren<Transform>(true).First(t => t.name == "helmetCamPoint");
            EXTools.Shot(Vector3.forward, Vector3.up, 0, "v_f14_eye.jpg", eye.position, true, 75f);
            EXTools.Shot(new Vector3(0.5f, -0.35f, -0.8f).normalized, Vector3.up, 1.3f, "v_f14_canopy_ext.jpg", c + new Vector3(0, 1.2f, 5.6f));
        }
        finally { UnityEngine.Object.DestroyImmediate(f); }
        var h = EXTools.Inst(FA18, c);
        try
        {
            EXTools.Shot(new Vector3(0.25f, 0.3f, 1f).normalized, Vector3.up, 1.2f, "v_fa18_ab_rear_iso.jpg", c + new Vector3(0, -0.34f, -8.2f));
            EXTools.Shot(Vector3.forward, Vector3.up, 0.9f, "v_fa18_ab_rear.jpg", c + new Vector3(0, -0.34f, -9.5f));
            EXTools.Shot(Vector3.left, Vector3.up, 1.8f, "v_fa18_ab_side.jpg", c + new Vector3(-0.44f, -0.34f, -9.5f));
            foreach (var mn in new[] { "AAM2_single", "AAM1_single" })
            {
                var st = h.GetComponentsInChildren<Transform>(true).First(t => t.name == "weaponbayMount_L");
                var pp = AssetDatabase.FindAssets(mn).Select(AssetDatabase.GUIDToAssetPath).First(x => Path.GetFileNameWithoutExtension(x) == mn + "_PLACEHOLDER" && x.EndsWith(".asset"));
                var pf = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(pp)).FindProperty("prefab").objectReferenceValue as GameObject;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf); inst.transform.SetParent(st, false); inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
                EXTools.Shot(Vector3.right, Vector3.up, 2.0f, "v_fa18_station_" + mn + ".jpg", c + new Vector3(0, -0.6f, -1.2f));
                EXTools.Shot(new Vector3(0.55f, 0.35f, -0.75f).normalized, Vector3.up, 1.6f, "v_fa18_station_iso_" + mn + ".jpg", c + new Vector3(-1.0f, -0.8f, -0.6f));
                EXTools.Shot(Vector3.forward, Vector3.up, 1.2f, "v_fa18_station_front_" + mn + ".jpg", c + new Vector3(-0.9f, -0.8f, 3f));
                UnityEngine.Object.DestroyImmediate(inst);
            }
            var eye = h.GetComponentsInChildren<Transform>(true).First(t => t.name == "helmetCamPoint");
            EXTools.Shot(Vector3.forward, Vector3.up, 0, "v_fa18_eye.jpg", eye.position, true, 75f);
            EXTools.Shot(new Vector3(0.5f, -0.35f, -0.8f).normalized, Vector3.up, 1.0f, "v_fa18_canopy_ext.jpg", c + new Vector3(0, 0.8f, 3.6f));
        }
        finally { UnityEngine.Object.DestroyImmediate(h); }
        Debug.Log("[F14] EX verify done");
    }

    // =====================================================================================  build
    static void Build(string key, string name, string ver)
    {
        const string mods = "Assets/Blueprinter/Mods/";
        File.WriteAllText(mods + key + "/modinfo.json", "{\n    \"displayName\": \"" + name + "\",\n    \"version\": \"" + ver + "\"\n}");
        AssetDatabase.Refresh();
        Directory.CreateDirectory("E:/Project/NOModding/Builds/EXBuilds");
        var bt = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Blueprinter.ModBuilder")).FirstOrDefault(x => x != null);
        var mi = bt.GetMethod("Build", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Debug.Log("[F14] EX building " + key + " " + ver);
        mi.Invoke(null, new object[] { key, name, ver, "E:/Project/NOModding/Builds/EXBuilds" });
        Debug.Log("[F14] EX built " + key + " " + ver);
    }
    public static void BuildF14B() { Build("F14B", "F-14B Tomcat", "0.2.0"); }
    public static void BuildFA18C() { Build("FA18C", "FA-18C Hornet", "0.7.0"); }
}
