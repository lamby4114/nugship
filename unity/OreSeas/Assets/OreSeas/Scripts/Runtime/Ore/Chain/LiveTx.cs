// OreSeas.Ore.Chain.LiveTx: v1.1.0 manual live mining. The PLAYER's wallet signs every transaction (MWA / Seed Vault).
// Non-custodial: SOL moves from the player's wallet straight to the ORE program; the game and its server never hold keys or SOL.
// Pure C# (Solana.Unity.Rpc/Wallet only) so it is unit-testable outside Unity.
#if ORESEAS_SOLANA && ORESEAS_LIVE_ORE
using System;
using System.Collections.Generic;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Wallet;

namespace OreSeas.Ore.Chain
{
    public static class LiveTx
    {
        /// <summary>Hard ceiling per square regardless of settings or server requests (0.01 SOL).</summary>
        public const ulong MaxLamportsPerSquare = 10_000_000UL;
        /// <summary>Lamports kept back for tx fees / rent when checking the balance.</summary>
        public const ulong FeeReserveLamports = 5_000_000UL;
        public const int MaxSquaresPerVolley = 25;

        public static int Count(uint mask) { int n = 0; for (int i = 0; i < 25; i++) if ((mask & (1u << i)) != 0) n++; return n; }

        /// <summary>Returns null if the deploy is allowed, otherwise a short player-facing reason.</summary>
        public static string Validate(ulong lamportsPerSquare, uint mask, ulong spentThisMatch, ulong matchCapLamports, ulong walletBalance)
        {
            int n = Count(mask);
            if (n == 0) return "NO SQUARES";
            if ((mask >> 25) != 0) return "BAD SQUARES";
            if (lamportsPerSquare == 0) return "NO STAKE SET";
            if (lamportsPerSquare > MaxLamportsPerSquare) return "STAKE ABOVE 0.01 SOL/SQUARE LIMIT";
            ulong cost = lamportsPerSquare * (ulong)n;
            if (spentThisMatch + cost > matchCapLamports) return "MATCH SPEND CAP REACHED";
            if (walletBalance < cost + FeeReserveLamports) return "NOT ENOUGH SOL";
            return null;
        }

        /// <summary>Manual fire: [CU limit, CU price], Checkpoint(prev) if the miner needs it, Deploy. Signer = authority = player wallet.</summary>
        public static List<TransactionInstruction> ManualFire(PublicKey wallet, ulong roundId, uint mask, ulong lamportsPerSquare, OreMiner minerOrNull)
        {
            ulong? cp = minerOrNull != null && minerOrNull.NeedsCheckpoint ? minerOrNull.RoundId : (ulong?)null;
            return OreSeasTx.FireTurn(wallet, wallet, roundId, mask, lamportsPerSquare, cp);
        }

        /// <summary>Claim everything: Checkpoint (if pending), ClaimSol, create ORE ATA (idempotent), ClaimOre.</summary>
        public static List<TransactionInstruction> ClaimAll(PublicKey wallet, OreMiner minerOrNull, bool sol = true, bool ore = true)
        {
            var l = new List<TransactionInstruction> { AuxInstructions.SetComputeUnitLimit(200_000), AuxInstructions.SetComputeUnitPrice(20_000) };
            if (minerOrNull != null && minerOrNull.NeedsCheckpoint) l.Add(OreInstructions.Checkpoint(wallet, wallet, minerOrNull.RoundId));
            if (sol) l.Add(OreInstructions.ClaimSol(wallet));
            if (ore) { l.Add(CreateAtaIdempotent(wallet, wallet, OreConstants.Mint)); l.Add(OreInstructions.ClaimOre(wallet)); }
            return l;
        }

        /// <summary>Associated Token Account program CreateIdempotent (data = [1]).</summary>
        public static TransactionInstruction CreateAtaIdempotent(PublicKey payer, PublicKey owner, PublicKey mint) =>
            new TransactionInstruction
            {
                ProgramId = OreConstants.AssociatedTokenProgramId.KeyBytes,
                Keys = new List<AccountMeta>
                {
                    AccountMeta.Writable(payer, true), AccountMeta.Writable(OrePda.AssociatedTokenAddress(owner, mint), false),
                    AccountMeta.ReadOnly(owner, false), AccountMeta.ReadOnly(mint, false),
                    AccountMeta.ReadOnly(OreConstants.SystemProgramId, false), AccountMeta.ReadOnly(OreConstants.TokenProgramId, false),
                },
                Data = new byte[] { 1 },
            };

        /// <summary>Unsigned legacy transaction (fee payer = wallet) ready for MWA signAndSend.</summary>
        public static Transaction BuildUnsigned(PublicKey feePayer, string recentBlockhash, List<TransactionInstruction> ixs) =>
            // Same shape the Solana.Unity-SDK WalletBase uses before handing a tx to MWA (signatures filled by the wallet).
            new Transaction { RecentBlockHash = recentBlockhash, FeePayer = feePayer, Instructions = new List<TransactionInstruction>(ixs), Signatures = new List<SignaturePubKeyPair>() };

        /// <summary>Wire bytes with an all-zero fee-payer signature slot (simulation with sigVerify=false / size checks).</summary>
        public static byte[] SerializeForSimulation(Transaction tx)
        {
            var msg = tx.CompileMessage();
            var o = new byte[1 + 64 + msg.Length]; o[0] = 1; Array.Copy(msg, 0, o, 65, msg.Length); return o;
        }

        public static string SolanaSignInMessage(string domain, string wallet, string nonce, string issuedAtIso) =>
            domain + " wants you to sign in with your Solana account:\n" + wallet + "\n\nSign in to NUGSHIP. This is not a transaction and costs nothing.\n\nNonce: " + nonce + "\nIssued At: " + issuedAtIso;
    }
}
#endif
