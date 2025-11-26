using System;
using System.Collections.Generic;
using System.Linq;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.App.Services.Strategies
{
    /// <summary>
    /// Stratégie de carry : les poids sont proportionnels à ExpectedReturn / Volatility pour chaque actif.
    /// </summary>

    public class CarryStrategy : InvestmentStrategy
    {
        public override Dictionary<string, double> ComputeWeights(List<Asset> assets)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in assets ?? Enumerable.Empty<Asset>())
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Ticker)) continue;
                double er = a.ExpectedReturn;
                double vol = a.Volatility;
                if (vol <= 0) dict[a.Ticker] = 0.0;
                else dict[a.Ticker] = er / vol;
            }
            return dict;
        }
    }
}
