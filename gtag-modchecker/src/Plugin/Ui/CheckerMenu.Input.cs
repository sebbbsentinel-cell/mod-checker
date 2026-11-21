using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GTagModChecker
{
    /// <summary>Posisjon (følger venstre hånd), fingertrykk, SELECT GUN-laser og fingerprikk.</summary>
    internal sealed partial class CheckerMenu
    {
        private float _nextLive;

        private void BuildLaser()
        {
            var go = new GameObject("GTagModCheckerLaser") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            _laser = go.AddComponent<LineRenderer>();
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;
            _laser.numCapVertices = 2;
            _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laser.receiveShadows = false;
            _laserMat = _kit.Mat(UiKit.Silver);
            _laserHoverMat = _kit.Mat(Color.white);
            _laser.sharedMaterial = _laserMat;
            _laser.startColor = UiKit.Silver; _laser.endColor = UiKit.Silver;
            _laser.enabled = false;
        }

        /// <summary>Liten sølv/hvit prikk der fingertuppen er (der trykket måles). Ingen collider.</summary>
        private void BuildDot()
        {
            _dot = _kit.Child(transform, "FingerDot", Vector3.zero);
            _dotGlowMat = _kit.AlphaMat(new Color(0.86f, 0.88f, 0.91f, 0.45f));
            _dotGlowHotMat = _kit.AlphaMat(new Color(1f, 1f, 1f, 0.70f));
            _dotCoreMat = _kit.Mat(UiKit.Silver);
            _dotCoreHotMat = _kit.Mat(Color.white);
            _dotGlow = _kit.Sphere(_dot, "Glow", 0.010f, _dotGlowMat);
            _dotCore = _kit.Sphere(_dot, "Core", 0.0055f, _dotCoreMat);
            _dot.gameObject.SetActive(false);
        }

        private void UpdateDot()
        {
            if (_dot == null) return;
            bool show = S.ShowFingerDot.Value && _tip != null && gameObject.activeSelf;
            if (_dot.gameObject.activeSelf != show) _dot.gameObject.SetActive(show);
            if (!show) return;
            bool hot = _kit.Hovered != null;
            float scale = Mathf.Max(0.2f, S.TabletScale.Value);
            float k = (hot ? 1.4f : 1f) / scale; // kompenser for menyens skala, prikken har fast størrelse i meter
            _dot.position = _tip.Value;
            _dot.localScale = new Vector3(k, k, k);
            if (_dotGlowMat != null) _dotGlow.sharedMaterial = hot ? _dotGlowHotMat : _dotGlowMat;
            if (_dotCoreMat != null) _dotCore.sharedMaterial = hot ? _dotCoreHotMat : _dotCoreMat;
        }

        private void Update()
        {
            try
            {
                UpdatePose();
                RefreshHands();
                _tip = GtRefs.RightFingerTip(_rh, S.FingerTipOffset.Value, out _tipSource);
                if (_tip != null)
                {
                    _kit.Touch(_tip.Value, Mathf.Max(0f, S.PressRadius.Value), Mathf.Max(0.002f, S.PressDepth.Value),
                               S.PressCooldown.Value, S.Haptics.Value, S.HapticAmplitude.Value);
                    TouchDebug();
                }
                else _kit.ClearHover();
                UpdateLaser();
                if (_cam != null) _cam.PreviewVisible = CurrentPage == Page.Camera;
                if (CurrentPage == Page.Spotify) MediaKeys.Poll();
                // ting som endrer seg uten nye data: tidtaker, "spilles nå", toast som går ut
                if (Time.unscaledTime >= _nextLive)
                {
                    _nextLive = Time.unscaledTime + 0.5f;
                    if (_toastShown && Time.unscaledTime >= _toastUntil) ShowPage(CurrentPage);
                    else if (!_toastShown)
                    {
                        if (CurrentPage == Page.Camera) RenderCamera();
                        else if (CurrentPage == Page.Spotify) RenderSpotify();
                        else if (CurrentPage == Page.World) RenderWorld();
                    }
                }
            }
            catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] Meny-feil: " + ex.Message); }
        }

        private void LateUpdate()
        {
            UpdatePose();
            // posituren kan ha flyttet seg etter Update: les fingertuppen på nytt så prikken sitter nøyaktig
            if (_rh != null || _tip != null) _tip = GtRefs.RightFingerTip(_rh, S.FingerTipOffset.Value, out _tipSource);
            UpdateDot();
            UpdateLaserLine();
        }

        /// <summary>Én logglinje i sekundet (DebugTouch) med nærmeste knapp, så vi kan se hvorfor et trykk ikke treffer.</summary>
        private void TouchDebug()
        {
            if (!S.DebugTouch.Value || Time.unscaledTime < _nextTouchDebug) return;
            _nextTouchDebug = Time.unscaledTime + 1f;
            var n = _kit.Nearest;
            string hand = _rh != null ? (_tip.Value - _rh.position).magnitude.ToString("0.000") : "?";
            Plugin.Log.LogInfo($"[GTag Mod Checker] Touch-debug: kilde={_tipSource} (hånd->tupp {hand} m), nærmest={n.Btn?.Id ?? "-"}, " +
                               $"plan={(n.Btn != null ? n.Plane.ToString("+0.000;-0.000") : "-")} m, utenfor rekt={(n.Btn != null ? n.RectDist.ToString("0.000") : "-")} m, " +
                               $"inRect={n.InRect}, hover={_kit.Hovered?.Id ?? "-"}, margin={S.PressRadius.Value:0.000}, dybde=±{S.PressDepth.Value:0.000}");
        }

        private void RefreshHands()
        {
            if (Time.unscaledTime < _nextHandLookup && _lh != null && _rh != null) return;
            _nextHandLookup = Time.unscaledTime + 1f;
            _lh = GtRefs.LeftHand;
            _rh = GtRefs.RightHand;
        }

        /// <summary>Kopierer posituren fra venstre hånd hver ramme (blir ikke barn av spillets objekter).</summary>
        private void UpdatePose()
        {
            if (this == null || !gameObject.activeSelf) return;
            RefreshHands();
            float scale = Mathf.Max(0.2f, S.TabletScale.Value);
            transform.localScale = new Vector3(scale, scale, scale);

            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 fwd = cam != null ? cam.transform.forward : new Vector3(0, 0, 1);
            fwd.y = 0; if (fwd.sqrMagnitude < 0.001f) fwd = new Vector3(0, 0, 1); fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0, -fwd.x);
            var off = new Vector3(S.TabletOffsetX.Value, S.TabletOffsetY.Value, S.TabletOffsetZ.Value);

            if (_lh == null)
            {
                transform.position = camPos + fwd * 0.45f + Vector3.up * -0.12f + right * -0.10f;
                transform.rotation = Quaternion.LookRotation(transform.position - camPos, Vector3.up);
                return;
            }

            if (S.TabletAttach.Value == TabletAttachMode.HandLocked)
            {
                transform.position = _lh.position + _lh.rotation * off;
                transform.rotation = _lh.rotation * Quaternion.Euler(S.TabletRotX.Value, S.TabletRotY.Value, S.TabletRotZ.Value);
            }
            else
            {
                transform.position = _lh.position + right * off.x + Vector3.up * off.y + fwd * off.z;
                Vector3 look = transform.position - camPos;
                transform.rotation = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.LookRotation(fwd, Vector3.up);
            }
        }

        private Vector3 LaserOrigin => _rh != null ? _rh.position : Vector3.zero;
        private Vector3 LaserDir => _rh != null ? (_rh.rotation * Quaternion.Euler(S.LaserRotX.Value, S.LaserRotY.Value, S.LaserRotZ.Value) * Vector3.forward).normalized : Vector3.forward;

        private float? ReadTriggerSafe() { try { return ReadTrigger?.Invoke(); } catch { return null; } }

        private bool GunActive => S.LaserEnabled.Value && _gun;

        /// <summary>
        /// Finner spilleren hvis hode ligger nærmest laserens retning (innenfor LaserConeAngle grader).
        /// Ingen raycast og ingen collidere: bare vinkelen mellom laseren og retningen til hodet.
        /// </summary>
        public static RigInfo PickTarget(Vector3 origin, Vector3 dir, List<RigInfo> rigs, float coneDeg, float maxDist, out float bestAngle)
        {
            RigInfo best = null;
            bestAngle = float.MaxValue;
            if (rigs == null) return null;
            foreach (var r in rigs)
            {
                if (r == null || r.IsLocal || !r.Alive) continue;
                Vector3 to = r.HeadPos - origin;
                float dist = to.magnitude;
                if (dist < 0.3f || dist > maxDist) continue;
                // litt større kjegle for spillere langt unna, så de ikke blir umulige å treffe
                float allowed = coneDeg + Mathf.Rad2Deg * Mathf.Atan2(0.15f, dist);
                float a = Vector3.Angle(dir, to);
                if (a <= allowed && a < bestAngle) { bestAngle = a; best = r; }
            }
            return best;
        }

        private void UpdateLaser()
        {
            if (!GunActive || _rh == null) { _hoverId = null; if (_laser != null) _laser.enabled = false; return; }

            var target = PickTarget(LaserOrigin, LaserDir, _rigs, S.LaserConeAngle.Value, S.LaserMaxDistance.Value, out _);
            string id = target?.PlayerId;
            if (id != _hoverId) { _hoverId = id; _hoverSince = Time.unscaledTime; }

            float? trig = ReadTriggerSafe();
            bool useTrigger = S.LaserSelect.Value == LaserSelectMode.Trigger || (S.LaserSelect.Value == LaserSelectMode.Auto && trig != null);
            bool down = trig != null && trig.Value > 0.6f;
            bool fire = false;
            if (useTrigger) fire = down && !_triggerWasDown && id != null;
            else if (id != null && id != _selected && Time.unscaledTime - _hoverSince >= S.LaserDwellTime.Value) fire = true;
            _triggerWasDown = down;

            if (fire)
            {
                if (S.Haptics.Value) GtRefs.Vibrate(false, S.HapticAmplitude.Value, 0.05f);
                SelectPlayer(id, useTrigger ? "select gun (trigger)" : "select gun (point)");
            }
        }

        private void UpdateLaserLine()
        {
            if (_laser == null) return;
            if (!GunActive || _rh == null || !gameObject.activeSelf) { _laser.enabled = false; return; }
            var target = _hoverId != null ? RigFor(_hoverId) : null;
            Vector3 a = LaserOrigin;
            Vector3 b = target != null ? target.HeadPos : a + LaserDir * 6f;
            float w = Mathf.Max(0.001f, S.LaserWidth.Value) * (target != null ? 1.6f : 1f);
            _laser.enabled = true;
            _laser.SetPosition(0, a);
            _laser.SetPosition(1, b);
            _laser.startWidth = w;
            _laser.endWidth = w * (target != null ? 1f : 0.4f);
            _laser.sharedMaterial = target != null ? _laserHoverMat : _laserMat;
        }

        // ---- for testing ----
        internal string HoveredButtonId => _kit.Hovered?.Id;
        internal Vector3? DotPosition => _dot != null && _dot.gameObject.activeSelf ? _dot.position : (Vector3?)null;
        internal bool DotHot => _kit.Hovered != null;
        internal Vector3? ButtonWorldPos(string id) => _kit.Buttons.FirstOrDefault(x => x.Id == id)?.Go.transform.position;
        internal string ButtonLabel(string id) => _kit.Buttons.FirstOrDefault(x => x.Id == id)?.Label?.text;
        internal bool ButtonVisible(string id) => _kit.Buttons.FirstOrDefault(x => x.Id == id)?.Go.activeInHierarchy ?? false;

        private void OnDestroy()
        {
            Application.onBeforeRender -= UpdatePose;
            if (_cam != null) Destroy(_cam.gameObject);
            _kit?.Destroy();
        }
    }
}
