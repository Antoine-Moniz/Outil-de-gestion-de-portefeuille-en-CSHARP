using System;
using System.Collections.Generic;
using System.Linq;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.App.Services;

/// <summary>
/// Optimiseur simple basé sur une recherche par grille sur le simplex.
/// Conçu pour un petit nombre d'actifs (<=4). Pour plus d'actifs, il faut
/// remplacer par un solveur QP plus performant.
/// </summary>
public class Optimizer
{
    /// <summary>
    /// Résultat d'optimisation contenant poids, rendement, volatilité et Sharpe.
    /// </summary>
    public record OptResult(List<double> Weights, double Return, double Volatility, double Sharpe);

    /// <summary>
    /// Recherche par grille pour maximiser le ratio de Sharpe (wi >= 0, somme = 1).
    /// step : pas de la grille (ex. 0.01 pour 1%).
    /// Limitation : la complexité augmente comme O((1/step)^{n-1}).
    /// </summary>
    public OptResult OptimizeMaxSharpe(List<Asset> assets, double rf = 0.0, double step = 0.01)
    {
        if (assets == null) throw new ArgumentNullException(nameof(assets));
        int n = assets.Count;
        if (n == 0) throw new ArgumentException("At least one asset required");
        if (n > 6) throw new NotSupportedException("Grid search is not supported for more than 6 assets. Use a dedicated optimizer.");

    var best = (Weights: (List<double>?)null, Sharpe: double.NegativeInfinity, Return: 0.0, Vol: 0.0);

    // générer les poids récursivement
        void Recurse(int idx, List<double> current, double remaining)
        {
            if (idx == n - 1)
            {
        // le dernier poids est le reste
                var w = new List<double>(current) { Math.Round(remaining, 12) };
        if (w.Any(x => x < -1e-12)) return; // invalide
        // normaliser les petites erreurs d'arrondi
                var sum = w.Sum();
                if (Math.Abs(sum - 1.0) > 1e-6) return;

                var port = new Portfolio(assets, w);
                var ret = port.ComputePortfolioReturn();
                var vol = port.ComputePortfolioVolatility();
                var sharpe = (vol > 0) ? (ret - rf) / vol : double.NegativeInfinity;
                if (sharpe > best.Sharpe)
                {
                    best = (w, sharpe, ret, vol);
                }
                return;
            }

            for (double x = 0.0; x <= remaining + 1e-12; x = Math.Round(x + step, 12))
            {
                // garantir la stabilité numérique
                if (x < -1e-12) continue;
                current.Add(x);
                Recurse(idx + 1, current, Math.Round(remaining - x, 12));
                current.RemoveAt(current.Count - 1);
            }
        }

        Recurse(0, new List<double>(), 1.0);

        if (best.Weights == null)
            throw new InvalidOperationException("No feasible weights found with the given grid");

        return new OptResult(best.Weights, best.Return, best.Vol, best.Sharpe);
    }

    /// <summary>
    /// Calcule une série de points (rendement, volatilité) pour la frontière efficiente
    /// en parcourant la grille de poids et en retournant les (r, sigma) uniques triés.
    /// </summary>
    public List<(double Return, double Volatility, List<double> Weights)> EfficientFrontier(List<Asset> assets, double step = 0.01)
    {
        if (assets == null) throw new ArgumentNullException(nameof(assets));
        int n = assets.Count;
        if (n == 0) return new List<(double, double, List<double>)>();
        if (n > 6) throw new NotSupportedException("Grid frontier is not supported for more than 6 assets.");

        var points = new List<(double r, double v, List<double> w)>();

        void Recurse(int idx, List<double> current, double remaining)
        {
            if (idx == n - 1)
            {
                var w = new List<double>(current) { Math.Round(remaining, 12) };
                if (w.Any(x => x < -1e-12)) return;
                var port = new Portfolio(assets, w);
                var r = port.ComputePortfolioReturn();
                var v = port.ComputePortfolioVolatility();
                points.Add((r, v, new List<double>(w)));
                return;
            }

            for (double x = 0.0; x <= remaining + 1e-12; x = Math.Round(x + step, 12))
            {
                current.Add(x);
                Recurse(idx + 1, current, Math.Round(remaining - x, 12));
                current.RemoveAt(current.Count - 1);
            }
        }

        Recurse(0, new List<double>(), 1.0);

    // supprimer les points dominés / dupliqués via l'arrondi
        var unique = points
            .Select(p => (r: Math.Round(p.r, 12), v: Math.Round(p.v, 12), w: p.w))
            .DistinctBy(p => (p.r, p.v))
            .OrderBy(p => p.v)
            .ThenByDescending(p => p.r)
            .ToList();

        return unique.Select(u => (u.r, u.v, u.w)).ToList();
    }

    /// <summary>
    /// Recherche par grille pour minimiser la volatilité du portefeuille (wi >= 0, somme = 1).
    /// Renvoie la configuration de poids qui donne la volatilité minimale.
    /// Utile comme exemple du portefeuille de variance minimale (global minimum variance portfolio).
    /// </summary>
    public OptResult OptimizeMinVariance(List<Asset> assets, double step = 0.01)
    {
        if (assets == null) throw new ArgumentNullException(nameof(assets));
        int n = assets.Count;
        if (n == 0) throw new ArgumentException("At least one asset required");
        if (n > 6) throw new NotSupportedException("Grid search is not supported for more than 6 assets. Use a dedicated optimizer.");

    var best = (Weights: (List<double>?)null, Vol: double.PositiveInfinity, Return: 0.0);

        void Recurse(int idx, List<double> current, double remaining)
        {
            if (idx == n - 1)
            {
                var w = new List<double>(current) { Math.Round(remaining, 12) };
                if (w.Any(x => x < -1e-12)) return;
                var port = new Portfolio(assets, w);
                var ret = port.ComputePortfolioReturn();
                var vol = port.ComputePortfolioVolatility();
                if (vol < best.Vol)
                {
                    best = (w, vol, ret);
                }
                return;
            }

            for (double x = 0.0; x <= remaining + 1e-12; x = Math.Round(x + step, 12))
            {
                current.Add(x);
                Recurse(idx + 1, current, Math.Round(remaining - x, 12));
                current.RemoveAt(current.Count - 1);
            }
        }

        Recurse(0, new List<double>(), 1.0);

        if (best.Weights == null)
            throw new InvalidOperationException("No feasible weights found with the given grid");

        // calculer sharpe (rf = 0) pour compatibilité du record
        var sharpe = best.Vol > 0 ? (best.Return / best.Vol) : double.NaN;
        return new OptResult(best.Weights, best.Return, best.Vol, sharpe);
    }
}
