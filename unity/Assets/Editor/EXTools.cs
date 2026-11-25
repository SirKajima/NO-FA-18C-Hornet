using System; using System.IO; using System.Linq; using System.Text; using System.Collections.Generic; using UnityEditor; using UnityEngine;
// Dev-only helpers for the "extended updates" builds (never shipped).
public static class EXTools
{
    public const string D = "E:/Project/NOModding/F14Bridge/";
    public const string F14 = "Assets/Blueprinter/Mods/F14B/F14B_FlyingPrefab.prefab";
    public const string FA18 = "Assets/Blueprinter/Mods/FA18C/FA18C_FlyingPrefab.prefab";

    // all AircraftDefinition assets: name, unitName, prefab
    public static void Defs()
    {
        var sb = new StringBuilder();
        foreach (var g in AssetDatabase.FindAssets("t:ScriptableObject"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var o = AssetDatabase.LoadMainAssetAtPath(p);
            if (o == null || o.GetType().Name != "AircraftDefinition") continue;
            var so = new SerializedObject(o);
            var un = so.FindProperty("unitName"); var pf = so.FindProperty("unitPrefab");
            sb.AppendLine(p + " | " + (un != null ? un.stringValue : "?") + " | " + (pf != null && pf.objectReferenceValue ? pf.objectReferenceValue.name : "?"));
        }
        File.WriteAllText(D + "ex_defs.txt", sb.ToString());
        Debug.Log("[F14] EX defs done");
    }

    // ---------- renders ----------
    public static void Shot(Vector3 dir, Vector3 up, float size, string file, Vector3 center, bool persp = false, float fov = 40f, int w = 1000, int h = 700)
    {
        var camGo = new GameObject("EXShotCam"); var cam = camGo.AddComponent<Camera>();
        cam.orthographic = !persp; cam.orthographicSize = size; cam.fieldOfView = fov; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.55f, 0.65f);
        cam.transform.position = persp ? center : center - dir * 50f; cam.transform.rotation = Quaternion.LookRotation(dir, up);
        cam.nearClipPlane = persp ? 0.02f : 0.1f; cam.farClipPlane = 300f;
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; cam.aspect = (float)w / h;
        var lightGo = new GameObject("EXShotLight"); var li = lightGo.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 1.2f; lightGo.transform.rotation = Quaternion.LookRotation(dir + Vector3.down * 0.6f + Vector3.right * 0.3f);
        cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        Directory.CreateDirectory(D + "ex"); File.WriteAllBytes(D + "ex/" + file, tex.EncodeToJPG(88));
        RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(lightGo); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
    }
    public static GameObject Marker(Transform parent, Vector3 worldPos, Color c, float size = 0.06f)
    {
        var m = GameObject.CreatePrimitive(PrimitiveType.Sphere); UnityEngine.Object.DestroyImmediate(m.GetComponent<Collider>());
        m.transform.SetParent(parent, true); m.transform.position = worldPos; m.transform.localScale = Vector3.one * size;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")); mat.color = c; mat.SetColor("_BaseColor", c);
        m.GetComponent<MeshRenderer>().sharedMaterial = mat; return m;
    }
    public static GameObject Inst(string path, Vector3 at)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.position = at; return go;
    }
    public static void Gear(GameObject go, float fold)
    {
        foreach (var lg in go.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "LandingGear"))
        {
            var so = new SerializedObject(lg);
            var hinge = so.FindProperty("gearHinge").objectReferenceValue as Transform;
            float fd = so.FindProperty("foldDegrees").floatValue; var fm = so.FindProperty("hingeFoldMotion").vector3Value;
            if (hinge) { hinge.localEulerAngles = new Vector3(fd * fold, 0, 0); hinge.localPosition = fm * fold; }
            var d = so.FindProperty("gearDoors");
            for (int i = 0; i < d.arraySize; i++) { var e = d.GetArrayElementAtIndex(i); var t = e.FindPropertyRelative("transform").objectReferenceValue as Transform; if (t) t.localEulerAngles = Vector3.Lerp(e.FindPropertyRelative("openAngle").vector3Value, e.FindPropertyRelative("closedAngle").vector3Value, fold); }
        }
    }
    static Transform Find(GameObject go, string name) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    // Diagnostic renders for the extended update
    public static void Diag()
    {
        var c = new Vector3(0, 1000, 0);
        var f = Inst(F14, c);
        try
        {
            Gear(f, 0f);
            Shot(Vector3.right, Vector3.up, 1.6f, "f14_nose_down_side.jpg", c + new Vector3(0, -0.6f, 6.0f));
            Shot(new Vector3(0.3f, 0.9f, -0.3f).normalized, Vector3.forward, 1.6f, "f14_nose_down_below.jpg", c + new Vector3(0, -0.6f, 6.0f));
            Gear(f, 1f);
            Shot(Vector3.right, Vector3.up, 1.6f, "f14_nose_up_side.jpg", c + new Vector3(0, -0.4f, 6.0f));
            Shot(new Vector3(0.3f, 0.9f, -0.3f).normalized, Vector3.forward, 1.6f, "f14_nose_up_below.jpg", c + new Vector3(0, -0.4f, 6.0f));
            // gun: current hardpoint + mount effect/bullet points, and the gunport from the model
            var hp = Find(f, "F14_cannon");
            Marker(f.transform, hp.position, Color.yellow, 0.08f);
            Marker(f.transform, hp.position + hp.forward * 0.764f, new Color(1, 0.5f, 0), 0.08f);
            Marker(f.transform, hp.position + hp.forward * 3f, Color.red, 0.08f);
            Marker(f.transform, c + new Vector3(-0.638f, -0.225f, 9.39f), Color.green, 0.08f);
            Shot(Vector3.right, Vector3.up, 1.6f, "f14_gun_side.jpg", c + new Vector3(0, 0.0f, 8.4f));
            Shot(Vector3.up * -1f, Vector3.forward, 1.6f, "f14_gun_top.jpg", c + new Vector3(0, 0.0f, 8.4f));
            Shot(new Vector3(0.6f, 0.1f, -0.8f).normalized, Vector3.up, 1.0f, "f14_gun_iso.jpg", c + new Vector3(-0.6f, -0.2f, 9.0f));
            Shot(Vector3.forward, Vector3.up, 2.4f, "f14_rear.jpg", c + new Vector3(0, 0.2f, -6f));
            Shot(Vector3.left, Vector3.up, 1.5f, "f14_nozzle_side.jpg", c + new Vector3(-1.4f, 0.0f, -6.8f));
        }
        finally { UnityEngine.Object.DestroyImmediate(f); }
        var h = Inst(FA18, c);
        try
        {
            var hp = Find(h, "cannon");
            Marker(h.transform, hp.position, Color.yellow, 0.06f);
            Marker(h.transform, hp.position + hp.forward * 0.764f + hp.up * 0.038f, new Color(1, 0.5f, 0), 0.06f);
            Marker(h.transform, hp.position + hp.forward * 3f + hp.up * 0.038f, Color.red, 0.06f);
            Shot(Vector3.right, Vector3.up, 1.4f, "fa18_gun_side.jpg", c + new Vector3(0, 0.2f, 7.6f));
            Shot(Vector3.down, Vector3.forward, 1.4f, "fa18_gun_top.jpg", c + new Vector3(0, 0.2f, 7.6f));
            Shot(new Vector3(0.3f, -0.5f, -0.8f).normalized, Vector3.up, 0.9f, "fa18_gun_iso.jpg", c + new Vector3(0, 0.3f, 7.6f));
            Shot(Vector3.forward, Vector3.up, 1.6f, "fa18_rear.jpg", c + new Vector3(0, -0.2f, -7f));
            Shot(Vector3.left, Vector3.up, 1.2f, "fa18_nozzle_side.jpg", c + new Vector3(-0.44f, -0.3f, -7.6f));
            Shot(Vector3.right, Vector3.up, 2.6f, "fa18_fuselage_side.jpg", c + new Vector3(0, -0.6f, 1.5f));
            Shot(new Vector3(0.6f, 0.5f, 0.6f).normalized, Vector3.up, 2.2f, "fa18_fuselage_iso.jpg", c + new Vector3(-1.0f, -0.8f, 1.5f));
        }
        finally { UnityEngine.Object.DestroyImmediate(h); }
        Debug.Log("[F14] EX diag done");
    }

    // ---------- round 2 ----------
    static string P(Transform t, Transform root) => F14Tools.Path(t, root);
    static void MatReport(GameObject go, StringBuilder sb)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                bool tr = m.renderQueue >= 2450 || (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f);
                if (!tr) continue;
                sb.AppendLine(P(r.transform, go.transform) + " layer=" + r.gameObject.layer + " en=" + r.enabled + " mat=" + m.name + " sh=" + m.shader.name + " q=" + m.renderQueue
                    + (m.HasProperty("_Surface") ? " surf=" + m.GetFloat("_Surface") : "") + (m.HasProperty("_BaseColor") ? " base=" + m.GetColor("_BaseColor") : "")
                    + (m.HasProperty("_Smoothness") ? " sm=" + m.GetFloat("_Smoothness") : "") + (m.HasProperty("_Cull") ? " cull=" + m.GetFloat("_Cull") : "")
                    + " kw=" + string.Join(",", m.shaderKeywords) + " path=" + AssetDatabase.GetAssetPath(m));
            }
    }
    static void GlassMode(GameObject go, int mode)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var ms = r.sharedMaterials; bool ch = false;
            for (int i = 0; i < ms.Length; i++)
            {
                var m = ms[i]; if (m == null || m.renderQueue < 2900 || !m.HasProperty("_Surface") || m.GetFloat("_Surface") < 0.5f) continue;
                if (m.shader.name.IndexOf("Lit", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var c = new Material(m);
                if (mode >= 1) c.SetFloat("_Cull", 2f);
                if (mode >= 2) { var col = c.GetColor("_BaseColor"); col.a = 0.10f; c.SetColor("_BaseColor", col); c.SetFloat("_Smoothness", 0.8f); c.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); c.SetFloat("_EnvironmentReflections", 0f); }
                ms[i] = c; ch = true;
            }
            if (ch) r.sharedMaterials = ms;
        }
    }
    static void AddCols(GameObject go, Func<Transform, bool> pick)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || !pick(mf.transform)) continue;
            var r = mf.GetComponent<Renderer>(); if (r == null || !r.enabled) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mf.gameObject.layer = 31;
        }
        Physics.SyncTransforms();
    }
    static GameObject Mount(string mountName, Transform at)
    {
        var g = AssetDatabase.FindAssets(mountName).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == mountName + "_PLACEHOLDER" || Path.GetFileNameWithoutExtension(p) == mountName);
        if (g == null) return null;
        var o = AssetDatabase.LoadMainAssetAtPath(g);
        var pf = o as GameObject;
        if (pf == null) { var so = new SerializedObject(o); var pp = so.FindProperty("prefab"); pf = pp != null ? pp.objectReferenceValue as GameObject : null; }
        if (pf == null) return null;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf); inst.transform.SetParent(at, false); inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; return inst;
    }
    static Bounds LocalBounds(GameObject g, Transform frame)
    {
        bool first = true; var b = new Bounds();
        foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = frame.InverseTransformPoint(mf.transform.TransformPoint(c));
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        }
        return b;
    }
    public static void Diag2()
    {
        var sb = new StringBuilder();
        var c = new Vector3(0, 1000, 0);
        // ---- Hornet ----
        var h = Inst(FA18, c);
        try
        {
            sb.AppendLine("== FA18 transparent mats"); MatReport(h, sb);
            var eye = Find(h, "helmetCamPoint");
            sb.AppendLine("FA18 eye " + (eye.position - c).ToString("F3"));
            Shot(Vector3.forward, Vector3.up, 0, "fa18_eye_0.jpg", eye.position, true, 75f);
            GlassMode(h, 1); Shot(Vector3.forward, Vector3.up, 0, "fa18_eye_1.jpg", eye.position, true, 75f);
            GlassMode(h, 2); Shot(Vector3.forward, Vector3.up, 0, "fa18_eye_2.jpg", eye.position, true, 75f);
            Shot(new Vector3(0.5f, -0.35f, -0.8f).normalized, Vector3.up, 1.0f, "fa18_canopy_ext2.jpg", c + new Vector3(0, 0.8f, 3.6f));
            // intake lip & station geometry
            foreach (var r in h.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("FA18C_Skin_intake")) sb.AppendLine(r.name + " bounds(local) min=" + (r.bounds.min - c).ToString("F3") + " max=" + (r.bounds.max - c).ToString("F3"));
            AddCols(h, t => t.name.StartsWith("FA18C_Skin"));
            var st = Find(h, "weaponbayMount_L");
            sb.AppendLine("station L pos=" + (st.position - c).ToString("F3") + " up=" + st.up.ToString("F3"));
            for (float z = -5.0f; z <= 3.01f; z += 0.25f)
            {
                var o = c + new Vector3(st.position.x - c.x, st.position.y - c.y, z) - st.up * 1.0f;
                var hit = Physics.RaycastAll(o, st.up, 2.5f, 1 << 31).OrderBy(x => x.distance).FirstOrDefault();
                sb.AppendLine("  stationLine z=" + z.ToString("F2") + " surf=" + (hit.collider ? (hit.distance - 1.0f).ToString("F3") + " (" + hit.collider.name + ")" : "none"));
            }
            for (float y = -0.9f; y <= -0.2f; y += 0.1f)
            {
                var hit = Physics.RaycastAll(c + new Vector3(-1.0f, y, 9f), Vector3.back, 12f, 1 << 31).OrderBy(x => x.distance).FirstOrDefault();
                sb.AppendLine("  fwdface x=-1.0 y=" + y.ToString("F2") + " z=" + (hit.collider ? (9f - hit.distance).ToString("F3") + " " + hit.collider.name : "none"));
            }
            // nose top surface along centreline for the gun
            for (float z = 5.8f; z <= 8.6f; z += 0.1f)
            {
                var hit = Physics.RaycastAll(c + new Vector3(0, 2f, z), Vector3.down, 4f, 1 << 31).OrderBy(x => x.distance).FirstOrDefault();
                if (hit.collider) { var col = Color.Lerp(Color.red, Color.blue, (z - 5.8f) / 2.8f); Marker(h.transform, hit.point, col, 0.035f); }
                sb.AppendLine("  noseTop z=" + z.ToString("F2") + " y=" + (hit.collider ? (hit.point.y - c.y).ToString("F3") + " " + hit.collider.name : "none"));
            }
            Shot(Vector3.right, Vector3.up, 0.8f, "fa18_gunport_side.jpg", c + new Vector3(0, 0.3f, 7.2f));
            Shot(new Vector3(0.45f, -0.6f, -0.65f).normalized, Vector3.up, 0.6f, "fa18_gunport_iso.jpg", c + new Vector3(0, 0.35f, 7.2f));
            Shot(Vector3.down, Vector3.forward, 0.8f, "fa18_gunport_top.jpg", c + new Vector3(0, 0.3f, 7.2f));
            // missiles on the fuselage stations
            foreach (var mn in new[] { "AAM2_single", "AAM1_single" })
            {
                var stL = Find(h, "weaponbayMount_L"); var m = Mount(mn, stL);
                if (m == null) { sb.AppendLine("mount " + mn + " not found"); continue; }
                var lb = LocalBounds(m, stL); sb.AppendLine("mount " + mn + " local bounds min=" + lb.min.ToString("F3") + " max=" + lb.max.ToString("F3"));
                Shot(Vector3.right, Vector3.up, 2.2f, "fa18_station_" + mn + ".jpg", c + new Vector3(0, -0.5f, 0.5f));
                Shot(new Vector3(0.55f, 0.35f, -0.75f).normalized, Vector3.up, 1.6f, "fa18_station_iso_" + mn + ".jpg", c + new Vector3(-1.0f, -0.8f, 1.5f));
                UnityEngine.Object.DestroyImmediate(m);
            }
            var rear = new Vector3(0, -0.34f, -7.9f);
            Shot(new Vector3(0.25f, 0.3f, 1f).normalized, Vector3.up, 0.9f, "fa18_nozzle_rear_iso.jpg", c + rear);
        }
        finally { UnityEngine.Object.DestroyImmediate(h); }
        // ---- F-14 ----
        var f = Inst(F14, c);
        try
        {
            sb.AppendLine("== F14 transparent mats"); MatReport(f, sb);
            var eye = Find(f, "helmetCamPoint");
            sb.AppendLine("F14 eye " + (eye.position - c).ToString("F3"));
            Shot(Vector3.forward, Vector3.up, 0, "f14_eye_0.jpg", eye.position, true, 75f);
            GlassMode(f, 1); Shot(Vector3.forward, Vector3.up, 0, "f14_eye_1.jpg", eye.position, true, 75f);
            GlassMode(f, 2); Shot(Vector3.forward, Vector3.up, 0, "f14_eye_2.jpg", eye.position, true, 75f);
            var red = new Material(Shader.Find("Universal Render Pipeline/Unlit")); red.SetColor("_BaseColor", Color.red);
            foreach (var r in f.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("F14_NoseDoor")) r.sharedMaterials = r.sharedMaterials.Select(_ => red).ToArray();
            var grn = new Material(Shader.Find("Universal Render Pipeline/Unlit")); grn.SetColor("_BaseColor", Color.green);
            foreach (var r in f.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("F14_NoseLeaf") || r.name.StartsWith("F14_NoseStrip")) r.sharedMaterials = r.sharedMaterials.Select(_ => grn).ToArray();
            Gear(f, 0f);
            Shot(new Vector3(0.5f, 0.6f, -0.6f).normalized, Vector3.up, 1.5f, "f14_nosedoor_down.jpg", c + new Vector3(0, -0.8f, 6.0f));
            Gear(f, 1f);
            Shot(new Vector3(0.5f, 0.6f, -0.6f).normalized, Vector3.up, 1.5f, "f14_nosedoor_up.jpg", c + new Vector3(0, -0.6f, 6.0f));
            var nz = Find(f, "nozzle1");
            Shot(new Vector3(0.25f, 0.3f, 1f).normalized, Vector3.up, 1.1f, "f14_nozzle_rear_iso.jpg", nz.position);
        }
        finally { UnityEngine.Object.DestroyImmediate(f); }
        F14Tools.DumpPrefab("Assets/Blueprinter/_donotship/GameObject/SmallFighter1_PLACEHOLDER.prefab", "dump_SmallFighter1.txt");
        File.WriteAllText(D + "ex_diag2.txt", sb.ToString());
        Debug.Log("[F14] EX diag2 done");
    }

    // ---------- round 3: vertex probes ----------
    static List<Vector3> Verts(GameObject go, Func<Transform, bool> pick, Transform frame)
    {
        var l = new List<Vector3>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || !pick(mf.transform)) continue;
            var r = mf.GetComponent<Renderer>(); if (r == null || !r.enabled) continue;
            Vector3[] vs; try { vs = mf.sharedMesh.vertices; } catch { continue; }
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in vs) l.Add(frame.InverseTransformPoint(m.MultiplyPoint3x4(v)));
        }
        return l;
    }
    static void Duct(StringBuilder sb, string tag, List<Vector3> vs, float ax, float ay, float z0, float z1)
    {
        for (float z = z0; z <= z1 + 1e-3f; z += 0.1f)
        {
            var sel = vs.Where(v => v.z >= z - 0.05f && v.z < z + 0.05f).Select(v => new Vector2(v.x - ax, v.y - ay).magnitude).Where(r => r < 0.8f).OrderBy(r => r).ToList();
            if (sel.Count == 0) { sb.AppendLine(tag + " z=" + z.ToString("F2") + " none"); continue; }
            // histogram of radii
            var h = new int[16]; foreach (var r in sel) h[Mathf.Min(15, (int)(r / 0.05f))]++;
            sb.AppendLine(tag + " z=" + z.ToString("F2") + " n=" + sel.Count + " rmin=" + sel[0].ToString("F3") + " hist(0.05)=" + string.Join(",", h));
        }
    }
    public static void Diag3()
    {
        var sb = new StringBuilder();
        var c = new Vector3(0, 1000, 0);
        var h = Inst(FA18, c);
        try
        {
            var vs = Verts(h, t => t.name.StartsWith("FA18C_Skin"), h.transform);
            sb.AppendLine("FA18 skin verts " + vs.Count);
            // nose top centreline
            for (float z = 6.4f; z <= 8.0f; z += 0.05f)
            {
                var sel = vs.Where(v => Mathf.Abs(v.x) < 0.04f && Mathf.Abs(v.z - z) < 0.025f).ToList();
                sb.AppendLine("noseTop z=" + z.ToString("F2") + (sel.Count > 0 ? " ymax=" + sel.Max(v => v.y).ToString("F3") : " none"));
            }
            // station clearance profile (station L frame)
            var st = Find(h, "weaponbayMount_L");
            for (float z = -6f; z <= 1.5f; z += 0.1f)
            {
                var sel = vs.Select(v => st.InverseTransformPoint(h.transform.TransformPoint(v))).Where(u => Mathf.Abs(u.x) < 0.06f && Mathf.Abs(u.z - (z - st.localPosition.z)) < 0.05f && u.y > -0.2f).ToList();
                sb.AppendLine("station z=" + z.ToString("F2") + (sel.Count > 0 ? " surf_u.y=" + sel.Min(u => u.y).ToString("F3") + " n=" + sel.Count : " none"));
            }
            // intake front: for y bands at x=-1.0..-0.8 find max z of verts
            for (float y = -1.0f; y <= 0.1f; y += 0.1f)
            {
                var sel = vs.Where(v => v.x < -0.75f && v.x > -1.35f && Mathf.Abs(v.y - y) < 0.05f && v.z > -2f && v.z < 3f).ToList();
                sb.AppendLine("side y=" + y.ToString("F2") + (sel.Count > 0 ? " zmax=" + sel.Max(v => v.z).ToString("F3") : " none"));
            }
            Duct(sb, "fa18duct", vs, -0.44f, -0.336f, -8.2f, -6.0f);
            foreach (var n in new[] { "afterburner_nozzle_glow", "FA18C_FlameScale_L", "afterburner_flame" })
            { var t = Find(h, n); var r = t.GetComponent<Renderer>(); sb.AppendLine("FA18 " + n + " pos=" + (t.position - c).ToString("F3") + " lossy=" + t.lossyScale.ToString("F3") + (r ? " bounds=" + (r.bounds.min - c).ToString("F3") + ".." + (r.bounds.max - c).ToString("F3") : "") + (t.GetComponent<MeshFilter>() ? " meshB=" + t.GetComponent<MeshFilter>().sharedMesh.bounds : "")); }
        }
        finally { UnityEngine.Object.DestroyImmediate(h); }
        var f = Inst(F14, c);
        try
        {
            var vs = Verts(f, t => t.name.StartsWith("F14_"), f.transform);
            sb.AppendLine("F14 verts " + vs.Count);
            Duct(sb, "f14duct", vs, -1.423f, 0f, -7.4f, -4.5f);
            var gp = vs.Where(v => v.x < -0.45f && v.x > -0.8f && v.y > -0.4f && v.y < 0f && v.z > 8.6f && v.z < 9.5f).ToList();
            sb.AppendLine("f14 gunport verts n=" + gp.Count + (gp.Count > 0 ? " zmax=" + gp.Max(v => v.z).ToString("F3") : ""));
            for (float z = 8.6f; z <= 9.5f; z += 0.05f)
            {
                var sel = gp.Where(v => Mathf.Abs(v.z - z) < 0.025f).ToList();
                if (sel.Count > 0) sb.AppendLine("  gp z=" + z.ToString("F2") + " x=" + sel.Min(v => v.x).ToString("F3") + ".." + sel.Max(v => v.x).ToString("F3") + " y=" + sel.Min(v => v.y).ToString("F3") + ".." + sel.Max(v => v.y).ToString("F3") + " n=" + sel.Count);
            }
            foreach (var n in new[] { "afterburner1_nozzle_glow", "afterburner1_flame", "nozzle1" })
            { var t = Find(f, n); var r = t.GetComponent<Renderer>(); sb.AppendLine("F14 " + n + " pos=" + (t.position - c).ToString("F3") + " lossy=" + t.lossyScale.ToString("F3") + (r ? " bounds=" + (r.bounds.min - c).ToString("F3") + ".." + (r.bounds.max - c).ToString("F3") : "") + (t.GetComponent<MeshFilter>() && t.GetComponent<MeshFilter>().sharedMesh ? " meshB=" + t.GetComponent<MeshFilter>().sharedMesh.bounds + " mesh=" + t.GetComponent<MeshFilter>().sharedMesh.name : "")); }
        }
        finally { UnityEngine.Object.DestroyImmediate(f); }
        var v1 = Inst("Assets/Blueprinter/_donotship/GameObject/SmallFighter1_PLACEHOLDER.prefab", c);
        try
        {
            foreach (var n in new[] { "afterburner_nozzle", "afterburner_flame", "thrustTransform", "nozzle", "heat_haze" })
            {
                var t = Find(v1, n); if (!t) continue; var r = t.GetComponent<Renderer>(); var mf = t.GetComponent<MeshFilter>();
                sb.AppendLine("VORTEX " + n + " pos=" + (t.position - c).ToString("F3") + " rot=" + t.rotation.eulerAngles.ToString("F1") + " lossy=" + t.lossyScale.ToString("F3") + (r ? " bounds=" + (r.bounds.min - c).ToString("F3") + ".." + (r.bounds.max - c).ToString("F3") + " mats=" + string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "@" + AssetDatabase.GetAssetPath(m) : "null")) : "") + (mf && mf.sharedMesh ? " meshB=" + mf.sharedMesh.bounds + " mesh=" + mf.sharedMesh.name + "@" + AssetDatabase.GetAssetPath(mf.sharedMesh) : ""));
            }
            var vv = Verts(v1, t => t.name == "nozzle" || t.name.StartsWith("fuselage") || t.name == "engine", v1.transform);
            Duct(sb, "vortexduct", vv, 0f, -0.246f, -6.2f, -4.0f);
            Shot(new Vector3(0.25f, 0.3f, 1f).normalized, Vector3.up, 1.1f, "vortex_nozzle_rear_iso.jpg", c + new Vector3(0, -0.25f, -5.6f));
            Shot(Vector3.left, Vector3.up, 1.5f, "vortex_nozzle_side.jpg", c + new Vector3(0, -0.25f, -5.6f));
        }
        finally { UnityEngine.Object.DestroyImmediate(v1); }
        File.WriteAllText(D + "ex_diag3.txt", sb.ToString());
        Debug.Log("[F14] EX diag3 done");
    }
}
