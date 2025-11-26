using System;
using System.Collections.Generic;
using System.Linq;
using PortfolioOptimizer.App.Services;

namespace PortfolioOptimizer.App.Models;

/// <summary>
/// Représente un actif avec ses prix historiques et les statistiques calculées.
/// </summary>
public class Asset
{
    private readonly DataProvider _provider = new DataProvider();

    public string Ticker { get; }
    public List<double> HistoricalPrices { get; private set; } = new();
    // Dates correspondantes aux prix historiques (UTC). Peut être vide si non fournies.
    public List<DateTime> HistoricalDates { get; private set; } = new();
    public List<double> Returns { get; private set; } = new();
    public double ExpectedReturn { get; private set; }
    public double Volatility { get; private set; }

    public Asset(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("ticker");

        Ticker = ticker;

    // Charger les prix via le DataProvider
    HistoricalPrices = _provider.GetHistoricalPrices(ticker) ?? new List<double>();

    // Calculer les rendements et les statistiques
    ComputeReturns();
    ComputeStatistics();
    }

    /// <summary>
    /// Constructeur alternatif : crée un Asset à partir d'une série de prix déjà récupérée.
    /// Utile lorsque l'appel au fournisseur de données doit être fait avec des paramètres (range/interval).
    /// </summary>
    public Asset(string ticker, List<double> historicalPrices)
    {
        if (string.IsNullOrWhiteSpace(ticker)) throw new ArgumentException("ticker");
        Ticker = ticker;
        HistoricalPrices = historicalPrices ?? new List<double>();
        HistoricalDates = new List<DateTime>();
        ComputeReturns();
        ComputeStatistics();
    }

    /// <summary>
    /// Constructeur avec dates associées aux prix (utile pour l'alignement par période).
    /// </summary>
    public Asset(string ticker, List<double> historicalPrices, List<DateTime> historicalDates)
    {
        if (string.IsNullOrWhiteSpace(ticker)) throw new ArgumentException("ticker");
        Ticker = ticker;
        HistoricalPrices = historicalPrices ?? new List<double>();
        HistoricalDates = historicalDates ?? new List<DateTime>();
        ComputeReturns();
        ComputeStatistics();
    }

    /// <summary>
    /// Constructeur utile pour reconstruire un actif à partir d'informations synthétiques (ER, vol) sans séries historiques.
    /// </summary>
    public Asset(string ticker, double expectedReturn, double volatility)
    {
        if (string.IsNullOrWhiteSpace(ticker)) throw new ArgumentException("ticker");
        Ticker = ticker;
        HistoricalPrices = new List<double>();
        HistoricalDates = new List<DateTime>();
        Returns = new List<double>();
        ExpectedReturn = expectedReturn;
        Volatility = volatility;
    }

    public void ComputeReturns()
    {
        Returns = HistoricalPrices != null && HistoricalPrices.Count > 1
            ? _provider.GetReturns(HistoricalPrices)
            : new List<double>();
    }

    public void ComputeStatistics()
    {
        if (Returns == null || Returns.Count == 0)
        {
            ExpectedReturn = 0;
            Volatility = 0;
            return;
        }

    // Utilise les rendements simples et annualise (approx.)
    var mean = Returns.Average();
    ExpectedReturn = mean * 252.0; // annualiser

    // variance (population)
    var variance = Returns.Select(r => (r - mean) * (r - mean)).Average();
    Volatility = Math.Sqrt(variance * 252.0);
    }
}
