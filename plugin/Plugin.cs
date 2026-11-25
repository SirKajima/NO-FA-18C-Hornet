using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace FA18C
{
    [BepInPlugin("fa18c.hornet.systems", "FA-18C Hornet Systems", "0.7.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> Transonic, GLimits, Structure, ToeIn, FlapLift, RollLimiter;
        internal static ConfigEntry<string> FuelModel, FlightProfile;
        internal static ConfigEntry<float> LowAltBoost, PitchResponse;
        internal static ConfigEntry<bool> TailHookOn, CarrierNoseGear, GearProbe, GearDiag, ClearTargetCam;
        internal static ConfigEntry<float> ArrestRelief;
        internal static ConfigEntry<bool> MountCollisionFix, MountCollisionLog, GunMuzzleFix, CockpitClipFix;
        internal static bool GameFeel => FlightProfile.Value == "GameFeel";
        // NATOPS: 7.5 g symmetric limit up to 32,357 lb gross weight; above it the limit keeps n*W constant
        internal const float DesignMass = 14677f;
        internal static float WeightScale(float mass) => mass > DesignMass ? DesignMass / mass : 1f;
        void Awake()
        {
            Log = Logger;
            FlightProfile = Config.Bind("General", "FlightProfile", "GameFeel", new ConfigDescription("GameFeel = Nuclear Option / War Thunder style: 8 g with Stability Assist on and 10 g off at any weight, -3 g / -5 g push, quicker pitch, extra low-altitude thrust. NATOPS = the 0.5.5 real-world limits and thrust.", new AcceptableValueList<string>("GameFeel", "NATOPS")));
            LowAltBoost = Config.Bind("General", "LowAltitudeThrustBoost", 0.08f, new ConfigDescription("GameFeel only: extra thrust below 15,000 ft (fades out by 20,000 ft). 0.08 = +8 %.", new AcceptableValueRange<float>(0f, 0.3f)));
            PitchResponse = Config.Bind("General", "PitchResponse", 1.2f, new ConfigDescription("GameFeel only: pitch controller gain multiplier (1 = 0.5.x feel). Higher = quicker g onset.", new AcceptableValueRange<float>(0.8f, 1.6f)));
            TailHookOn = Config.Bind("General", "TailHook", true, "Working tail hook (needs the 0.6.2 .nobp): deploys automatically on approach below ~190 kt and 100-400 ft, catches carrier arresting wires.");
            ArrestRelief = Config.Bind("Carrier", "ArrestPitchRelief", 0.5f, new ConfigDescription("How much of the nose-down slam from the arresting wire is removed (0 = full real nose-down moment, 1 = none). The wire pulls at the hook point, 2 m below the centre of gravity.", new AcceptableValueRange<float>(0f, 1f)));
            CarrierNoseGear = Config.Bind("Carrier", "CarrierNoseGear", true, "Stiffer, carrier-rated nose gear strut so wire arrests and hard deck landings don't break it.");
            GearProbe = Config.Bind("Carrier", "GearContactProbe", true, "If a wheel's ground ray misses (deck seams, ray starting under a deck edge), probe again from higher up and with the twin nose wheels so the wheel doesn't sink into the deck.");
            GearDiag = Config.Bind("Carrier", "GearDiagnostics", true, "Write a line to the BepInEx log when the contact probe saves a wheel or the nose wheel loses the deck (helps track the deck-sinking bug).");
            MountCollisionFix = Config.Bind("Weapons", "MountSelfCollisionFix", true, "Weapon mounts (including other mods' weapons such as Multi-Missile) never collide with this jet's own parts. Stops parts getting damaged at spawn when a mod's missile or bomb collider reaches into a neighbouring part.");
            MountCollisionLog = Config.Bind("Weapons", "MountCollisionLog", false, "Log a line for every mount the fix is applied to.");
            GunMuzzleFix = Config.Bind("Weapons", "GunMuzzleFix", true, "Put the 20 mm muzzle flash and smoke at the gun port on top of the nose (the game's gun puts them 0.7 m ahead of it). The bullets are unchanged.");
            CockpitClipFix = Config.Bind("Camera", "CockpitClipFix", true, "Keep the cockpit camera's near clip plane at 0.2 m. The game sometimes leaves it at 1 m after the spawn menu closes (airfield spawns), which cuts away the instrument panel and HUD frame.");
            ClearTargetCam = Config.Bind("General", "ClearTargetingCamera", true, "Hide your own jet (fuselage, wings, stores) from the targeting camera so it never blocks the view of the target. The landing camera is not affected.");
            Transonic = Config.Bind("Flight model", "TransonicModel", true, "Wave drag, ram thrust and real thrust lapse with altitude (real acceleration at altitude).");
            GLimits = Config.Bind("Flight model", "FcsGLimits", true, "Weight-scaled FCS limits: +7.5 g / -3 g up to 32,357 lb, the 10 g G-limit override (Stability Assist off) at all speeds.");
            Structure = Config.Bind("Damage", "OverGStructuralFailure", true, "A wing breaks off above about 12.5 g or below about -5 g (lower when heavier than 32,357 lb).");
            ToeIn = Config.Bind("Flight model", "RudderToeIn", true, "NATOPS rudder toe-in/toe-out with flaps down: 30 deg on the ground or below 2 deg AoA, 0 at ~8 deg, 15 deg toe-out at 11 deg. The ground toe-in helps the nose come up at ~140 kt.");
            FlapLift = Config.Bind("Flight model", "FlapLift", true, "Extra flap lift in the air (NATOPS full-flap approach speed: ~130 kt at 8.1 deg AoA at 30,000 lb).");
            RollLimiter = Config.Bind("Flight model", "RollRateLimiter", true, "NATOPS roll-rate limiter: max roll rate cut ~33% with air-to-ground stores or tanks on the wing pylons.");
            FuelModel = Config.Bind("Flight model", "FuelModel", "AltitudeOnly", new ConfigDescription("Fuel flow vs altitude and speed (NATOPS NFM-200, ~10% generous). AltitudeOnly = never burns more than the stock rate (keeps the low-level burn you tested). Realistic = also burns more at low-level high speed. Off = stock game fuel.", new AcceptableValueList<string>("AltitudeOnly", "Realistic", "Off")));
            new Harmony("fa18c.hornet.systems").PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"FA-18C Hornet Systems 0.7.0 loaded (profile {FlightProfile.Value})");
        }
        internal static bool IsHornet(Aircraft a)
        {
            if (a == null) return false;
            var d = a.definition;
            return d != null && d.jsonKey == "FA18C";
        }
    }

    // ---------------- FCS: -3 g push limit and G-limit override at all speeds ----------------
    [HarmonyPatch(typeof(ControlsFilter.FlyByWire), "Filter")]
    static class FbwPatch
    {
        class St { public bool hornet; public bool saOff; public float speed; public float scale = 1f; public float massT = -1f; public float gBase = -1f; public float lim = 7.5f, prevV = -1f, decel, c = 1f, pBase = -1f; }
        static readonly Dictionary<ControlsFilter.FlyByWire, St> state = new Dictionary<ControlsFilter.FlyByWire, St>();
        const float G = 9.81f, OverrideG = 10f, PushG = 3f, NormalG = 7.5f;
        static readonly AccessTools.FieldRef<ControlsFilter.FlyByWire, float> maxRate = AccessTools.FieldRefAccess<ControlsFilter.FlyByWire, float>("maxPitchAngularVel");
        static readonly AccessTools.FieldRef<ControlsFilter.FlyByWire, float> pGain = AccessTools.FieldRefAccess<ControlsFilter.FlyByWire, float>("pFactorFast");
        static readonly AccessTools.FieldRef<ControlsFilter.FlyByWire, float> gLimit = AccessTools.FieldRefAccess<ControlsFilter.FlyByWire, float>("gLimitPositive");

        static void Prefix(ControlsFilter.FlyByWire __instance, Aircraft aircraft, ControlInputs inputs, bool stabilityAssist)
        {
            if (!state.TryGetValue(__instance, out var s)) { s = new St(); state[__instance] = s; s.hornet = Plugin.IsHornet(aircraft); }
            s.saOff = !stabilityAssist && !aircraft.IsAutoHoverEnabled();
            s.speed = Mathf.Max(aircraft.speed, 1f);
            if (!s.hornet) return;
            if (s.gBase < 0f) { s.gBase = gLimit(__instance); s.pBase = pGain(__instance); }
            bool gf = Plugin.GameFeel;
            pGain(__instance) = gf ? s.pBase * Plugin.PitchResponse.Value : s.pBase;
            if (!Plugin.GLimits.Value) { gLimit(__instance) = s.gBase; s.scale = 1f; return; }
            if (Time.time - s.massT > 0.5f) { s.massT = Time.time; s.scale = Plugin.WeightScale(aircraft.GetMass()); }
            // NATOPS NFM-000 2.8.2.3: +7.5 g at 32,357 lb, lowered with weight, held at +5.5 g above 44,000 lb
            float lim = gf ? 8.0f : Mathf.Max(s.gBase * s.scale, 5.5f);   // GameFeel: 8 g (NATOPS-permitted limiter overshoot) at any weight
            // transonic: command limit reduced up to 1 g when decelerating through Mach 1 (not below 5 g)
            float dtt = Mathf.Max(Time.fixedDeltaTime, 1e-3f);
            if (s.prevV > 0f) s.decel += ((s.prevV - s.speed) / dtt - s.decel) * Mathf.Clamp01(dtt / 0.5f);
            s.prevV = s.speed;
            float M = s.speed / LevelInfo.GetSpeedOfSound(aircraft.transform.GlobalPosition().y);
            if (!gf && s.decel > 1f && M > 0.9f && M < 1.1f && lim > 5f)
                lim = Mathf.Max(5f, lim - (1f - Mathf.Abs(M - 1f) / 0.1f));
            s.lim = lim;
            // the game's g law commands a pitch rate (n - gravity), NATOPS limits are load factor:
            // subtract the 1 g already on the airframe (c = cos of pitch/bank) so the limits are true load factors
            s.c = Mathf.Clamp(Vector3.Dot(aircraft.transform.up, Vector3.up), -1f, 1f);
            gLimit(__instance) = lim - s.c;
            // -3.0 g push command, fixed at all gross weights (NFM-000 2.8.2.3)
            // pitch > 0 is a push (nose-down) in the game's convention (angularVelocity.x > 0 = nose down)
            if (inputs.pitch > 0f)
            {
                float push = ((gf && s.saOff) ? 5f : PushG) + s.c;     // GameFeel: -5 g with Stability Assist off     // -3 g load factor = 3 + 1 g of rate from level flight
                float k = s.saOff ? Mathf.Min(1f, push * G / (s.speed * MaxPitchRate(__instance))) : Mathf.Min(1f, push / (lim - s.c));
                inputs.pitch *= k;
            }
            // roll-rate limiter with A/G stores or tanks on the wing pylons (NFM-000 2.8.2.8)
            if (Plugin.RollLimiter.Value && AircraftPatch.RLim(aircraft)) inputs.roll *= 0.67f;
        }
        // replaces "qRatio > 1.2" (switch back to the 7.5 g law at high speed when Stability Assist is off)
        public static double Threshold(ControlsFilter.FlyByWire fbw)
        {
            if (Plugin.GLimits.Value && state.TryGetValue(fbw, out var s) && s.hornet) return 1e9;
            return 1.2;
        }
        // replaces the Stability-Assist-off pitch rate: capped at the 10 g override
        public static float MaxPitchRate(ControlsFilter.FlyByWire fbw)
        {
            float r = maxRate(fbw);
            if (Plugin.GLimits.Value && state.TryGetValue(fbw, out var s) && s.hornet)
                r = Mathf.Min(r, ((Plugin.GameFeel ? OverrideG : Mathf.Max(OverrideG * s.scale, 5.5f * 1.33f)) - s.c) * G / s.speed);   // override = +33 %, as load factor
            return r;
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var fMax = AccessTools.Field(typeof(ControlsFilter.FlyByWire), "maxPitchAngularVel");
            int thr = 0, rate = 0;
            foreach (var ins in code)
            {
                if (ins.opcode == OpCodes.Ldc_R8 && ins.operand is double v && Math.Abs(v - 1.2) < 1e-9)
                {
                    var a = new CodeInstruction(OpCodes.Ldarg_0); a.labels.AddRange(ins.labels);
                    yield return a;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FbwPatch), nameof(Threshold)));
                    thr++; continue;
                }
                if (ins.opcode == OpCodes.Ldfld && Equals(ins.operand, fMax))
                {
                    var c = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FbwPatch), nameof(MaxPitchRate)));
                    c.labels.AddRange(ins.labels); yield return c; rate++; continue;
                }
                yield return ins;
            }
            Plugin.Log.LogInfo($"FBW transpiler: threshold {thr}, rate {rate}");
        }
    }

    // ---------------- Transonic model, real thrust lapse, structural g limit ----------------
    [HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
    static class AircraftPatch
    {
        class St
        {
            public bool init, hornet; public Rigidbody rb; public Turbojet[] engines; public AeroPart[] parts; public Vector3 vPrev; public bool hasPrev;
            public float nFilt; public float overT; public bool broken; public float limitHi, limitLo;
            public AeroPart wingL, wingR;
            public float scale = 1f, massT = -1f;
            // rudder toe-in
            public LandingGear[] gears; public Transform[] rudVis = new Transform[2]; public Transform[] rudPivot = new Transform[2];
            public Vector3[] restPos = new Vector3[2]; public Quaternion[] restRot = new Quaternion[2]; public float[] toeSign = new float[2];
            public float toe, toeVis, airT, logT = -99f, initT, rlimT = -99f; public bool lastWow, rl;
        }
        const float ToeDeg = 30f;          // rudders 30 deg with weight on wheels (F/A-18C FCS, as modelled in DCS)
        const float ToeK = 20f;            // nose-up pitching moment at full toe-in with full aft stick, N*m per Pa (calibrated to the 0.5.2 test)
        const float FlapFull = 77f, FlapUp = 128.6f;   // flap schedule (m/s), same as the TEF HighLiftDevices
        static readonly Dictionary<Aircraft, St> state = new Dictionary<Aircraft, St>();
        static readonly AccessTools.FieldRef<Turbojet, AnimationCurve> altThrust = AccessTools.FieldRefAccess<Turbojet, AnimationCurve>("altitudeThrust");
        const float S = 37.16f;               // reference wing area, m^2
        // 0.5.5: fitted to NFM-200 (F404-GE-400 charts) and NFM-210 (F404-GE-402 acceleration) together
        const float DragCredit = 1.828f;      // parasite-drag credit, m^2 (0.5*q*credit)
        static readonly float[] Mw = { 0.85f, 0.9f, 0.95f, 1.0f, 1.05f, 1.1f, 1.2f, 1.4f, 1.6f, 1.8f };
        static readonly float[] Cw = { 0f, 0.0086f, 0.0159f, 0.0295f, 0.0374f, 0.0406f, 0.0420f, 0.0437f, 0.0473f, 0.0508f };
        static readonly float[] Mr = { 0f, 0.5f, 0.9f, 1.2f, 1.5f, 1.8f };
        static readonly float[] Cr = { 0f, -0.0598f, 0.0026f, 0.3436f, 0.8327f, 1.314f };
        static readonly float[] Hg = { 0f, 3048f, 6096f, 9144f, 10973f, 12192f, 13716f };      // altitude thrust factor G(h)
        static readonly float[] Gh = { 1f, 1.0615f, 1.0483f, 1.0548f, 1.0394f, 0.8946f, 0.7216f };
        const float FlapCLS = 8.05f;          // extra flap lift dCL*S (m^2) at full flaps
        static readonly Dictionary<Aircraft, bool> rlim = new Dictionary<Aircraft, bool>();
        internal static bool RLim(Aircraft a) => a != null && rlim.TryGetValue(a, out var b) && b;
        static readonly Keyframe[] Lapse = {
            new Keyframe(0f,1f), new Keyframe(3000f,0.79f), new Keyframe(6000f,0.62f), new Keyframe(9000f,0.47f),
            new Keyframe(11000f,0.37f), new Keyframe(12192f,0.335f), new Keyframe(15000f,0.26f), new Keyframe(20000f,0.15f) };
        static float Interp(float[] xs, float[] ys, float x)
        {
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++) if (x <= xs[i]) return Mathf.Lerp(ys[i - 1], ys[i], (x - xs[i - 1]) / (xs[i] - xs[i - 1]));
            return ys[ys.Length - 1];
        }
        // At runtime the game unparents every UnitPart, so a.GetComponentsInChildren only sees the root part.
        static T[] All<T>(Aircraft a) where T : Component
        {
            var set = new HashSet<T>();
            foreach (var c in a.GetComponentsInChildren<T>(true)) set.Add(c);
            var parts = a.GetAllParts();
            if (parts != null) foreach (var p in parts) if (p != null) foreach (var c in p.GetComponentsInChildren<T>(true)) set.Add(c);
            var arr = new T[set.Count]; set.CopyTo(arr); return arr;
        }
        static void Init(Aircraft a, St s)
        {
            if (!s.init) s.initT = Time.time;
            s.init = true; s.hornet = Plugin.IsHornet(a);
            if (!s.hornet) return;
            s.rb = a.rb;
            if (s.rb == null && a.cockpit != null) s.rb = a.cockpit.rb;
            s.engines = All<Turbojet>(a);
            s.parts = All<AeroPart>(a);
            if (Plugin.Transonic.Value)
            {
                var curve = new AnimationCurve(Lapse);
                for (int i = 0; i < Lapse.Length; i++) curve.SmoothTangents(i, 0f);
                foreach (var tj in s.engines) altThrust(tj) = curve;
            }
            foreach (var p in s.parts)
            {
                if (p.gameObject.name == "wing1a_L") s.wingL = p;
                if (p.gameObject.name == "wing1a_R") s.wingR = p;
            }
            s.gears = All<LandingGear>(a);
            if (Plugin.CarrierNoseGear.Value)
                foreach (var g in s.gears)
                    if (g != null && g.gameObject.name == "gear_F")
                    {
                        float k0 = GearPatch.Spring(g), c0 = GearPatch.Damp(g);
                        if (k0 < 700000f) { GearPatch.Spring(g) = 700000f; GearPatch.Damp(g) = 52000f; }
                        Plugin.Log.LogInfo($"Hornet nose gear: spring {k0:F0} -> {GearPatch.Spring(g):F0} N/m, damping {c0:F0} -> {GearPatch.Damp(g):F0}");
                    }
            var hooks = All<TailHook>(a);
            if (!Plugin.TailHookOn.Value) foreach (var th in hooks) if (th != null) UnityEngine.Object.Destroy(th);
            HookPatch.Has[a] = Plugin.TailHookOn.Value && hooks.Length > 0;
            string[] piv = { "FA18C_Pivot_Rudder_L", "FA18C_Pivot_Rudder_R" }, vis = { "FA18C_Rudder_L", "FA18C_Rudder_R" };
            foreach (var t in All<Transform>(a))
                for (int i = 0; i < 2; i++)
                {
                    if (t.name == piv[i]) s.rudPivot[i] = t;
                    if (t.name == vis[i]) s.rudVis[i] = t;
                }
            for (int i = 0; i < 2; i++)
            {
                var pv = s.rudPivot[i]; var vv = s.rudVis[i];
                if (pv == null || vv == null || vv.parent != pv) { s.rudVis[i] = null; continue; }
                s.restPos[i] = vv.localPosition; s.restRot[i] = vv.localRotation;
                // +angle about the pivot's hinge (local right) moves the trailing edge toward the pivot's local up;
                // toe-in = trailing edges toward the centreline
                float side = Vector3.Dot(pv.TransformDirection(Vector3.up), a.transform.right);
                s.toeSign[i] = (i == 0 ? 1f : -1f) * Mathf.Sign(side);
            }
            Plugin.Log.LogInfo($"Hornet init: engines={s.engines.Length} aeroParts={s.parts.Length} rb={(s.rb != null ? s.rb.name : "NULL")} (unit rb {(a.rb != null)}), gears={(s.gears != null ? s.gears.Length : -1)}, rudders L={(s.rudVis[0] != null)} R={(s.rudVis[1] != null)} sign {s.toeSign[0]}/{s.toeSign[1]}, remoteSim={a.remoteSim}");
            var r = new System.Random(a.GetInstanceID());
            s.limitHi = 12.2f + (float)r.NextDouble() * 0.6f;
            s.limitLo = -4.8f - (float)r.NextDouble() * 0.4f;
        }
        static readonly AccessTools.FieldRef<AeroPart, PartJoint[]> jointsOf = AccessTools.FieldRefAccess<AeroPart, PartJoint[]>("joints");
        static readonly string[] WingParts = { "wing1a", "wing1b", "wing2", "wingtip", "aileron", "flap" };
        static bool InWing(AeroPart p, string side)
        {
            if (p == null) return false; string n = p.gameObject.name;
            if (!n.EndsWith(side)) return false;
            foreach (var w in WingParts) if (n == w + side) return true;
            return false;
        }
        static int BreakWing(St s, string side)
        {
            int k = 0;
            foreach (var p in s.parts)
            {
                if (p == null) continue;
                var js = jointsOf(p); if (js == null) continue;
                bool pin = InWing(p, side);
                foreach (var j in js)
                {
                    if (j == null || j.joint == null) continue;
                    var other = j.connectedPart as AeroPart;
                    if (pin != InWing(other, side)) { UnityEngine.Object.Destroy(j.joint); k++; }
                }
            }
            return k;
        }
        static readonly AccessTools.FieldRef<Unit, System.Collections.IList> stationsOf = AccessTools.FieldRefAccess<Unit, System.Collections.IList>("weaponStations");
        static bool rlErr;
        static bool WingAgStores(Aircraft a)
        {
            try
            {
                var sts = stationsOf(a); if (sts == null) return false;
                foreach (var st in sts)
                {
                    if (st == null) continue;
                    var tr = Traverse.Create(st);
                    var info = tr.Field("WeaponInfo").GetValue<WeaponInfo>();
                    var ws = tr.Field("Weapons").GetValue() as System.Collections.IEnumerable;
                    if (info == null || ws == null) continue;
                    var eff = Traverse.Create(info).Field("effectiveness");
                    float ag = Convert.ToSingle(eff.Field("antiSurface").GetValue()), aa = Convert.ToSingle(eff.Field("antiAir").GetValue());
                    string nm = (info.weaponName ?? "").ToLowerInvariant();
                    bool agStore = info.bomb || info.glideBomb || ag > aa || nm.Contains("tank") || nm.Contains("fuel") || (!info.missile && !info.gun);
                    if (!agStore) continue;
                    foreach (var o in ws)
                    {
                        var w = o as Weapon; if (w == null || !w.gameObject.activeInHierarchy) continue;
                        if (Convert.ToSingle(Traverse.Create(w).Field("ammo").GetValue()) <= 0f) continue;
                        float x = Mathf.Abs(a.transform.InverseTransformPoint(w.transform.position).x);
                        if (x > 1.4f && x < 5.2f) return true;    // wing pylons (inboard 2.25 m, outboard 3.4 m); tips are 5.7 m
                    }
                }
            }
            catch (Exception e) { if (!rlErr) { rlErr = true; Plugin.Log.LogWarning("Hornet roll limiter: store check failed: " + e.Message); } }
            return false;
        }
        static string GearStr(St s)
        {
            if (s.gears == null) return "none";
            var sb = new System.Text.StringBuilder();
            foreach (var g in s.gears) { if (g == null) continue; sb.Append(g.name).Append(':').Append(g.WeightOnWheel(0.02f) ? 1 : 0).Append(' '); }
            return sb.ToString();
        }
        static void Postfix(Aircraft __instance)
        {
            var a = __instance;
            if (!state.TryGetValue(a, out var s)) { s = new St(); state[a] = s; }
            if (!s.init || (s.hornet && s.gears != null && s.gears.Length == 0 && Time.time - s.initT < 10f)) { Init(a, s); }
            if (!s.hornet) return;
            if (a.remoteSim || s.rb == null || a.disabled)
            {
                if (Time.time - s.logT > 5f) { s.logT = Time.time; Plugin.Log.LogInfo($"Hornet skipped: remoteSim={a.remoteSim} rb={(s.rb != null)} disabled={a.disabled}"); }
                return;
            }
            Vector3 v = s.rb.velocity; float V = v.magnitude;
            float dt = Time.fixedDeltaTime;
            if (Time.time - s.massT > 0.5f) { s.massT = Time.time; s.scale = Plugin.WeightScale(a.GetMass()); }
            // ---- rudder toe-in (ground, flaps down): nose-up moment for rotation ----
            {
                bool wow = false;
                if (s.gears != null) foreach (var g in s.gears) if (g != null && g.WeightOnWheel(0.02f)) { wow = true; break; }
                if (!wow && a.radarAlt < 0.6f && V < 100f) wow = true;   // fallback if the wheel test fails
                s.airT = wow ? 0f : s.airT + dt;
                float flap = 1f - Mathf.Clamp01((V - FlapFull) / (FlapUp - FlapFull));
                // ground only: weight on wheels + gear down + flaps out (flap schedule by speed)
                bool gearDown = a.gearDeployed;
                float target = (Plugin.ToeIn.Value && gearDown) ? flap * Mathf.Clamp01(1f - s.airT / 1.0f) : 0f;
                s.toe = Mathf.MoveTowards(s.toe, target, dt / 1.0f);    // ~1 s to full travel (drives the rotation moment)
                // NATOPS T/O & land mode: 30 deg toe-in at WOW or AoA < 2, through 0 (~8 deg) to 15 deg toe-out at 11 deg
                Vector3 lv = V > 1f ? a.transform.InverseTransformDirection(v) : Vector3.forward;
                float aoa = Mathf.Atan2(-lv.y, lv.z) * Mathf.Rad2Deg;
                float sched = wow ? (gearDown ? 1f : 0f) : Mathf.Clamp((30f - 5f * (aoa - 2f)) / 30f, -0.5f, 1f);
                float visT = Plugin.ToeIn.Value ? flap * sched : 0f;
                s.toeVis = Mathf.MoveTowards(s.toeVis, visT, dt / 1.0f);
                // extra flap lift in the air (ramps in over 1 s after lift-off, fades out above 15 deg AoA)
                if (Plugin.FlapLift.Value && !wow && flap > 0f && V > 20f)
                {
                    float fade = Mathf.Clamp01(1f - (aoa - 15f) / 15f) * Mathf.Clamp01((aoa + 6f) / 4f) * Mathf.Clamp01(s.airT / 1.0f);
                    float qf = 0.5f * a.airDensity * V * V;
                    float dL = qf * FlapCLS * flap * fade;
                    Vector3 up = Vector3.Cross(v, a.transform.right).normalized;
                    s.rb.AddForce(up * dL - (v / V) * (0.12f * dL), ForceMode.Force);
                }
                // roll-rate limiter: A/G stores or tanks on the wing pylons (checked once a second)
                if (Time.time - s.rlimT > 1f) { s.rlimT = Time.time; s.rl = WingAgStores(a); rlim[a] = s.rl; }
                if (wow != s.lastWow || (wow && Time.time - s.logT > 3f))
                {
                    s.logT = Time.time; s.lastWow = wow;
                    Plugin.Log.LogInfo($"Hornet ground: wow={wow} V={V * 1.944f:F0}kt toe={s.toe:F2} vis={s.toeVis:F2} rlim={s.rl} pull={(a.GetInputs() != null ? -a.GetInputs().pitch : 0f):F2} radarAlt={a.radarAlt:F2} gearWow=[{GearStr(s)}]");
                }
                float pull = 0f;
                var inp = a.GetInputs(); if (inp != null) pull = Mathf.Clamp01(-inp.pitch);   // pitch < 0 = aft stick
                if (s.toe > 0f && V > 1f && pull > 0f)
                {
                    float q = 0.5f * a.airDensity * V * V;
                    s.rb.AddTorque(-a.transform.right * (ToeK * q * s.toe * pull), ForceMode.Force);   // +x rotation is nose-down
                }
                for (int i = 0; i < 2; i++)
                {
                    var vv = s.rudVis[i]; if (vv == null) continue;
                    var rq = Quaternion.AngleAxis(s.toeSign[i] * ToeDeg * s.toeVis, Vector3.right);
                    vv.localPosition = rq * s.restPos[i]; vv.localRotation = rq * s.restRot[i];
                }
            }
            // ---- transonic: wave drag, ram thrust, drag credit ----
            if (Plugin.Transonic.Value && V > 1f)
            {
                float rho = a.airDensity;
                float h = a.transform.GlobalPosition().y;
                float M = V / LevelInfo.GetSpeedOfSound(h);
                float q = 0.5f * rho * V * V;
                float thrust = 0f;
                if (s.engines != null) foreach (var e in s.engines) if (e != null) thrust += e.GetThrust();
                float boost = Plugin.GameFeel ? Plugin.LowAltBoost.Value * Mathf.Clamp01((6096f - h) / 1524f) : 0f;   // full below 15k ft, gone by 20k ft
                float fwd = thrust * ((1f + Interp(Mr, Cr, M)) * Interp(Hg, Gh, h) * (1f + boost) - 1f);   // ram recovery x altitude factor x GameFeel boost
                float drag = Interp(Mw, Cw, M) * q * S - 0.5f * q * DragCredit;
                Vector3 dir = v / V;
                s.rb.AddForce(a.transform.forward * fwd - dir * drag, ForceMode.Force);
            }
            // ---- structural over-g ----
            if (s.hasPrev && Plugin.Structure.Value && !s.broken)
            {
                Vector3 acc = (v - s.vPrev) / dt - Physics.gravity;
                float n = Vector3.Dot(acc, a.transform.up) / 9.81f;
                s.nFilt += (n - s.nFilt) * Mathf.Clamp01(dt / 0.08f);
                float hi = s.limitHi * s.scale, lo = s.limitLo * s.scale;     // structure: n*W limit
                if (Plugin.GameFeel) { hi = s.limitHi - 0.8f; lo = s.limitLo - 2.9f; }    // GameFeel: ~11.4-12 g / ~-7.7..-8.1 g at any weight
                if ((s.nFilt > hi || s.nFilt < lo) && a.radarAlt > 2f) s.overT += dt; else s.overT = 0f;
                if (s.overT > 0.12f)
                {
                    string side = (UnityEngine.Random.value < 0.5f) ? "_L" : "_R";
                    int k = BreakWing(s, side);
                    Plugin.Log.LogInfo($"Hornet wing{side} failed at {s.nFilt:F1} g ({k} joints)");
                    s.broken = true;
                }
            }
            s.vPrev = v; s.hasPrev = true;
        }
    }

    // ---------------- Fuel flow vs altitude and Mach (NFM-200 combat fuel flow + specific range, ~10% generous) ----------------
    // Stock game: burn = Lerp(min, max, rpm ratio) + sum(afterburner amount x consumption): no altitude or speed effect.
    [HarmonyPatch(typeof(Turbojet), "UseFuel")]
    static class FuelPatch
    {
        static readonly AccessTools.FieldRef<Turbojet, Aircraft> acOf = AccessTools.FieldRefAccess<Turbojet, Aircraft>("aircraft");
        static readonly AccessTools.FieldRef<Turbojet, float> fMin = AccessTools.FieldRefAccess<Turbojet, float>("fuelConsumptionMin");
        static readonly AccessTools.FieldRef<Turbojet, float> fMax = AccessTools.FieldRefAccess<Turbojet, float>("fuelConsumptionMax");
        static readonly AccessTools.FieldRef<Turbojet, float> tRatio = AccessTools.FieldRefAccess<Turbojet, float>("thrustRatio");
        static readonly float[] Hab = { 0f, 6096f, 9144f, 10668f, 12192f, 13716f };
        static readonly float[] Tab = { 0.907f, 0.765f, 0.600f, 0.508f, 0.332f, 0.199f };
        static bool logged;
        internal static float Delta(float h)
        {
            if (h <= 11000f) return Mathf.Pow(Mathf.Max(0.05f, 1f - 2.25577e-5f * h), 5.25588f);
            return 0.22336f * Mathf.Exp(-(h - 11000f) / 6341.6f);
        }
        internal static float CoreFactor(float h, float M) => Mathf.Pow(Delta(h), 0.735f) * (1f + 0.21f * M);
        internal static float AbFactor(float h, float M)
        {
            float t;
            if (h <= Hab[0]) t = Tab[0];
            else if (h >= Hab[Hab.Length - 1]) t = Tab[Tab.Length - 1] * Delta(h) / Delta(Hab[Hab.Length - 1]);
            else { int i = 1; while (h > Hab[i]) i++; t = Mathf.Lerp(Tab[i - 1], Tab[i], (h - Hab[i - 1]) / (Hab[i] - Hab[i - 1])); }
            return t * (1f + 0.5f * M);
        }
        static void Prefix(Turbojet __instance, ref float __0)
        {
            string mode = Plugin.FuelModel.Value;
            if (mode == "Off") return;
            var a = acOf(__instance);
            if (a == null || !Plugin.IsHornet(a)) return;
            float h = a.transform.GlobalPosition().y;
            float M = a.speed / LevelInfo.GetSpeedOfSound(h);
            float core = Mathf.Lerp(fMin(__instance), fMax(__instance), tRatio(__instance));
            core = Mathf.Min(core, __0);
            float ab = Mathf.Max(0f, __0 - core);
            float kc = CoreFactor(h, M), ka = AbFactor(h, M);
            if (mode != "Realistic") { kc = Mathf.Min(kc, 1f); ka = Mathf.Min(ka, 1f); }
            float before = __0;
            __0 = core * kc + ab * ka;
            if (!logged) { logged = true; Plugin.Log.LogInfo($"Hornet fuel model {mode}: {before:F2} -> {__0:F2} kg/s at {h:F0} m M{M:F2}"); }
        }
    }

    // ---------------- Tail hook: tell the game (AI landing, airbase overlay) that the Hornet has one ----------------
    [HarmonyPatch(typeof(WeaponManager), "HasTailHook")]
    static class HookPatch
    {
        internal static readonly Dictionary<Aircraft, bool> Has = new Dictionary<Aircraft, bool>();
        static readonly AccessTools.FieldRef<WeaponManager, Aircraft> acOf = AccessTools.FieldRefAccess<WeaponManager, Aircraft>("aircraft");
        static void Postfix(WeaponManager __instance, ref bool __result)
        {
            if (__result) return;
            var a = acOf(__instance);
            if (a == null || !Plugin.IsHornet(a)) return;
            if (!Has.TryGetValue(a, out var h)) { h = Plugin.TailHookOn.Value && a.GetComponentsInChildren<TailHook>(true).Length > 0; Has[a] = h; }
            __result = h;
        }
    }

    // ---------------- 0.6.2: arresting wire pulls nearer the centre of gravity (less nose slam) ----------------
    [HarmonyPatch(typeof(TailHook), "ApplyForce")]
    static class ArrestPatch
    {
        static bool logged;
        static void Prefix(TailHook __instance, out Vector3 __state)
        {
            __state = new Vector3(float.NaN, 0f, 0f);
            float k = Plugin.ArrestRelief.Value;
            if (k <= 0f) return;
            var up = __instance.unitPart; var e = __instance.hookEnd;
            if (up == null || e == null || up.rb == null) return;
            if (!Plugin.IsHornet(up.parentUnit as Aircraft)) return;
            var rb = up.rb;
            Vector3 p = e.position, ax = rb.transform.up;
            float dh = Vector3.Dot(rb.worldCenterOfMass - p, ax);
            __state = e.localPosition;
            e.position = p + ax * (dh * k);
            if (!logged) { logged = true; Plugin.Log.LogInfo($"Hornet arrest: wire force applied {dh * k:F2} m higher (hook point {dh:F2} m below CG, relief {k:F2})"); }
        }
        static void Postfix(TailHook __instance, Vector3 __state)
        {
            if (float.IsNaN(__state.x)) return;
            var e = __instance.hookEnd; if (e != null) e.localPosition = __state;
        }
    }

    // ---------------- 0.6.2: wheel ground ray fallback + diagnostics ----------------
    [HarmonyPatch(typeof(LandingGear), "FixedUpdate")]
    static class GearPatch
    {
        internal static readonly AccessTools.FieldRef<LandingGear, float> Spring = AccessTools.FieldRefAccess<LandingGear, float>("springRate");
        internal static readonly AccessTools.FieldRef<LandingGear, float> Damp = AccessTools.FieldRefAccess<LandingGear, float>("dampingRate");
        static readonly AccessTools.FieldRef<LandingGear, float> Travel = AccessTools.FieldRefAccess<LandingGear, float>("suspensionTravel");
        static readonly AccessTools.FieldRef<LandingGear, float> MaxComp = AccessTools.FieldRefAccess<LandingGear, float>("maxCompression");
        static readonly AccessTools.FieldRef<LandingGear, Aircraft> AcOf = AccessTools.FieldRefAccess<LandingGear, Aircraft>("aircraft");
        class G { public bool hornet, nose; public Aircraft ac; public float lastHit = -99f, lastLog = -99f, missSince = -1f; }
        static readonly Dictionary<LandingGear, G> info = new Dictionary<LandingGear, G>();
        static readonly Dictionary<Aircraft, List<G>> mains = new Dictionary<Aircraft, List<G>>();
        static readonly RaycastHit[] buf = new RaycastHit[16];
        const float Raise = 0.35f, TwinOff = 0.2f;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var lc = AccessTools.Method(typeof(Physics), "Linecast", new[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(int) });
            var mine = AccessTools.Method(typeof(GearPatch), nameof(GearCast));
            int n = 0;
            foreach (var ci in code)
            {
                if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo mi && mi == lc)
                {
                    n++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, mine);
                    continue;
                }
                yield return ci;
            }
            if (n != 1) Plugin.Log.LogWarning($"GearPatch: expected 1 Linecast in LandingGear.FixedUpdate, patched {n}");
        }

        static G Get(LandingGear g)
        {
            if (info.TryGetValue(g, out var x) && x.ac != null) return x;
            x = new G { ac = AcOf(g) };
            x.hornet = Plugin.IsHornet(x.ac);
            x.nose = g.gameObject.name == "gear_F";
            info[g] = x;
            if (x.hornet && !x.nose && x.ac != null)
            {
                if (!mains.TryGetValue(x.ac, out var l)) mains[x.ac] = l = new List<G>();
                l.Add(x);
            }
            return x;
        }
        static bool Own(Collider c, Aircraft ac)
        {
            var rb = c.attachedRigidbody; if (rb == null) return false;
            var up = rb.GetComponent<UnitPart>();
            return up != null && up.parentUnit == ac;
        }

        static bool GearCast(Vector3 s, Vector3 e, out RaycastHit hit, int mask, LandingGear g)
        {
            bool ok = Physics.Linecast(s, e, out hit, mask);
            if (!Plugin.GearProbe.Value && !Plugin.GearDiag.Value) return ok;
            var x = Get(g);
            if (!x.hornet) return ok;
            float now = Time.time;
            if (ok) { x.lastHit = now; x.missSince = -1f; return true; }
            if (x.missSince < 0f) x.missSince = now;
            // the stock ray missed: is the rest of the jet still on the ground?
            bool othersDown = false;
            if (x.nose && mains.TryGetValue(x.ac, out var ml)) foreach (var m in ml) if (now - m.lastHit < 0.1f) othersDown = true;
            Vector3 d = e - s; float len = d.magnitude; if (len < 1e-4f) return false; d /= len;
            Vector3 right = x.ac != null ? x.ac.transform.right : g.transform.right;
            bool found = false; float best = float.MaxValue; RaycastHit bh = default; int which = -1;
            if (Plugin.GearProbe.Value)
            {
                int probes = x.nose ? 3 : 1;
                for (int i = 0; i < probes; i++)
                {
                    Vector3 off = i == 0 ? Vector3.zero : right * (i == 1 ? TwinOff : -TwinOff);
                    int c = Physics.RaycastNonAlloc(s + off - d * Raise, d, buf, len + Raise, mask);
                    for (int j = 0; j < c; j++)
                    {
                        var h = buf[j];
                        if (h.collider == null || Own(h.collider, x.ac)) continue;
                        if (h.distance < best) { best = h.distance; bh = h; found = true; which = i; }
                    }
                }
            }
            if (found)
            {
                float dist = best - Raise;                              // from the castPoint; < 0 = ground above the ray start
                float minD = Travel(g) - MaxComp(g) + 0.02f;            // never more compression than just under the break limit
                bool clamped = dist < minD;
                bh.distance = Mathf.Max(dist, minD);
                hit = bh; x.lastHit = now;
                if (Plugin.GearDiag.Value && now - x.lastLog > 1f)
                {
                    x.lastLog = now;
                    Plugin.Log.LogInfo($"Hornet gear probe saved {g.gameObject.name}: {(which == 0 ? "centre (raised start)" : "twin wheel " + (which == 1 ? "right" : "left"))}, ground {dist:F2} m below cast point{(clamped ? " (ABOVE/at the ray start - clamped)" : "")}, collider '{bh.collider.name}' layer {bh.collider.gameObject.layer} on '{(bh.collider.attachedRigidbody != null ? bh.collider.attachedRigidbody.name : "static")}'");
                }
                return true;
            }
            if (Plugin.GearDiag.Value && x.nose && othersDown && now - x.missSince > 0.25f && now - x.lastLog > 2f)
            {
                x.lastLog = now;
                var sb = new System.Text.StringBuilder();
                int c = Physics.RaycastNonAlloc(s - d * 1.5f, d, buf, 4f);
                for (int j = 0; j < c; j++)
                {
                    var h = buf[j]; if (h.collider == null) continue;
                    sb.Append($" ['{h.collider.name}' L{h.collider.gameObject.layer}{(h.collider.isTrigger ? " trig" : "")}{(Own(h.collider, x.ac) ? " OWN" : "")} {h.distance - 1.5f:F2}m]");
                }
                Plugin.Log.LogWarning($"Hornet nose wheel lost the deck while the mains are down (mask {mask}). Hits below the nose cast point (m below it):{(c == 0 ? " none" : sb.ToString())}");
            }
            return false;
        }
    }

    // ---------------- 0.6.3: own jet never blocks the targeting camera ----------------
    [HarmonyPatch]
    static class TargetCamPatch
    {
        static readonly AccessTools.FieldRef<TargetCam, Camera> CamOf = AccessTools.FieldRefAccess<TargetCam, Camera>("cam");
        static readonly AccessTools.FieldRef<TargetCam, Aircraft> AcOf = AccessTools.FieldRefAccess<TargetCam, Aircraft>("aircraft");
        static readonly FieldInfo ModeF = AccessTools.Field(typeof(TargetCam), "currentMode");
        class C { public Aircraft ac; public bool hornet; public readonly List<Renderer> rs = new List<Renderer>(); public float t = -99f; }
        static readonly Dictionary<TargetCam, C> cache = new Dictionary<TargetCam, C>();
        static readonly List<Renderer> hidden = new List<Renderer>();
        static bool logged;
        [HarmonyPostfix, HarmonyPatch(typeof(TargetCam), "OnBeginCameraRendering")]
        static void Begin(TargetCam __instance, Camera __1)
        {
            if (hidden.Count > 0) Restore();
            if (!Plugin.ClearTargetCam.Value || __1 == null || __1 != CamOf(__instance)) return;
            if (!cache.TryGetValue(__instance, out var c) || c.ac == null) { c = new C { ac = AcOf(__instance) }; c.hornet = Plugin.IsHornet(c.ac); cache[__instance] = c; }
            if (!c.hornet) return;
            if (ModeF != null && Convert.ToInt32(ModeF.GetValue(__instance)) == 2) return;   // landing camera: keep the jet visible
            if (Time.time - c.t > 2f)
            {
                c.t = Time.time; c.rs.Clear();
                c.rs.AddRange(c.ac.GetComponentsInChildren<Renderer>(false));
                var parts = c.ac.GetAllParts();
                if (parts != null) foreach (var p in parts) if (p != null && p.transform.root != c.ac.transform.root) c.rs.AddRange(p.GetComponentsInChildren<Renderer>(false));
                if (!logged) { logged = true; Plugin.Log.LogInfo($"Hornet targeting camera: hiding {c.rs.Count} own renderers while it draws"); }
            }
            foreach (var r in c.rs) if (r != null && !r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }
        }
        [HarmonyPostfix, HarmonyPatch(typeof(TargetCam), "OnEndCameraRendering")]
        static void End(TargetCam __instance, Camera __1)
        {
            if (__1 != null && __1 == CamOf(__instance)) Restore();
        }
        static void Restore()
        {
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            hidden.Clear();
        }
    }

    // Weapon mounts from other mods (e.g. Multi-Missile) can carry solid colliders that reach into a neighbouring part
    // of this jet. At spawn the physics engine pushes the overlapping parts apart, which damages them. Make every
    // spawned mount ignore collisions with this aircraft's own colliders (it still collides with everything else).
    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    static class MountSelfCollisionPatch
    {
        static void Postfix(Hardpoint __instance, Aircraft aircraft)
        {
            try
            {
                if (!Plugin.MountCollisionFix.Value || !Plugin.IsHornet(aircraft)) return;
                var v = Traverse.Create(__instance).Field("spawnedPrefab").GetValue();
                GameObject go = v as GameObject; if (go == null && v is Component comp) go = comp.gameObject;
                if (go == null) return;
                var own = go.GetComponentsInChildren<Collider>(true);
                if (own.Length == 0) return;
                var mine = new HashSet<Collider>(own);
                var all = new HashSet<Collider>(aircraft.GetComponentsInChildren<Collider>(true));
                var parts = aircraft.GetAllParts();
                if (parts != null) foreach (var p in parts) if (p != null) foreach (var c in p.GetComponentsInChildren<Collider>(true)) all.Add(c);
                int n = 0;
                foreach (var c in all)
                {
                    if (c == null || c.isTrigger || mine.Contains(c)) continue;
                    foreach (var m in own) { if (m == null || m.isTrigger) continue; Physics.IgnoreCollision(m, c, true); n++; }
                }
                if (Plugin.MountCollisionLog.Value) Plugin.Log.LogInfo($"mount {go.name}: {own.Length} collider(s) set to ignore {n} pairs with own airframe");
            }
            catch (Exception e) { Plugin.Log.LogWarning("mount collision fix failed: " + e.Message); }
        }
    }

    // ---------------- 0.7.0: muzzle flash/smoke at the gun port on top of the nose ----------------
    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    static class GunFlashPatch
    {
        static void Postfix(Hardpoint __instance, Aircraft aircraft)
        {
            try
            {
                if (!Plugin.GunMuzzleFix.Value || !Plugin.IsHornet(aircraft) || __instance == null || __instance.transform == null) return;
                var v = Traverse.Create(__instance).Field("spawnedPrefab").GetValue();
                GameObject go = v as GameObject; if (go == null && v is Component comp) go = comp.gameObject;
                if (go == null) return;
                var gun = go.GetComponentInChildren<Gun>(true); if (gun == null) return;
                var ps = Traverse.Create(gun).Field("muzzleParticles").GetValue() as System.Collections.IEnumerable; if (ps == null) return;
                // gun port (aircraft frame (0, 0.07, 7.05)) in the cannon hardpoint frame (hardpoint at (0, -0.05, 7.0))
                Vector3 port = __instance.transform.TransformPoint(new Vector3(0f, 0.12f, 0.05f));
                int n = 0; foreach (var o in ps) { var p = o as ParticleSystem; if (p == null) continue; p.transform.position = port; n++; }
                Plugin.Log.LogInfo($"Hornet gun: {n} muzzle effects moved to the gun port");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Hornet gun flash: " + e.Message); }
        }
    }

    // ---------------- 0.7.0: cockpit camera near clip (game bug: AircraftSelectionMenu.OnDestroy sets it to 1 m) ----------------
    [HarmonyPatch(typeof(CameraCockpitState), "UpdateState")]
    static class CockpitClipPatch
    {
        static readonly AccessTools.FieldRef<CameraCockpitState, Aircraft> acOf = AccessTools.FieldRefAccess<CameraCockpitState, Aircraft>("aircraft");
        static bool logged;
        static void Postfix(CameraCockpitState __instance, CameraStateManager cam)
        {
            try
            {
                if (!Plugin.CockpitClipFix.Value || cam == null || cam.mainCamera == null) return;
                if (!Plugin.IsHornet(acOf(__instance))) return;
                if (cam.mainCamera.nearClipPlane > 0.21f)
                {
                    if (!logged) { logged = true; Plugin.Log.LogInfo($"Hornet cockpit camera near clip {cam.mainCamera.nearClipPlane} -> 0.2"); }
                    cam.mainCamera.nearClipPlane = 0.2f;
                }
            }
            catch (Exception) { }
        }
    }
}
