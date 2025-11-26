using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PortfolioOptimizer.App.Models;
using PortfolioOptimizer.App.Utils;
using PortfolioOptimizer.App.Services;

namespace PortfolioOptimizer.Tests.Integration;

/// <summary>
/// Test d'intégration : sauvegarde un portefeuille minimal dans SQLite (sans séries),
/// charge via DatabaseManager.LoadPortfolio, stubbe DataProvider pour fournir des séries
/// factices, remplace les assets et vérifie que PortfolioComparer renvoie des séries non-vides.
/// </summary>
[TestFixture]
public class LoadPortfolioComparisonIntegrationTests
{
    [SetUp]
    public void SetUp()
    {
        var db = Path.Combine(AppContext.BaseDirectory, "portfolio.db");
        try { if (File.Exists(db)) File.Delete(db); } catch { }
        DatabaseManager.InitDatabase();
    }

    private record FakeSeries(List<double> Prices, List<DateTime> Dates);

    private class FakeDataProvider
    {
        // retourne une série synthétique de prix croissants (10 jours)
        public Task<(List<double> Prices, List<DateTime> Timestamps)> GetHistoricalPricesWithTimestampsAsync(string ticker, string range = "1y", string interval = "1d", DateTime? from = null, DateTime? to = null)
        {
            var prices = new List<double>();
            var dates = new List<DateTime>();
            var start = DateTime.UtcNow.Date.AddDays(-20);
            double p = 100.0;
            for (int i = 0; i < 10; i++)
            {
                p *= 1.01 + (i * 0.001); // small growth
                prices.Add(Math.Round(p, 4));
                dates.Add(start.AddDays(i));
            }
            return Task.FromResult<(List<double>, List<DateTime>)>((prices, dates));
        }
    }

    [Test]
    public async Task LoadPortfolio_WithStubbedDataProvider_PopulatesAssetReturns_And_ComparerReturnsNonEmpty()
    {
        // arrange: créer et sauvegarder deux portefeuilles simples (sans séries)
        var a1 = new Asset("TKA", 0.05, 0.10);
        var a2 = new Asset("TKB", 0.03, 0.12);
        var p1 = new Portfolio(new List<Asset> { a1, a2 }, new List<double> { 0.5, 0.5 });
        DatabaseManager.SavePortfolio(p1, "P_TEST_1");

        var b1 = new Asset("TKC", 0.06, 0.15);
        var b2 = new Asset("TKD", 0.02, 0.08);
        var p2 = new Portfolio(new List<Asset> { b1, b2 }, new List<double> { 0.4, 0.6 });
        DatabaseManager.SavePortfolio(p2, "P_TEST_2");

        // act: charger depuis la DB
        var loaded1 = DatabaseManager.LoadPortfolio("P_TEST_1");
        var loaded2 = DatabaseManager.LoadPortfolio("P_TEST_2");

        Assert.That(loaded1, Is.Not.Null);
        Assert.That(loaded2, Is.Not.Null);

        var dp = new FakeDataProvider();

        // simuler la récupération des séries pour chaque ticker et remplacer les assets
        var portfolios = new List<Portfolio> { loaded1!, loaded2! };
        var tickers = portfolios.SelectMany(p => p.Assets.Select(a => a.Ticker)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var fetched = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tickers)
        {
            var res = await dp.GetHistoricalPricesWithTimestampsAsync(t);
            if (res.Prices != null && res.Prices.Count >= 2)
            {
                var newA = new Asset(t, res.Prices, res.Timestamps);
                fetched[t] = newA;
            }
        }

        // Remplacer les assets dans les portefeuilles chargés
        foreach (var p in portfolios)
        {
            for (int i = 0; i < p.Assets.Count; i++)
            {
                var t = p.Assets[i].Ticker;
                if (t != null && fetched.TryGetValue(t, out var na)) p.Assets[i] = na;
            }
        }

        // assert: chaque asset doit avoir des returns non-vides
        foreach (var p in portfolios)
        {
            foreach (var a in p.Assets)
            {
                Assert.That(a.Returns, Is.Not.Null);
                Assert.That(a.Returns.Count, Is.GreaterThan(1), $"Ticker {a.Ticker} should have >1 returns");
            }
        }

        // comparer
        var comp = PortfolioComparer.Compare(portfolios);

        Assert.That(comp, Is.Not.Null);
        Assert.That(comp.CumulativeReturns, Is.Not.Null);
        Assert.That(comp.CumulativeReturns.Count, Is.EqualTo(2));
        Assert.That(comp.CumulativeReturns.All(c => c != null && c.Count > 0));
        Assert.That(comp.Sharpe.Length, Is.EqualTo(2));
        // Sharpe peut être NaN, on vérifie juste qu'il est calculé
    }
}
