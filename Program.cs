using System;
using System.Collections.Generic;
using System.IO;
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
            var stagenetNode = new Node("https://nodes-stagenet.wavesnodes.com/", 'S');

            var chainCollectorTestnet = "3N6VvqXLuZ4uxWG6E92orif1KR61bqF6JCk";
            var tokenPortTestnet = "3Mz3XPi4hQnfVK9ZaA27YwFfiPApPgFnCts";
            var tokenPortStagenet = "3Mgqt4HLefP3bafouR4gXPrP9EgU4pajTct";

            var aliceSeed = "whip disagree egg melt satisfy repeat engine envelope federal toward shoulder cattle rare much lava";
            var Alice = PrivateKeyAccount.CreateFromSeed(aliceSeed, stagenetNode.ChainId); // in stagenet
            var Bob = "3MuR872m3WiW1DRBD8CfoLbZpJgo3xzLyy7"; // in testnet

            // 1. transfer tokens from Alice to TokenPort (in stagenet)
            /*var response = stagenetNode.Transfer(Alice, tokenPortStagenet, Assets.WAVES, 0.00000002m, 0.005m, null, Bob.FromBase58()); // Bob.FromBase58() ???
            Thread.Sleep(10000);

            var txId = response.ParseJsonObject().GetString("id");
            Console.WriteLine($"transfer tx id: {txId}");
            */
            var txId = "3bsuQrwX2QM9g2WqXBEYNs1RUHrSPJzGeJo1Jpj17o2p";

            var blockHeight = stagenetNode.GetTransactionHeight(txId);
            var txBytes = stagenetNode.GetTransactionById(txId).GetBytes();
            var key = blockHeight.ToString() + "_merkleRoot";

            // 2. wait for the chain collector to put Merkle root (in testnet)
            while (true)
            {
                if (testnetNode.GetAddressData(chainCollectorTestnet).ContainsKey(key))
                    break;
                Thread.Sleep(10000);
            }

            // 3. genegare MerkleProof (for transaction in stagenet)
            var merkleProof = GenerateMerkleProof(stagenetNode, txId, blockHeight);

            // 4. invoke script of tokenPort (in testnet) --> Bob receives money
            var callerSeed = "whip disagree egg satisfy repeat engine envelope federal toward shoulder cattle rare much lava melt";
            var caller = PrivateKeyAccount.CreateFromSeed(callerSeed, testnetNode.ChainId);
            testnetNode.InvokeScript(caller,tokenPortTestnet,"withdraw",new List<object> { txBytes, (long)blockHeight, merkleProof });
        }

        public static byte[] GenerateMerkleProof(Node stagenetNode, string txId, int blockHeight)
        {
            return new byte[32];
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
                { "chainCollector", "3N6VvqXLuZ4uxWG6E92orif1KR61bqF6JCk".FromBase58()}
            },0.01m);

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
