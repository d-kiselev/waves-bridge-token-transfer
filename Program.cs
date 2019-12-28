using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using WavesCS;

namespace wavesbridgetokentransfer
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            // transfer tokens from stagenet to testnet

            var testnetNode = new Node(Node.TestNetChainId);
            var stagenetNode = new Node("https://stagenet-aws-fr-1.wavesnodes.com/", 'S');

            var chainCollectorTestnet = "3N6VvqXLuZ4uxWG6E92orif1KR61bqF6JCk";
            var tokenPortTestnet = "3Mz3XPi4hQnfVK9ZaA27YwFfiPApPgFnCts";
            var tokenPortStagenet = "3Mgqt4HLefP3bafouR4gXPrP9EgU4pajTct";

            var aliceSeed = "whip disagree egg melt satisfy repeat engine envelope federal toward shoulder cattle rare much lava";
            var Alice = PrivateKeyAccount.CreateFromSeed(aliceSeed, stagenetNode.ChainId); // in stagenet
            var Bob = "3MuR872m3WiW1DRBD8CfoLbZpJgo3xzLyy7"; // in testnet

            // 0. Set token port script and data
            // SetTokenPort();
            // return;

            // 1. transfer tokens from Alice to TokenPort (in stagenet)
            var response = stagenetNode.Transfer(Alice, tokenPortStagenet, Assets.WAVES, 0.00000002m, 0.005m, null, Bob.FromBase58()); // Bob.FromBase58() ???
            Thread.Sleep(10000);

            var txId = response.ParseJsonObject().GetString("id");
            Console.WriteLine($"transfer tx id: {txId}");

            var blockHeight = stagenetNode.GetTransactionHeight(txId);
            var txBytes = stagenetNode.GetTransactionById(txId).GetBody();
            var key = blockHeight.ToString() + "_transactionsRoot";

            // 2. wait for the chain collector to put Merkle root (in testnet)
            while (true)
            {
                if (testnetNode.GetAddressData(chainCollectorTestnet).ContainsKey(key))
                    break;
                Thread.Sleep(10000);
            }

            // 3. genegare MerkleProof (for transaction in stagenet)
            var merkleProof = GenerateMerkleProof(stagenetNode, txId);

            // 4. invoke script of tokenPort (in testnet) --> Bob receives money
            var callerSeed = "whip disagree egg satisfy repeat engine envelope federal toward shoulder cattle rare much lava melt";
            var caller = PrivateKeyAccount.CreateFromSeed(callerSeed, testnetNode.ChainId);

            var invokeScriptTx = new InvokeScriptTransaction(testnetNode.ChainId, caller.PublicKey, tokenPortTestnet, "withdraw",
                new List<object> { txBytes, (long)blockHeight, merkleProof }, null, 0.005m, Assets.WAVES);
            invokeScriptTx.Sign(caller);
            Console.WriteLine(invokeScriptTx.GetJsonWithSignature().ToJson());

            // testnetNode.Broadcast(invokeScriptTx);
        }

        public static byte[] GenerateMerkleProof(Node node, string txId)
        {
            var height = node.GetTransactionHeight(txId);

            var txIds = node.GetObject($"blocks/at/{height}")
                            .GetObjects("transactions")
                            .Select(tx => tx.GetString("id").FromBase58())
                            .ToList();

            var LeafPrefix = (byte)0;
            var InternalNodePrefix = (byte)1;

            var oldSize = txIds.Count;

            var newSize = 1;
            while (newSize < oldSize)
            {
                newSize *= 2;
            }

            for (int i = 0; i < newSize - oldSize; i++)
            {
                txIds.Add(new byte[0]);
            }

            var tree = Enumerable.Repeat(new byte[0], newSize * 2 - 1).ToList();

            for (int i = 0; i < txIds.Count; i++)
            {
                var t = new byte[1 + txIds[i].Length];
                t[0] = LeafPrefix;
                if (txIds[i].Length > 0)
                {
                    txIds[i].CopyTo(t, 1);
                }
                tree[i + newSize - 1] = AddressEncoding.FastHash(t, 0, t.Length);
            }

            for (int i = newSize - 2; i >= 0; i--)
            {
                var a = tree[2 * i + 1];
                var b = tree[2 * i + 2];

                var t = new byte[1 + a.Length + b.Length];
                t[0] = InternalNodePrefix;
                if (a.Length > 0)
                    a.CopyTo(t, 1);

                if (b.Length > 0)
                    b.CopyTo(t, 1 + a.Length);

                tree[i] = AddressEncoding.FastHash(t, 0, t.Length);
            }

            // generate proof

            var val = txId.FromBase58();

            var LeftSide = (byte)0;
            var RightSide = (byte)1;

            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);

            var treeSize = tree.Count;
            var arrSize = (treeSize + 1) / 2;


            var z = new byte[1 + val.Length];
            z[0] = 0;
            if (val.Length > 0)
            {
                val.CopyTo(z, 1);
            }

            if (!tree.Skip(arrSize - 1)
                            .Select(x => x.ToBase58())
                            .ToList()
                            .Contains(AddressEncoding.FastHash(z, 0, z.Length).ToBase58()))
            {
                throw new Exception("The list doesn't contain this value");
            }

            var index = tree.Skip(arrSize - 1)
                                    .Select(x => x.ToBase58())
                                    .ToList()
                                    .IndexOf(AddressEncoding.FastHash(z, 0, z.Length).ToBase58());

            index += arrSize - 1;

            while (index != 0)
            {
                var side = (index % 2 == 1) ? LeftSide : RightSide;
                var proofIndex = (side == LeftSide) ? index + 1 : index - 1;
                var len = /* !!! */ (byte)tree[proofIndex].Length;

                writer.Write(side);
                writer.Write(len);
                writer.Write(tree[proofIndex]);

                index = (index - 1) / 2;
            }

            return stream.ToArray();
        }

        public static void SetTokenPort()
        {
            var node = new Node(Node.TestNetChainId);
            var chainId = 'T';
            var tokenPortSeed = "seed take purity craft away cake month layer napkin nasty void entire theme slam explain";
            var tokenPort = PrivateKeyAccount.CreateFromSeed(tokenPortSeed, chainId);

            node.PutData(tokenPort, new Dictionary<string, object>
            {
                { "tokenPortInOtherChain", "3Mgqt4HLefP3bafouR4gXPrP9EgU4pajTct".FromBase58() },
                { "chainCollector", "3N6VvqXLuZ4uxWG6E92orif1KR61bqF6JCk".FromBase58()},
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
    }
}
