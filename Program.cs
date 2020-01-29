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
        public static string chainCollectorTestnet = "3N6VvqXLuZ4uxWG6E92orif1KR61bqF6JCk";
        public static string tokenPortTestnet = "3Mz3XPi4hQnfVK9ZaA27YwFfiPApPgFnCts";
        public static string tokenPortStagenet = "3Mgqt4HLefP3bafouR4gXPrP9EgU4pajTct";

        public static void Main(string[] args)
        {
            // transfer tokens from stagenet to testnet

            var testnetNode = new Node(Node.TestNetChainId);
            var stagenetNode = new Node("https://stagenet-aws-fr-1.wavesnodes.com/", 'S');

            var aliceSeed = "whip disagree egg melt satisfy repeat engine envelope federal toward shoulder cattle rare much lava";
            var Alice = PrivateKeyAccount.CreateFromSeed(aliceSeed, stagenetNode.ChainId); // in stagenet
            var Bob = "3MuR872m3WiW1DRBD8CfoLbZpJgo3xzLyy7"; // in testnet

            // 0. Set token port script and data
            // SetTokenPortTestNet();
            // return;

            // 1. transfer tokens from Alice to TokenPort (in stagenet)
            
            /*var response = stagenetNode.Transfer(Alice, tokenPortStagenet, Assets.WAVES, 0.00000002m, 0.005m, null, Bob.FromBase58());
            Thread.Sleep(10000);
            var txId = response.ParseJsonObject().GetString("id");
            */

            var txId = "5g3zao9BPs8U1t5S1AwcHMEzeU6BR8HCqD5v6mYvuZoY";
            Console.WriteLine($"transfer tx id: {txId}");

            var blockHeight = stagenetNode.GetTransactionHeight(txId);
            var txBytes = GetTransactionProtobufBytes("stagenet-aws-fr-1.wavesnodes.com:6870", txId);

            var key = blockHeight.ToString() + "_transactionsRoot";

            // 2. wait for the chain collector to put Merkle root (in testnet)
            while (true)
            {
                if (testnetNode.GetAddressData(chainCollectorTestnet).ContainsKey(key))
                    break;
                Thread.Sleep(10000);
            }

            // 3. genegare MerkleProof (for transaction in stagenet)
            var merkleProof = stagenetNode.GetMerkleProof(txId);
            Console.WriteLine("Merkle proof: ");

            foreach(var p in merkleProof)
                Console.WriteLine($"\t{p.ToBase58()}");
            
            // 4. invoke script of tokenPort (in testnet) --> Bob receives money
            var callerSeed = "whip disagree egg satisfy repeat engine envelope federal toward shoulder cattle rare much lava melt";
            var caller = PrivateKeyAccount.CreateFromSeed(callerSeed, testnetNode.ChainId);

            var invokeScriptTx = new InvokeScriptTransaction(testnetNode.ChainId, caller.PublicKey, tokenPortTestnet, "withdraw",
                new List<object> { txBytes, (long)blockHeight, merkleProof }, null, 0.005m, Assets.WAVES);
            invokeScriptTx.Sign(caller);
            Console.WriteLine(invokeScriptTx.GetJsonWithSignature().ToJson());

            // testnetNode.Broadcast(invokeScriptTx);
        }

        const byte LeftSide = 0;
        const byte RightSide = 1;

        public static byte[] GenerateMerkleProof(int index, List<byte[]> proofs)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);

            for (var i = 0; i < proofs.Count; i++)
            {
                var side = (index % 2 == 1) ? LeftSide : RightSide;
                var len = (byte)proofs[i].Length;

                writer.Write(side);
                writer.Write(len);
                writer.Write(proofs[i]);

                index = (index - 1) / 2;
            }

            return stream.ToArray();
        }

        public static void SetTokenPortTestNet()
        {
            var node = new Node(Node.TestNetChainId);
            var chainId = 'T';
            var tokenPortSeed = "seed take purity craft away cake month layer napkin nasty void entire theme slam explain";
            var tokenPort = PrivateKeyAccount.CreateFromSeed(tokenPortSeed, chainId);

            node.PutData(tokenPort, new Dictionary<string, object>
            {
                { "tokenPortInOtherChain", tokenPortStagenet.FromBase58() },
                { "chainCollector", chainCollectorTestnet.FromBase58()},
                { "WAVES_asset", "WAVES"}
            }, 0.01m);

            var tokenPortScript = "";
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("wavesbridgetokentransfer.Resources.TokenPort.ride"))
            using (var reader = new StreamReader(stream))
            {
                tokenPortScript = reader.ReadToEnd();
            }

            var compiledScript = node.CompileCode(tokenPortScript);
            node.SetScript(tokenPort, compiledScript);
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

            return t.ResponseStream.Current.ToByteArray();
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

        public static List<byte[]> GetMerkleProof(this Node node, string txId)
        {
            var response = node.GetObjects($"transactions/merkleProof?id={txId}")
                               .First()
                               .GetValue("merkleProof");

            var proof = (Newtonsoft.Json.Linq.JArray)response;

            return proof.Select(x => x.ToString().FromBase64()).ToList();
        }
    }
}
