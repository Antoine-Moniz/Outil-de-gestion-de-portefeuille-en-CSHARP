using System;
using System.Collections.Generic;
using System.Linq;

namespace PortfolioOptimizer.App.Models;

/// <summary>
/// Représente un portefeuille composé d'actifs et de poids associés.
/// Fournit des méthodes pour calculer rendement, volatilité et ratio de Sharpe.
/// </summary>
public class Portfolio
{
    public List<Asset> Assets { get; }
    public List<double> Weights { get; }

    public Portfolio(List<Asset> assets, List<double> weights)
    {
        if (assets == null) throw new ArgumentNullException(nameof(assets));
        if (weights == null) throw new ArgumentNullException(nameof(weights));
        if (assets.Count != weights.Count) throw new ArgumentException("Assets and weights must have the same length");

        // Vérifier les poids (>=0) et somme ~1
        foreach (var w in weights)
        {
            if (double.IsNaN(w) || double.IsInfinity(w)) throw new ArgumentException("Invalid weight value");
            if (w < -1e-12) throw new ArgumentException("Weights must be non-negative");
        }

        var sum = weights.Sum();
        if (Math.Abs(sum - 1.0) > 1e-6)
            throw new ArgumentException($"Weights must sum to 1. Current sum={sum}");

        Assets = assets;
        Weights = weights;
    }

    /// <summary>
    /// Rendement annualisé du portefeuille (somme pondérée des ExpectedReturn des actifs).
    /// </summary>
    public double ComputePortfolioReturn()
    {
        double r = 0.0;
        for (int i = 0; i < Assets.Count; i++)
            r += Weights[i] * Assets[i].ExpectedReturn;
        return r;
    }

    /// <summary>
    /// Volatilité annualisée du portefeuille calculée à partir des séries de rendements des actifs.
    /// Alignement : on prend les dernières N observations où N = min(Returns.Count).
    /// </summary>
    public double ComputePortfolioVolatility()
    {
        int m = Assets.Count;
        if (m == 0) return 0.0;

        // déterminer longueur minimale des séries de rendements
        int N = Assets.Min(a => a.Returns?.Count ?? 0);
        if (N <= 0) return 0.0;

        // construire matrice des rendements (m x N) en alignant sur la fin
        var series = new double[m][];
        for (int i = 0; i < m; i++)
        {
            var ret = Assets[i].Returns ?? new List<double>();
            series[i] = new double[N];
            int offset = ret.Count - N;
            for (int j = 0; j < N; j++) series[i][j] = ret[offset + j];
        }

        // calcul des moyennes (journalières)
        var means = new double[m];
        for (int i = 0; i < m; i++) means[i] = series[i].Average();

        // covariance (population) matrice m x m
        var cov = new double[m, m];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double acc = 0.0;
                for (int t = 0; t < N; t++) acc += (series[i][t] - means[i]) * (series[j][t] - means[j]);
                acc /= N; // population covariance
                cov[i, j] = acc;
                cov[j, i] = acc;
            }
        }

        // annualiser la covariance (approx. 252 jours)
        double factor = 252.0;
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++) cov[i, j] *= factor;

        // variance portefeuille = w^T * cov * w
        double var = 0.0;
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
                var += Weights[i] * Weights[j] * cov[i, j];

        return Math.Sqrt(Math.Max(0.0, var));
    }

    /// <summary>
    /// Ratio de Sharpe annualisé (utilise rendement annualisé et volatilité annualisée).
    /// </summary>
    public double ComputeSharpeRatio(double rf)
    {
        var rp = ComputePortfolioReturn();
        var vol = ComputePortfolioVolatility();
        if (vol <= 0) return double.NaN;
        return (rp - rf) / vol;
    }

    /// <summary>
    /// Placeholder : appelle un Optimiseur externe pour optimiser les poids.
    /// </summary>
    public void Optimize()
    {
        throw new NotImplementedException("Optimizer not implemented yet. This method should call the Optimizer component.");
    }
}
