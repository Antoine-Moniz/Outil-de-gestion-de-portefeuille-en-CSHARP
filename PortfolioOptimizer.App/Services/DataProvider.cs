using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PortfolioOptimizer.App.Services;

/// <summary>
/// Fournisseur de données qui interroge l'endpoint JSON "chart" v8 de Yahoo Finance.
/// Méthode plus fiable que le téléchargement CSV car évite les mécanismes crumb/cookie.
/// </summary>
public class DataProvider
{
    private static readonly HttpClient _httpClient = new HttpClient();

    static DataProvider()
    {
        // User-Agent pour éviter certains refus (403)
        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }
        catch
        {
            // ignorer
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Récupère les prix de clôture ajustés (si disponibles) ou les closes pour la dernière année.
    /// Utilise l'endpoint : https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range=1y&interval=1d
    /// </summary>
    public List<double> GetHistoricalPrices(string ticker)
    {
        return GetHistoricalPricesAsync(ticker, "1y", "1d").GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchrone : récupère les prix via le chart v8 endpoint et parse le JSON.
    /// </summary>
    /// <summary>
    /// Récupère les prix via le chart v8 endpoint et parse le JSON.
    /// Si from/to sont fournis, on utilise period1/period2 (timestamps unix) plutôt que range.
    /// </summary>
    public async Task<List<double>> GetHistoricalPricesAsync(string ticker, string range = "1y", string interval = "1d", DateTime? from = null, DateTime? to = null)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("ticker");

        string url;
        if (from.HasValue && to.HasValue)
        {
            // utiliser period1/period2 en secondes UTC
            var p1 = new DateTimeOffset(from.Value.ToUniversalTime()).ToUnixTimeSeconds();
            var p2 = new DateTimeOffset(to.Value.ToUniversalTime()).ToUnixTimeSeconds();
            url = $"https://query1.finance.yahoo.com/v8/finance/chart/{ticker}?period1={p1}&period2={p2}&interval={interval}&includePrePost=false&events=div%2Csplit";
        }
        else
        {
            url = $"https://query1.finance.yahoo.com/v8/finance/chart/{ticker}?range={range}&interval={interval}&includePrePost=false&events=div%2Csplit";
        }

        // fetch json (with a single retry)
        var json = await FetchChartJsonAsync(url).ConfigureAwait(false);

        var parsed = ParseChartJson(json);
        return parsed.Prices;
    }

    private async Task<string> FetchChartJsonAsync(string url)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode} when fetching URL: {resp.ReasonPhrase} - {content}");
                }

                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch when (attempt == 0)
            {
                await Task.Delay(500).ConfigureAwait(false);
                continue;
            }
        }

        throw new InvalidOperationException("Impossible de récupérer le JSON après plusieurs tentatives.");
    }

    // Petit type pour renvoyer le résultat parsé
    private record ParsedChart(List<double> Prices, List<long> Timestamps);

    private ParsedChart ParseChartJson(string json)
    {
        using var doc = JsonDocument.Parse(json);

        // chemin : chart -> result[0]
        if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var resultArray) ||
            resultArray.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Structure JSON inattendue : pas de chart/result");
        }

        var result = resultArray[0];

        // timestamps
        List<long> timestamps = new();
        if (result.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var v))
                    timestamps.Add(v);
            }
        }

        // indicators.quote[0].close etc.
        List<double?> closes = new();
        if (result.TryGetProperty("indicators", out var indicators) && indicators.TryGetProperty("quote", out var quoteArr) && quoteArr.GetArrayLength() > 0)
        {
            var quote = quoteArr[0];
            if (quote.TryGetProperty("close", out var closeEl) && closeEl.ValueKind == JsonValueKind.Array)
            {
                closes = new List<double?>();
                foreach (var c in closeEl.EnumerateArray())
                {
                    if (c.ValueKind == JsonValueKind.Number && c.TryGetDouble(out var d))
                        closes.Add(d);
                    else
                        closes.Add(null);
                }
            }
        }

        // indicators.adjclose may exist
        List<double?> adjcloses = new();
        if (result.TryGetProperty("indicators", out var indicators2) && indicators2.TryGetProperty("adjclose", out var adjArr) && adjArr.GetArrayLength() > 0)
        {
            var adj = adjArr[0];
            if (adj.TryGetProperty("adjclose", out var adjEl) && adjEl.ValueKind == JsonValueKind.Array)
            {
                adjcloses = new List<double?>();
                foreach (var a in adjEl.EnumerateArray())
                {
                    if (a.ValueKind == JsonValueKind.Number && a.TryGetDouble(out var d))
                        adjcloses.Add(d);
                    else
                        adjcloses.Add(null);
                }
            }
        }

    // Choisir adjclose si présent sinon close
        var prices = new List<double>();
        int n = Math.Max(timestamps.Count, closes.Count);
        for (int i = 0; i < n; i++)
        {
            double? val = null;
            if (i < adjcloses.Count)
                val = adjcloses[i];
            if (val == null && i < closes.Count)
                val = closes[i];

            if (val.HasValue)
                prices.Add(val.Value);
            // sinon on ignore les jours manquants
        }

        return new ParsedChart(prices, timestamps);
    }

    /// <summary>
    /// Récupère les prix et les timestamps convertis en DateTime (UTC).
    /// Utile pour afficher les bornes de la série renvoyée par Yahoo.
    /// </summary>
    public async Task<(List<double> Prices, List<DateTime> Timestamps)> GetHistoricalPricesWithTimestampsAsync(string ticker, string range = "1y", string interval = "1d", DateTime? from = null, DateTime? to = null)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("ticker");

        string url;
        if (from.HasValue && to.HasValue)
        {
            var p1 = new DateTimeOffset(from.Value.ToUniversalTime()).ToUnixTimeSeconds();
            var p2 = new DateTimeOffset(to.Value.ToUniversalTime()).ToUnixTimeSeconds();
            url = $"https://query1.finance.yahoo.com/v8/finance/chart/{ticker}?period1={p1}&period2={p2}&interval={interval}&includePrePost=false&events=div%2Csplit";
        }
        else
        {
            url = $"https://query1.finance.yahoo.com/v8/finance/chart/{ticker}?range={range}&interval={interval}&includePrePost=false&events=div%2Csplit";
        }

        var json = await FetchChartJsonAsync(url).ConfigureAwait(false);
        var parsed = ParseChartJson(json);

        var dates = new List<DateTime>();
        foreach (var ts in parsed.Timestamps)
        {
            dates.Add(DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime);
        }

        return (parsed.Prices, dates);
    }

    /// <summary>
    /// Calcule les rendements simples : r_t = p_t / p_{t-1} - 1
    /// Retourne une liste de longueur prices.Count - 1.
    /// </summary>
    public List<double> GetReturns(List<double> prices)
    {
        if (prices == null)
            throw new ArgumentNullException(nameof(prices));

        var returns = new List<double>();
        for (int i = 1; i < prices.Count; i++)
        {
            var prev = prices[i - 1];
            var cur = prices[i];
            if (Math.Abs(prev) < double.Epsilon)
            {
                returns.Add(double.NaN);
                continue;
            }
            returns.Add(cur / prev - 1.0);
        }

        return returns;
    }
}
