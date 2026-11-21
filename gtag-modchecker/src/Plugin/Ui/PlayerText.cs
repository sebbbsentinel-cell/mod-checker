using FanGame.ModChecker;
using UnityEngine;

namespace GTagModChecker
{
    /// <summary>Tekst om en spiller som både menyen og navnelappene bruker. Viser aldri oppdiktede verdier.</summary>
    internal static class PlayerText
    {
        public const string NotFound = "NOT FOUND";

        public static string Name(PlayerReport r, int max = 14) =>
            UiKit.Trunc(PhotonFeed.Sanitize(r?.Info?.DisplayName) ?? r?.PlayerId ?? "?", max);

        /// <summary>
        /// Plattform: fra spillerens properties hvis de har den. Ellers fra kosmetikk-strengen i riggen:
        /// "S. FIRST LOGIN" finnes bare på Steam-kontoer, "FIRST LOGIN" på PC-kontoer (kjent metode i GT-modmiljøet).
        /// Ellers null (vises som NOT FOUND).
        /// </summary>
        public static string Platform(PlayerReport r, RigInfo rig)
        {
            string p = PhotonFeed.Sanitize(r?.Info?.Platform);
            if (!string.IsNullOrEmpty(p)) return p.ToUpperInvariant();
            string c = rig?.Cosmetics;
            if (!string.IsNullOrEmpty(c))
            {
                if (c.Contains("S. FIRST LOGIN")) return "STEAM";
                if (c.Contains("FIRST LOGIN")) return "PC";
            }
            return null;
        }

        public static int? Fps(PlayerReport r, RigInfo rig) => rig?.Fps ?? r?.Info?.Fps;

        /// <summary>Gorilla Tag sin 0-9-skala, f.eks. "5 0 0". Null hvis fargen ikke kan leses.</summary>
        public static string Color09(RigInfo rig, PlayerReport r)
        {
            Color c;
            if (rig?.Color != null) c = rig.Color.Value;
            else if (!string.IsNullOrEmpty(r?.Info?.Color) && ColorUtility.TryParseHtmlString(r.Info.Color, out Color parsed)) c = parsed;
            else return null;
            return $"{To9(c.r)} {To9(c.g)} {To9(c.b)}";
        }

        public static Color? RawColor(RigInfo rig, PlayerReport r)
        {
            if (rig?.Color != null) return rig.Color.Value;
            if (!string.IsNullOrEmpty(r?.Info?.Color) && ColorUtility.TryParseHtmlString(r.Info.Color, out Color parsed)) return parsed;
            return null;
        }

        private static int To9(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 9f), 0, 9);
    }
}
