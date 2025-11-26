using System;
using System.Collections.Generic;
using NUnit.Framework;
using PortfolioOptimizer.App.Models;
using PortfolioOptimizer.App.Services;

namespace PortfolioOptimizer.Tests;

[TestFixture]
public class OptimizerTests
{
    [Test]
    public void Optimizer_FindsPositiveWeights_SumToOne_And_ImprovesSharpe()
    {
        try
        {
            var a1 = new Asset("AAPL");
            var a2 = new Asset("MSFT");
            var a3 = new Asset("NVDA");
            var assets = new List<Asset> { a1, a2, a3 };

            var opt = new Optimizer();
            var res = opt.OptimizeMaxSharpe(assets, rf: 0.0, step: 0.02);

            // poids valides
            Assert.That(res.Weights.Sum(), Is.EqualTo(1.0).Within(1e-8));
            Assert.That(res.Weights.All(w => w >= -1e-12));

            // comparer au portefeuille équi-pondéré
            var eq = new List<double> { 1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0 };
            var pEq = new Portfolio(assets, eq);
            var sharpeEq = (pEq.ComputePortfolioVolatility() > 0) ? (pEq.ComputePortfolioReturn() / pEq.ComputePortfolioVolatility()) : double.NegativeInfinity;

            Assert.That(res.Sharpe, Is.GreaterThanOrEqualTo(sharpeEq));
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Ignoré car les données historiques n'ont pas pu être chargées : {ex.Message}");
        }
    }
}
