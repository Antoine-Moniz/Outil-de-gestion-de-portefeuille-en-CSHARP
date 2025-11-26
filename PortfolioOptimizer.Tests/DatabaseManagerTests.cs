using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PortfolioOptimizer.App.Utils;
using PortfolioOptimizer.App.Models;
using System.Collections.Generic;

namespace PortfolioOptimizer.Tests
{
    [TestFixture]
    public class DatabaseManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            // s'assure que la base de données est propre avant chaque test
            var db = Path.Combine(AppContext.BaseDirectory, "portfolio.db");
            try { if (File.Exists(db)) File.Delete(db); } catch { }
            DatabaseManager.InitDatabase();
        }

        [Test]
        public void SaveAndLoadPortfolio_Roundtrip()
        {
            // arrange
            var assets = new List<Asset>
            {
                new Asset("AAA", 0.10, 0.20),
                new Asset("BBB", 0.05, 0.15),
                new Asset("CCC", 0.08, 0.18)
            };
            var weights = new List<double> { 0.4, 0.3, 0.3 };
            var p = new Portfolio(assets, weights);
            var name = "test_portfolio_roundtrip";

            // act
            DatabaseManager.SavePortfolio(p, name);
            var list = DatabaseManager.ListPortfolios();
            var loaded = DatabaseManager.LoadPortfolio(name);

            // assert
            NUnit.Framework.Assert.That(list, NUnit.Framework.Does.Contain(name));
            NUnit.Framework.Assert.That(loaded, NUnit.Framework.Is.Not.Null);
            NUnit.Framework.Assert.That(loaded!.Assets.Count, NUnit.Framework.Is.EqualTo(p.Assets.Count));
            for (int i = 0; i < p.Assets.Count; i++)
            {
                NUnit.Framework.Assert.That(p.Assets[i].Ticker, NUnit.Framework.Is.EqualTo(loaded.Assets[i].Ticker));
                NUnit.Framework.Assert.That(p.Weights[i], NUnit.Framework.Is.EqualTo(loaded.Weights[i]).Within(1e-9));
                NUnit.Framework.Assert.That(p.Assets[i].ExpectedReturn, NUnit.Framework.Is.EqualTo(loaded.Assets[i].ExpectedReturn).Within(1e-9));
                NUnit.Framework.Assert.That(p.Assets[i].Volatility, NUnit.Framework.Is.EqualTo(loaded.Assets[i].Volatility).Within(1e-9));
            }
        }

        [Test]
        public void InitDatabase_Recreates_File_When_Deleted()
        {
            var db = Path.Combine(AppContext.BaseDirectory, "portfolio.db");
            if (File.Exists(db)) File.Delete(db);
            NUnit.Framework.Assert.That(File.Exists(db), NUnit.Framework.Is.False);

            // act
            DatabaseManager.InitDatabase();

            // assert
            NUnit.Framework.Assert.That(File.Exists(db), NUnit.Framework.Is.True);
        }
    }
}
