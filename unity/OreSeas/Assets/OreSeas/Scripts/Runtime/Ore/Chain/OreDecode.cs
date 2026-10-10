// OreSeas.Ore (csharp/OreSeas.Ore) under OreSeas.Ore.Chain. Read-only: account decoders / PDAs / constants.
// Needs Solana.Unity.Wallet (Solana.Unity-SDK package) -> compiled only when ORESEAS_SOLANA is defined.
#if ORESEAS_SOLANA
using System;
using Solana.Unity.Wallet;

namespace OreSeas.Ore.Chain
{
    /// <summary>Instruction-data and event decoders (for reading ORE txs/logs on device).</summary>
    public static class OreDecode
    {
        public sealed class DeployArgs { public ulong Amount; public uint Mask; }
        public static DeployArgs Deploy(byte[] d) =>
            d.Length == 13 && d[0] == OreConstants.IxDeploy ? new DeployArgs { Amount = Le.U64(d, 1), Mask = (uint)(d[9] | d[10] << 8 | d[11] << 16 | d[12] << 24) } : null;

        public sealed class DeployEvent { public PublicKey Authority, Signer; public ulong Amount, Mask, RoundId, Strategy, TotalSquares; public long Ts; }
        /// <summary>Decode the payload of an ORE Log instruction (data after the 0x08 discriminator).</summary>
        public static DeployEvent DeployEventFromLog(byte[] payload)
        {
            if (payload == null || payload.Length < 120 || Le.U64(payload, 0) != 2) return null;
            return new DeployEvent
            {
                Authority = Le.Pk(payload, 8), Amount = Le.U64(payload, 40), Mask = Le.U64(payload, 48), RoundId = Le.U64(payload, 56),
                Signer = Le.Pk(payload, 64), Strategy = Le.U64(payload, 96), TotalSquares = Le.U64(payload, 104), Ts = Le.I64(payload, 112),
            };
        }
    }
}

#endif
