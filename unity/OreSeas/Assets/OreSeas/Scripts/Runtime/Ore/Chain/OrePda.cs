// OreSeas.Ore (csharp/OreSeas.Ore) under OreSeas.Ore.Chain. Read-only: account decoders / PDAs / constants.
// Needs Solana.Unity.Wallet (Solana.Unity-SDK package) -> compiled only when ORESEAS_SOLANA is defined.
#if ORESEAS_SOLANA
using System;
using System.Collections.Generic;
using System.Text;
using Solana.Unity.Wallet;

namespace OreSeas.Ore.Chain
{
    /// <summary>PDA derivations (api/src/state/mod.rs, entropy_api var_pda, SPL associated token address).</summary>
    public static class OrePda
    {
        static readonly Dictionary<string, PublicKey> Cache = new Dictionary<string, PublicKey>();

        static PublicKey Find(PublicKey program, params byte[][] seeds)
        {
            var sb = new StringBuilder(program.Key);
            foreach (var s in seeds) sb.Append(':').Append(BitConverter.ToString(s));
            var k = sb.ToString();
            lock (Cache)
            {
                if (Cache.TryGetValue(k, out var hit)) return hit;
                if (!PublicKey.TryFindProgramAddress(seeds, program, out var addr, out _)) throw new InvalidOperationException("no PDA");
                Cache[k] = addr;
                return addr;
            }
        }

        public static byte[] U64Le(ulong v)
        {
            var b = new byte[8];
            for (int i = 0; i < 8; i++) b[i] = (byte)(v >> (8 * i));
            return b;
        }

        static byte[] Seed(string s) => Encoding.UTF8.GetBytes(s);

        public static PublicKey Board() => Find(OreConstants.ProgramId, Seed("board"));
        public static PublicKey Config() => Find(OreConstants.ProgramId, Seed("config"));
        public static PublicKey Treasury() => Find(OreConstants.ProgramId, Seed("treasury"));
        public static PublicKey Round(ulong roundId) => Find(OreConstants.ProgramId, Seed("round"), U64Le(roundId));
        public static PublicKey Miner(PublicKey authority) => Find(OreConstants.ProgramId, Seed("miner"), authority.KeyBytes);
        public static PublicKey Automation(PublicKey authority) => Find(OreConstants.ProgramId, Seed("automation"), authority.KeyBytes);
        public static PublicKey EntropyVar(ulong id = 0) => Find(OreConstants.EntropyProgramId, Seed("var"), Board().KeyBytes, U64Le(id));
        public static PublicKey AssociatedTokenAddress(PublicKey owner, PublicKey mint) =>
            Find(OreConstants.AssociatedTokenProgramId, owner.KeyBytes, OreConstants.TokenProgramId.KeyBytes, mint.KeyBytes);
        public static PublicKey OreTokenAccount(PublicKey owner) => AssociatedTokenAddress(owner, OreConstants.Mint);
    }
}

#endif
