using CryptoMiddleware.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.ServiceModel.Web;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using System.Web.Http.Cors;
using System.Xml.Linq;

namespace CryptoMiddleware
{
    [ServiceContract]
    public interface IService
    {
        [OperationContract]
        [WebInvoke(Method = "GET", ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Wrapped, UriTemplate = "getCoinList")]
        [return: MessageParameter(Name = "Data")]
        List<CoinModel> GetCoinList();
    }

    public class Service : IService
    {
        public List<CoinModel> GetCoinList()
        {
            try
            {

                //List<CoinModel> data = new List<CoinModel>
                //{
                //    new CoinModel
                //    {
                //        Symbol = "BTC",
                //        Name = "BitCoin",
                //        ContractAddress = "123 Boulverad",
                //        Price = (decimal)30000.00
                //    },
                //    new CoinModel
                //    {
                //        Symbol = "ETH",
                //        Name = "Ethereum",
                //        ContractAddress = "Abderer",
                //        Price = (decimal)23.00
                //    },
                //    new CoinModel
                //    {
                //        Symbol = "USDT",
                //        Name = "Usdt",
                //        ContractAddress = "Texas",
                //        Price = (decimal)0.00
                //    }
                //};

                List<CoinModel> data = Global.CoinDictionary.Values.ToList();

                return data;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }



    public class Program
    {
        static HttpClient client = new HttpClient();
        static CancellationTokenSource cts = new CancellationTokenSource();

        static void Main(string[] args)
        {
            Console.WriteLine("Getting Config...");
            GetConfig();
            Console.WriteLine("Config info:");
            Console.WriteLine($"\t Connection String: {Global.ConnectionString}");
            Console.WriteLine($"\t Interval: {Global.Interval}");
            Console.WriteLine($"\t GetCoinListAPI: {Global.GetTopVolumeAPI}");
            Console.WriteLine("Checking Database...");
            DBCheck();
            Console.WriteLine("Database check completed.");

            client.DefaultRequestHeaders.Add("Authorization", "");

            // Start task to retrieve coin from API (CryptoCompare) at interval.
            Task.Factory.StartNew(x => { StartInterval((CancellationToken)x); }, cts.Token, TaskCreationOptions.LongRunning);

            // Host as API.
            WebServiceHost hostWeb = new WebServiceHost(typeof(Service));
            ServiceEndpoint ep = hostWeb.AddServiceEndpoint(typeof(IService), new WebHttpBinding() { CrossDomainScriptAccessEnabled = true }, "");
            ServiceDebugBehavior stp = hostWeb.Description.Behaviors.Find<ServiceDebugBehavior>();
            stp.HttpHelpPageEnabled = false;
            hostWeb.Open();

            Console.WriteLine("API Ready: http://localhost:8080");
            Console.ReadLine();
        }

        private static async void StartInterval(CancellationToken cts)
        {
            Thread.Sleep(1000);

            // Loop to retrieve the Top Volume API, based on the Interval in App.Config.
            while (!cts.IsCancellationRequested)
            {
                HttpResponseMessage response = await client.GetAsync(Global.GetTopVolumeAPI);
                if (response.IsSuccessStatusCode)
                {
                    string item = await response.Content.ReadAsStringAsync();
                    JObject json = JObject.Parse(item);
                    foreach (var data in json["Data"])
                    {
                        CoinModel coin = new CoinModel();
                        bool isNew = false;

                        foreach (var coinInfo in data.First)
                        {
                            string sym = (string)coinInfo.SelectToken("Name");

                            if (Global.CoinDictionary.TryGetValue(sym, out coin))
                            {
                                coin.Name = (string)coinInfo.SelectToken("FullName");
                            }
                            else
                            {
                                isNew = true;
                                coin = new CoinModel();
                                coin.Symbol = sym;
                                coin.Name = (string)coinInfo.SelectToken("FullName");
                            }
                        }

                        foreach (var rawData in data["RAW"].First)
                        {
                            coin.Price = (decimal)rawData.SelectToken("PRICE");
                            coin.TotalSupply = (long)rawData.SelectToken("SUPPLY");

                            // Unable to retrieve total holder and contract address from API.
                            coin.TotalHolder = 0;
                            coin.ContractAddress = "0x00";
                        }

                        if (isNew)
                        {
                            Global.CoinDictionary.TryAdd(coin.Symbol, coin);
                        }
                    }
                }

                using (TransactionScope scope = new TransactionScope())
                {
                    using (CryptoMiddlewareDBDataContext db = new CryptoMiddlewareDBDataContext(Global.ConnectionString))
                    {
                        var itemInDB = db.tokens;
                        foreach (var item in Global.CoinDictionary)
                        {
                            // Update the coin in DB if the coin is existed.
                            if (itemInDB.Any(x => x.symbol == item.Key))
                            {
                                token dbItem = itemInDB.SingleOrDefault(x => x.symbol == item.Key);
                                dbItem.name = item.Value.Name;
                                dbItem.total_supply = item.Value.TotalSupply;
                                dbItem.contract_address = item.Value.ContractAddress;
                                dbItem.total_holders = item.Value.TotalHolder;
                                dbItem.price = item.Value.Price;
                            }
                            else
                            {
                                // Add the item to DB if the coin symbol is not exist in DB.
                                token newCoin = new token()
                                {
                                    symbol = item.Key,
                                    name = item.Value.Name,
                                    total_supply = item.Value.TotalSupply,
                                    contract_address = item.Value.ContractAddress,
                                    total_holders = item.Value.TotalHolder,
                                    price = item.Value.Price
                                };
                                db.tokens.InsertOnSubmit(newCoin);
                            }
                        }
                        db.SubmitChanges();
                    }
                    scope.Complete();
                }

                // Debug use.
                foreach (var item in Global.CoinDictionary.Values)
                {
                    Console.WriteLine($"Symbol: {item.Symbol}\tName: {item.Name}\t\tTS: {item.TotalSupply}\t\tCA: {item.ContractAddress}\tTH: {item.TotalHolder}\tPrice: {item.Price}");
                }
                Console.WriteLine("");

                Thread.Sleep(Global.Interval * 1000);
            }
        }

        private static void GetConfig()
        {
            Global.ConnectionString = ConfigurationManager.AppSettings["DB"].ToString();

            if (int.TryParse(ConfigurationManager.AppSettings["Interval"], out int result))
            {
                Global.Interval = result;
            }
            else
            {
                Global.Interval = 300;
            }

            Global.GetTopVolumeAPI = ConfigurationManager.AppSettings["GetTopVolumeAPI"].ToString();
        }

        private static void DBCheck()
        {
            try
            {
                using (CryptoMiddlewareDBDataContext db = new CryptoMiddlewareDBDataContext(Global.ConnectionString))
                {
                    if (!db.DatabaseExists())
                    {
                        Console.WriteLine("Creating database...");
                        db.CreateDatabase();
                        Console.WriteLine("Database created.");
                        //List<token> @tkn = new List<token>()
                        //{
                        //    new token()
                        //    {
                        //        symbol = "VEN",
                        //        name = "Vechain",
                        //        total_supply = 35987113,
                        //        contract_address = "0xd850942ef8811f2a866692a623011bde52a462c1",
                        //        total_holders = 65,
                        //        price = (decimal) 0.00,
                        //    },
                        //    new token()
                        //    {
                        //        symbol = "ZIR",
                        //        name = "Zilliqa",
                        //        total_supply = 53272942,
                        //        contract_address = "0x05f4a42e251f2d52b8ed15e9fedaacfcef1fad27",
                        //        total_holders = 54,
                        //        price = (decimal) 0.00,
                        //    },
                        //    new token()
                        //    {
                        //        symbol = "MKR",
                        //        name = "Maker",
                        //        total_supply = 45987133,
                        //        contract_address = "0x9f8f72aa9304c8b593d555f12ef6589cc3a579a2",
                        //        total_holders = 567,
                        //        price = (decimal) 0.00,
                        //    },
                        //    new token()
                        //    {
                        //        symbol = "BNB",
                        //        name = "Binance",
                        //        total_supply = 16579517,
                        //        contract_address = "0xB8c77482e45F1F44dE1745F52C74426C631bDD52",
                        //        total_holders = 4234234,
                        //        price = (decimal) 0.00,
                        //    },
                        //};
                        //db.tokens.InsertAllOnSubmit(@tkn);
                        //db.SubmitChanges();
                    }
                    else
                    {
                        foreach (var item in db.tokens)
                        {
                            Global.CoinDictionary.TryAdd(item.symbol, new CoinModel()
                            {
                                Symbol = item.symbol,
                                Name = item.name,
                                TotalSupply = item.total_supply,
                                ContractAddress = item.contract_address,
                                TotalHolder = item.total_holders,
                                Price = (decimal)item.price,
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
