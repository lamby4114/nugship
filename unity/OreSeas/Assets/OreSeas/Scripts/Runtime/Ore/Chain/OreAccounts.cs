// OreSeas.Ore (csharp/OreSeas.Ore) under OreSeas.Ore.Chain. Read-only: account decoders / PDAs / constants.
// Needs Solana.Unity.Wallet (Solana.Unity-SDK package) -> compiled only when ORESEAS_SOLANA is defined.
#if ORESEAS_SOLANA
using System;
using Solana.Unity.Wallet;

namespace OreSeas.Ore.Chain
{
    /// <summary>Little-endian readers for steel/bytemuck zero-copy accounts (8-byte discriminator, then repr(C) fields).</summary>
    internal static class Le
    {
        public static ulong U64(byte[] d, int o) { ulong v = 0; for (int i = 7; i >= 0; i--) v = (v << 8) | d[o + i]; return v; }
        public static long I64(byte[] d, int o) => unchecked((long)U64(d, o));
        public static ushort U16(byte[] d, int o) => (ushort)(d[o] | (d[o + 1] << 8));
        public static PublicKey Pk(byte[] d, int o) { var b = new byte[32]; Buffer.BlockCopy(d, o, b, 0, 32); return new PublicKey(b); }
        public static byte[] Bytes(byte[] d, int o, int n) { var b = new byte[n]; Buffer.BlockCopy(d, o, b, 0, n); return b; }
        public static ulong[] U64Arr(byte[] d, int o, int n = 25) { var a = new ulong[n]; for (int i = 0; i < n; i++) a[i] = U64(d, o + 8 * i); return a; }
        public static void Check(byte[] d, byte disc, int size, string name)
        {
            if (d == null || d.Length < size) throw new ArgumentException(name + ": data too short");
            if (d[0] != disc) throw new ArgumentException(name + ": bad discriminator " + d[0]);
            for (int i = 1; i < 8; i++) if (d[i] != 0) throw new ArgumentException(name + ": bad discriminator");
        }
    }

    public sealed class OreBoard
    {
        public const int Size = 40;
        public ulong RoundId, StartSlot, EndSlot, ProductionCostEma;
        /// <summary>end_slot == u64::MAX means the round waits for its first deploy.</summary>
        public bool WaitingForFirstDeploy => EndSlot == ulong.MaxValue;
        public static OreBoard Decode(byte[] d)
        {
            Le.Check(d, OreConstants.AccBoard, Size, "Board");
            return new OreBoard { RoundId = Le.U64(d, 8), StartSlot = Le.U64(d, 16), EndSlot = Le.U64(d, 24), ProductionCostEma = Le.U64(d, 32) };
        }
    }

    public sealed class OreRound
    {
        public const int Size = 952;
        public ulong Id; public ulong[] Deployed, Mass, Count, Rewards; public byte[] SlotHash; public ulong ExpiresAt, Motherlode;
        public PublicKey RentPayer, TopMiner; public ulong TotalVaulted, TotalReturnedSol, TotalMiners;

        public static OreRound Decode(byte[] d)
        {
            Le.Check(d, OreConstants.AccRound, Size, "Round");
            return new OreRound
            {
                Id = Le.U64(d, 8), Deployed = Le.U64Arr(d, 16), Mass = Le.U64Arr(d, 216), Count = Le.U64Arr(d, 416), SlotHash = Le.Bytes(d, 616, 32),
                ExpiresAt = Le.U64(d, 648), Motherlode = Le.U64(d, 656), RentPayer = Le.Pk(d, 664), Rewards = Le.U64Arr(d, 696),
                TotalVaulted = Le.U64(d, 896), TotalReturnedSol = Le.U64(d, 904), TotalMiners = Le.U64(d, 912), TopMiner = Le.Pk(d, 920),
            };
        }

        /// <summary>round.rs rng(): xor of the four LE u64 words of slot_hash; null while unset.</summary>
        public ulong? Rng()
        {
            bool z = true, f = true;
            foreach (var b in SlotHash) { if (b != 0) z = false; if (b != 0xff) f = false; }
            if (z || f) return null;
            return Le.U64(SlotHash, 0) ^ Le.U64(SlotHash, 8) ^ Le.U64(SlotHash, 16) ^ Le.U64(SlotHash, 24);
        }
        public int? WinningSquare() { var r = Rng(); return r.HasValue ? (int?)(int)(r.Value % 25) : null; }
        public bool HitMotherlode() { var r = Rng(); return r.HasValue && ReverseBits(r.Value) % 500 == 0; }
        public bool IsSplit => TopMiner.Equals(OreConstants.SplitAddress);
        public ulong TotalDeployed() { ulong s = 0; foreach (var x in Deployed) s += x; return s; }

        public static ulong ReverseBits(ulong x)
        {
            ulong r = 0;
            for (int i = 0; i < 64; i++) { r = (r << 1) | (x & 1); x >>= 1; }
            return r;
        }
    }

    public sealed class OreMiner
    {
        public const int Size = 752;
        public PublicKey Authority; public ulong AutoReturn, CheckpointId, CheckpointFee; public ulong[] Deployed, Mass, Cumulative;
        public ulong RoundId; public byte[] RewardsFactor; public ulong RewardsSol, RefinedOre, RewardsOre; public long LastClaimOreAt, LastClaimSolAt;
        public ulong LifetimeRewardsOre, LifetimeDeployed, LifetimeRewardsSol;

        public static OreMiner Decode(byte[] d)
        {
            Le.Check(d, OreConstants.AccMiner, Size, "Miner");
            return new OreMiner
            {
                Authority = Le.Pk(d, 8), AutoReturn = Le.U64(d, 40), CheckpointId = Le.U64(d, 48), CheckpointFee = Le.U64(d, 56),
                Deployed = Le.U64Arr(d, 64), Mass = Le.U64Arr(d, 264), Cumulative = Le.U64Arr(d, 464), RoundId = Le.U64(d, 664),
                RewardsFactor = Le.Bytes(d, 672, 16), RewardsSol = Le.U64(d, 688), RefinedOre = Le.U64(d, 696), RewardsOre = Le.U64(d, 704),
                LastClaimOreAt = Le.I64(d, 712), LastClaimSolAt = Le.I64(d, 720), LifetimeRewardsOre = Le.U64(d, 728),
                LifetimeDeployed = Le.U64(d, 736), LifetimeRewardsSol = Le.U64(d, 744),
            };
        }
        /// <summary>Squares deployed to in RoundId (bit i = square i).</summary>
        public uint DeployMask() { uint m = 0; for (int i = 0; i < 25; i++) if (Deployed[i] > 0) m |= 1u << i; return m; }
        /// <summary>True if the next Deploy needs a Checkpoint(RoundId) first (deploy.rs asserts checkpoint_id == round_id).</summary>
        public bool NeedsCheckpoint => CheckpointId != RoundId;
    }

    public struct AutomationConditions
    {
        public ulong MaxProductionCost; public ushort MinMotherlode, MaxMotherlode, SplitTiles, SoloTiles; public ulong Buffer;
        public static AutomationConditions Default => new AutomationConditions { MaxProductionCost = ulong.MaxValue, MinMotherlode = 0, MaxMotherlode = ushort.MaxValue, SplitTiles = 0, SoloTiles = 0, Buffer = 0 };
        public byte[] ToBytes()
        {
            var b = new byte[24];
            Array.Copy(OrePda.U64Le(MaxProductionCost), 0, b, 0, 8);
            b[8] = (byte)MinMotherlode; b[9] = (byte)(MinMotherlode >> 8);
            b[10] = (byte)MaxMotherlode; b[11] = (byte)(MaxMotherlode >> 8);
            b[12] = (byte)SplitTiles; b[13] = (byte)(SplitTiles >> 8);
            b[14] = (byte)SoloTiles; b[15] = (byte)(SoloTiles >> 8);
            Array.Copy(OrePda.U64Le(Buffer), 0, b, 16, 8);
            return b;
        }
        public static AutomationConditions FromBytes(byte[] d, int o) => new AutomationConditions
        {
            MaxProductionCost = Le.U64(d, o), MinMotherlode = Le.U16(d, o + 8), MaxMotherlode = Le.U16(d, o + 10),
            SplitTiles = Le.U16(d, o + 12), SoloTiles = Le.U16(d, o + 14), Buffer = Le.U64(d, o + 16),
        };
    }

    public sealed class OreAutomation
    {
        public const int Size = 160;
        public ulong Amount; public PublicKey Authority; public ulong Balance; public PublicKey Executor; public ulong Fee, Strategy, Mask, Reload, TotalSolSpent, TotalOreEarned;
        public AutomationConditions Conditions;
        public static OreAutomation Decode(byte[] d)
        {
            Le.Check(d, OreConstants.AccAutomation, Size, "Automation");
            return new OreAutomation
            {
                Amount = Le.U64(d, 8), Authority = Le.Pk(d, 16), Balance = Le.U64(d, 48), Executor = Le.Pk(d, 56), Fee = Le.U64(d, 88),
                Strategy = Le.U64(d, 96), Mask = Le.U64(d, 104), Reload = Le.U64(d, 112), TotalSolSpent = Le.U64(d, 120), TotalOreEarned = Le.U64(d, 128),
                Conditions = AutomationConditions.FromBytes(d, 136),
            };
        }
    }

    public sealed class OreTreasury
    {
        public const int Size = 48;
        public ulong Motherlode; public byte[] MinerRewardsFactor; public ulong TotalRefined, TotalUnclaimed;
        public static OreTreasury Decode(byte[] d)
        {
            Le.Check(d, OreConstants.AccTreasury, Size, "Treasury");
            return new OreTreasury { Motherlode = Le.U64(d, 8), MinerRewardsFactor = Le.Bytes(d, 16, 16), TotalRefined = Le.U64(d, 32), TotalUnclaimed = Le.U64(d, 40) };
        }
    }

    public sealed class OreConfig
    {
        public const int Size = 232;
        public PublicKey AdminAuthority, AdminFeeCollector, ProtocolAuthority, ProtocolFeeCollector, EntropyVarAddress, EntropyProgramId;
        public ulong AdminFeeRate, ProtocolFeeRate, IntermissionSlots, RoundSlots;
        public static OreConfig Decode(byte[] d)
        {
            Le.Check(d, OreConstants.AccConfig, Size, "Config");
            return new OreConfig
            {
                AdminAuthority = Le.Pk(d, 8), AdminFeeCollector = Le.Pk(d, 40), AdminFeeRate = Le.U64(d, 72),
                ProtocolAuthority = Le.Pk(d, 80), ProtocolFeeCollector = Le.Pk(d, 112), ProtocolFeeRate = Le.U64(d, 144),
                IntermissionSlots = Le.U64(d, 152), RoundSlots = Le.U64(d, 160), EntropyVarAddress = Le.Pk(d, 168), EntropyProgramId = Le.Pk(d, 200),
            };
        }
    }
}

#endif
