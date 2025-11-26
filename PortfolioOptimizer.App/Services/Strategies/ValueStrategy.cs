using System;
using System.Collections.Generic;
using System.Linq;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.App.Services.Strategies
{
    /// <summary>
    /// Stratégie Value : poids inversement proportionnels au ratio P/B récupéré depuis Yahoo.
    /// Si le P/B n'est pas disponible pour un ticker, un proxy basé sur les prix (médiane / courant) est utilisé en repli.
    /// La méthode legacy basée sur un CSV a été supprimée.
    /// </summary>
    public class ValueStrategy : InvestmentStrategy
    {
        // Conserver un wrapper synchrone pour satisfaire l'API abstraite tout en déléguant à l'implémentation asynchrone.
        public override Dictionary<string, double> ComputeWeights(List<Asset> assets)
        {
            // Appel bloquant : acceptable comme shim de compatibilité. Préférer appeler ComputeWeightsAsync depuis le code UI.
            return ComputeWeightsAsync(assets, null).GetAwaiter().GetResult();
        }

        public override async System.Threading.Tasks.Task<Dictionary<string, double>> ComputeWeightsAsync(List<Asset> assets, System.DateTime? asOf = null)
        {
            // Si asOf n'est pas fourni, utiliser la date d'aujourd'hui
            var date = asOf ?? DateTime.Today;
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var fetcher = new ValueFactorFetcher();
            foreach (var a in assets ?? Enumerable.Empty<Asset>())
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Ticker)) continue;
                try
                {
                    var pb = await fetcher.FetchPbAtDateAsync(a.Ticker!, date);
                    if (pb.HasValue && pb.Value > 0)
                    {
                        result[a.Ticker] = 1.0 / pb.Value;
                        continue;
                    }

                    // Repli : si pas de P/B disponible, utiliser un proxy basé sur les prix historiques
                    // score = (prix médian long terme) / (prix courant)
                    // Cela donne un score >1 pour les titres dont le prix courant est inférieur à la médiane ("pas cher").
                    var prices = a.HistoricalPrices ?? new List<double>();
                    if (prices.Count >= 10)
                    {
                        var recentCount = Math.Min(252, prices.Count);
                        var window = prices.Skip(Math.Max(0, prices.Count - recentCount)).ToList();
                        // calcul de la médiane
                        window.Sort();
                        double median;
                        int n = window.Count;
                        if (n % 2 == 1)
                            median = window[n / 2];
                        else
                            median = (window[n / 2 - 1] + window[n / 2]) / 2.0;

                        var current = prices.Last();
                        if (median > 0 && current > 0)
                        {
                            var score = median / current;
                            // éviter valeurs extrêmes: clamp
                            if (double.IsFinite(score) && score > 0)
                                result[a.Ticker] = Math.Min(score, 10.0); // cap à 10x pour stabilité
                        }
                    }
                }
                catch { }
            }

            return result;
        }
    }
}
