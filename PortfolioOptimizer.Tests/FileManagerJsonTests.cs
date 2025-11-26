using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using PortfolioOptimizer.App.Models;
using PortfolioOptimizer.App.Utils;

namespace PortfolioOptimizer.Tests;

[TestFixture]
public class FileManagerJsonTests
{
    private string _tempFile = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"portfolio_json_test_{Guid.NewGuid()}.json");
    }

    [TearDown]
    public void TearDown()
    {
        try { if (!string.IsNullOrEmpty(_tempFile) && File.Exists(_tempFile)) File.Delete(_tempFile); } catch { }
    }

    [Test]
    public void JsonRoundTrip_PreservesPricesTickersAndWeights()
    {
        // créer deux actifs avec séries de prix non triviales
        var pricesA = new List<double> { 100, 101, 102, 103 };
        var pricesB = new List<double> { 50, 51, 52, 53 };

        var a1 = new Asset("AAA", pricesA);
        var a2 = new Asset("BBB", pricesB);
        var assets = new List<Asset> { a1, a2 };
        var weights = new List<double> { 0.7, 0.3 };
        var p = new Portfolio(assets, weights);

    // sauvegarder en JSON
        FileManager.SavePortfolio(p, _tempFile);
        Assert.That(File.Exists(_tempFile), Is.True);

    // charger
        var loaded = FileManager.LoadPortfolio(_tempFile);

    // vérifier tickers et poids
        Assert.That(loaded.Assets.Count, Is.EqualTo(p.Assets.Count));
        for (int i = 0; i < p.Assets.Count; i++)
        {
            Assert.That(loaded.Assets[i].Ticker, Is.EqualTo(p.Assets[i].Ticker));
            Assert.That(loaded.Weights[i], Is.EqualTo(p.Weights[i]).Within(1e-12));

            // prix doivent être identiques
            Assert.That(loaded.Assets[i].HistoricalPrices, Is.EqualTo(p.Assets[i].HistoricalPrices));
        }
    }
}
