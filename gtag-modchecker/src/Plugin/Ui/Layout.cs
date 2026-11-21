namespace GTagModChecker
{
    /// <summary>
    /// Mål for sigma-menyen, i "layout-enheter" fra de godkjente preview-bildene (origo = panelets øverste venstre hjørne, y nedover).
    /// U = meter per enhet. tools/render_preview.py leser disse tallene direkte, så preview-bildene viser nøyaktig samme oppsett.
    /// Bare rene tall her (ingen uttrykk), så Python-skriptet kan lese dem.
    /// </summary>
    internal static class Layout
    {
        public const float U = 0.00066667f;          // 300 enheter = 0,20 m

        // ---- venstre panel (LOBBY og de andre fanene) ----
        public const float PW = 300f, PH = 430f, PR = 18f, Edge = 1.6f;
        public const float TopIconX0 = 22f, TopIconStep = 44f, TopIconY = 20f, TopIconW = 36f, TopIconH = 30f;
        public const float BX0 = 22f, BX1 = 278f;
        public const float PlayerBtnW = 120f, PlayerBtnH = 26f, PlayerRowY0 = 64f, PlayerRowStep = 34f, PlayerFont = 8f;
        public const int PlayerRows = 5;
        public const float LobbyStatusY = 238f, LobbyStatusFont = 5.5f;
        public const float GunX0 = 28f, GunX1 = 272f, GunY0 = 318f, GunY1 = 358f, GunFont = 13f;
        public const float RowY0 = 62f, RowStep = 42f, RowH = 34f, RowFont = 11f, CheckerRowFont = 10f;
        public const float TitleY = 390f, TitleFont = 26f, TitleFontLong = 15f, FooterY = 416f, FooterFont = 5f;
        public const float NoteY = 282f, NoteFont = 6f;

        // ---- WORLD SETTINGS ----
        public const float RainY0 = 60f, RainH = 32f, TimeY0 = 102f, TimeH = 30f, TimeBtnW = 30f, TimeFont = 9f, WorldRowY0 = 142f, WorldFont = 12f;

        // ---- SPOTIFY ----
        public const float NowPlayingY = 250f, TrackY = 266f, PrivateY = 290f, SpotifyFont = 7f, PrivateFont = 6f;

        // ---- fanestripe (høyre side av panelet) ----
        public const float StripGap = 12f, StripY = 34f, StripW = 40f, TabStep = 36f, TabPad = 6f, TabSize = 28f, StripR = 12f;
        public const int TabCount = 7;

        // ---- spillerpanel ----
        public const float QX = 400f, QY = 20f, QW = 250f, QH = 300f;
        public const float PortraitX0 = 16f, PortraitY0 = 16f, PortraitSize = 84f, PortraitArt = 60f;
        public const float InfoX = 108f, InfoY0 = 22f, InfoStep = 13f, InfoFont = 7.5f, JoinedY = 70f, JoinedValY = 83f, StatusY = 96f;
        public const float VolY0 = 112f, VolBtn = 26f, VolLabelX = 113f, VolLabelFont = 9f, VolValueFont = 6f;
        public const float ReportY0 = 150f, ReportY1 = 182f, ReportFont = 13f;
        public const float CloneY0 = 192f, CloneY1 = 222f, CloneFont = 10f;
        public const float CountY0 = 234f, CountY1 = 266f, CountFont = 9f;
        public const float PFooterY = 284f, PFooterFont = 5.2f;
        public const float ListY0 = 112f, ListFont = 6f;

        // ---- kameranettbrett ----
        public const float CamScale = 0.8f;
        public const float CW = 700f, CH = 430f, CBodyY0 = 18f, CBodyY1 = 310f, CR = 18f;
        public const float StemX0 = 235f, StemX1 = 465f, StemY0 = 290f, StemR = 14f;
        public const float HandleW = 50f, HandleX0 = 16f, HandleY0 = 44f, HandleY1 = 250f;
        public const float CGridX0 = 78f, CGridDX = 34f, CGridY0 = 58f, CGridDY = 42f, CGridSize = 28f;
        public const float ScreenX0 = 152f, ScreenY0 = 36f, ScreenX1 = 610f, ScreenY1 = 234f, ScreenInset = 8f, ScreenR = 12f;
        public const float ScreenBtn = 22f;
        public const float ModeW = 62f, ModeGap = 10f, ModeY0 = 250f, ModeY1 = 292f, ModeFont = 6.5f;
        public const float RecTextX = 310f, RecY0 = 335f, RecStep = 15f, RecFont = 8f, RecSubY = 385f;
        public const float FolderX = 396f, FolderY = 348f, RecBtnX = 438f, RecBtnY = 346f, RecBtnSize = 36f;
    }
}
