// OreSeas.Ore (csharp/OreSeas.Ore) under OreSeas.Ore.Chain.
// TRANSACTION BUILDERS: compiled ONLY with the ORESEAS_LIVE_ORE scripting define, which this project never sets.
// ServerConfig.LIVE_ORE=false is the matching runtime gate. Enabling either needs Victor's explicit OK.
#if ORESEAS_SOLANA && ORESEAS_LIVE_ORE
using System;
using System.Collections.Generic;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Wallet;

namespace OreSeas.Ore.Chain
{
    /// <summary>
    /// ORE instruction builders (mirror api/src/sdk.rs @48c203b). Data = [discriminator] ++ packed little-endian args.
    /// Verified byte-for-byte against real mainnet transactions (see OreSeas.Ore.Tests).
    /// </summary>
    public static class OreInstructions
    {
        static AccountMeta W(PublicKey k, bool signer = false) => AccountMeta.Writable(k, signer);
        static AccountMeta R(PublicKey k) => AccountMeta.ReadOnly(k, false);
        static TransactionInstruction Ix(List<AccountMeta> keys, byte[] data) =>
            new TransactionInstruction { ProgramId = OreConstants.ProgramId.KeyBytes, Keys = keys, Data = data };

        static void PutU64(byte[] d, int o, ulong v) { for (int i = 0; i < 8; i++) d[o + i] = (byte)(v >> (8 * i)); }
        static void PutU32(byte[] d, int o, uint v) { for (int i = 0; i < 4; i++) d[o + i] = (byte)(v >> (8 * i)); }

        /// <summary>Deploy `amount` lamports on each square in `mask` (bit i = square i). signer = wallet (manual) or session key (automation executor).</summary>
        public static TransactionInstruction Deploy(PublicKey signer, PublicKey authority, ulong amount, ulong roundId, uint mask)
        {
            if ((mask & ~OreConstants.AllSquaresMask) != 0) throw new ArgumentException("mask uses bits above 24");
            var data = new byte[13];
            data[0] = OreConstants.IxDeploy;
            PutU64(data, 1, amount);
            PutU32(data, 9, mask);
            return Ix(new List<AccountMeta>
            {
                W(signer, true), W(authority), W(OrePda.Automation(authority)), W(OrePda.Board()), W(OrePda.Config()), W(OrePda.Miner(authority)),
                W(OrePda.Round(roundId)), W(OrePda.Treasury()), R(OreConstants.SystemProgramId), R(OreConstants.ProgramId),
                W(OrePda.EntropyVar(0)), R(OreConstants.EntropyProgramId),
            }, data);
        }

        /// <summary>Settle `authority`'s miner for `roundId` (its previous round). Any signer may checkpoint.</summary>
        public static TransactionInstruction Checkpoint(PublicKey signer, PublicKey authority, ulong roundId) =>
            Ix(new List<AccountMeta>
            {
                W(signer, true), W(authority), W(OrePda.Automation(authority)), W(OrePda.Board()), W(OrePda.Miner(authority)),
                W(OrePda.Round(roundId)), W(OrePda.Treasury()), R(OreConstants.SystemProgramId),
            }, new[] { OreConstants.IxCheckpoint });

        public static TransactionInstruction ClaimSol(PublicKey signer) =>
            Ix(new List<AccountMeta>
            {
                W(signer, true), W(OrePda.Board()), W(OrePda.Miner(signer)), R(OreConstants.SystemProgramId), R(OreConstants.ProgramId),
            }, new[] { OreConstants.IxClaimSol });

        /// <summary>bps of claimable ORE (10000 = all). Unrefined ORE pays a 10% refining fee on claim.</summary>
        public static TransactionInstruction ClaimOre(PublicKey signer, ulong bps = 10_000)
        {
            var data = new byte[9];
            data[0] = OreConstants.IxClaimOre;
            PutU64(data, 1, bps);
            var treasury = OrePda.Treasury();
            return Ix(new List<AccountMeta>
            {
                W(signer, true), W(OrePda.Board()), W(OrePda.Miner(signer)), W(OreConstants.Mint), W(OrePda.OreTokenAccount(signer)), W(treasury),
                W(OrePda.OreTokenAccount(treasury)), R(OreConstants.SystemProgramId), R(OreConstants.TokenProgramId),
                R(OreConstants.AssociatedTokenProgramId), R(OreConstants.ProgramId),
            }, data);
        }

        /// <summary>AutomateV2 (66-byte data). executor = PublicKey(default/all-zero) closes the automation (teardown).</summary>
        public static TransactionInstruction Automate(PublicKey signer, ulong amount, ulong deposit, PublicKey executor, ulong fee, ulong mask,
            AutomationStrategy strategy, bool reload, AutomationConditions? conditions = null)
        {
            var data = new byte[66];
            data[0] = OreConstants.IxAutomate;
            PutU64(data, 1, amount);
            PutU64(data, 9, deposit);
            PutU64(data, 17, fee);
            PutU64(data, 25, mask);
            data[33] = (byte)strategy;
            PutU64(data, 34, reload ? 1UL : 0UL);
            Array.Copy((conditions ?? AutomationConditions.Default).ToBytes(), 0, data, 42, 24);
            return Ix(new List<AccountMeta>
            {
                W(signer, true), W(OrePda.Automation(signer)), W(executor), W(OrePda.Miner(signer)), R(OreConstants.SystemProgramId),
            }, data);
        }

        /// <summary>ORE SEAS match setup: Discretionary strategy, session key as executor, budget deposited, returned SOL reloaded.</summary>
        public static TransactionInstruction AutomateSession(PublicKey wallet, PublicKey sessionKey, ulong perSquareLamports, ulong budgetLamports,
            ulong executorFeeLamports = 5_000, bool reload = true) =>
            Automate(wallet, perSquareLamports, budgetLamports, sessionKey, executorFeeLamports, 0, AutomationStrategy.Discretionary, reload);

        /// <summary>Teardown: closes the automation account and returns its balance + rent to the wallet.</summary>
        public static TransactionInstruction AutomateClose(PublicKey wallet) =>
            Automate(wallet, 0, 0, new PublicKey(new byte[32]), 0, 0, AutomationStrategy.Random, false);
    }

    /// <summary>Non-ORE helpers used in ORE SEAS transactions (hand-encoded to avoid extra package deps).</summary>
    public static class AuxInstructions
    {
        public static readonly PublicKey ComputeBudgetProgramId = new PublicKey("ComputeBudget111111111111111111111111111111");

        /// <summary>System transfer (e.g. seed the session key with lamports for its tx fees).</summary>
        public static TransactionInstruction SystemTransfer(PublicKey from, PublicKey to, ulong lamports)
        {
            var data = new byte[12];
            data[0] = 2; // SystemInstruction::Transfer (u32 LE)
            for (int i = 0; i < 8; i++) data[4 + i] = (byte)(lamports >> (8 * i));
            return new TransactionInstruction
            {
                ProgramId = OreConstants.SystemProgramId.KeyBytes,
                Keys = new List<AccountMeta> { AccountMeta.Writable(from, true), AccountMeta.Writable(to, false) },
                Data = data,
            };
        }

        public static TransactionInstruction SetComputeUnitLimit(uint units)
        {
            var d = new byte[5]; d[0] = 2; for (int i = 0; i < 4; i++) d[1 + i] = (byte)(units >> (8 * i));
            return new TransactionInstruction { ProgramId = ComputeBudgetProgramId.KeyBytes, Keys = new List<AccountMeta>(), Data = d };
        }

        public static TransactionInstruction SetComputeUnitPrice(ulong microLamports)
        {
            var d = new byte[9]; d[0] = 3; for (int i = 0; i < 8; i++) d[1 + i] = (byte)(microLamports >> (8 * i));
            return new TransactionInstruction { ProgramId = ComputeBudgetProgramId.KeyBytes, Keys = new List<AccountMeta>(), Data = d };
        }
    }

    /// <summary>Compose the per-turn "fire" instruction list: [CU limit, CU price], Checkpoint(prev) if needed, Deploy(targets).</summary>
    public static class OreSeasTx
    {
        /// <param name="signer">session key (auto-fire) or wallet (manual)</param>
        /// <param name="authority">player's wallet (miner authority)</param>
        /// <param name="checkpointRoundId">miner.RoundId when miner.NeedsCheckpoint, else null</param>
        public static List<TransactionInstruction> FireTurn(PublicKey signer, PublicKey authority, ulong roundId, uint mask, ulong lamportsPerSquare,
            ulong? checkpointRoundId, uint computeUnits = 120_000, ulong microLamportsPerCu = 20_000)
        {
            var list = new List<TransactionInstruction>();
            if (computeUnits > 0) list.Add(AuxInstructions.SetComputeUnitLimit(computeUnits));
            if (microLamportsPerCu > 0) list.Add(AuxInstructions.SetComputeUnitPrice(microLamportsPerCu));
            if (checkpointRoundId.HasValue) list.Add(OreInstructions.Checkpoint(signer, authority, checkpointRoundId.Value));
            list.Add(OreInstructions.Deploy(signer, authority, lamportsPerSquare, roundId, mask));
            return list;
        }

        /// <summary>One wallet approval at match start: fund session key fees + automation budget with session key as executor.</summary>
        public static List<TransactionInstruction> SessionSetup(PublicKey wallet, PublicKey sessionKey, ulong lamportsPerSquare, ulong budgetLamports,
            ulong sessionKeyFeeFloat = 2_000_000, ulong executorFeeLamports = 5_000)
        {
            return new List<TransactionInstruction>
            {
                AuxInstructions.SystemTransfer(wallet, sessionKey, sessionKeyFeeFloat),
                OreInstructions.AutomateSession(wallet, sessionKey, lamportsPerSquare, budgetLamports, executorFeeLamports, true),
            };
        }
    }

    /// <summary>Board coordinates: square = row*5 + col, rows A..E, cols 1..5 (A1=0, A5=4, B1=5, E5=24).</summary>
    public static class OreSquares
    {
        public static int FromCoord(string c)
        {
            c = c.Trim().ToUpperInvariant();
            if (c.Length != 2 || c[0] < 'A' || c[0] > 'E' || c[1] < '1' || c[1] > '5') throw new ArgumentException("bad coordinate " + c);
            return (c[0] - 'A') * 5 + (c[1] - '1');
        }
        public static string ToCoord(int square) => ((char)('A' + square / 5)).ToString() + (square % 5 + 1);
        public static uint ToMask(IEnumerable<int> squares) { uint m = 0; foreach (var s in squares) { if (s < 0 || s > 24) throw new ArgumentException("square"); m |= 1u << s; } return m; }
        public static List<int> FromMask(uint mask) { var l = new List<int>(); for (int i = 0; i < 25; i++) if ((mask & (1u << i)) != 0) l.Add(i); return l; }
    }
}

#endif
