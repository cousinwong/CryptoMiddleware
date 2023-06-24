using CryptoMiddleware.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoMiddleware
{
    public static class Global
    {
        public static string ConnectionString { get; set; }
        public static int Interval { get; set; }
        public static string GetTopVolumeAPI { get; set; }
        public static ConcurrentDictionary<string, CoinModel> CoinDictionary { get; set; } = new ConcurrentDictionary<string, CoinModel>();

    }
}
