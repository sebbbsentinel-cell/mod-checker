using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace GTagModChecker
{
    public enum TriggerMode { SinglePress = 1, DoublePress = 2 }

    public enum TabletAttachMode
    {
        /// <summary>Svever over venstre hånd og vender alltid mot hodet (robust, standard).</summary>
        FloatAboveHand,
        /// <summary>Låst til håndens rotasjon med TabletOffset/TabletRotation.</summary>
        HandLocked
    }

    /// <summary>Hvordan laseren velger en spiller.</summary>
    public enum LaserSelectMode
    {
        /// <summary>Høyre avtrekker hvis den kan leses, ellers pek i LaserDwellTime sekunder.</summary>
        Auto,
        Trigger,
        Dwell
    }

    public enum NametagFont { Default, Bold }

    /// <summary>Hvilken meny X åpner.</summary>
    public enum MenuStyle { Panel, Disc }

    public enum DiscTheme { Violet, Pink, Silver }

    /// <summary>
    /// GTag Mod Checker: passiv lobby-observatør for Gorilla Tag (PC).
    /// Leser andre spilleres Photon custom properties, rig-posisjoner og innkommende nettverkshendelser.
    /// Endrer ingenting i spillet og sender ingenting over nettverket.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.seb.gtagmodchecker";
        public const string Name = "GTag Mod Checker";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log;
        internal static PluginSettings Settings;
        internal static GameObject Host;

        private void Awake()
        {
            Log = Logger;
            try
            {
                Settings = new PluginSettings(Config);
                // BepInEx-pluginobjektet kan bli ødelagt i Gorilla Tag, så vi lager et eget, skjult objekt som overlever scener.
                Host = new GameObject("GTagModChecker");
                Host.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(Host);
                Host.AddComponent<CheckerBehaviour>();
                Log.LogInfo($"[GTag Mod Checker] v{Version} lastet. Trykk X (venstre kontroller) eller tasten {Settings.KeyboardKey.Value} for å sjekke lobbyen.");
            }
            catch (Exception ex)
            {
                Log.LogError("[GTag Mod Checker] Klarte ikke å starte: " + ex);
            }
        }
    }

    /// <summary>Innstillinger i BepInEx/config/com.seb.gtagmodchecker.cfg.</summary>
    internal sealed class PluginSettings
    {
        public readonly ConfigEntry<TriggerMode> TriggerMode;
        public readonly ConfigEntry<float> DoublePressWindow;
        public readonly ConfigEntry<bool> UseXRController;
        public readonly ConfigEntry<KeyCode> KeyboardKey;
        public readonly ConfigEntry<bool> Haptics;
        public readonly ConfigEntry<float> HapticAmplitude;

        public readonly ConfigEntry<bool> AutoCheck;
        public readonly ConfigEntry<float> AutoCheckInterval;
        public readonly ConfigEntry<float> PlayerSyncInterval;

        public readonly ConfigEntry<float> PanelDistance;
        public readonly ConfigEntry<float> PanelLineHeight;
        public readonly ConfigEntry<bool> PanelFollowHead;

        public readonly ConfigEntry<bool> HandheldMode;
        public readonly ConfigEntry<MenuStyle> Style;
        public readonly ConfigEntry<DiscTheme> Theme;
        public readonly ConfigEntry<float> DiscDistance, DiscScale, PlayerDiscScale;
        public readonly ConfigEntry<string> TransparentShader;
        public readonly ConfigEntry<TabletAttachMode> TabletAttach;
        public readonly ConfigEntry<float> TabletOffsetX, TabletOffsetY, TabletOffsetZ;
        public readonly ConfigEntry<float> TabletRotX, TabletRotY, TabletRotZ;
        public readonly ConfigEntry<float> TabletScale;
        public readonly ConfigEntry<float> PressRadius;
        public readonly ConfigEntry<float> PressDepth;
        public readonly ConfigEntry<bool> ShowFingerDot;
        public readonly ConfigEntry<bool> DebugTouch;
        public readonly ConfigEntry<int> ConfigVersion;
        public readonly ConfigEntry<int> SpeedSustainSamples;
        public readonly ConfigEntry<float> PressCooldown;
        public readonly ConfigEntry<float> FingerTipOffset;

        public readonly ConfigEntry<bool> LaserEnabled;
        public readonly ConfigEntry<LaserSelectMode> LaserSelect;
        public readonly ConfigEntry<float> LaserRotX, LaserRotY, LaserRotZ;
        public readonly ConfigEntry<float> LaserConeAngle, LaserMaxDistance, LaserDwellTime, LaserWidth;

        public readonly ConfigEntry<bool> Nametags, SelfTags, HideSpecTag, TagPlatforms, TagFpsHz, TagVerdict;
        public readonly ConfigEntry<NametagFont> TagFont;
        public readonly ConfigEntry<float> TagHeight, TagSize;

        public readonly ConfigEntry<bool> MovementChecks;
        public readonly ConfigEntry<bool> NetworkChecks;
        public readonly ConfigEntry<bool> LogUnknownProperties;
        public readonly ConfigEntry<float> MaxSpeed;
        public readonly ConfigEntry<float> TeleportDistance;
        public readonly ConfigEntry<float> MaxArmReach;
        public readonly ConfigEntry<CamMode> CamMode;
        public readonly ConfigEntry<int> CamFovIndex, CamSmooth;

        public PluginSettings(ConfigFile cfg)
        {
            TriggerMode = cfg.Bind("Trigger", "TriggerMode", GTagModChecker.TriggerMode.SinglePress, "SinglePress = ett trykk på X, DoublePress = to trykk innenfor DoublePressWindow.");
            DoublePressWindow = cfg.Bind("Trigger", "DoublePressWindow", 0.4f, "Sekunder mellom to trykk ved DoublePress.");
            UseXRController = cfg.Bind("Trigger", "UseXRController", true, "Les X fra venstre VR-kontroller (primaryButton).");
            KeyboardKey = cfg.Bind("Trigger", "KeyboardKey", KeyCode.X, "Tastatur-tast for å åpne/lukke panelet (testing på PC).");
            Haptics = cfg.Bind("Trigger", "Haptics", true, "Kort vibrasjon i venstre kontroller når panelet åpnes.");
            HapticAmplitude = cfg.Bind("Trigger", "HapticAmplitude", 0.4f, "Styrke på vibrasjonen (0-1).");

            AutoCheck = cfg.Bind("Check", "AutoCheck", true, "Sjekk lobbyen automatisk i bakgrunnen og logg flaggede spillere.");
            AutoCheckInterval = cfg.Bind("Check", "AutoCheckInterval", 10f, "Sekunder mellom automatiske sjekker.");
            PlayerSyncInterval = cfg.Bind("Check", "PlayerSyncInterval", 1f, "Sekunder mellom hver gang spillerliste og properties leses.");

            PanelDistance = cfg.Bind("Panel", "PanelDistance", 0.8f, "Avstand (meter) fra hodet til panelet.");
            PanelLineHeight = cfg.Bind("Panel", "PanelLineHeight", 0.022f, "Linjehøyde (meter). Øk hvis teksten er for liten.");
            PanelFollowHead = cfg.Bind("Panel", "PanelFollowHead", false, "La panelet følge hodet.");

            HandheldMode = cfg.Bind("Tablet", "HandheldMode", true, "true = sigma-meny (svart/sølv) ved venstre hånd (trykk med høyre pekefinger, laser for å velge spillere). false = gammelt flytende tekstpanel.");
            Style = cfg.Bind("Menu", "MenuStyle", MenuStyle.Panel, "Panel = sigma-meny ved venstre hånd. Disc = sølvring foran deg og rundt valgt spiller. Kan også byttes i spillet.");
            Theme = cfg.Bind("Menu", "DiscTheme", DiscTheme.Silver, "Farge på disc-menyen: Silver (sigma), Violet eller Pink.");
            DiscDistance = cfg.Bind("Menu", "DiscDistance", 0.45f, "Avstand (meter) fra hodet til innstillings-discen når den åpnes.");
            DiscScale = cfg.Bind("Menu", "DiscScale", 1f, "Størrelse på innstillings-discen.");
            PlayerDiscScale = cfg.Bind("Menu", "PlayerDiscScale", 1f, "Størrelse på spiller-discen (1 = ca. 0,9 m ring).");
            TransparentShader = cfg.Bind("Menu", "TransparentShader", "auto", "Shader for gjennomsiktig ring/glød. auto = URP Unlit (Transparent) hvis URP, ellers Sprites/Default. Hvis ringen ser helt solid ut i spillet: sett til Sprites/Default.");
            UiKit.PreferredAlphaShader = TransparentShader.Value;
            TabletAttach = cfg.Bind("Tablet", "TabletAttach", TabletAttachMode.FloatAboveHand, "FloatAboveHand = menyen svever over venstre hånd og vender mot hodet. HandLocked = låst til håndens rotasjon (bruk TabletRot*).");
            TabletOffsetX = cfg.Bind("Tablet", "TabletOffsetX", 0.0f, "Forskyvning (meter). FloatAboveHand: høyre/venstre sett fra hodet. HandLocked: håndens lokale X.");
            TabletOffsetY = cfg.Bind("Tablet", "TabletOffsetY", 0.16f, "Forskyvning (meter). FloatAboveHand: opp. HandLocked: håndens lokale Y.");
            TabletOffsetZ = cfg.Bind("Tablet", "TabletOffsetZ", 0.0f, "Forskyvning (meter). FloatAboveHand: fremover. HandLocked: håndens lokale Z.");
            TabletRotX = cfg.Bind("Tablet", "TabletRotX", 0f, "Rotasjon (grader) i HandLocked-modus.");
            TabletRotY = cfg.Bind("Tablet", "TabletRotY", 90f, "Rotasjon (grader) i HandLocked-modus.");
            TabletRotZ = cfg.Bind("Tablet", "TabletRotZ", 0f, "Rotasjon (grader) i HandLocked-modus.");
            TabletScale = cfg.Bind("Tablet", "TabletScale", 1f, "Størrelse på menyen (1 = ca. 20 x 28 cm venstre panel).");
            PressRadius = cfg.Bind("Tablet", "PressRadius", 0.006f, "Margin (meter) på hver side av en knapp: fingertuppen treffer hvis den er innenfor knappen + denne marginen.");
            PressDepth = cfg.Bind("Tablet", "PressDepth", 0.025f, "Hvor langt (meter) foran eller bak menyens plan fingertuppen kan være og fortsatt trykke.");
            ShowFingerDot = cfg.Bind("Tablet", "ShowFingerDot", true, "Vis en liten rosa/hvit prikk der fingertuppen er (blir hvit og større over en knapp).");
            DebugTouch = cfg.Bind("Tablet", "DebugTouch", true, "Skriv nærmeste knapp, avstand og inRect til loggen én gang i sekundet mens menyen er åpen.");
            PressCooldown = cfg.Bind("Tablet", "PressCooldown", 0.35f, "Sekunder mellom knappetrykk.");
            FingerTipOffset = cfg.Bind("Tablet", "FingerTipOffset", 0.0f, "Brukes bare hvis spillets fingertupp-collider ikke finnes: flytter trykkpunktet langs høyre hånds fremover-retning (meter).");

            LaserEnabled = cfg.Bind("Laser", "LaserEnabled", true, "Rosa laser fra høyre hånd for å velge en spiller i verden (vises bare mens menyen er åpen).");
            LaserSelect = cfg.Bind("Laser", "LaserSelect", LaserSelectMode.Auto, "Auto = høyre avtrekker hvis den kan leses, ellers pek i LaserDwellTime sekunder. Trigger = bare avtrekker. Dwell = bare peking.");
            LaserRotX = cfg.Bind("Laser", "LaserRotX", 90f, "Rotasjon (grader) av laserretningen i forhold til høyre hånd. 90/0/0 = langs fingrene (-up) i Gorilla Tag.");
            LaserRotY = cfg.Bind("Laser", "LaserRotY", 0f, "Se LaserRotX.");
            LaserRotZ = cfg.Bind("Laser", "LaserRotZ", 0f, "Se LaserRotX.");
            LaserConeAngle = cfg.Bind("Laser", "LaserConeAngle", 7f, "Hvor nøyaktig du må peke (grader rundt laseren) for å treffe et hode.");
            LaserMaxDistance = cfg.Bind("Laser", "LaserMaxDistance", 40f, "Maks avstand (meter) til spillere laseren kan velge.");
            LaserDwellTime = cfg.Bind("Laser", "LaserDwellTime", 0.6f, "Sekunder du må peke på en spiller for å velge den uten avtrekker.");
            LaserWidth = cfg.Bind("Laser", "LaserWidth", 0.006f, "Tykkelse på laseren (meter).");

            Nametags = cfg.Bind("Nametags", "Nametags", true, "Navnelapper over hodet til andre spillere (bare du ser dem).");
            TagFont = cfg.Bind("Nametags", "Font", NametagFont.Default, "Default eller Bold.");
            SelfTags = cfg.Bind("Nametags", "SelfTags", false, "Vis navnelapp over ditt eget hode også.");
            HideSpecTag = cfg.Bind("Nametags", "HideSpecTag", false, "Skjul spectator-lapp. Ingen effekt i denne versjonen (det finnes ingen spectator-lapp).");
            TagPlatforms = cfg.Bind("Nametags", "Platforms", true, "Vis plattform (STEAM / PC / QUEST) over hodet når den kan leses.");
            TagFpsHz = cfg.Bind("Nametags", "FpsHz", true, "Vis f.eks. 72hz over hodet.");
            TagVerdict = cfg.Bind("Nametags", "Verdict", true, "Vis NOT DETECTED / SUSPICIOUS / CHEATING i farge over hodet.");
            TagHeight = cfg.Bind("Nametags", "Height", 0.45f, "Hvor høyt over hodet (meter) navnelappen sitter.");
            TagSize = cfg.Bind("Nametags", "Size", 1f, "Størrelse på navnelappene.");

            MovementChecks = cfg.Bind("Detection", "MovementChecks", true, "Bevegelsessjekker (fart, teleport, armlengde, skala). Krever at rigger kan leses.");
            NetworkChecks = cfg.Bind("Detection", "NetworkChecks", true, "Tell innkommende RPC/instantiate/ownership-hendelser per spiller (kun lesing).");
            LogUnknownProperties = cfg.Bind("Detection", "LogUnknownProperties", true, "Logg ukjente property-nøkler til LogOutput.log (nyttig for å utvide modlista).");
            MaxSpeed = cfg.Bind("Detection", "MaxSpeed", 22f, "Maks fart (m/s, snitt over 0,5 s) før det regnes som brudd. Gode spillere er raske, så ikke sett denne for lavt.");
            SpeedSustainSamples = cfg.Bind("Detection", "SpeedSustainSamples", 6, "Hvor mange målinger på rad (10 per sekund) farten må være over MaxSpeed før det teller som ett brudd.");

            // ---- Migrering av gamle standardverdier ----
            ConfigVersion = cfg.Bind("Internal", "ConfigVersion", 0, "Ikke endre. Brukes til å oppdatere gamle standardverdier.");
            if (ConfigVersion.Value < 2)
            {
                if (Math.Abs(PressRadius.Value - 0.015f) < 1e-5f) PressRadius.Value = 0.006f;
                if (Math.Abs(MaxSpeed.Value - 20f) < 1e-4f) MaxSpeed.Value = 22f;
                ConfigVersion.Value = 2;
            }
            if (ConfigVersion.Value < 3)
            {
                // v1.1 sigma-design: disc i sølv som standard
                Theme.Value = DiscTheme.Silver;
                ConfigVersion.Value = 3;
            }
            CamMode = cfg.Bind("Camera", "Mode", GTagModChecker.CamMode.ThirdPerson, "Kameramodus: FirstPerson, ThirdPerson, Follow, TrackMonkey, Lock, GoPro.");
            CamFovIndex = cfg.Bind("Camera", "FovIndex", 2, "FOV-trinn: 0=60, 1=75, 2=90, 3=110.");
            CamSmooth = cfg.Bind("Camera", "Smooth", 2, "Utjevning: 0=OFF, 1=LOW, 2=MED, 3=HIGH.");
            TeleportDistance = cfg.Bind("Detection", "TeleportDistance", 12f, "Meter hodet kan flytte seg mellom to målinger før det regnes som teleport.");
            MaxArmReach = cfg.Bind("Detection", "MaxArmReach", 2.0f, "Maks avstand (meter, ved skala 1) fra hode til hånd.");
        }
    }
}
