using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.App.Utils;

/// <summary>
/// Gestion simple de sauvegarde / chargement de portefeuilles en CSV.
/// Format CSV : Ticker,Weight,ExpectedReturn,Volatility
/// Lors du chargement, on reconstruit des séries de prix synthétiques à partir
/// des statistiques sauvegardées pour permettre la recomputation des métriques.
/// </summary>
public static class FileManager
{
    private sealed class Row
    {
        public string Ticker { get; set; } = string.Empty;
        public double Weight { get; set; }
        public double ExpectedReturn { get; set; }
        public double Volatility { get; set; }
    }

    /// <summary>
    /// Sauvegarde le portefeuille en CSV (écrase si existant).
    /// </summary>
    public static void SavePortfolio(Portfolio p, string path)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        // Si le fichier a une extension .json, sauvegarder en JSON détaillé
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var dto = new
            {
                Assets = p.Assets.Select((a, idx) => new
                {
                    Ticker = a.Ticker,
                    Weight = p.Weights[idx],
                    HistoricalPrices = a.HistoricalPrices
                }).ToList()
            };

            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(dto, opts);
            File.WriteAllText(path, json);
            return;
        }

        // Sinon sauvegarde en CSV simple
        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteHeader<Row>();
        csv.NextRecord();

        for (int i = 0; i < p.Assets.Count; i++)
        {
            var a = p.Assets[i];
            var r = new Row
            {
                Ticker = a.Ticker,
                Weight = p.Weights[i],
                ExpectedReturn = a.ExpectedReturn,
                Volatility = a.Volatility
            };
            csv.WriteRecord(r);
            csv.NextRecord();
        }
    }

    /// <summary>
    /// Charge un portefeuille depuis un CSV. Lance FileNotFoundException si absent.
    /// Reconstruit des séries de prix synthétiques (252 jours) à partir des stats
    /// sauvegardées pour recréer des objets Asset compatibles.
    /// </summary>
    public static Portfolio LoadPortfolio(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Portfolio file not found", path);

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return LoadPortfolioFromJson(path);

        return LoadPortfolioFromCsv(path);
    }

    private static Portfolio LoadPortfolioFromJson(string path)
    {
        var txt = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(txt);
        var root = doc.RootElement;
        var assets = new List<Asset>();
        var weights = new List<double>();

        if (!root.TryGetProperty("Assets", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new Portfolio(assets, weights);

        foreach (var el in arr.EnumerateArray())
        {
            var parsed = ParseAssetElement(el);
            assets.Add(parsed.asset);
            weights.Add(parsed.weight);
        }

        return new Portfolio(assets, weights);
    }

    private static Portfolio LoadPortfolioFromCsv(string path)
    {
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var rows = new List<Row>();

        // Protection contre fichier vide
        if (!csv.Read()) return new Portfolio(new List<Asset>(), new List<double>());

        csv.ReadHeader();
        while (csv.Read())
        {
            var ticker = csv.GetField("Ticker") ?? string.Empty;
            var weight = csv.GetField<double?>("Weight") ?? 0.0;
            var expected = csv.GetField<double?>("ExpectedReturn") ?? 0.0;
            var vol = csv.GetField<double?>("Volatility") ?? 0.0;

            rows.Add(new Row { Ticker = ticker, Weight = weight, ExpectedReturn = expected, Volatility = vol });
        }

        // Reconstruire des séries de prix synthétiques à partir des stats
        var assets2 = new List<Asset>();
        var weights2 = new List<double>();
        const int N = 252; // jours de trading par an
        foreach (var r in rows)
        {
            // cible : série de rendements journaliers avec moyenne et écart-type donnés
            double dailyMean = r.ExpectedReturn / 252.0;
            double dailyStd = r.Volatility / Math.Sqrt(252.0);

            // construire une série simple alternant + et - écart-type autour de la moyenne
            var returns = new List<double>(N);
            for (int i = 0; i < N; i++)
            {
                returns.Add(dailyMean + ((i % 2 == 0) ? dailyStd : -dailyStd));
            }

            // convertir en série de prix, départ à 100.0
            var prices = new List<double>(N + 1) { 100.0 };
            for (int i = 0; i < N; i++) prices.Add(prices[^1] * (1.0 + returns[i]));

            var a = new Asset(r.Ticker, prices);
            assets2.Add(a);
            weights2.Add(r.Weight);
        }

        return new Portfolio(assets2, weights2);
    }

    private static (Asset asset, double weight) ParseAssetElement(JsonElement el)
    {
        string ticker = string.Empty;
        if (el.TryGetProperty("Ticker", out var tEl) && tEl.ValueKind == JsonValueKind.String)
            ticker = tEl.GetString() ?? string.Empty;

        double weight = 0.0;
        if (el.TryGetProperty("Weight", out var wEl) && wEl.ValueKind == JsonValueKind.Number)
            weight = wEl.GetDouble();

        var prices = new List<double>();
        if (el.TryGetProperty("HistoricalPrices", out var hp) && hp.ValueKind == JsonValueKind.Array)
        {
            foreach (var pval in hp.EnumerateArray())
            {
                if (pval.ValueKind == JsonValueKind.Number && pval.TryGetDouble(out var d)) prices.Add(d);
            }
        }

        return (new Asset(ticker, prices), weight);
    }
}
