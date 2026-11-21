using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.XR;

namespace GTagModChecker
{
    /// <summary>Leser X på venstre kontroller (UnityEngine.XR) og en tastatur-tast (gammel Input, ellers nytt Input System).</summary>
    internal sealed class InputReader
    {
        private InputDevice _left;
        private bool _loggedFound;
        private bool _wasPressed;
        private bool _legacyBroken, _newBroken;
        private readonly List<InputDevice> _buffer = new List<InputDevice>();

        // Gorilla Tag sin egen input-klasse (ControllerInputPoller), lest via refleksjon.
        // Under SteamVR/OpenVR gir UnityEngine.XR ofte ikke primaryButton, men spillet selv vet alltid om X er trykket.
        private bool _gtSearched, _gtLogged;
        private Type _gtType;
        private System.Reflection.MemberInfo _gtInstance, _gtButton;

        public bool LeftValid => _left.isValid;
        public string LeftName => _left.isValid ? _left.name : "-";

        public void TryAcquire()
        {
            try
            {
                var d = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                if (!d.isValid)
                {
                    _buffer.Clear();
                    InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, _buffer);
                    foreach (var c in _buffer) if (c.isValid) { d = c; break; }
                }
                _left = d;
                if (d.isValid && !_loggedFound)
                {
                    _loggedFound = true;
                    Plugin.Log.LogInfo($"[GTag Mod Checker] Venstre kontroller funnet: {d.name}");
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[GTag Mod Checker] XR-oppslag feilet: " + ex.Message); }
        }

        /// <summary>Returnerer kilde ("XR"/"keyboard") hvis knappen ble trykket ned denne rammen, ellers null.</summary>
        public string Poll(bool useXR, KeyCode key)
        {
            bool pressed = false;
            string src = null;
            if (useXR)
            {
                if (GameLeftPrimary()) { pressed = true; src = "GT input"; }
                if (!pressed)
                {
                    if (!_left.isValid) TryAcquire(); // kontrollere er ofte ikke klare ved oppstart
                    if (_left.isValid && _left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool x) && x) { pressed = true; src = "XR"; }
                }
            }
            bool edge = pressed && !_wasPressed;
            _wasPressed = pressed;
            if (edge) return src;
            return KeyDown(key) ? "keyboard" : null;
        }

        private bool GameLeftPrimary()
        {
            try
            {
                if (!_gtSearched)
                {
                    _gtSearched = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name != "Assembly-CSharp") continue;
                        _gtType = asm.GetType("ControllerInputPoller");
                        break;
                    }
                    if (_gtType != null)
                    {
                        const System.Reflection.BindingFlags S = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
                        const System.Reflection.BindingFlags I = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        _gtInstance = (System.Reflection.MemberInfo)_gtType.GetField("instance", S) ?? _gtType.GetProperty("instance", S);
                        _gtButton = (System.Reflection.MemberInfo)_gtType.GetField("leftControllerPrimaryButton", I) ?? _gtType.GetProperty("leftControllerPrimaryButton", I);
                    }
                    Plugin.Log.LogInfo($"[GTag Mod Checker] Gorilla Tag input: type={(_gtType != null)}, instance={(_gtInstance != null)}, leftPrimary={(_gtButton != null)}");
                }
                if (_gtInstance == null || _gtButton == null) return false;
                object inst = _gtInstance is System.Reflection.FieldInfo fi ? fi.GetValue(null) : ((System.Reflection.PropertyInfo)_gtInstance).GetValue(null, null);
                if (inst == null) return false;
                object v = _gtButton is System.Reflection.FieldInfo bf ? bf.GetValue(inst) : ((System.Reflection.PropertyInfo)_gtButton).GetValue(inst, null);
                return v is bool b && b;
            }
            catch (Exception ex)
            {
                if (!_gtLogged) { _gtLogged = true; Plugin.Log.LogWarning("[GTag Mod Checker] Kunne ikke lese Gorilla Tag input: " + ex.Message); }
                _gtInstance = null;
                return false;
            }
        }

        // ------------------------------------------------------------------
        //  Høyre avtrekker (for laser-valg). Kun lesing av dine egne kontrollere.
        // ------------------------------------------------------------------
        private bool _rtSearched, _rtLogged;
        private System.Reflection.MemberInfo _rtFloat, _rtGrab;
        private InputDevice _right;

        /// <summary>Hvor mye høyre avtrekker er trykket (0-1), eller null hvis den ikke kan leses.</summary>
        public float? RightTrigger()
        {
            try
            {
                if (!_rtSearched)
                {
                    _rtSearched = true;
                    if (!_gtSearched) GameLeftPrimary(); // finner ControllerInputPoller
                    if (_gtType != null)
                    {
                        const System.Reflection.BindingFlags I = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        _rtFloat = (System.Reflection.MemberInfo)_gtType.GetField("rightControllerIndexFloat", I) ?? _gtType.GetProperty("rightControllerIndexFloat", I);
                        _rtGrab = (System.Reflection.MemberInfo)_gtType.GetField("rightGrab", I) ?? _gtType.GetProperty("rightGrab", I);
                    }
                    Plugin.Log.LogInfo($"[GTag Mod Checker] Høyre avtrekker: indexFloat={(_rtFloat != null)}, rightGrab={(_rtGrab != null)}");
                }
                if (_gtInstance != null && (_rtFloat != null || _rtGrab != null))
                {
                    object inst = _gtInstance is System.Reflection.FieldInfo fi ? fi.GetValue(null) : ((System.Reflection.PropertyInfo)_gtInstance).GetValue(null, null);
                    if (inst != null)
                    {
                        if (_rtFloat != null)
                        {
                            object v = _rtFloat is System.Reflection.FieldInfo ff ? ff.GetValue(inst) : ((System.Reflection.PropertyInfo)_rtFloat).GetValue(inst, null);
                            if (v is float f) return f;
                        }
                        if (_rtGrab != null)
                        {
                            object v = _rtGrab is System.Reflection.FieldInfo gf ? gf.GetValue(inst) : ((System.Reflection.PropertyInfo)_rtGrab).GetValue(inst, null);
                            if (v is bool b) return b ? 1f : 0f;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_rtLogged) { _rtLogged = true; Plugin.Log.LogWarning("[GTag Mod Checker] Kunne ikke lese høyre avtrekker fra spillet: " + ex.Message); }
                _rtFloat = null; _rtGrab = null;
            }
            try
            {
                if (!_right.isValid) _right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if (_right.isValid && _right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float t)) return t;
            }
            catch { }
            return null;
        }

        private bool KeyDown(KeyCode key)
        {
            if (key == KeyCode.None) return false;
            if (!_legacyBroken)
            {
                try { return Input.GetKeyDown(key); }
                catch (Exception ex)
                {
                    // Kastes når spillet kun bruker nytt Input System.
                    _legacyBroken = true;
                    Plugin.Log.LogInfo("[GTag Mod Checker] Gammel Input er ikke aktiv, bruker nytt Input System for tastatur (" + ex.GetType().Name + ")");
                }
            }
            if (!_newBroken)
            {
                try { return NewInputKeyDown(key); }
                catch (Exception ex)
                {
                    _newBroken = true;
                    Plugin.Log.LogWarning("[GTag Mod Checker] Tastatur-fallback utilgjengelig: " + ex.Message);
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool NewInputKeyDown(KeyCode key)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return false;
            if (!Enum.TryParse(key.ToString(), true, out UnityEngine.InputSystem.Key k)) k = UnityEngine.InputSystem.Key.X;
            return kb[k].wasPressedThisFrame;
        }

        public void Haptic(float amplitude, float duration)
        {
            try
            {
                if (!_left.isValid) TryAcquire();
                if (_left.isValid && _left.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                    _left.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[GTag Mod Checker] Haptikk feilet: " + ex.Message); }
        }
    }
}
