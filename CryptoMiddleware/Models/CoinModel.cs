using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoMiddleware.Models
{
    public class CoinModel
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public long TotalSupply { get; set; }
        public string ContractAddress { get; set; }
        public int TotalHolder { get; set; }
        public decimal Price { get; set; }
    }
}
