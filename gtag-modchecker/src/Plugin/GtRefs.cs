using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace GTagModChecker
{
    /// <summary>
    /// Refleksjonsoppslag mot Gorilla Tag (Assembly-CSharp) uten kompileringsavhengighet:
    /// GorillaTagger.Instance.leftHandTransform / rightHandTransform / offlineVRRig og StartVibration.
    /// Alt er lesing, bortsett fra vibrasjon i dine egne kontrollere.
    /// </summary>
    internal static class GtRefs
    {
        private const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags I = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static bool _searched, _loggedMissing, _vibrationBroken;
        private static Type _taggerType;
        private static MethodInfo _startVibration;

        private static void Find()
        {
            if (_searched) return;
            _searched = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "Assembly-CSharp") continue;
                    _taggerType = asm.GetType("GorillaTagger");
                    break;
                }
                if (_taggerType != null)
                    _startVibration = _taggerType.GetMethod("StartVibration", I, null, new[] { typeof(bool), typeof(float), typeof(float) }, null);
                Plugin.Log.LogInfo($"[GTag Mod Checker] GorillaTagger funnet={_taggerType != null}, StartVibration={_startVibration != null}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[GTag Mod Checker] GorillaTagger-oppslag feilet: " + ex.Message); }
        }

        public static object Tagger
        {
            get
            {
                Find();
                if (_taggerType == null) return null;
                try
                {
                    var p = _taggerType.GetProperty("Instance", S);
                    if (p != null) return p.GetValue(null, null);
                    var f = _taggerType.GetField("_instance", S) ?? _taggerType.GetField("instance", S);
                    return f?.GetValue(null);
                }
                catch { return null; }
            }
        }

        /// <summary>Din egen rig (GorillaTagger.Instance.offlineVRRig), eller null.</summary>
        public static object LocalRig
        {
            get { try { return Get(Tagger, "offlineVRRig"); } catch { return null; } }
        }

        public static Transform LeftHand => Hand("leftHandTransform");
        public static Transform RightHand => Hand("rightHandTransform");

        private static Transform Hand(string member)
        {
            try
            {
                var tagger = Tagger;
                if (tagger == null) return null;
                if (Get(tagger, member) is Transform t && t != null) return t;
                var rig = Get(tagger, "offlineVRRig");
                if (rig != null && Get(rig, member) is Transform t2 && t2 != null) return t2;
            }
            catch { }
            if (!_loggedMissing) { _loggedMissing = true; Plugin.Log.LogWarning($"[GTag Mod Checker] Fant ikke {member} (bruker kamera-fallback)"); }
            return null;
        }

        // ------------------------------------------------------------------
        //  Høyre pekefinger-tupp (for å trykke på menyknapper). Kun lesing.
        // ------------------------------------------------------------------
        private static Func<Vector3?> _tipGetter;
        private static string _tipSource;
        private static float _nextTipResolve;
        private static readonly System.Collections.Generic.HashSet<string> _tipLogged = new System.Collections.Generic.HashSet<string>();
        private static readonly System.Text.RegularExpressions.Regex TipName =
            new System.Text.RegularExpressions.Regex("right.*(index|finger|tip|trigger.*collider)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static string TipSource => _tipSource ?? "rightHandTransform+offset";

        /// <summary>
        /// Fingertuppen på høyre hånd. Prioritet:
        /// 1) GorillaTagger.rightHandTriggerCollider (fingertupp-collideren GT bruker til sine egne knapper; bounds-senter eller posisjon),
        /// 2) andre felt som heter noe med right + index/finger/tip på GorillaTagger eller din egen VRRig,
        /// 3) rightHandTransform + forward * fallbackOffset.
        /// </summary>
        public static Vector3? RightFingerTip(Transform rightHand, float fallbackOffset, out string source)
        {
            if (_tipGetter == null && Time.unscaledTime >= _nextTipResolve)
            {
                _nextTipResolve = Time.unscaledTime + 2f;
                ResolveTip();
            }
            if (_tipGetter != null)
            {
                Vector3? p = null;
                try { p = _tipGetter(); } catch { }
                if (p != null) { source = _tipSource; LogTipOnce(source, p.Value, rightHand); return p; }
                _tipGetter = null; _tipSource = null; // prøv igjen om 2 sekunder
            }
            source = "rightHandTransform+offset";
            if (rightHand == null) return null;
            Vector3 fb = rightHand.position + rightHand.forward * fallbackOffset;
            LogTipOnce(source, fb, rightHand);
            return fb;
        }

        private static void LogTipOnce(string source, Vector3 tip, Transform hand)
        {
            if (!_tipLogged.Add(source)) return;
            string dist = hand != null ? (tip - hand.position).magnitude.ToString("0.000") + " m" : "?";
            Plugin.Log.LogInfo($"[GTag Mod Checker] Fingertupp-kilde: {source} (avstand fra rightHandTransform: {dist})");
        }

        private static void ResolveTip()
        {
            try
            {
                var tagger = Tagger;
                if (tagger == null) return;
                // 1) Den kjente fingertupp-collideren
                if (TryMember(tagger, "rightHandTriggerCollider", "GorillaTagger.rightHandTriggerCollider")) return;
                // 2) Andre felt med passende navn
                if (ScanOwner(tagger, "GorillaTagger")) return;
                var rig = LocalRig;
                if (rig != null && ScanOwner(rig, "VRRig")) return;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[GTag Mod Checker] Fingertupp-oppslag feilet: " + ex.Message); }
        }

        private static bool TryMember(object owner, string member, string label)
        {
            object v;
            try { v = Get(owner, member); } catch { return false; }
            if (PosOf(v) == null) return false;
            _tipGetter = () => PosOf(Get(owner, member));
            _tipSource = label;
            return true;
        }

        private static bool ScanOwner(object owner, string ownerName)
        {
            var t = owner.GetType();
            foreach (var f in t.GetFields(I))
            {
                if (!TipName.IsMatch(f.Name)) continue;
                if (!(typeof(GameObject).IsAssignableFrom(f.FieldType) || typeof(Component).IsAssignableFrom(f.FieldType))) continue;
                if (TryMember(owner, f.Name, ownerName + "." + f.Name)) return true;
            }
            foreach (var pr in t.GetProperties(I))
            {
                if (!TipName.IsMatch(pr.Name) || pr.GetIndexParameters().Length > 0) continue;
                if (!(typeof(GameObject).IsAssignableFrom(pr.PropertyType) || typeof(Component).IsAssignableFrom(pr.PropertyType))) continue;
                if (TryMember(owner, pr.Name, ownerName + "." + pr.Name)) return true;
            }
            return false;
        }

        /// <summary>Posisjonen til et GameObject/Component/Transform; for collidere brukes senteret av bounds.</summary>
        private static Vector3? PosOf(object v)
        {
            GameObject go = v as GameObject;
            if (go == null && v is Component c && c != null) go = c.gameObject;
            if (go == null) return null;
            try
            {
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null || !comp.GetType().Name.EndsWith("Collider")) continue;
                    var bp = comp.GetType().GetProperty("bounds", I);
                    if (bp != null && bp.GetValue(comp, null) is Bounds b && b.extents.sqrMagnitude > 1e-8f) return b.center;
                }
            }
            catch { }
            return go.transform.position;
        }

        /// <summary>Vibrasjon i egen kontroller. Prøver Gorilla Tag sin StartVibration, ellers UnityEngine.XR.</summary>
        public static void Vibrate(bool left, float amplitude, float duration)
        {
            if (!_vibrationBroken && _startVibration != null)
            {
                try
                {
                    var tagger = Tagger;
                    if (tagger != null) { _startVibration.Invoke(tagger, new object[] { left, amplitude, duration }); return; }
                }
                catch { _vibrationBroken = true; }
            }
            try
            {
                var d = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
                if (d.isValid && d.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                    d.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
            }
            catch { }
        }

        private static object Get(object o, string member)
        {
            if (o == null) return null;
            var t = o.GetType();
            var p = t.GetProperty(member, I);
            if (p != null) return p.GetValue(o, null);
            var f = t.GetField(member, I);
            return f?.GetValue(o);
        }
    }
}
