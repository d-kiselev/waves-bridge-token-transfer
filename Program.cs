using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using WavesCS;

using Grpc.Core;
using Waves;
using System.Threading.Tasks;
using Google.Protobuf;
using Waves.Node.Grpc;

namespace wavesbridgetokentransfer
{
    class MainClass
    {
        public static string chainCollectorPSeed = "take purity craft away cake month layer napkin theme slam explain seed take purity craft";
        public static string chainCollectorQSeed = "void entire theme slam explain seed take purity craft away cake month layer napkin nasty";
        public static string tokenPortPSeed = "theme slam explain seed take purity craft away cake month layer napkin nasty void entire";
        public static string tokenPortQSeed = "seed take purity craft away cake month layer napkin nasty void entire theme slam explain";
        public static string AlicePSeed = "whip disagree egg melt satisfy repeat engine envelope federal toward shoulder cattle rare much lava";
        public static string BobQSeed = "whip disagree egg satisfy repeat engine envelope federal toward shoulder cattle rare much lava melt";

        public static PrivateKeyAccount chainCollectorP = PrivateKeyAccount.CreateFromSeed(chainCollectorPSeed, 'P');
        public static PrivateKeyAccount chainCollectorQ = PrivateKeyAccount.CreateFromSeed(chainCollectorQSeed, 'Q');
        public static PrivateKeyAccount tokenPortP = PrivateKeyAccount.CreateFromSeed(tokenPortPSeed, 'P');
        public static PrivateKeyAccount tokenPortQ = PrivateKeyAccount.CreateFromSeed(tokenPortQSeed, 'Q');
        public static PrivateKeyAccount AliceP = PrivateKeyAccount.CreateFromSeed(AlicePSeed, 'P');
        public static PrivateKeyAccount BobQ = PrivateKeyAccount.CreateFromSeed(BobQSeed, 'Q');
        public static PrivateKeyAccount faucetP = PrivateKeyAccount.CreateFromSeed("seed", 'P');
        public static PrivateKeyAccount faucetQ = PrivateKeyAccount.CreateFromSeed("seed", 'Q');

        public static Node nodeP;
        public static Node nodeQ;

        public static void Init()
        {
            chainCollectorP = PrivateKeyAccount.CreateFromSeed(chainCollectorPSeed, 'P');
            chainCollectorQ = PrivateKeyAccount.CreateFromSeed(chainCollectorQSeed, 'Q');
            tokenPortP = PrivateKeyAccount.CreateFromSeed(tokenPortPSeed, 'P');
            tokenPortQ = PrivateKeyAccount.CreateFromSeed(tokenPortQSeed, 'Q');
            AliceP = PrivateKeyAccount.CreateFromSeed(AlicePSeed, 'P');
            BobQ = PrivateKeyAccount.CreateFromSeed(BobQSeed, 'Q');
            faucetP = PrivateKeyAccount.CreateFromSeed("seed", 'P');
            faucetQ = PrivateKeyAccount.CreateFromSeed("seed", 'Q');

            nodeP = new Node("http://127.0.0.1:6870", 'P');
            nodeQ = new Node("http://127.0.0.1:6869", 'Q');
        }

        // transfer tokens from network "P" to network "Q"
        public static void Main(string[] args)
        {
            Init();

            // 0. set accounts' scripts and data
            SetTokenPorts(); return;

            // 1. transfer tokens from Alice to TokenPort (in network P)

            /*var response = nodeP.Transfer(AliceP, tokenPortP.Address, Assets.WAVES, 0.00000002m, 0.005m, null, BobQ.Address.FromBase58());
            Thread.Sleep(10000);
            var txId = response.ParseJsonObject().GetString("id");
            */

            var txId = "HwgHpwKPeNRG2frrR4UTWfmhxRpGb5aLiRaL5KvurwbH";

            Console.WriteLine($"transfer tx id: {txId}");

            var blockHeight = nodeP.GetTransactionHeight(txId);
            var txBytes = GetTransactionProtobufBytes("0.0.0.0:6890", txId);

            var key = blockHeight.ToString() + "_transactionsRoot";

            // 2. wait for the chain collector to put Merkle root (in network Q)
            while (true)
            {
                if (nodeQ.GetAddressData(chainCollectorQ.Address).ContainsKey(key))
                    break;
                Thread.Sleep(10000);
            }
            
            // 3. genegate MerkleProof (for transaction in network P)
            var merkleProof = nodeP.GetMerkleProof(txId);
            Console.WriteLine($"Merkle proof: {merkleProof.ToBase58()}");

            // 4. invoke script of tokenPort (in network Q) --> Bob receives money
            var caller = BobQ;
            var invokeScriptTx = new InvokeScriptTransaction(nodeQ.ChainId, caller.PublicKey, tokenPortQ.Address, "withdraw",
                new List<object> { txBytes, (long)blockHeight, merkleProof }, null, 0.005m, Assets.WAVES);
            invokeScriptTx.Sign(caller);
            Console.WriteLine(invokeScriptTx.GetJsonWithSignature().ToJson());
            
            // testnetNode.Broadcast(invokeScriptTx);
        }

        public static void GetTokensFromFaucet(decimal amount = 1000m)
        {
            nodeP.Transfer(faucetP, tokenPortP.Address, Assets.WAVES, amount);
            nodeP.Transfer(faucetP, chainCollectorP.Address, Assets.WAVES, amount);
            nodeP.Transfer(faucetP, AliceP.Address, Assets.WAVES, amount);

            nodeQ.Transfer(faucetQ, tokenPortQ.Address, Assets.WAVES, amount);
            nodeQ.Transfer(faucetQ, chainCollectorQ.Address, Assets.WAVES, amount);
            nodeQ.Transfer(faucetQ, BobQ.Address, Assets.WAVES, amount);
        }

        public static void SetTokenPorts()
        {
            nodeP.PutData(tokenPortP, new Dictionary<string, object>
            {
                { "tokenPortInOtherChain", tokenPortQ.Address.FromBase58() },
                { "chainCollector", chainCollectorP.Address.FromBase58()},
                { "WAVES_asset", "WAVES"}
            }, 0.01m);

            nodeQ.PutData(tokenPortQ, new Dictionary<string, object>
            {
                { "tokenPortInOtherChain", tokenPortP.Address.FromBase58() },
                { "chainCollector", chainCollectorQ.Address.FromBase58()},
                { "WAVES_asset", "WAVES"}
            }, 0.01m);

            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("wavesbridgetokentransfer.Resources.TokenPort.ride"))
            using (var reader = new StreamReader(stream))
            {
                var tokenPortScript = reader.ReadToEnd();
                var compiledScript = nodeP.CompileCode(tokenPortScript);

                nodeP.SetScript(tokenPortP, compiledScript);
                nodeQ.SetScript(tokenPortQ, compiledScript);
            }
        }

        public static byte[] GetTransactionProtobufBytes(string target, string id)
        {
            Channel channel = new Channel(target, ChannelCredentials.Insecure);

            var client = new TransactionsApi.TransactionsApiClient(channel);

            var request = new TransactionsRequest()
            {
                TransactionIds = { ByteString.CopyFrom(id.FromBase58()) }
            };

            var t = client.GetTransactions(request);
            var task = Task.Run(async () => { await t.ResponseStream.MoveNext(); });
            task.Wait();


            return t.ResponseStream.Current
                .ToByteArray()
                .Skip(34)
                .SkipWhile(x => x != 10)
                .ToArray();
        }
    }

    static class NodeExtentions
    {
        public static decimal GetTransactionHeight(this Node node, string txId)
        {
            return node.GetObject($"transactions/info/{txId}").GetLong("height");
        }

        public static byte[] GetMerkleRoot(this Node node, int height)
        {
            return node.GetObject($"blocks/headers/at/{height}").GetString("transactionsRoot").FromBase58();
        }

        private static byte[] GenerateMerkleProof(long index, List<byte[]> proofs)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);

            for (var i = 0; i < proofs.Count; i++)
            {
                const byte LeftSide = 0;
                const byte RightSide = 1;
                var side = (index % 2 == 0) ? LeftSide : RightSide;
                var len = (byte)proofs[i].Length;

                writer.Write(side);
                writer.Write(len);
                writer.Write(proofs[i]);

                index /= 2;
            }

            return stream.ToArray();
        }

        public static byte[] GetMerkleProof(this Node node, string txId)
        {
            var response = node.GetObjects($"transactions/merkleProof?id={txId}")
                               .First();

            var txIndex = response.GetLong("transactionIndex");

            var arr = (Newtonsoft.Json.Linq.JArray)response.GetValue("merkleProof");
            var proofArray = arr.Select(x => x.ToString().FromBase64()).ToList();

            return GenerateMerkleProof(txIndex, proofArray);
        }
    }
}
