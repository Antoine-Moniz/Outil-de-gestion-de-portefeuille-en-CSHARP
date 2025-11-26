using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PortfolioOptimizer.App.Models;
using PortfolioOptimizer.App.Services;

namespace PortfolioOptimizer.Tests;

[TestFixture]
public class Step8UnitTests
{
    [Test]
    public void TestAssetStatistics()
    {
        // Série de prix simple
        var prices = new List<double> { 100.0, 105.0, 110.0 }; // rendements: 0.05, ~0.047619
        var asset = new Asset("TST", prices);

        // calculs manuels
        var returns = new List<double>();
        for (int i = 1; i < prices.Count; i++) returns.Add(prices[i] / prices[i - 1] - 1.0);
        var mean = returns.Average();
        var expectedReturn = mean * 252.0;
        var variance = returns.Select(r => (r - mean) * (r - mean)).Average(); // population
        var expectedVol = Math.Sqrt(variance * 252.0);

        Assert.That(asset.ExpectedReturn, Is.EqualTo(expectedReturn).Within(1e-12));
        Assert.That(asset.Volatility, Is.EqualTo(expectedVol).Within(1e-12));
    }

    [Test]
    public void TestPortfolioComputation()
    {
        // Deux actifs avec séries de rendements différentes
        var pricesA = new List<double> { 100.0, 102.0, 105.0, 107.0 }; // some returns
        var pricesB = new List<double> { 50.0, 51.0, 52.0, 55.0 };
        var a1 = new Asset("A", pricesA);
        var a2 = new Asset("B", pricesB);
        var assets = new List<Asset> { a1, a2 };
        var weights = new List<double> { 0.6, 0.4 };
        var p = new Portfolio(assets, weights);

        // Rendement portefeuille attendu (pondéré simple)
        var expectedReturn = 0.0;
        for (int i = 0; i < assets.Count; i++) expectedReturn += weights[i] * assets[i].ExpectedReturn;
        Assert.That(p.ComputePortfolioReturn(), Is.EqualTo(expectedReturn).Within(1e-12));

        // Volatilité : recalcul manuellement en reproduisant l'algorithme
        int m = assets.Count;
        int N = assets.Min(a => a.Returns.Count);
        Assert.That(N > 0, Is.True);
        var series = new double[m][];
        for (int i = 0; i < m; i++)
        {
            var ret = assets[i].Returns;
            series[i] = new double[N];
            int offset = ret.Count - N;
            for (int j = 0; j < N; j++) series[i][j] = ret[offset + j];
        }
        var means = new double[m];
        for (int i = 0; i < m; i++) means[i] = series[i].Average();
        var cov = new double[m, m];
        for (int i = 0; i < m; i++)
            for (int j = 0; j <= i; j++)
            {
                double acc = 0.0;
                for (int t = 0; t < N; t++) acc += (series[i][t] - means[i]) * (series[j][t] - means[j]);
                acc /= N; // population
                cov[i, j] = acc;
                cov[j, i] = acc;
            }
        double factor = 252.0;
        for (int i = 0; i < m; i++) for (int j = 0; j < m; j++) cov[i, j] *= factor;
        double var = 0.0;
        for (int i = 0; i < m; i++) for (int j = 0; j < m; j++) var += weights[i] * weights[j] * cov[i, j];
        var expectedVol = Math.Sqrt(Math.Max(0.0, var));

        Assert.That(p.ComputePortfolioVolatility(), Is.EqualTo(expectedVol).Within(1e-12));
    }

    [Test]
    public void TestOptimizerConvergence()
    {
        // Trois actifs simples
        var a = new Asset("X", new List<double> { 100, 101, 102, 103 });
        var b = new Asset("Y", new List<double> { 200, 202, 201, 205 });
        var c = new Asset("Z", new List<double> { 50, 51, 52, 54 });
        var assets = new List<Asset> { a, b, c };

    // utiliser le solveur quadratique (tangency) pour une solution rapide
    var w = PortfolioOptimizer.App.Services.QuadraticSolver.TangencyWeights(assets, rf: 0.0, enforceNonNegative: true);
    Assert.That(w.Sum(), Is.EqualTo(1.0).Within(1e-9));
    foreach (var wi in w) Assert.That(wi, Is.GreaterThanOrEqualTo(-1e-9));
    }

    [Test]
    public void TestSharpeImprovement()
    {
        // Réutiliser le même univers d'actifs
        var a = new Asset("X", new List<double> { 100, 101, 102, 103 });
        var b = new Asset("Y", new List<double> { 200, 202, 201, 205 });
        var c = new Asset("Z", new List<double> { 50, 51, 52, 54 });
        var assets = new List<Asset> { a, b, c };

        // portefeuille égalitaire
        var eqWeights = Enumerable.Repeat(1.0 / assets.Count, assets.Count).ToList();
        var eqPortfolio = new Portfolio(assets, eqWeights);
        var eqSharpe = eqPortfolio.ComputeSharpeRatio(0.0);

    // obtenir poids via solveur quadratique
    var w = PortfolioOptimizer.App.Services.QuadraticSolver.TangencyWeights(assets, rf: 0.0, enforceNonNegative: true);
    var pOpt = new Portfolio(assets, w);
    var optSharpe = pOpt.ComputeSharpeRatio(0.0);

    // l'optimisation théorique (tangency) doit améliorer (ou égaler) le Sharpe du portefeuille égalitaire
    Assert.That(optSharpe, Is.GreaterThanOrEqualTo(eqSharpe - 1e-9));
    }
}
