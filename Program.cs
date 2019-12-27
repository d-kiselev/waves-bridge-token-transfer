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
            SetTokenPortScript(); return;

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
            var response = stagenetNode.Transfer(Alice, tokenPortStagenet, Assets.WAVES, 0.00000002m, 0.005m, null, Bob.FromBase58()); // Bob.FromBase58() ???
            Thread.Sleep(3000);

            var txId = response.ParseJsonObject().GetString("id");
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
            var merkleProof = new byte[32]; // ???

            // 4. invoke script of tokenPort (in testnet) --> Bob receives money
            var caller = PrivateKeyAccount.CreateFromSeed("seed", testnetNode.ChainId);
            testnetNode.InvokeScript(caller,tokenPortTestnet,"withdraw",new List<object> { txBytes, (long)blockHeight, merkleProof });
        }

        public static void SetTokenPortScript()
        {
            var node = new Node(Node.TestNetChainId);
            var chainId = 'T';
            var tokenPortSeed = "seed take purity craft away cake month layer napkin nasty void entire theme slam explain";
            var tokenPort = PrivateKeyAccount.CreateFromSeed(tokenPortSeed, chainId);

            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream("waves-bridge-token-transfer.Resources.TokenPort.ride");
            var reader = new StreamReader(stream);
            var tokenPortScript = reader.ReadToEnd();
            
            Console.WriteLine(tokenPortScript);
            return;

            var compiledScript = node.CompileCode(tokenPortScript);
            node.SetScript(tokenPort, compiledScript);
        }
    }
}
