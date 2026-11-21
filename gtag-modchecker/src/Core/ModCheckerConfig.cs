namespace FanGame.ModChecker
{
    /// <summary>Alle terskler kan justeres. Verdier er i meter/sekunder og må tilpasses ditt spill.</summary>
    public sealed class ModCheckerConfig
    {
        // ---- Verdikt ----
        public float SusThreshold = 0.40f;
        public float CheatingThreshold = 0.85f;

        // ---- Falske positiver ----
        /// <summary>Sekunder etter join der bevegelses- og nettverkssjekker ignoreres.</summary>
        public double JoinGracePeriod = 5.0;
        /// <summary>Antall brudd innenfor ViolationWindow som kreves før et bevegelses-/nettverkstreff rapporteres.</summary>
        public int MinViolationSamples = 3;
        /// <summary>Hvor lenge (sek) et brudd teller.</summary>
        public double ViolationWindow = 10.0;
        /// <summary>Snapshots med større tidsgap enn dette sammenlignes ikke (lagg/pakketap).</summary>
        public double MaxSnapshotGap = 0.5;
        /// <summary>Hvor mange snapshots som lagres per spiller.</summary>
        public int SnapshotHistory = 120;

        // ---- Bevegelse ----
        public float MaxSpeed = 12f;              // m/s
        public float TeleportDistance = 4f;       // m per snapshot
        /// <summary>Farten måles som snitt over minst så mange sekunder (glatter ut nettverks-hakk der posisjonen står stille og så hopper).</summary>
        public double SpeedWindow = 0.5;
        /// <summary>Så mange målinger på rad må være over MaxSpeed før ett SPEED-brudd registreres (korte topper ignoreres).</summary>
        public int SpeedSustainSamples = 6;
        /// <summary>Ett enkelt hopp større enn dette (m) mellom to målinger regnes som respawn/portal: fartssjekken pauses.
        /// Mindre lagg-hopp håndteres av snittet over SpeedWindow og kravet om flere målinger på rad.</summary>
        public float SpeedJumpIgnore = 6f;
        /// <summary>Så mange slike hopp innenfor SpeedJumpWindow er ikke respawn lenger, men regnes som SPEED-brudd.</summary>
        public int SpeedRepeatedJumps = 3;
        public double SpeedJumpWindow = 2.0;
        /// <summary>Sekunder fartssjekken er av etter et slikt hopp eller en LegitTeleport.</summary>
        public double SpeedSuppressAfterJump = 1.5;
        public float MaxAirtime = 4f;             // sek uten bakkekontakt
        public float FlyMinRise = -0.5f;          // hvis Y ikke har sunket mer enn dette i lufta => flyging
        public float MaxArmReach = 1.6f;          // m fra hode til hånd ved skala 1
        public float MinScale = 0.9f;
        public float MaxScale = 1.1f;
        public float MaxTagDistance = 2.0f;       // m fra taggerens hånd til målets kropp ved skala 1

        // ---- Nettverk (per RateWindow sekunder) ----
        public double RateWindow = 1.0;
        public int MaxRpcPerWindow = 30;
        public int MaxCustomEventsPerWindow = 30;
        public int MaxInstantiatePerWindow = 5;
        public int MaxDestroyPerWindow = 5;
        public int MaxOwnershipTakesPerWindow = 3;
        public int MaxPropertyChangesPerWindow = 10;
        /// <summary>Rapporter RPC-navn som ikke står i allowedRpcs (hvis listen er satt).</summary>
        public bool FlagUnknownRpcs = true;
        public float UnknownRpcConfidence = 0.5f;

        // ---- Confidence per sjekk (grunnverdi; øker med antall brudd) ----
        public float SpeedConfidence = 0.45f;
        public float TeleportConfidence = 0.6f;
        public float FlyConfidence = 0.6f;
        public float ArmReachConfidence = 0.55f;
        public float ScaleConfidence = 0.5f;
        public float TagRangeConfidence = 0.6f;
        public float RateConfidence = 0.5f;
        /// <summary>Hvor mye confidence øker per ekstra brudd over MinViolationSamples.</summary>
        public float ConfidencePerExtraViolation = 0.05f;
        public float MaxHeuristicConfidence = 0.95f;
    }
}
