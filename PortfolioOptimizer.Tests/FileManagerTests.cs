using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PortfolioOptimizer.App.Models;
using PortfolioOptimizer.App.Utils;
using System.Collections.Generic;

namespace PortfolioOptimizer.Tests;

[TestFixture]
public class FileManagerTests
{
    private static string _tempFile => Path.Combine(Path.GetTempPath(), "portfolio_test.csv");

    [SetUp]
    public void SetUp()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Test]
    public void SaveAndLoad_Portfolio_PreservesTickersWeightsAndMetrics()
    {
    // créer deux actifs synthétiques avec des séries de prix déterministes
        var pricesA = new List<double> { 100, 101, 102, 103, 104, 105, 106 };
        var pricesB = new List<double> { 50, 51, 52, 53, 54, 55, 56 };

        var a1 = new Asset("AAA", pricesA);
        var a2 = new Asset("BBB", pricesB);

        var assets = new List<Asset> { a1, a2 };
        var weights = new List<double> { 0.6, 0.4 };

        var p = new Portfolio(assets, weights);

    // sauvegarder
    FileManager.SavePortfolio(p, _tempFile);
    Assert.That(File.Exists(_tempFile), Is.True);

        // charger
        var loaded = FileManager.LoadPortfolio(_tempFile);

        // verifier tickers et poids
        Assert.That(loaded.Assets.Count, Is.EqualTo(p.Assets.Count));
        for (int i = 0; i < p.Assets.Count; i++)
        {
            Assert.That(loaded.Assets[i].Ticker, Is.EqualTo(p.Assets[i].Ticker));
            Assert.That(loaded.Weights[i], Is.EqualTo(p.Weights[i]).Within(1e-8));
        }

        // verifier les metriques globales
        var ret1 = p.ComputePortfolioReturn();
        var vol1 = p.ComputePortfolioVolatility();
        var ret2 = loaded.ComputePortfolioReturn();
        var vol2 = loaded.ComputePortfolioVolatility();

        Assert.That(ret2, Is.EqualTo(ret1).Within(1e-6));
        Assert.That(vol2, Is.EqualTo(vol1).Within(1e-6));
    }

    [Test]
    public void LoadPortfolio_MissingFile_ThrowsFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), "does_not_exist_12345.csv");
        if (File.Exists(missing)) File.Delete(missing);
        Assert.Throws<System.IO.FileNotFoundException>(() => FileManager.LoadPortfolio(missing));
    }
}
