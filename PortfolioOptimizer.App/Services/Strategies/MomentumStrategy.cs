using System;
using System.Collections.Generic;
using System.Linq;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.App.Services.Strategies
{
    /// <summary>
    /// Momentum : poids proportionnels au rendement moyen sur ~6 mois (env. 126 jours de bourse)
    /// </summary>
    public class MomentumStrategy : InvestmentStrategy
    {
        private readonly int _lookbackDays;

        public MomentumStrategy(int lookbackDays = 126)
        {
            _lookbackDays = lookbackDays;
        }

        public override Dictionary<string, double> ComputeWeights(List<Asset> assets)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in assets ?? Enumerable.Empty<Asset>())
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Ticker)) continue;
                var rets = a.Returns ?? new List<double>();
                if (rets.Count == 0)
                {
                    dict[a.Ticker] = 0.0;
                    continue;
                }

                int N = Math.Min(_lookbackDays, rets.Count);
                var last = rets.Skip(rets.Count - N).Take(N).ToList();
                double mean = last.Average();
                // n'utiliser que la momentum positive (momentum négative -> 0)
                dict[a.Ticker] = Math.Max(0.0, mean);
            }

            return dict;
        }
    }
}
