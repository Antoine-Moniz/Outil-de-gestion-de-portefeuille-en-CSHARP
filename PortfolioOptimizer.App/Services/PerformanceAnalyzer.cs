using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace PortfolioOptimizer.App.Services;

/// <summary>
/// Fournit des mesures de performance avancée pour un portefeuille.
/// Toutes les méthodes sont statiques et utilisent MathNet.Numerics pour les calculs linéaires/statistiques.
/// Remarques :
/// - Les entrées "returns" sont des rendements périodiques (par ex. rendements journaliers).
/// - Plusieurs métriques sont annualisées en supposant 252 périodes par an lorsque pertinent.
/// </summary>
public static class PerformanceAnalyzer
{
    private const double TradingDaysPerYear = 252.0;

    /// <summary>
    /// Calcule (alpha, beta) d'une régression linéaire simple : r_p = alpha + beta * r_b + eps
    /// Les listes doivent avoir la même longueur.
    /// </summary>
    public static (double alpha, double beta) ComputeAlphaBeta(List<double> portfolioReturns, List<double> benchmarkReturns)
    {
        if (portfolioReturns == null) throw new ArgumentNullException(nameof(portfolioReturns));
        if (benchmarkReturns == null) throw new ArgumentNullException(nameof(benchmarkReturns));
        if (portfolioReturns.Count != benchmarkReturns.Count) throw new ArgumentException("Les séries doivent avoir la même longueur.");
        int n = portfolioReturns.Count;
        if (n == 0) return (0.0, 0.0);

        // construire X = [1, benchmark] et y = portfolio
        var X = DenseMatrix.Create(n, 2, 0.0);
        var y = DenseVector.Create(n, i => portfolioReturns[i]);
        for (int i = 0; i < n; i++)
        {
            X[i, 0] = 1.0;
            X[i, 1] = benchmarkReturns[i];
        }

        // solution par moindres carrés : beta_hat = (X^T X)^{-1} X^T y
        var Xt = X.Transpose();
        var XtX = Xt * X;
        Vector<double> sol;
        try
        {
            sol = XtX.Solve(Xt * y);
        }
        catch
        {
            // régulariser si XtX singulier
            var reg = XtX + DenseMatrix.CreateIdentity(XtX.RowCount) * 1e-8;
            sol = reg.Solve(Xt * y);
        }

        double alpha = sol[0];
        double beta = sol[1];
        return (alpha, beta);
    }

    /// <summary>
    /// Calcule l'indicateur de Treynor : (Rp - Rf) / beta
    /// </summary>
    public static double ComputeTreynor(double portfolioReturn, double rf, double beta)
    {
        if (Math.Abs(beta) < 1e-12) return double.NaN; // évite division par zéro
        return (portfolioReturn - rf) / beta;
    }

    /// <summary>
    /// Calcule le ratio d'information (annualisé) : mean(excess) / stddev(excess) * sqrt(TradingDaysPerYear)
    /// où excess sont des rendements excédentaires périodiques (p.ex. r_p - r_b).
    /// </summary>
    public static double ComputeInformationRatio(List<double> excessReturns)
    {
        if (excessReturns == null) throw new ArgumentNullException(nameof(excessReturns));
        int n = excessReturns.Count;
        if (n == 0) return double.NaN;

        double mean = excessReturns.Average();
        // Ecart-type (echantillon)
        double var = 0.0;
        for (int i = 0; i < n; i++) var += (excessReturns[i] - mean) * (excessReturns[i] - mean);
        var /= Math.Max(1, n - 1);
        double std = Math.Sqrt(var);
        if (std <= 0) return double.NaN;
        // annualiser
        return mean / std * Math.Sqrt(TradingDaysPerYear);
    }

    /// <summary>
    /// Calcule le maximum drawdown à partir d'une série de cumulative returns (valeur de l'indice de richesse, ex. 1.0, 1.02, ...).
    /// Retourne la plus grande perte en fraction positive (ex. 0.25 pour -25%).
    /// </summary>
    public static double ComputeMaxDrawdown(List<double> cumulativeReturns)
    {
        if (cumulativeReturns == null) throw new ArgumentNullException(nameof(cumulativeReturns));
        if (cumulativeReturns.Count == 0) return 0.0;

        double peak = cumulativeReturns[0];
        double maxDd = 0.0;
        foreach (var v in cumulativeReturns)
        {
            if (v > peak) peak = v;
            double dd = (peak - v) / peak; // drawdown fraction
            if (dd > maxDd) maxDd = dd;
        }
        return maxDd;
    }

    /// <summary>
    /// Rendement annualisé à partir de rendements périodiques (par ex. journaliers).
    /// </summary>
    public static double ComputeAnnualizedReturnFromPeriodic(List<double> returns)
    {
        if (returns == null) throw new ArgumentNullException(nameof(returns));
        int n = returns.Count;
        if (n == 0) return 0.0;
        double cum = 1.0;
        foreach (var r in returns) cum *= (1.0 + r);
        return Math.Pow(cum, TradingDaysPerYear / n) - 1.0;
    }

    /// <summary>
    /// Volatilité annualisée à partir de rendements périodiques (sample stddev * sqrt(N)).
    /// </summary>
    public static double ComputeAnnualizedVolatility(List<double> returns)
    {
        if (returns == null) throw new ArgumentNullException(nameof(returns));
        int n = returns.Count;
        if (n < 2) return 0.0;
        double mean = returns.Average();
        double var = 0.0;
        for (int i = 0; i < n; i++) var += (returns[i] - mean) * (returns[i] - mean);
        var /= Math.Max(1, n - 1);
        return Math.Sqrt(var) * Math.Sqrt(TradingDaysPerYear);
    }

    /// <summary>
    /// Sharpe ratio (annualisé) = (Rp - Rf) / sigma_p
    /// </summary>
    public static double ComputeSharpe(double annualReturn, double rf, double annualVol)
    {
        if (annualVol <= 0) return double.NaN;
        return (annualReturn - rf) / annualVol;
    }

    /// <summary>
    /// Sortino ratio calculé à partir de rendements périodiques.
    /// Le taux sans risque rf est annualisé ; on utilise l'approximation rf_period = rf / 252.
    /// </summary>
    public static double ComputeSortino(List<double> returns, double rf)
    {
        if (returns == null) throw new ArgumentNullException(nameof(returns));
        int n = returns.Count;
        if (n == 0) return double.NaN;

        // annualisé
        double annReturn = ComputeAnnualizedReturnFromPeriodic(returns);
        double rfPeriod = rf / TradingDaysPerYear;

        // ecart-type des rendements en dessous de rf_period
        var downs = returns.Where(r => r < rfPeriod).Select(r => (r - rfPeriod) * (r - rfPeriod)).ToList();
        if (downs.Count == 0) return double.NaN;
        double ddVar = downs.Sum() / Math.Max(1, downs.Count - 1);
        double dd = Math.Sqrt(ddVar) * Math.Sqrt(TradingDaysPerYear);
        if (dd <= 0) return double.NaN;
        return (annReturn - rf) / dd;
    }

    /// <summary>
    /// Calmar ratio : annualReturn / maxDrawdown
    /// </summary>
    public static double ComputeCalmar(double annualReturn, double maxDrawdown)
    {
        if (maxDrawdown <= 0) return double.NaN;
        return annualReturn / maxDrawdown;
    }

    /// <summary>
    /// Tracking error annualisé à partir d'une série d'excès périodiques (r_p - r_b)
    /// </summary>
    public static double ComputeTrackingError(List<double> excessPeriodic)
    {
        if (excessPeriodic == null) throw new ArgumentNullException(nameof(excessPeriodic));
        int n = excessPeriodic.Count;
        if (n < 2) return double.NaN;
        double mean = excessPeriodic.Average();
        double var = 0.0;
        for (int i = 0; i < n; i++) var += (excessPeriodic[i] - mean) * (excessPeriodic[i] - mean);
        var /= Math.Max(1, n - 1);
        return Math.Sqrt(var) * Math.Sqrt(TradingDaysPerYear);
    }
}
