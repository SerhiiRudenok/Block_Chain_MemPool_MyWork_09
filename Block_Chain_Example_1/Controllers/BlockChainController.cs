using Block_Chain_Example_1.Hubs;
using Block_Chain_Example_1.Models;
using Block_Chain_Example_1.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NuGet.Packaging.Signing;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;


namespace Block_Chain_Example_1.Controllers
{
    public class BlockChainController : Controller
    {
        private static CancellationTokenSource _miningCts;

        //private static BlockChainService blockChainService = new BlockChainService();
        private static readonly Dictionary<string, BlockChainService> _nodes = new Dictionary<string, BlockChainService>()
        {
            ["A"] = new BlockChainService(),
            ["B"] = new BlockChainService(),
            ["C"] = new BlockChainService(),
        };

        private BlockChainService GetNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                nodeId = "A";
            }
            if (!_nodes.TryGetValue(nodeId, out var node))
            {
                node = new BlockChainService();
                _nodes[nodeId] = node;
            }
            return node;
        }

        // Index
        public IActionResult Index(string nodeId = "A")
        {
            var serv = GetNode(nodeId);
            
            ViewBag.NodeId = nodeId;                                  // Поточний вузол
            ViewBag.Nodes = _nodes.Keys.ToList();                     // Список доступних вузлів

            ViewBag.Valid = serv.IsValid();                           // Перевірити валідність всього блокчейну
            ViewBag.FirstInvalidIndex = serv.GetFirstInvalidIndex();  // Отримати індекс першого невалідного блоку
            ViewBag.Difficulty = serv.Difficulty;                     // Поточна складність майнінгу
            ViewBag.PrivateKey = serv.PrivateKeyXml;                  // Приватний ключ майнера у форматі XML
            ViewBag.PublicKey = serv.PublicKeyXml;                    // Публічний ключ майнера у форматі XML
            ViewBag.MempoolCount = serv.Mempool.Count;                 // Кількість транзакцій у мемпулі
            ViewBag.Mempool = serv.Mempool;                            // Список транзакцій у мемпулі
            ViewBag.Wallets = serv.Wallets.Values.ToList();            // Список зареєстрованих гаманців
            ViewBag.Balances = serv.GetBalances(true);                  // Баланс гаманця майнера
            ViewBag.CurrentReward = serv.GetCurrentBlockReward(serv.Chain.LastOrDefault().Index);     // Поточна нагорода за майнінг блоку
            ViewBag.LastBlockIndex = serv.Chain.LastOrDefault().Index;  // Індекс останнього блоку в ланцюжку
            return View(serv.Chain);
        }


        [HttpPost]
        public IActionResult SetDifficulty(int difficulty, string nodeId)
        {
            if (difficulty < 1) difficulty = 1;
            if (difficulty > 10) difficulty = 10;
            var serv = GetNode(nodeId);
            serv.Difficulty = difficulty;
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult RegisterWallet(string PublicKeyXml, string displayName, string nodeId)    // Реєстрація нового гаманця
        {
            var serv = GetNode(nodeId);
            var wallet = serv.RegisterWallet(PublicKeyXml, displayName);
            return RedirectToAction("Index");
        }


        // Створення нової транзакції
        [HttpPost]
        public IActionResult CreateTransaction(string fromAddress, string toAddress, decimal amount, decimal fee, string privateKey, string note, string nodeId)
        {
            var tx = new Models.Transaction
            {
                FromAddress = fromAddress,
                ToAddress = toAddress,
                Amount = amount,
                Fee = fee,
                Note = note
            };
            var serv = GetNode(nodeId);
            tx.Signature = BlockChainService.SignPayload(tx.CanonicalPayload(), privateKey);
            try
            {
                var balances = serv.GetBalances(true);             // Отримую баланси всіх гаманців з урахуванням мемпулу

                if (!balances.TryGetValue(fromAddress, out var senderBalance))  // Перевіряю гаманець відправника
                {
                    TempData["Error"] = $"Транзакцію відхилено. Гаманець {fromAddress} не знайдено.";
                    return RedirectToAction("Index");
                }

                if (senderBalance < amount + fee)                               // Перевіряю достатність коштів
                {
                    TempData["Error"] = $"Транзакцію відхилено. Недостатньо коштів: доступно {senderBalance}, потрібно {amount + fee}.";
                    return RedirectToAction("Index");
                }

                serv.CreateTransaction(tx);                        // Створюю транзакцію
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Index");
        }


        //Функція майнінгу
        [HttpPost]
        public IActionResult MinePending(string privateKey, string nodeId)     // Майнінг нового блоку з транзакціями з мемпулу
        {
            try
            {
                var serv = GetNode(nodeId);
                var newBlock = serv.MinePending(privateKey);

                int acceptedCount = BroadCastChainBlock(nodeId, newBlock); // вузли, які прийняли
                int totalNodes = _nodes.Count; // включає всі ноди


                TempData["Info"] = $"Блок # {newBlock.Index} розповсюджено. Прийняли: {acceptedCount} вузли з {totalNodes}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index", new { nodeId });
        }




        [HttpPost]
        public IActionResult DemoSetup(string nodeId)    // Демонстрація з двома гаманцями та транзакцією між ними
        {
            var serv = GetNode(nodeId);
            var (Ivan, prvKey1) = serv.CreateWallet("Ivan");
            var (Taras, prvKey2) = serv.CreateWallet("Taras");

            decimal amount = 5.0m;
            decimal fee = 0.5m;

            var tx = new Models.Transaction
            {
                FromAddress = Ivan.Address,
                ToAddress = Taras.Address,
                Amount = amount,
                Fee = fee,
                Note = "Payment for services"
            };

            for (int i = 0; i < 7; i++)
            {
                MinePending(prvKey1, nodeId);
                MinePending(prvKey2, nodeId);
            }

            var sig = BlockChainService.SignPayload(tx.CanonicalPayload(), prvKey1);

            tx.Signature = sig;

            serv.CreateTransaction(tx);

            return RedirectToAction("Index");
        }


        // GET: BlockChainController/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: BlockChainController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: BlockChainController/Edit/5
        public ActionResult Edit(int id, string nodeId)
        {
            var serv = GetNode(nodeId);
            var block = serv.Chain.FirstOrDefault(b => b.Index == id);
            if (block == null)
                return NotFound();

            return View(block);
        }

        // POST: BlockChainController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, Block_Chain_Example_1.Models.Block updatedBlock, string nodeId)
        {
            var serv = GetNode(nodeId);
            var block = serv.Chain.FirstOrDefault(b => b.Index == id);
            if (block == null)
                return NotFound();

            block.Signature = updatedBlock.Signature; // оновити підпис
            block.Hash = updatedBlock.Hash;           // оновити хеш

            ViewBag.Valid = serv.IsValid();

            return RedirectToAction(nameof(Index));
        }

        // Пошук блоку за індексом або хешем
        [HttpGet]
        public IActionResult Search()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Search(string query, string nodeId)
        {
            var serv = GetNode(nodeId);
            var block = serv.FindBlock(query);
            ViewBag.FoundBlock = block;
            ViewBag.IsPost = true;
            return View();
        }


        // Генерація нового приватного ключа RSA-ключа
        [HttpPost]
        public IActionResult GenerateKey(string nodeId)
        {
            // Генерація RSA-ключа
            using var rsa = RSA.Create(512);
            byte[] privateKeyBytes = rsa.ExportRSAPrivateKey();
            string base64Key = Convert.ToBase64String(privateKeyBytes);

            ViewBag.GeneratedKey = base64Key;
            var serv = GetNode(nodeId);
            ViewBag.Difficulty = serv.Difficulty; // коли генерується ключ потрібно зберегти складність майнінгу

            // Повернення на Index.cshtml
            return View("Index", serv.Chain);
        }


        private int BroadCastChainBlock(string nodeId, Block block)      // Розповсюдження нового блоку на інші вузли
        {
            var fromNode = GetNode(nodeId);
            int accepted = 0;
            foreach (var (_nodeId, node) in _nodes)
            {
                if (_nodeId == nodeId) continue;

                try
                {
                    if (node.TryAddExternalBlock(fromNode.Chain))
                        accepted++;
                }
                catch
                {
                    throw new Exception($"Вузол {_nodeId} недоступний.");
                }
            }
            return accepted;
        }
    }
}
