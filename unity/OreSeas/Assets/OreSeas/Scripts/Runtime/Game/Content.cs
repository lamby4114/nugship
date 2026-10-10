using System;
using UnityEngine;

namespace OreSeas.Game
{
    public enum BiomeId { Sunset, Night, Storm, Fog, Frozen, Volcanic }

    public sealed class BiomeDef
    {
        public BiomeId id; public string name, key; public SeaPalette sea, map; public string weather; public string music = "music_battle_calm";
    }

    /// <summary>Six seas (spec: day/night tint + weather layers).</summary>
    public static class Biomes
    {
        public static readonly BiomeDef[] All =
        {
            new BiomeDef { id = BiomeId.Sunset, name = "SUNSET COVE", key = "sunset", sea = SeaPalette.Open, map = SeaPalette.Map, weather = "" },
            new BiomeDef { id = BiomeId.Night, name = "MOONLIT REEF", key = "night", sea = new SeaPalette("142238","0e1828","1e3450","2a4664","8aa0c8","d6e0f0",20,4), map = new SeaPalette("2a4a62","1e3a50","3a6078","5a7e96","7e9cb4","d6e0f0",16,3), weather = "stars" },
            new BiomeDef { id = BiomeId.Storm, name = "STORM STRAIT", key = "storm", sea = SeaPalette.Storm, map = new SeaPalette("34586a","263e4e","46707e","5e8696","8aa8b4","d0dce4",14,8), weather = "rain" },
            new BiomeDef { id = BiomeId.Fog, name = "FOG BANKS", key = "fog", sea = new SeaPalette("3e5260","32444e","52687a","687e8e","a4b4bc","e8ecf0",20,3), map = new SeaPalette("4e6e78","40606a","648690","80a0a8","a4bcc0","e8ecf0",16,3), weather = "fog" },
            new BiomeDef { id = BiomeId.Frozen, name = "FROZEN FJORD", key = "frozen", sea = new SeaPalette("2a4a6a","1e3a58","3a6488","5a84a8","b8d0e4","ffffff",22,3), map = new SeaPalette("5a8aa4","4a7894","78a4bc","98bcd0","c8dcec","ffffff",16,3), weather = "snow" },
            new BiomeDef { id = BiomeId.Volcanic, name = "VOLCANIC ISLES", key = "volcanic", sea = new SeaPalette("2a1e24","1e1418","3e2a2e","5a3a34","c86a4a","e8c8b0",18,5), map = new SeaPalette("5a5052","4a4042","6e6064","8a7a7a","c89a7a","e8dcd0",16,4), weather = "ash" },
        };
        public static BiomeDef Get(BiomeId id) => All[(int)id];
    }

    public sealed class CaptainDef
    {
        public string id, name, title; public AiStyle ai; public BiomeId biome; public int skulls; public int bonusShots;
        public string[] taunt, winLine, loseLine;
    }

    /// <summary>Solo campaign: a chart of islands, each with an AI captain personality (spec: Barnacle, Admiral Brine, Mad Molly, Ghost Captain, The Kraken Cult).</summary>
    public static class Captains
    {
        public static readonly CaptainDef[] Campaign =
        {
            new CaptainDef { id = "barnacle", name = "BARNACLE", title = "SUNSET COVE", ai = AiStyle.RandomHunter, biome = BiomeId.Sunset, skulls = 1, bonusShots = -1,
                taunt = new[] { "Arr, fire where ye like!", "I aim with me eyes shut." }, winLine = new[] { "Beginner's luck... mine!" }, loseLine = new[] { "Me poor barnacles!" } },
            new CaptainDef { id = "brine", name = "ADMIRAL BRINE", title = "MOONLIT REEF", ai = AiStyle.HuntTargetParity, biome = BiomeId.Night, skulls = 2,
                taunt = new[] { "Every other square, sailor.", "Discipline sinks fleets." }, winLine = new[] { "By the book. As always." }, loseLine = new[] { "Most irregular!" } },
            new CaptainDef { id = "molly", name = "MAD MOLLY", title = "STORM STRAIT", ai = AiStyle.HuntTargetCenter, biome = BiomeId.Storm, skulls = 3,
                taunt = new[] { "Straight down the middle!", "Once I smell blood, I bite." }, winLine = new[] { "HA! Into the deep!" }, loseLine = new[] { "Next storm, ye're mine." } },
            new CaptainDef { id = "ghost", name = "GHOST CAPTAIN", title = "FOG BANKS", ai = AiStyle.ProbabilityDensity, biome = BiomeId.Fog, skulls = 4,
                taunt = new[] { "I see through the fog...", "...and through ye." }, winLine = new[] { "Join my crew. Forever." }, loseLine = new[] { "The mist... fades..." } },
            new CaptainDef { id = "cult", name = "KRAKEN CULT", title = "VOLCANIC ISLES", ai = AiStyle.ProbabilityDensity, biome = BiomeId.Volcanic, skulls = 5, bonusShots = 1,
                taunt = new[] { "The Deep favours us.", "Our salvos never end." }, winLine = new[] { "Feed the Kraken!" }, loseLine = new[] { "The Deep... abandons us?" } },
        };

        /// <summary>Daily challenge: date-seeded captain + sea (the Frozen Fjord unless the seed rolls otherwise).</summary>
        public static CaptainDef Daily(DateTime utcDate, out int seed)
        {
            seed = utcDate.Year * 10000 + utcDate.Month * 100 + utcDate.Day;
            var rng = new System.Random(seed);
            var b = Campaign[rng.Next(Campaign.Length)];
            return new CaptainDef { id = b.id, name = b.name, title = "DAILY TIDE", ai = (AiStyle)rng.Next(1, 4), biome = BiomeId.Frozen, skulls = 3, bonusShots = 0,
                taunt = new[] { "Today's tide, today's foe.", "One shot at glory!" }, winLine = b.winLine, loseLine = b.loseLine };
        }

        public static CaptainDef Rival(string name) => new CaptainDef { id = "barnacle", name = name, title = "RANKED 1V1", ai = AiStyle.HuntTargetParity, biome = BiomeId.Sunset, skulls = 3,
            taunt = new[] { "Fleet sealed. Let's duel." }, winLine = new[] { "GG, captain." }, loseLine = new[] { "GG. Well fought." } };
    }
}

