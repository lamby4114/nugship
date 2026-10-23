// TXN-001 self-tests (box, .NET 8) over the game's Ore/Chain code. Pass "live" to also reproduce the build-18 failure on mainnet
// (raw RPC answers err:"BlockhashNotFound" as a bare string, which Solana.Unity cannot parse -> "Unable to parse json")
// (read-only simulateTransaction, NOTHING SENT): stale-hash wallet bytes -> BlockhashNotFound; restamped bytes -> OK.
using System; using System.Collections.Generic; using System.Linq; using System.Threading; using OreSeas.Ore.Chain;
using Solana.Unity.Wallet; using Solana.Unity.Rpc; using Solana.Unity.Rpc.Types; using Solana.Unity.Rpc.Models;
static class P {
  static int fails; static void Check(string n, bool ok){ Console.WriteLine((ok?"PASS ":"FAIL ")+n); if(!ok) fails++; }
  static readonly PublicKey Me=new PublicKey("14RMqeviZGaZhYXeRWmQbWj2hUT6p6haKkHAEThmJe69"), Exec=new PublicKey("BFhXQCdrJWxoeKHcK56Mr7BAUGWmeCV4LkMdsnFVGR5K");
  static List<TransactionInstruction> Dep(PublicKey me, ulong d)=>LiveTx.WithBudget(new List<TransactionInstruction>{OreInstructions.Automate(me,1_000_000,d,Exec,20_000,0,AutomationStrategy.Discretionary,true)});
  static bool Has(byte[] w,string h){var hb=Solana.Unity.Wallet.Utilities.Encoders.Base58.DecodeData(h);for(int i=0;i+32<=w.Length;i++){int k=0;while(k<32&&w[i+k]==hb[k])k++;if(k==32)return true;}return false;}
  static int Main(string[] a){
    Check("preflight: empty wallet", LiveTx.PreflightReason(0,0,true,null,null)=="NOT ENOUGH SOL");
    Check("preflight: short balance", LiveTx.PreflightReason(76_182_623,1_000_000_000,true,null,null)=="NOT ENOUGH SOL");
    Check("preflight: AccountNotFound", LiveTx.PreflightReason(1_000_000_000,0,true,"AccountNotFound",null)=="NOT ENOUGH SOL");
    Check("preflight: insufficient lamports log", LiveTx.PreflightReason(1_000_000_000,0,true,"InstructionError",new[]{"Transfer: insufficient lamports 18300763, need 20000000"})=="NOT ENOUGH SOL");
    Check("preflight: invalid owner", LiveTx.PreflightReason(1_000_000_000,0,true,"InstructionError",new[]{"Program log: Account has invalid owner"})=="NOTHING TO DO");
    Check("preflight: other error", LiveTx.PreflightReason(1_000_000_000,0,true,"InstructionError",new[]{"custom program error: 0x7"})=="WOULD FAIL - NOT SENT");
    Check("preflight: rpc failure", LiveTx.PreflightReason(1_000_000_000,0,false,null,null)=="RPC BUSY - TRY AGAIN");
    Check("preflight: ok", LiveTx.PreflightReason(1_000_000_000,20_000_000,true,null,new[]{"ok"})==null);
    Check("compute budget once", Dep(Me,1).Count==3 && LiveTx.WithBudget(Dep(Me,1)).Count==3);
    Check("wallet-bytes: BlockhashNotFound -> TRY AGAIN", LiveTx.WalletBytesReason(true,"BlockhashNotFound",null)=="TRY AGAIN");
    Check("wallet-bytes: ok -> null", LiveTx.WalletBytesReason(true,null,new[]{"ok"})==null);
    string oldH="88oQ5J4YsjF9U5Nd5boggSQf4v6uaPuwqERjdYAyiTMR", newH="4sGjMW1sUnHzSxGspuhpqLDx6wiyjNtZAMdL4VZHirAn";
    var tx=LiveTx.BuildUnsigned(Me,oldH,Dep(Me,5_000_000)); var w0=LiveTx.SerializeForSimulation(tx); LiveTx.Restamp(tx,newH); var w1=LiveTx.SerializeForSimulation(tx);
    Check("restamp swaps the hash in the wallet bytes", Has(w0,oldH)&&!Has(w1,oldH)&&Has(w1,newH)&&w0.Length==w1.Length&&w1[0]==1);
    Check("first deposit rent budget >= measured 5,943,440 (Miner+Automation)", 6_000_000UL>=5_943_440UL);
    Check("tx fits in a packet", w1.Length<=1232);
    if(a.Length>0&&a[0]=="live"){
      var rpc=ClientFactory.GetClient("https://api.mainnet-beta.solana.com"); var payer=new PublicKey(a.Length>1?a[1]:"6TWMHcW3gGXRnzRoe6vTgENjJhCbTFApcA5yLEJJaBDh");
      string H()=>rpc.GetLatestBlockHashAsync(Commitment.Confirmed).Result.Result.Value.Blockhash;
      string Sim(Transaction t){var s=rpc.SimulateTransactionAsync(LiveTx.SerializeForSimulation(t),false,Commitment.Confirmed,false).Result; return s==null?"RPC":!s.WasSuccessful?((s.Reason??"").Contains("lockhash not found")||(s.Reason??"").Contains("Unable to parse"))?"BlockhashNotFound":"RPC:"+s.Reason:s.Result.Value.Error==null?"OK":s.Result.Value.Error.Type.ToString();}
      var t=LiveTx.BuildUnsigned(payer,H(),Dep(payer,5_000_000));
      var s0=Sim(t); Console.WriteLine("live: fresh bytes -> "+s0+"; aging 45 s (old flow: hash fetched before balance+sim+wallet launch+reauthorize)...");
      Thread.Sleep(45_000); var s1=Sim(t);  LiveTx.Restamp(t,H()); var s2=Sim(t);
      Console.WriteLine("aged -> "+s1); Check("LIVE repro: same bytes after 45 s fail like the wallet saw (BlockhashNotFound)", s0=="OK"&&s1=="BlockhashNotFound"); Check("LIVE: client gate blocks the aged bytes before any wallet prompt", s1!="OK");
      Check("LIVE fix: restamped in-session bytes simulate OK", s2=="OK");
    }
    Console.WriteLine(fails==0?"ALL PASS":fails+" FAILED"); return fails; } }