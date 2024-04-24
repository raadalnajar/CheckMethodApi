using NBitcoin.Protocol;
using Newtonsoft.Json;
using RSA;
using System.Security.Cryptography;

using System.Net;
using System.Text;
using WebApplication1.Data;
using WebApplication1.Model.CryptocurrencyModel.UserBalance;

namespace WebApplication1.Model.CryptocurrencyModel
{
    public class Cryptocurrency
    {
        MyDbContext _context;
        private List<Transaction> _currenttransaction = new List<Transaction>();
        private List<Block> _Chain = new List<Block>();
        private List<Node> _Nodes = new List<Node>();
        private Block _lastblock => _Chain.Last();
        public string _nodeId { get; private set; }
        static int blockcount = 0;
        static decimal reward = 50;
        static string minerprivatekey = "";
        static Wallet _minerwallet = RSA.Rsa.KeyGeneration();

        public Cryptocurrency(MyDbContext myDb)
        {
            _context = myDb;
            minerprivatekey = _minerwallet.PrivateKey;
            _nodeId = _minerwallet.PublicKey;

            var Transaction = new Transaction() { sender = "0", recipient = _nodeId, Amount = 50, Fee = 0, signature = "" };
            _currenttransaction.Add(Transaction);
            CreatNewBlock(proof: 100, previousHash: "1");


        }

        private Block CreatNewBlock(int proof, string previousHash)
        {
            var block = new Block()
            {
                Index = _Chain.Count,
                Timestamp = DateTime.Now,
                Transactions = _currenttransaction.ToList(),
                Proof = proof,
                PreviousHash = previousHash ?? GetHash(_Chain.Last())
            };

            _currenttransaction.Clear();
            _Chain.Add(block);

            return block;
        }
       

        private string GetHash(Block block)
        {
            string blocktext = JsonConvert.SerializeObject(block);
            return Getsh256(blocktext);

        }

        private string Getsh256(string Data)
        {
            var sh256 = new SHA256Managed();
            var hashbuilder = new StringBuilder();
            byte[] bytes = Encoding.Unicode.GetBytes(Data);
            byte[] hash = sh256.ComputeHash(bytes);
            foreach (byte x in hash)

                hashbuilder.Append($"{x:x2}");
            return hashbuilder.ToString();

        }
        private int CreatProofOfWork(string previoushash)
        {
            int proof = 0;
            while (!isValidProof(_currenttransaction, proof, previoushash))
                proof++;

            if (blockcount == 10)
            {

                blockcount = 0;
                reward = reward / 2;
            }
            var transaction = new Transaction() { sender = "0", recipient = _nodeId, Amount = reward, Fee = 0, signature = "" };
            _currenttransaction.Add(transaction);
            return proof;

        }
        private bool isValidProof(List<Transaction> transaction, int proof, string previoushash)
        {
            var signtures = transaction.Select(x => x.signature).ToList();
            string guess = $"{signtures}{proof}{previoushash}";
            string result = Getsh256(guess);
            return result.StartsWith("00");
        }

        private List<Transaction> thetransacionByadress(string owneradress)
        {
            List<Transaction> thetransaction = new List<Transaction>();
            foreach (var block in _Chain.OrderByDescending(x => x.Index))
            {
                var ownertransaction = block.Transactions.Where(x => x.sender == owneradress || x.recipient == owneradress).ToList();
                thetransaction.AddRange(ownertransaction);
            }
            return thetransaction;
        }
        public bool HasBalance(Transaction transaction)
        {
            var transe = thetransacionByadress(transaction.sender);
            // balance this for use email
            var balanceResponse = GetBalanceByEmailAndPublicKey(transaction.sender).Result; // Wait for the result
            var userEmail = balanceResponse.email;
            var balance = balanceResponse.balanceitem;
            foreach (var item in transe)
            {

                if (item.recipient == transaction.sender)
                {
                    balance = balance += item.Amount;
                }
                else
                {
                    balance = balance -= item.Amount;
                }

            }
            var userAccount = _context.balances.FirstOrDefault(x => x.publickey == transaction.sender);
            if (userAccount != null)
            {
                userAccount.balanceitem = balance;
                _context.SaveChanges();
            }
            return balance >= (transaction.Amount + transaction.Fee);
        }
        public void AddTransaction(Transaction transaction)
        {
            _currenttransaction.Add(transaction);
            if (transaction.sender != _nodeId)
            {
                _currenttransaction.Add(new Transaction
                {
                    sender = transaction.sender,
                    recipient = _nodeId,
                    Amount = transaction.Fee,
                    Fee = 0,
                    signature = ""
                });
            }
        }
      
        private bool IsValidChain(List<Block> chain)
        {
            Block block = null;
            Block lastblock = chain.First();
            int currentIndex = 1; 
            while (currentIndex < chain.Count)
            {
                block = chain.ElementAt(currentIndex);
                if (block.PreviousHash != GetHash(lastblock))
                    return false;
                if (!isValidProof(block.Transactions, block.Proof, lastblock.PreviousHash))
                    return false;
                lastblock = block;
                currentIndex++;
            }
            return true;
        }
        internal Block mine()
        {
            int proof = CreatProofOfWork(_lastblock.PreviousHash);
            Block block = CreatNewBlock(proof, _lastblock.PreviousHash);
            return block;
        }
        internal string GetFullChain()
        {
            var response = new
            {
                chain = _Chain.ToArray(),
                length = _Chain.Count
            };
            return JsonConvert.SerializeObject(_Chain);
        }
        internal string Registernodes(string[] nodes)
        {
            var builder = new StringBuilder();
            foreach (string node in nodes)
            {
                string url = node;// $"http://{node}";
                RegisterNode(url);
                builder.Append($"{url},");

            }
            builder.Insert(0, $"{nodes.Count()} new node have been added:");
            string result = builder.ToString();
            return result.Substring(0, result.Length - 2);
        }
        private void RegisterNode(string address)
        {
            _Nodes.Add(new Node { Address = new Uri(address) });
        }
        private bool resolveconflicts()
        {

            List<Block> newchain = null;
            int maxlenght = _Chain.Count;
            foreach (Node node in _Nodes)
            {
                var url = new Uri(node.Address, "/chain");
                var requist = (HttpWebRequest)WebRequest.Create(url);
                var response = (HttpWebResponse)requist.GetResponse();
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var model = new
                    {
                        chain = new List<Block>(),
                        length = 0
                    };
                    string json = new StreamReader(response.GetResponseStream()).ReadToEnd();
                    var data = JsonConvert.DeserializeAnonymousType(json, model);
                    if (data.chain.Count > _Chain.Count && IsValidChain(data.chain))
                    {
                        maxlenght = data.chain.Count;
                        newchain = data.chain.ToList();
                    }
                }
            }
            if (newchain != null)
            {
                _Chain = newchain;
                return true;
            }
            return false;
        } 
        internal object Consensus()
        {
            bool replaced = resolveconflicts();
            string message = replaced ? "was replaced" : "is authoritive";
            var response = new
            {
                message = $"our chain {message}",
                chain = _Chain
            };
            return response;
        }
   

        internal object CreatTransaction(Transaction transaction)
        {
            var resp = new object();
            var verffied = Verfy_transaction_signature(transaction, transaction.signature, transaction.sender);
            if (verffied == false || transaction.sender == transaction.recipient)
            {
                resp = new
                {
                    message = "invalid transaction"
                };
                return resp;
            }
            if (HasBalance(transaction) == false)
            {
                resp = new
                {
                    message = "insufficient balance"
                };
                return resp;
            }
            AddTransaction(transaction);
            var blockindex = _lastblock != null ? _lastblock.Index + 1 : 0;
            resp = new
            {
                message = $"transaction will be added to block {blockindex}"
            };
            return resp;
        }

        private bool Verfy_transaction_signature(Transaction transaction, string signedmessage, string publickey)
        {
            string originalmessage = transaction.ToString();
            bool verfied = RSA.Rsa.Verify(publickey, signedmessage, originalmessage);
            return verfied;
        }

        internal List<Transaction> GetTransaction()
        {
            return _currenttransaction;
        }
        internal List<Block> Getblocks()
        {
            return _Chain;
        }
        internal List<Node> GetNodes()
        {
            return _Nodes;
        }
        internal Wallet GetminersWallet()
        {
            return _minerwallet;
        }
        public async Task<balance> GetBalanceByEmailAndPublicKey(string transactionPublicKey)
        {
            var userAccount = _context.balances.FirstOrDefault(x => x.publickey == transactionPublicKey);

            if (userAccount != null)
            {
                // You've found the user account, so you can access its email and balance
                var userEmail = userAccount.email;
                var userBalance = userAccount.balanceitem;

                // Create a balance object to return
                var response = new balance
                {
                    id = userAccount.id,
                    email = userEmail,
                    balanceitem = userBalance,
                    publickey = transactionPublicKey,
                };

                return response;
            }

            // If user not found, you may return null or throw an exception as needed
            return null;
        }

    }
}
