using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PortfolioOptimizer.App.Services
{
    /// <summary>
    /// Récupérateur de ratios Value (P/B) depuis Yahoo Finance (quoteSummary endpoint).
    /// - Pour chaque ticker, interroge : https://query2.finance.yahoo.com/v10/finance/quoteSummary/{TICKER}?modules=defaultKeyStatistics,financialData,price
    /// - Cherche un champ "priceToBook" (ou équivalent) et récupère sa valeur brute.
    /// - Si updateCsv=true, met à jour (ou crée) le fichier Data/ValueFactors.csv en fusionnant les nouvelles valeurs.
    /// </summary>
    public class ValueFactorFetcher
    {
        private readonly HttpClient _httpClient;

        public ValueFactorFetcher(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            }
            catch
            {
                 // ignorer les erreurs d'analyse de l'en-tête User-Agent
            }
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

    // Récupère le P/B courant pour plusieurs tickers lorsque disponible (sans mise à jour de CSV).
        public async Task<Dictionary<string, double>> FetchAsync(IEnumerable<string> tickers)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in tickers)
            {
                var t = raw?.Trim();
                if (string.IsNullOrWhiteSpace(t)) continue;
                try
                {
                    var url = $"https://query2.finance.yahoo.com/v10/finance/quoteSummary/{Uri.EscapeDataString(t)}?modules=defaultKeyStatistics,financialData,price";
                    using var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) continue;
                    var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(txt);
                    if (TryFindPriceToBook(doc.RootElement, out var pb) && !double.IsNaN(pb) && !double.IsInfinity(pb))
                    {
                        dict[t] = pb;
                    }
                }
                catch
                {
                    // ignorer les erreurs pour un ticker individuel et poursuivre
                }
            }

            return dict;
        }

        /// <summary>
    /// Tente d'obtenir le Price/Book pour le ticker donné à la date fournie.
    /// Stratégie : préférer un champ direct priceToBook fourni par Yahoo ; si absent,
    /// tenter d'obtenir une valeur comptable (book value) et calculer P/B = prix_a_date / bookValue.
    /// Retourne null si la valeur n'est pas disponible.
        /// </summary>
        public async Task<double?> FetchPbAtDateAsync(string ticker, DateTime asOf)
        {
            if (string.IsNullOrWhiteSpace(ticker)) return null;

            try
            {
                var url = $"https://query2.finance.yahoo.com/v10/finance/quoteSummary/{Uri.EscapeDataString(ticker)}?modules=defaultKeyStatistics,financialData,price";
                using var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(txt);
                if (TryFindPriceToBook(doc.RootElement, out var pb) && !double.IsNaN(pb) && !double.IsInfinity(pb))
                    return pb;

                    // repli : essayer d'obtenir la bookValue et le prix à la date
                double? bookValue = TryFindBookValue(doc.RootElement);
                if (bookValue.HasValue && bookValue.Value > 0)
                {
                    // obtenir le prix à la date donnée
                    var dp = new DataProvider();
                    try
                    {
                        var from = asOf.Date;
                        var to = asOf.Date.AddDays(1);
                        var result = await dp.GetHistoricalPricesWithTimestampsAsync(ticker, range: "1d", interval: "1d", from: from, to: to).ConfigureAwait(false);
                        var prices = result.Prices;
                        if (prices != null && prices.Count > 0)
                        {
                            var price = prices[0];
                            return price / bookValue.Value;
                        }
                    }
                    catch
                    {
                        // ignorer et retourner null
                    }
                }
            }
            catch
            {
                // ignorer
            }

            return null;
        }

        private double? TryFindBookValue(JsonElement el)
        {
            // Essayer d'abord les champs courants de valeur comptable par action
            var perShareNames = new[] { "bookValue", "bookValuePerShare", "bookValuePerShareBasic", "bookValuePerShareBasicYtd" };
            if (TryFindNumericField(el, perShareNames, out var perShare)) return perShare;

            // Sinon, essayer de calculer à partir des capitaux propres totaux / actions en circulation
            var equityNames = new[] { "totalShareholderEquity", "totalStockholderEquity", "shareHolderEquity", "totalStockholdersEquity", "totalStockholderEquity" };
            var sharesNames = new[] { "sharesOutstanding", "sharesOutstandingBasic", "floatShares", "commonStockSharesOutstanding" };

            if (TryFindNumericField(el, equityNames, out var equity) && TryFindNumericField(el, sharesNames, out var shares) && shares > 0)
            {
                return equity / shares;
            }

            // recherche récursive d'un champ "bookValue"
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var found = TryFindBookValue(prop.Value);
                        if (found.HasValue) return found.Value;
                    }
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                {
                    var found = TryFindBookValue(item);
                    if (found.HasValue) return found.Value;
                }
            }

            return null;
        }

        private bool TryFindNumericField(JsonElement el, string[] names, out double value)
        {
            value = double.NaN;
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    foreach (var n in names)
                    {
                        if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase))
                        {
                            var obj = prop.Value;
                            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("raw", out var raw) && raw.TryGetDouble(out var d))
                            {
                                value = d;
                                return true;
                            }
                            if (obj.ValueKind == JsonValueKind.Number && obj.TryGetDouble(out var d2))
                            {
                                value = d2;
                                return true;
                            }
                        }
                    }

                    if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        if (TryFindNumericField(prop.Value, names, out value)) return true;
                    }
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                {
                    if (TryFindNumericField(item, names, out value)) return true;
                }
            }

            return false;
        }

        private bool TryFindPriceToBook(JsonElement el, out double value)
        {
            // recherche récursive d'une propriété "priceToBook" et récupération de la valeur "raw" si présente
            value = double.NaN;
            try
            {
                if (el.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "priceToBook", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(prop.Name, "priceToBookMRQ", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(prop.Name, "priceToBookAnnual", StringComparison.OrdinalIgnoreCase))
                        {
                            var obj = prop.Value;
                            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("raw", out var raw) && raw.TryGetDouble(out var d))
                            {
                                value = d;
                                return true;
                            }
                            if (obj.ValueKind == JsonValueKind.Number && obj.TryGetDouble(out var d2))
                            {
                                value = d2;
                                return true;
                            }
                        }

                        // approfondir la recherche
                        if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            if (TryFindPriceToBook(prop.Value, out value)) return true;
                        }
                    }
                }
                else if (el.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in el.EnumerateArray())
                    {
                        if (TryFindPriceToBook(item, out value)) return true;
                    }
                }
            }
            catch
            {
                // ignorer
            }

            return false;
        }

    }
}
