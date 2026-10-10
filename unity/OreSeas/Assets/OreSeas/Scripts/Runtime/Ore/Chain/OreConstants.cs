// OreSeas.Ore (csharp/OreSeas.Ore) under OreSeas.Ore.Chain. Read-only: account decoders / PDAs / constants.
// Needs Solana.Unity.Wallet (Solana.Unity-SDK package) -> compiled only when ORESEAS_SOLANA is defined.
#if ORESEAS_SOLANA
// ORE v3 constants (mirrors regolith-labs/ore api/src/consts.rs @48c203b). Unity: drop into Assets/Scripts/Ore.
using Solana.Unity.Wallet;

namespace OreSeas.Ore.Chain
{
    public static class OreConstants
    {
        public static readonly PublicKey ProgramId = new PublicKey("oreV3EG1i9BEgiAJ8b177Z2S2rMarzak4NMv1kULvWv");
        public static readonly PublicKey EntropyProgramId = new PublicKey("3jSkUuYBoJzQPMEzTvkDFXCZUBksPamrVhrnHR9igu2X");
        public static readonly PublicKey SystemProgramId = new PublicKey("11111111111111111111111111111111");
        public static readonly PublicKey TokenProgramId = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        public static readonly PublicKey AssociatedTokenProgramId = new PublicKey("ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL");

        public static readonly PublicKey Mint = new PublicKey("oreoU2P8bN6jkk3jbaiVxYnG1dCXcYxwhwyK9jSybcp");
        public static readonly PublicKey BoardAddress = new PublicKey("BrcSxdp1nXFzou1YyDnQJcPNBNHgoypZmTsyKBSLLXzi");
        public static readonly PublicKey ConfigAddress = new PublicKey("9c9X7aDRAF41faiDs94ELjT19UrGnn72wBW9hPsS4Awy");
        public static readonly PublicKey TreasuryAddress = new PublicKey("45db2FSR4mcXdSVVZbKbwojU6uYDpMyhpEi7cC8nHaWG");
        public static readonly PublicKey VarAddress = new PublicKey("BWCaDY96Xe4WkFq1M7UiCCRcChsJ3p51L5KrGzhxgm2E");
        public static readonly PublicKey SplitAddress = new PublicKey("SpLiT11111111111111111111111111111111111112");
        public static readonly PublicKey ExecutorAddress = new PublicKey("executor11111111111111111111111111111111112");

        public const int TokenDecimals = 11;
        public const ulong OneOre = 100_000_000_000UL;
        public const ulong CheckpointFee = 10_000UL;
        public const int Squares = 25;
        public const uint AllSquaresMask = (1u << 25) - 1;

        // Instruction discriminators (first data byte).
        public const byte IxAutomate = 0, IxCheckpoint = 2, IxClaimSol = 3, IxClaimOre = 4, IxClose = 5, IxDeploy = 6, IxLog = 8, IxReset = 9;
        // Account discriminators (first byte of the 8-byte steel discriminator).
        public const byte AccAutomation = 100, AccConfig = 101, AccMiner = 103, AccTreasury = 104, AccBoard = 105, AccRound = 109;
    }

    public enum AutomationStrategy : byte { Random = 0, Preferred = 1, Discretionary = 2, DiscretionaryBps = 3 }
}

#endif
