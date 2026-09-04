using System;

namespace DSDeaths {
    public sealed class GameDefinition {
        internal GameDefinition(
            string processName,
            string displayName,
            int[] offsets32,
            int[] offsets64,
            bool isEldenRing) {
            ProcessName = processName;
            DisplayName = displayName;
            Offsets32 = offsets32;
            Offsets64 = offsets64;
            IsEldenRing = isEldenRing;
        }

        public string ProcessName { get; private set; }
        public string DisplayName { get; private set; }
        public bool IsEldenRing { get; private set; }
        internal int[] Offsets32 { get; private set; }
        internal int[] Offsets64 { get; private set; }
    }

    public static class GameCatalog {
        private static readonly GameDefinition[] Games =
        {
            new GameDefinition(
                "DARKSOULS",
                "DARK SOULS: Prepare To Die Edition",
                new[] {0xF78700, 0x5C},
                null,
                false),
            new GameDefinition(
                "DarkSoulsII",
                "DARK SOULS II / Scholar of the First Sin",
                new[] {0x1150414, 0x74, 0xB8, 0x34, 0x4, 0x28C, 0x100},
                new[] {0x16148F0, 0xD0, 0x490, 0x104},
                false),
            new GameDefinition(
                "DarkSoulsIII",
                "DARK SOULS III",
                null,
                new[] {0x47572B8, 0x98},
                false),
            new GameDefinition(
                "DarkSoulsRemastered",
                "DARK SOULS: REMASTERED",
                null,
                new[] {0x1C8A530, 0x98},
                false),
            new GameDefinition(
                "Sekiro",
                "Sekiro: Shadows Die Twice",
                null,
                new[] {0x3D5AAC0, 0x90},
                false),
            new GameDefinition("eldenring", "ELDEN RING", null, null, true)
        };

        public static GameDefinition[] GetSupportedGames() {
            return (GameDefinition[])Games.Clone();
        }

        internal static GameDefinition[] InternalGames {
            get { return Games; }
        }
    }
}
