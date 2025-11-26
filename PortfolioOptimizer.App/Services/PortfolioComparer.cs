using System;
using System.Collections.Generic;
using System.Linq;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.App.Services
{
    public class ComparisonResult
    {
        public List<string> Labels { get; set; } = new();
        public double[] Sharpe { get; set; } = Array.Empty<double>();
        public double[] Treynor { get; set; } = Array.Empty<double>();
        public double[] Information { get; set; } = Array.Empty<double>();
        public double[] Alpha { get; set; } = Array.Empty<double>();
        public double[] Beta { get; set; } = Array.Empty<double>();
        public List<List<double>> CumulativeReturns { get; set; } = new();
        public List<List<double>> PeriodicReturns { get; set; } = new();
    // Dates correspondant à la période commune utilisée pour Periodic/CumulativeReturns.
    // Si vide, l'affichage utilisera des indices entiers en abscisse.
        public List<DateTime> Dates { get; set; } = new();
    }

    public static class PortfolioComparer
    {
    /// <summary>
    /// Compare plusieurs portefeuilles. Retourne un ComparisonResult contenant les métriques et les séries de rendement cumulées alignées sur une période commune.
    /// </summary>
        public static ComparisonResult Compare(List<Portfolio> portfolios)
        {
            if (portfolios == null) throw new ArgumentNullException(nameof(portfolios));
            var result = new ComparisonResult();
            if (portfolios.Count == 0) return result;

            int m = portfolios.Count;

            // construire les séries de rendements périodiques par portefeuille (aligner les actifs selon la plus courte longueur de séries)
            var periodic = new List<List<double>>();
            for (int idx = 0; idx < m; idx++)
            {
                var p = portfolios[idx];
                int N = p.Assets.Min(a => a.Returns?.Count ?? 0);
                if (N <= 0)
                {
                    periodic.Add(new List<double>());
                    continue;
                }

                var series = new double[p.Assets.Count][];
                for (int i = 0; i < p.Assets.Count; i++)
                {
                    var ret = p.Assets[i].Returns ?? new List<double>();
                    series[i] = new double[N];
                    int offset = ret.Count - N;
                    for (int t = 0; t < N; t++) series[i][t] = ret[offset + t];
                }

                var portR = new List<double>(N);
                for (int t = 0; t < N; t++)
                {
                    double r = 0.0;
                    for (int i = 0; i < p.Assets.Count; i++) r += p.Weights[i] * series[i][t];
                    portR.Add(r);
                }
                periodic.Add(portR);
            }

            // déterminer la longueur commune
            int common = periodic.Where(s => s != null && s.Count > 0).Select(s => s.Count).DefaultIfEmpty(0).Min();
            if (common == 0)
            {
                // pas de données exploitables
                result.Labels = portfolios.Select((p, i) => $"P{i+1}").ToList();
                return result;
            }

            // tronquer les séries au segment commun
            var truncated = periodic.Select(s => s.Count >= common ? s.Skip(s.Count - common).Take(common).ToList() : Enumerable.Repeat(0.0, common).ToList()).ToList();

            // construire la série de benchmark comme la moyenne des portefeuilles
            var bench = new List<double>(common);
            for (int t = 0; t < common; t++) bench.Add(truncated.Select(s => s[t]).Average());

            result.PeriodicReturns = truncated;
            result.CumulativeReturns = truncated.Select(pr =>
            {
                var cum = new List<double>(); double c = 1.0; foreach (var r in pr) { c *= (1.0 + r); cum.Add(c); } return cum;
            }).ToList();

            // Essayer d'inférer les dates pour la période commune. On cherche un actif qui expose des HistoricalDates
            // alignées avec ses prix et on utilise ses `common` dernières dates comme timeline.
            var dates = new List<DateTime>();
            for (int idx = 0; idx < portfolios.Count; idx++)
            {
                var p = portfolios[idx];
                foreach (var a in p.Assets ?? Enumerable.Empty<PortfolioOptimizer.App.Models.Asset>())
                {
                    if (a.HistoricalDates != null && a.HistoricalDates.Count == (a.HistoricalPrices?.Count ?? 0) && a.HistoricalDates.Count >= common + 1)
                    {
                        // Les prix sont au nombre de P, les rendements au nombre de P-1. 
                        // On veut les `common` derniers rendements -> prendre les `common` dernières dates à partir de l'indice P - common
                        int P = a.HistoricalDates.Count;
                        int start = P - common;
                        dates = a.HistoricalDates.Skip(start).Take(common).ToList();
                        break;
                    }
                }
                if (dates.Count == common) break;
            }

            result.Dates = dates;

            result.Labels = portfolios.Select((p, i) => p.Assets != null && p.Assets.Count > 0 ? string.Join(",", p.Assets.Select(a => a.Ticker)) : $"P{i+1}").ToList();

            result.Sharpe = new double[m]; result.Treynor = new double[m]; result.Information = new double[m]; result.Alpha = new double[m]; result.Beta = new double[m];
            for (int i = 0; i < m; i++)
            {
                var pr = truncated[i];
                if (pr == null || pr.Count < 2)
                {
                    result.Sharpe[i] = double.NaN; result.Treynor[i] = double.NaN; result.Information[i] = double.NaN; result.Alpha[i] = double.NaN; result.Beta[i] = double.NaN;
                    continue;
                }

                var annR = PerformanceAnalyzer.ComputeAnnualizedReturnFromPeriodic(pr);
                var annV = PerformanceAnalyzer.ComputeAnnualizedVolatility(pr);
                result.Sharpe[i] = PerformanceAnalyzer.ComputeSharpe(annR, 0.0, annV);

                var (alpha, beta) = PerformanceAnalyzer.ComputeAlphaBeta(pr, bench);
                result.Alpha[i] = alpha; result.Beta[i] = beta;
                result.Treynor[i] = PerformanceAnalyzer.ComputeTreynor(annR, 0.0, beta);

                var excess = pr.Zip(bench, (rp, rb) => rp - rb).ToList();
                result.Information[i] = PerformanceAnalyzer.ComputeInformationRatio(excess);
            }

            return result;
        }
    }
}
