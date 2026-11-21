using System;
using UnityEngine;

namespace GTagModChecker
{
    public enum CamMode { FirstPerson, ThirdPerson, Follow, TrackMonkey, Lock, GoPro }

    /// <summary>
    /// Kameramod (CAMERA-fanen): et eget lokalt kamera som rendrer til en RenderTexture som vises på nettbrettet.
    /// Valgfritt vises samme bilde på PC-skjermen (desktop/spectator-vinduet) via OnGUI, så du kan ta opp med OBS/Game Bar.
    /// Kameraet er bare lokalt: ingen nettverk, ingen endringer på spilleren din eller andre.
    /// </summary>
    internal sealed class CameraMod : MonoBehaviour
    {
        public static readonly float[] FovSteps = { 60f, 75f, 90f, 110f };
        public static readonly string[] SmoothNames = { "OFF", "LOW", "MED", "HIGH" };
        private static readonly float[] SmoothRates = { 0f, 18f, 8f, 3.5f };

        public CamMode Mode { get; private set; } = CamMode.ThirdPerson;
        public int FovIndex = 2, SmoothIndex = 2;
        public bool Flip, TargetSelected, DesktopOut, Recording;
        public bool PreviewVisible;
        public float RecordingStarted;

        /// <summary>Hode-transform til valgt spiller (fra menyen), eller null.</summary>
        public Func<Transform> SelectedHead;

        public RenderTexture Texture { get; private set; }
        public event Action TextureChanged;

        private Camera _cam;
        private bool _hasPose;
        private Vector3 _anchorPos;
        private int _fpMask = -1, _tpMask = -1;
        private float _nextMaskLookup;

        private PluginSettings S => Plugin.Settings;

        public static CameraMod Create()
        {
            var go = new GameObject("GTagModCheckerCam") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            var c = go.AddComponent<CameraMod>();
            c.Init();
            return c;
        }

        private void Init()
        {
            try
            {
                FovIndex = Mathf.Clamp(S.CamFovIndex.Value, 0, FovSteps.Length - 1);
                SmoothIndex = Mathf.Clamp(S.CamSmooth.Value, 0, SmoothNames.Length - 1);
                Mode = S.CamMode.Value;
            }
            catch { }
            _cam = gameObject.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.nearClipPlane = 0.03f;
            _cam.farClipPlane = 1500f;
            _cam.depth = -50;
            _cam.clearFlags = CameraClearFlags.Skybox;
            MakeTexture(false);
        }

        public float Fov => FovSteps[Mathf.Clamp(FovIndex, 0, FovSteps.Length - 1)];
        public string SmoothName => SmoothNames[Mathf.Clamp(SmoothIndex, 0, SmoothNames.Length - 1)];

        public void SetMode(CamMode m)
        {
            Mode = m;
            if (m == CamMode.TrackMonkey) _anchorPos = _hasPose ? transform.position : HeadPos() + Vector3.up * 0.5f;
            try { S.CamMode.Value = m; } catch { }
            Plugin.Log.LogInfo("[GTag Mod Checker] Kamera: modus " + m);
        }

        public void CycleFov() { FovIndex = (FovIndex + 1) % FovSteps.Length; try { S.CamFovIndex.Value = FovIndex; } catch { } }
        public void StepFov(int d) { FovIndex = Mathf.Clamp(FovIndex + d, 0, FovSteps.Length - 1); try { S.CamFovIndex.Value = FovIndex; } catch { } }
        public void CycleSmooth() { SmoothIndex = (SmoothIndex + 1) % SmoothNames.Length; try { S.CamSmooth.Value = SmoothIndex; } catch { } }
        public void Recenter() { _hasPose = false; if (Mode == CamMode.TrackMonkey) _anchorPos = HeadPos() + Vector3.up * 0.5f; }

        public void SetDesktop(bool on)
        {
            if (DesktopOut == on) return;
            DesktopOut = on;
            MakeTexture(on);
            Plugin.Log.LogInfo("[GTag Mod Checker] Kamera: PC-skjerm " + (on ? "PÅ" : "AV"));
        }

        /// <summary>REC: viser kameraet på PC-skjermen og starter en tidtaker. Selve videofilen tas opp med OBS/Game Bar.</summary>
        public void ToggleRecording()
        {
            Recording = !Recording;
            if (Recording) { RecordingStarted = Time.unscaledTime; SetDesktop(true); }
            Plugin.Log.LogInfo("[GTag Mod Checker] Kamera: REC " + (Recording ? "PÅ (PC-skjerm)" : "AV"));
        }

        public string RecTime
        {
            get
            {
                int s = Recording ? Mathf.FloorToInt(Time.unscaledTime - RecordingStarted) : 0;
                return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
            }
        }

        private void MakeTexture(bool hi)
        {
            var old = Texture;
            int w = hi ? 1280 : 768, h = hi ? 720 : 432;
            Texture = new RenderTexture(w, h, 24) { name = "GTagModCheckerCamRT", antiAliasing = 1 };
            Texture.Create();
            if (_cam != null) _cam.targetTexture = Texture;
            TextureChanged?.Invoke();
            if (old != null) { old.Release(); Destroy(old); }
        }

        // ------------------------------------------------------------------ oppdatering

        private Transform Head
        {
            get
            {
                var main = Camera.main;
                return main != null ? main.transform : null;
            }
        }

        private Vector3 HeadPos() { var h = Head; return h != null ? h.position : transform.position; }

        private Transform Target
        {
            get
            {
                if (TargetSelected) { try { var t = SelectedHead?.Invoke(); if (t != null) return t; } catch { } }
                return Head;
            }
        }

        private void LookupMasks()
        {
            if (Time.unscaledTime < _nextMaskLookup && _fpMask != -1) return;
            _nextMaskLookup = Time.unscaledTime + 5f;
            var main = Camera.main;
            if (main != null) _fpMask = main.cullingMask;
            // Spillets eget tredjepersonskamera (PC-vinduet) viser også ditt eget hode: bruk masken dets for 3. person.
            try
            {
                var tagger = GtRefs.Tagger;
                var f = tagger?.GetType().GetField("thirdPersonCamera");
                var go = f?.GetValue(tagger) as GameObject;
                var tp = go != null ? go.GetComponentInChildren<Camera>(true) : null;
                if (tp != null) _tpMask = tp.cullingMask;
            }
            catch { }
            if (_tpMask == -1 && _fpMask != -1)
            {
                _tpMask = _fpMask;
                int mirror = LayerMask.NameToLayer("MirrorOnly");
                if (mirror >= 0) _tpMask |= 1 << mirror;
                int noMirror = LayerMask.NameToLayer("NoMirror");
                if (noMirror >= 0) _tpMask &= ~(1 << noMirror);
            }
        }

        private void LateUpdate()
        {
            bool active = PreviewVisible || DesktopOut;
            if (_cam == null) return;
            if (!active) { if (_cam.enabled) _cam.enabled = false; return; }
            var head = Head;
            if (head == null) { _cam.enabled = false; return; }

            LookupMasks();
            bool firstPerson = (Mode == CamMode.FirstPerson || Mode == CamMode.GoPro) && !Flip;
            int mask = firstPerson ? _fpMask : _tpMask;
            if (mask != -1) _cam.cullingMask = mask;
            _cam.fieldOfView = Mode == CamMode.GoPro ? Mathf.Max(Fov, 110f) : Fov;

            Vector3 pos; Quaternion rot;
            Desired(head, out pos, out rot);
            float rate = SmoothRates[Mathf.Clamp(SmoothIndex, 0, SmoothRates.Length - 1)];
            if (Mode == CamMode.Follow) rate = rate <= 0f ? 6f : rate * 0.6f;
            if (Mode == CamMode.Lock && _hasPose) { pos = transform.position; rot = transform.rotation; }
            if (!_hasPose || rate <= 0f)
            {
                transform.SetPositionAndRotation(pos, rot);
                _hasPose = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime);
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, pos, k), Quaternion.Slerp(transform.rotation, rot, k));
            }
            if (!_cam.enabled) _cam.enabled = true;
        }

        private void Desired(Transform head, out Vector3 pos, out Quaternion rot)
        {
            Vector3 hp = head.position;
            Vector3 fwd = head.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            Transform tgt = Target;
            Vector3 tp = tgt != null ? tgt.position : hp;
            switch (Mode)
            {
                case CamMode.FirstPerson:
                    pos = hp; rot = head.rotation;
                    if (Flip) { pos = hp + head.forward * 0.9f; rot = Quaternion.LookRotation(hp - pos, Vector3.up); }
                    break;
                case CamMode.ThirdPerson:
                {
                    Vector3 f = TargetSelected && tgt != head ? FlatForward(tgt) : fwd;
                    pos = tp - f * (Flip ? -1.6f : 1.6f) + Vector3.up * 0.45f;
                    rot = Quaternion.LookRotation((tp + Vector3.up * 0.05f) - pos, Vector3.up);
                    break;
                }
                case CamMode.Follow:
                {
                    Vector3 cur = _hasPose ? transform.position : tp - fwd * 2f;
                    Vector3 dir = cur - tp; dir.y = 0;
                    if (dir.sqrMagnitude < 0.01f) dir = -fwd;
                    pos = tp + dir.normalized * 2.2f + Vector3.up * 0.6f;
                    rot = Quaternion.LookRotation(tp - pos, Vector3.up);
                    break;
                }
                case CamMode.TrackMonkey:
                    if (_anchorPos == Vector3.zero) _anchorPos = hp + Vector3.up * 0.5f - fwd;
                    pos = _anchorPos;
                    rot = (tp - pos).sqrMagnitude > 1e-4f ? Quaternion.LookRotation(tp - pos, Vector3.up) : transform.rotation;
                    break;
                case CamMode.GoPro:
                    pos = hp + head.up * 0.10f + head.forward * 0.06f;
                    rot = head.rotation;
                    if (Flip) { pos = hp + head.forward * 0.45f + head.up * 0.05f; rot = Quaternion.LookRotation(hp - pos, head.up); }
                    break;
                default: // Lock
                    pos = _hasPose ? transform.position : hp; rot = _hasPose ? transform.rotation : head.rotation;
                    break;
            }
        }

        private static Vector3 FlatForward(Transform t)
        {
            Vector3 f = t.forward; f.y = 0;
            return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        }

        /// <summary>PC-skjerm: tegner kamerabildet over hele spillvinduet (kun på PC-en, ikke i headsettet).</summary>
        private void OnGUI()
        {
            if (!DesktopOut || Texture == null || Event.current.type != EventType.Repaint) return;
            GUI.depth = -1000;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture, ScaleMode.ScaleAndCrop, false);
            if (Recording)
            {
                var c = GUI.color; GUI.color = new Color(0.82f, 0.16f, 0.2f, 1f);
                GUI.Label(new Rect(16, 12, 300, 30), "● REC " + RecTime);
                GUI.color = c;
            }
        }

        private void OnDestroy()
        {
            if (Texture != null) { Texture.Release(); Destroy(Texture); }
        }
    }
}
