using System.Collections.Generic;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.App.Services.Strategies
{
    public abstract class InvestmentStrategy
    {
        /// <summary>
        /// Calcule les poids du portefeuille pour la liste d'actifs fournie.
        /// Retourne un dictionnaire mappant Ticker -> poids (pas forcément normalisés).
        /// </summary>
        public abstract Dictionary<string, double> ComputeWeights(List<Asset> assets);

        /// <summary>
        /// Variante asynchrone qui peut calculer les poids en effectuant des appels réseau (p. ex. récupération de fondamentaux).
        /// L'implémentation par défaut délègue à la méthode synchrone ComputeWeights pour préserver la rétrocompatibilité.
        /// </summary>
        public virtual System.Threading.Tasks.Task<Dictionary<string, double>> ComputeWeightsAsync(List<Asset> assets, System.DateTime? asOf = null)
        {
            return System.Threading.Tasks.Task.FromResult(ComputeWeights(assets));
        }
    }
}
