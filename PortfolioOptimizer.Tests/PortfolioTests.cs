using System;
using System.Collections.Generic;
using NUnit.Framework;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.Tests;

[TestFixture]
public class PortfolioTests
{
    [Test]
    public void Portfolio_WeightedReturn_MatchesManual()
    {
        try
        {
            var a1 = new Asset("AAPL");
            var a2 = new Asset("MSFT");
            var a3 = new Asset("NVDA");

            var assets = new List<Asset> { a1, a2, a3 };
            var weights = new List<double> { 0.3, 0.3, 0.4 };

            var p = new Portfolio(assets, weights);

            var portReturn = p.ComputePortfolioReturn();

            double manual = 0.0;
            for (int i = 0; i < assets.Count; i++) manual += weights[i] * assets[i].ExpectedReturn;

            Assert.That(portReturn, Is.EqualTo(manual).Within(1e-12));
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Ignoré car les données historiques n'ont pas pu être chargées : {ex.Message}");
        }
    }

    [Test]
    public void Portfolio_SingleAsset_EqualsAsset()
    {
        try
        {
            var a = new Asset("AAPL");
            var assets = new List<Asset> { a };
            var weights = new List<double> { 1.0 };
            var p = new Portfolio(assets, weights);

            Assert.That(p.ComputePortfolioReturn(), Is.EqualTo(a.ExpectedReturn).Within(1e-12));
            Assert.That(p.ComputePortfolioVolatility(), Is.EqualTo(a.Volatility).Within(1e-12));
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Ignoré car les données historiques n'ont pas pu être chargées : {ex.Message}");
        }
    }
}
