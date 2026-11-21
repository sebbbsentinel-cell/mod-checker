using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FanGame.ModChecker;
using Photon.Pun;
using UnityEngine;
using MC = FanGame.ModChecker.ModChecker;

namespace GTagModChecker
{
    /// <summary>
    /// Leser andre spilleres rigger (VRRig) via REFLEKSJON, så pluginen ikke må kompileres mot Assembly-CSharp
    /// og tåler Gorilla Tag-oppdateringer bedre. Feiler noe, slås bevegelsessjekkene av og det logges én gang.
    /// Medlemsnavn (VRRigCache.ActiveRigContainers, RigContainer.Rig, VRRig.Creator.ActorNumber, headMesh,
    /// leftHandTransform, rightHandTransform) er de samme som GorillaCaster (MIT) bruker.
    /// </summary>
    internal sealed class RigReader
    {
        private Type _rigType, _cacheType;
        private bool _initDone;
        public bool Available { get; private set; }
        public string Status { get; private set; } = "ikke startet";

        private readonly Dictionary<string, Func<object, object>> _getters = new Dictionary<string, Func<object, object>>();
        private readonly List<Component> _rigs = new List<Component>();
        private float _nextFallbackScan;
        private Component[] _fallbackCache = new Component[0];
        private int _errors;

        private void Init()
        {
            if (_initDone) return;
            _initDone = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "Assembly-CSharp") continue;
                    _rigType = asm.GetType("VRRig");
                    _cacheType = asm.GetType("VRRigCache");
                    break;
                }
                Available = _rigType != null && typeof(Component).IsAssignableFrom(_rigType);
                Status = Available ? (_cacheType != null ? "VRRigCache" : "FindObjectsOfType(VRRig)") : "fant ikke VRRig, bevegelsessjekker av";
                Plugin.Log.LogInfo("[GTag Mod Checker] Rig-lesing: " + Status);
            }
            catch (Exception ex)
            {
                Available = false; Status = "feil: " + ex.Message;
                Plugin.Log.LogWarning("[GTag Mod Checker] Rig-lesing av: " + ex.Message);
            }
        }

        /// <summary>Tar ett snapshot av hver annen spillers rig og sender det til checkeren.</summary>
        public void Sample(MC checker, PhotonFeed feed, double now)
        {
            Init();
            if (!Available || !PhotonFeed.InRoom) return;
            try
            {
                int localActor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
                foreach (var rig in GetRigs())
                {
                    if (rig == null) continue;
                    int actor = GetActor(rig);
                    if (actor <= 0 || actor == localActor) continue;
                    string id = PhotonFeed.Id(actor);
                    if (!feed.IsKnown(id)) continue; // rig i pool uten spiller i rommet

                    Vector3 head = GetPos(rig, "headMesh") ?? rig.transform.position;
                    Vector3 left = GetPos(rig, "leftHandTransform") ?? head;
                    Vector3 right = GetPos(rig, "rightHandTransform") ?? head;
                    float scale = rig.transform.localScale.x;

                    checker.RecordSnapshot(id, new PlayerSnapshot
                    {
                        Timestamp = now,
                        BodyPosition = V(head),
                        HeadPosition = V(head),
                        LeftHandPosition = V(left),
                        RightHandPosition = V(right),
                        Scale = scale <= 0 ? 1f : scale,
                        Grounded = true // ukjent for andre spillere -> FLY-sjekk er av (se README)
                    });

                    // Valgfri info (fps/farge) hvis feltene finnes
                    int? fps = null; string color = null;
                    try { var f = Get(rig, "fps"); if (f is int i) fps = i; } catch { }
                    try { var c = Get(rig, "playerColor"); if (c is Color col) color = "#" + ColorUtility.ToHtmlStringRGB(col); } catch { }
                    if (fps != null || color != null)
                        checker.UpdatePlayerInfo(id, new PlayerInfo { Fps = fps, Color = color }, now);
                }
            }
            catch (Exception ex)
            {
                if (_errors++ < 5) Plugin.Log.LogWarning("[GTag Mod Checker] Rig-sampling feilet: " + ex.Message);
                if (_errors > 50) { Available = false; Status = "for mange feil, bevegelsessjekker av"; Plugin.Log.LogWarning("[GTag Mod Checker] " + Status); }
            }
        }

        // ------------------------------------------------------------------
        //  Til menyen/navnelappene: liste over rigger med hode, farge, fps, kosmetikk og stemme (kun lesing).
        // ------------------------------------------------------------------
        private readonly Dictionary<Component, AudioSource> _voiceCache = new Dictionary<Component, AudioSource>();
        private readonly HashSet<Component> _voiceSearched = new HashSet<Component>();

        /// <summary>Samler alle kjente rigger i rommet (andre spillere, og egen rig hvis includeLocal). Endrer ingenting.</summary>
        public List<RigInfo> Collect(PhotonFeed feed, bool includeLocal)
        {
            var list = new List<RigInfo>();
            Init();
            if (!Available || !PhotonFeed.InRoom) return list;
            try
            {
                int localActor = PhotonFeed.LocalActor;
                foreach (var rig in GetRigs())
                {
                    if (rig == null) continue;
                    int actor = GetActor(rig);
                    if (actor <= 0 || actor == localActor) continue;
                    string id = PhotonFeed.Id(actor);
                    if (!feed.IsKnown(id)) continue;
                    list.Add(Describe(rig, actor, false));
                }
                if (includeLocal && GtRefs.LocalRig is Component local && local != null)
                    list.Add(Describe(local, localActor, true));
            }
            catch (Exception ex)
            {
                if (_errors++ < 5) Plugin.Log.LogWarning("[GTag Mod Checker] Rig-liste feilet: " + ex.Message);
            }
            return list;
        }

        private RigInfo Describe(Component rig, int actor, bool isLocal)
        {
            var info = new RigInfo { Actor = actor, PlayerId = PhotonFeed.Id(actor), Rig = rig, IsLocal = isLocal };
            var h = Get(rig, "headMesh");
            info.Head = h is GameObject g ? g.transform : h is Component c ? c.transform : rig.transform;
            try { var f = Get(rig, "fps"); if (f is int i && i > 0) info.Fps = i; } catch { }
            try { var col = Get(rig, "playerColor"); if (col is Color cc) info.Color = cc; } catch { }
            try { info.Cosmetics = Get(rig, "concatStringOfCosmeticsAllowed") as string; } catch { }
            info.Voice = FindVoice(rig);
            return info;
        }

        /// <summary>
        /// Stemme-AudioSource for en rig: VRRig.voiceAudio hvis feltet finnes, ellers en AudioSource som sitter sammen med en
        /// Photon Voice "Speaker"-komponent under riggen. Tar ALDRI tilfeldige lydkilder (f.eks. tag-lyder).
        /// </summary>
        private AudioSource FindVoice(Component rig)
        {
            if (_voiceCache.TryGetValue(rig, out var cached) && cached != null) return cached;
            if (_voiceSearched.Contains(rig) && Time.time < _nextVoiceRetry) return null;
            _voiceSearched.Add(rig);
            AudioSource found = null;
            try
            {
                found = Get(rig, "voiceAudio") as AudioSource;
                if (found == null)
                {
                    foreach (var src in rig.GetComponentsInChildren<AudioSource>(true))
                    {
                        if (src == null) continue;
                        foreach (var comp in src.GetComponents<Component>())
                        {
                            if (comp == null) continue;
                            string n = comp.GetType().Name;
                            if (n.IndexOf("Speaker", StringComparison.OrdinalIgnoreCase) >= 0) { found = src; break; }
                        }
                        if (found != null) break;
                    }
                }
            }
            catch { found = null; }
            if (found != null) _voiceCache[rig] = found;
            if (_voiceSearched.Count > 64) { _voiceSearched.Clear(); _nextVoiceRetry = Time.time + 5f; }
            return found;
        }
        private float _nextVoiceRetry;

        private IEnumerable<Component> GetRigs()
        {
            _rigs.Clear();
            if (_cacheType != null)
            {
                try
                {
                    var containers = GetStatic(_cacheType, "ActiveRigContainers") as IEnumerable;
                    if (containers != null)
                    {
                        foreach (var c in containers)
                            if (c != null && Get(c, "Rig") is Component r) _rigs.Add(r);
                        return _rigs;
                    }
                }
                catch { /* faller tilbake */ }
            }
            // Fallback: finn alle aktive VRRig-objekter (tregt, derfor maks én gang i sekundet)
            if (Time.time >= _nextFallbackScan)
            {
                _nextFallbackScan = Time.time + 1f;
                _fallbackCache = UnityEngine.Object.FindObjectsOfType(_rigType).OfType<Component>().ToArray();
            }
            _rigs.AddRange(_fallbackCache);
            return _rigs;
        }

        private int GetActor(Component rig)
        {
            try
            {
                var creator = Get(rig, "Creator");
                if (creator != null && Get(creator, "ActorNumber") is int a) return a;
            }
            catch { }
            try
            {
                var pv = rig.GetComponentInParent<PhotonView>();
                if (pv != null) return pv.OwnerActorNr;
            }
            catch { }
            return -1;
        }

        private Vector3? GetPos(object rig, string member)
        {
            var v = Get(rig, member);
            if (v is Transform t && t != null) return t.position;
            if (v is GameObject g && g != null) return g.transform.position;
            if (v is Component c && c != null) return c.transform.position;
            return null;
        }

        private object Get(object obj, string member)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            string key = t.FullName + "." + member;
            if (!_getters.TryGetValue(key, out var g))
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var p = t.GetProperty(member, F);
                var f = p == null ? t.GetField(member, F) : null;
                if (p != null) g = o => p.GetValue(o, null);
                else if (f != null) g = o => f.GetValue(o);
                else g = o => null;
                _getters[key] = g;
            }
            return g(obj);
        }

        private static object GetStatic(Type t, string member)
        {
            const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var p = t.GetProperty(member, F);
            if (p != null) return p.GetValue(null, null);
            var f = t.GetField(member, F);
            return f?.GetValue(null);
        }

        private static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }

    /// <summary>Det menyen og navnelappene trenger om én rig. Alt er lest, ingenting skrives tilbake til spillet.</summary>
    internal sealed class RigInfo
    {
        public int Actor;
        public string PlayerId;
        public bool IsLocal;
        public Component Rig;
        public Transform Head;
        public int? Fps;
        public Color? Color;
        public string Cosmetics;
        public AudioSource Voice;

        public Vector3 HeadPos => Head != null ? Head.position : (Rig != null ? Rig.transform.position : Vector3.zero);
        public float Scale => Rig != null && Rig.transform.localScale.x > 0 ? Rig.transform.localScale.x : 1f;
        public bool Alive => Rig != null && Rig.gameObject.activeInHierarchy;
    }
}
