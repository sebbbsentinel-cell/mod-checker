using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GTagModChecker
{
    /// <summary>
    /// SPOTIFY-fanen: sender Windows medietaster (play/pause, neste, forrige, volum) med user32 keybd_event,
    /// akkurat som tastene på et tastatur. Lyden spilles av Spotify på PC-en (din standard lydenhet / headsettet)
    /// og går ALDRI inn i spillets mikrofon eller voice chat; pluginen rører ikke Photon Voice.
    /// "Spilles nå" leses fra tittelen på Spotify-vinduet (Spotify skriver "Artist - Sang" der mens musikken spiller).
    /// </summary>
    internal static class MediaKeys
    {
        private const byte VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0, VK_MEDIA_PREV_TRACK = 0xB1, VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;

        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);

        public static bool PlayPause() => Send(VK_MEDIA_PLAY_PAUSE, "PLAY/PAUSE");
        public static bool Next() => Send(VK_MEDIA_NEXT_TRACK, "NEXT");
        public static bool Previous() => Send(VK_MEDIA_PREV_TRACK, "PREVIOUS");
        public static bool VolumeUp() => Send(VK_VOLUME_UP, "VOL+");
        public static bool VolumeDown() => Send(VK_VOLUME_DOWN, "VOL-");
        public static bool Mute() => Send(VK_VOLUME_MUTE, "MUTE");

        private static bool Send(byte vk, string what)
        {
            try
            {
                keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                Plugin.Log.LogInfo("[GTag Mod Checker] Spotify/medietast: " + what);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[GTag Mod Checker] Medietast feilet (" + what + "): " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ "spilles nå"

        private static volatile string _nowPlaying = "";
        private static volatile bool _spotifyRunning;
        private static int _busy;
        private static DateTime _next = DateTime.MinValue;
        private static readonly EnumWindowsProc _cb = OnWindow; // holdes i live (GC)
        private static HashSet<uint> _pids;
        private static string _found;

        /// <summary>"ARTIST - SANG", tom hvis pauset/ukjent.</summary>
        public static string NowPlaying => _nowPlaying;
        public static bool SpotifyRunning => _spotifyRunning;

        /// <summary>Kalles fra Update; leser vindustittelen i bakgrunnen maks hvert 2. sekund.</summary>
        public static void Poll()
        {
            if (DateTime.UtcNow < _next) return;
            _next = DateTime.UtcNow.AddSeconds(2);
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { ReadTitle(); }
                catch { _nowPlaying = ""; }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }

        private static void ReadTitle()
        {
            var procs = Process.GetProcessesByName("Spotify");
            _spotifyRunning = procs.Length > 0;
            if (procs.Length == 0) { _nowPlaying = ""; return; }
            _pids = new HashSet<uint>();
            foreach (var p in procs) { try { _pids.Add((uint)p.Id); } catch { } finally { p.Dispose(); } }
            _found = null;
            EnumWindows(_cb, IntPtr.Zero);
            string t = _found ?? "";
            // Når Spotify er pauset heter vinduet bare "Spotify", "Spotify Premium" eller "Spotify Free".
            if (t.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase) && !t.Contains(" - ")) t = "";
            _nowPlaying = t;
        }

        private static bool OnWindow(IntPtr hWnd, IntPtr lParam)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (_pids == null || !_pids.Contains(pid)) return true;
                int len = GetWindowTextLength(hWnd);
                if (len <= 0) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string t = sb.ToString();
                if (string.IsNullOrEmpty(t) || t == "Default IME" || t == "MSCTFIME UI" || t.StartsWith("GDI+")) return true;
                if (_found == null || (t.Contains(" - ") && !_found.Contains(" - "))) _found = t;
            }
            catch { }
            return true;
        }
    }
}
