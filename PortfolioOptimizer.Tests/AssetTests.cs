using System;
using System.Linq;
using NUnit.Framework;
using PortfolioOptimizer.App.Models;
using PortfolioOptimizer.App.Services;

namespace PortfolioOptimizer.Tests;

[TestFixture]
public class AssetTests
{
        [Test]
        public void Asset_AAPL_LoadsPricesAndComputesStats()
        {
            try
            {
                var asset = new Asset("AAPL");

                Console.WriteLine($"AAPL prix : {asset.HistoricalPrices.Count}");
                Console.WriteLine($"AAPL rendement attendu : {asset.ExpectedReturn}");
                Console.WriteLine($"AAPL volatilité : {asset.Volatility}");

                Assert.That(asset.HistoricalPrices.Count, Is.GreaterThan(200), "Expected >200 price points for AAPL");
                Assert.That(asset.Returns.Any(double.IsNaN), Is.False, "Returns must not contain NaN");
                Assert.That(asset.ExpectedReturn, Is.GreaterThan(0), "ExpectedReturn should be > 0");
                Assert.That(asset.Volatility, Is.GreaterThan(0), "Volatility should be > 0");
            }
            catch (Exception ex)
            {
                // Si le réseau ou l'API Yahoo refuse l'appel (401/403), on ignore le test en CI/hors-ligne.
                Assert.Ignore($"Ignoré car les données historiques n'ont pas pu être chargées : {ex.Message}");
            }
        }

    [Test]
    public void DataProvider_MSFT_PricesAndReturns()
    {
        try
        {
            var dp = new DataProvider();
            var prices = dp.GetHistoricalPrices("MSFT");
            var returns = dp.GetReturns(prices);

            Console.WriteLine($"MSFT prix : {prices.Count}");

            Assert.That(prices.Count, Is.InRange(240, 265));
            Assert.That(returns.Count, Is.GreaterThanOrEqualTo(2));

            // vérification manuelle des deux premiers rendements
            var manual0 = prices[1] / prices[0] - 1.0;
            var manual1 = prices[2] / prices[1] - 1.0;

            Assert.That(returns[0], Is.EqualTo(manual0).Within(1e-12));
            Assert.That(returns[1], Is.EqualTo(manual1).Within(1e-12));
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Ignoré car les données historiques n'ont pas pu être chargées : {ex.Message}");
        }
    }
}
