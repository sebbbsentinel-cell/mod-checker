using System;
using System.Linq;
using System.Reflection;
using Photon.Pun;

namespace GTagModChecker
{
    /// <summary>
    /// WORLD SETTINGS: lokal regn og tid på døgnet via refleksjon på spillets BetterDayNightManager.
    /// Bruker KUN de lokale metodene SetTimeOfDay / ClearTimeOfDay / SetFixedWeather / ClearFixedWeather
    /// (aldri *Networked-variantene, aldri RPC). Endrer bare det DU ser.
    ///
    /// Sikkerhet: spillet sender selv tid/vær videre til nye spillere hvis du er master client OG har abonnement.
    /// Derfor slår vi av våre lokale overstyringer automatisk så lenge du er master client i et rom, så ingenting
    /// kan nå andre spillere. Finnes ikke klassen/metodene i denne versjonen, vises knappene som N/A.
    /// </summary>
    internal static class WorldSettings
    {
        private static Type _type, _weatherEnum;
        private static bool _looked;
        private static bool _rainFlag;
        private static int? _timeFlag;

        /// <summary>Leser spillets eget flagg (overrideWeather), så det stemmer også etter at spillet nullstiller ved rom-bytte.</summary>
        public static bool RainOn
        {
            get { try { var i = Instance; var f = _type?.GetField("overrideWeather", All); if (i != null && f != null) return (bool)f.GetValue(i); } catch { } return _rainFlag; }
            private set { _rainFlag = value; }
        }

        /// <summary>Tiden vi satte, eller null hvis spillet går på vanlig tid (currentSetting = Normal).</summary>
        public static int? LastSetTime
        {
            get
            {
                try
                {
                    var i = Instance; var f = _type?.GetField("currentSetting", All);
                    if (i != null && f != null && f.GetValue(i).ToString() == "Normal") return null;
                }
                catch { }
                return _timeFlag;
            }
            private set { _timeFlag = value; }
        }
        public static string BlockReason { get; private set; }

        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static void Lookup()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = null;
                    try { t = asm.GetType("BetterDayNightManager", false); } catch { }
                    if (t != null) { _type = t; break; }
                }
                if (_type != null)
                {
                    _weatherEnum = _type.GetNestedTypes(All).FirstOrDefault(n => n.IsEnum && n.Name == "WeatherType");
                    Plugin.Log.LogInfo("[GTag Mod Checker] WORLD: fant BetterDayNightManager (tid=" + (M("SetTimeOfDay") != null) + ", regn=" + (M("SetFixedWeather") != null) + ")");
                }
                else Plugin.Log.LogWarning("[GTag Mod Checker] WORLD: fant ikke BetterDayNightManager; regn/tid er ikke tilgjengelig.");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[GTag Mod Checker] WORLD: oppslag feilet: " + ex.Message); }
        }

        private static MethodInfo M(string name) => _type?.GetMethods(All).FirstOrDefault(m => m.Name == name && !m.IsStatic);

        private static object Instance
        {
            get
            {
                Lookup();
                if (_type == null) return null;
                try
                {
                    var f = _type.GetField("instance", All);
                    var v = f?.GetValue(null);
                    if (v != null) return v;
                    return UnityEngine.Object.FindObjectOfType(_type);
                }
                catch { return null; }
            }
        }

        public static bool TimeAvailable => Instance != null && M("SetTimeOfDay") != null;
        public static bool RainAvailable => Instance != null && _weatherEnum != null && M("SetFixedWeather") != null;

        /// <summary>Master client i et rom: da kan spillet selv videresende tid/vær, så vi lar det være.</summary>
        private static bool Unsafe
        {
            get
            {
                try { return PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient; } catch { return false; }
            }
        }

        private static object[] Args(MethodInfo m, params object[] first)
        {
            var ps = m.GetParameters();
            var a = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++) a[i] = i < first.Length ? first[i] : (ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
            if (ps.Length > first.Length && ps[ps.Length - 1].ParameterType == typeof(bool)) a[ps.Length - 1] = true; // forceUpdate
            return a;
        }

        private static bool Call(string name, params object[] args)
        {
            var inst = Instance; var m = M(name);
            if (inst == null || m == null) return false;
            try { m.Invoke(inst, Args(m, args)); return true; }
            catch (Exception ex) { Plugin.Log.LogWarning("[GTag Mod Checker] WORLD: " + name + " feilet: " + (ex.InnerException ?? ex).Message); return false; }
        }

        /// <summary>Antall tider på døgnet (timeOfDayRange), ellers 10.</summary>
        public static int TimeCount
        {
            get
            {
                try
                {
                    var inst = Instance;
                    var p = _type?.GetProperty("timeOfDayRange", All);
                    object v = p != null ? p.GetValue(inst, null) : _type?.GetField("timeOfDayRange", All)?.GetValue(inst);
                    if (v is Array a && a.Length > 0) return a.Length;
                }
                catch { }
                return 10;
            }
        }

        public static int? CurrentTime
        {
            get
            {
                try { var inst = Instance; var f = _type?.GetField("currentTimeIndex", All); if (inst != null && f != null) return Convert.ToInt32(f.GetValue(inst)); }
                catch { }
                return LastSetTime;
            }
        }

        /// <summary>Spillets navn på en tid (f.eks. "10am"), eller null.</summary>
        public static string TimeName(int index)
        {
            try
            {
                var inst = Instance;
                if (inst != null && _type.GetField("dayNightLightmapNames", All)?.GetValue(inst) is string[] names && index >= 0 && index < names.Length) return names[index];
            }
            catch { }
            return null;
        }

        public static bool SetTime(int index)
        {
            if (Unsafe) { BlockReason = "MASTER CLIENT: OFF"; return false; }
            int n = TimeCount;
            index = ((index % n) + n) % n;
            if (!Call("SetTimeOfDay", index)) return false;
            LastSetTime = index; BlockReason = null;
            Plugin.Log.LogInfo("[GTag Mod Checker] WORLD: tid -> " + index + " (" + (TimeName(index) ?? "?") + ", kun lokalt)");
            return true;
        }

        public static bool ResetTime()
        {
            if (!Call("ClearTimeOfDay")) return false;
            LastSetTime = null;
            return true;
        }

        public static bool ToggleRain()
        {
            if (!RainAvailable) return false;
            if (!RainOn && Unsafe) { BlockReason = "MASTER CLIENT: OFF"; return false; }
            bool ok;
            if (!RainOn)
            {
                object rain = Enum.Parse(_weatherEnum, Enum.GetNames(_weatherEnum).FirstOrDefault(n => n.StartsWith("Rain", StringComparison.OrdinalIgnoreCase)) ?? "Raining");
                ok = Call("SetFixedWeather", rain);
            }
            else ok = Call("ClearFixedWeather");
            if (ok) { RainOn = !RainOn; BlockReason = null; Plugin.Log.LogInfo("[GTag Mod Checker] WORLD: regn " + (RainOn ? "PÅ" : "AV") + " (kun lokalt)"); }
            return ok;
        }

        /// <summary>Kalles jevnlig: blir du master client, fjernes våre lokale overstyringer med en gang.</summary>
        public static void Guard()
        {
            if ((!RainOn && LastSetTime == null) || !Unsafe) return;
            Plugin.Log.LogInfo("[GTag Mod Checker] WORLD: du er master client, fjerner lokal regn/tid så ingenting sendes videre.");
            if (RainOn && Call("ClearFixedWeather")) RainOn = false;
            if (LastSetTime != null) ResetTime();
            BlockReason = "MASTER CLIENT: OFF";
        }
    }
}
