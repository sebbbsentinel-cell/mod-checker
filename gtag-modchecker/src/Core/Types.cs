using System;
using System.Collections.Generic;

namespace FanGame.ModChecker
{
    /// <summary>Enkel, motoruavhengig 3D-vektor (konverter fra UnityEngine.Vector3 i spillet).</summary>
    [Serializable]
    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public float HorizontalMagnitude => (float)Math.Sqrt(X * X + Z * Z);
        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;
        public override string ToString() => $"({X:0.00}, {Y:0.00}, {Z:0.00})";
    }

    /// <summary>Ett øyeblikksbilde av en spillers rig, sendt inn f.eks. hver nettverks-tick.</summary>
    public sealed class PlayerSnapshot
    {
        /// <summary>Tid i sekunder (f.eks. PhotonNetwork.Time eller Time.time). Må være monotont økende per spiller.</summary>
        public double Timestamp;
        public Vec3 BodyPosition;
        public Vec3 HeadPosition;
        public Vec3 LeftHandPosition;
        public Vec3 RightHandPosition;
        /// <summary>Spillerens skala (1 = normal).</summary>
        public float Scale = 1f;
        /// <summary>True når spilleren står/holder i en overflate (hånd eller kropp i kontakt).</summary>
        public bool Grounded;
        /// <summary>Sett til true når spillet selv flytter spilleren (respawn, portal, rundestart) så det ikke regnes som teleport.</summary>
        public bool LegitTeleport;
    }

    public enum NetworkEventType
    {
        Rpc,
        CustomEvent,
        Instantiate,
        Destroy,
        OwnershipTake,
        PropertyChange
    }

    /// <summary>En nettverkshendelse som en spiller har forårsaket.</summary>
    public sealed class NetworkEventRecord
    {
        public string PlayerId;
        public NetworkEventType Type;
        /// <summary>RPC-navn, event-kode eller prefab-navn (valgfritt).</summary>
        public string Name;
        public double Timestamp;
        /// <summary>Mål-spiller hvis relevant (f.eks. ownership-tyveri fra en annen spiller).</summary>
        public string TargetPlayerId;
    }

    public enum DetectionCategory { Property, Movement, Network }

    public enum Verdict { CLEAN, SUS, CHEATING }

    /// <summary>Ett enkelt treff fra en av sjekkene.</summary>
    public sealed class Detection
    {
        public DetectionCategory Category;
        /// <summary>Maskinlesbar kode, f.eks. PROP_CHEAT, SPEED, TELEPORT, RPC_RATE.</summary>
        public string Code;
        /// <summary>Menneskelesbar begrunnelse.</summary>
        public string Reason;
        /// <summary>Tillitsgrad 0..1.</summary>
        public float Confidence;
        /// <summary>Kort etikett (navn på mod/cheat eller sjekk).</summary>
        public string Label;

        public override string ToString() =>
            $"[{Category}/{Code}] {Label} ({Confidence:P0}) - {Reason}";
    }

    /// <summary>Valgfri spillerinfo som vises i panelet (spillet fyller inn det det har).</summary>
    public sealed class PlayerInfo
    {
        public string DisplayName;
        public int? Fps;
        public int? RefreshRateHz;
        public int? PingMs;
        /// <summary>F.eks. "Steam", "Quest", "PCVR".</summary>
        public string Platform;
        /// <summary>F.eks. "#FF0000" eller "1.0, 0.0, 0.0".</summary>
        public string Color;

        public PlayerInfo Clone() => (PlayerInfo)MemberwiseClone();
    }

    /// <summary>Samlet rapport for én spiller.</summary>
    public sealed class PlayerReport
    {
        public string PlayerId;
        public Verdict Verdict;
        /// <summary>Samlet score = 1 - Π(1 - c_i).</summary>
        public float Score;
        /// <summary>Statustekst for visning: NOT DETECTED (CLEAN), SUSPICIOUS (SUS), CHEATING.</summary>
        public string StatusText => Verdict == Verdict.CHEATING ? "CHEATING" : Verdict == Verdict.SUS ? "SUSPICIOUS" : "NOT DETECTED";
        public bool InGracePeriod;
        public double EvaluatedAt;
        public List<Detection> Detections = new List<Detection>();
        /// <summary>Kjente, ikke-juksende mods (modliste-kategori "mod"). Etiketter.</summary>
        public List<string> AllowedMods = new List<string>();
        /// <summary>Cheats: property-treff i kategori "cheat" + alle bevegelses-/nettverkstreff. Etiketter.</summary>
        public List<string> Cheats = new List<string>();
        /// <summary>Property-treff i kategori "unsure". Etiketter.</summary>
        public List<string> Unsure = new List<string>();
        public int AllowedModCount => AllowedMods.Count;
        public int CheatCount => Cheats.Count;
        public int UnsureCount => Unsure.Count;
        /// <summary>Valgfri info (FPS, Hz, ping, plattform, farge). Kan være null.</summary>
        public PlayerInfo Info;

        /// <summary>Tekstpanel i stil med mod checker-panel: info, ALLOWED MODS, CHEATS, UNSURE, verdikt og REPORT-handling.</summary>
        public string ToDisplayString()
        {
            var sb = new System.Text.StringBuilder();
            string line = new string('=', 40);
            var i = Info ?? new PlayerInfo();
            sb.AppendLine(line);
            sb.AppendLine($" {(i.DisplayName ?? PlayerId).ToUpperInvariant()}   {StatusText}  score {Score:0.00}{(InGracePeriod ? "  (grace)" : "")}");
            sb.AppendLine($" {(i.RefreshRateHz?.ToString() ?? "?")}Hz / {(i.Fps?.ToString() ?? "?")}FPS   PING {(i.PingMs?.ToString() ?? "?")}ms");
            sb.AppendLine($" PLATFORM: {(i.Platform ?? "?").ToUpperInvariant()}   COLOR: {i.Color ?? "?"}");
            sb.AppendLine(new string('-', 40));
            sb.AppendLine($" ALLOWED MODS: {AllowedModCount}");
            foreach (var m in AllowedMods) sb.AppendLine("   • " + m.ToUpperInvariant());
            sb.AppendLine($" CHEATS: {CheatCount}");
            foreach (var c in Cheats) sb.AppendLine("   • " + c.ToUpperInvariant());
            if (UnsureCount > 0)
            {
                sb.AppendLine($" UNSURE: {UnsureCount}");
                foreach (var u in Unsure) sb.AppendLine("   • " + u.ToUpperInvariant());
            }
            sb.AppendLine(new string('-', 40));
            sb.AppendLine("            [ REPORT ]");
            sb.Append(line);
            return sb.ToString();
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"{PlayerId}: {StatusText} (score {Score:0.00}{(InGracePeriod ? ", grace" : "")})");
            foreach (var d in Detections) sb.Append("\n   - ").Append(d);
            return sb.ToString();
        }
    }

    /// <summary>En ukjent property-nøkkel som ikke matchet modliste eller allowlist. Til manuell gjennomgang.</summary>
    public sealed class UnknownPropertyRecord
    {
        public string Key;
        public string SampleValue;
        public double FirstSeen;
        public double LastSeen;
        public int Count;
        public HashSet<string> PlayerIds = new HashSet<string>();
    }

    public sealed class PlayerFlaggedEventArgs : EventArgs
    {
        public PlayerReport Report;
        public Verdict PreviousVerdict;
    }
}
