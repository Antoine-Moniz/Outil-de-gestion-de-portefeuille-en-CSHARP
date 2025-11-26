using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.App.Services;

/// <summary>
/// Petit solveur quadratique pour calculer le portefeuille de tangence (max Sharpe sans contraintes de non-short).
/// Méthode : w ∝ Σ^{-1} (μ - rf), puis normalisation pour somme = 1.
/// Utilisé dans les tests pour fournir une solution rapide et numériquement stable.
/// </summary>
public static class QuadraticSolver
{
    /// <param name="enforceNonNegative">si true, projette la solution sur le simplexe non-négatif (w>=0, sum=1).</param>
    public static List<double> TangencyWeights(List<Asset> assets, double rf = 0.0, bool enforceNonNegative = false)
    {
        if (assets == null) throw new ArgumentNullException(nameof(assets));
        int n = assets.Count;
        if (n == 0) return new List<double>();

        // construire les séries de rendements alignées et la matrice de covariance
        int N = assets.Min(a => a.Returns?.Count ?? 0);
        if (N <= 0) throw new ArgumentException("Assets must contain returns data");

        var series = BuildAlignedSeries(assets, N);
        // moyennes des séries
        var means = new double[n];
        for (int i = 0; i < n; i++) means[i] = series[i].Average();

        var cov = ComputeCovarianceMatrix(series, means, N);
        // annualiser la covariance (supposant des rendements journaliers)
        cov = (DenseMatrix)cov.Multiply(252.0);

        // vecteur des rendements attendus
        var mu = DenseVector.Create(n, i => assets[i].ExpectedReturn);

        // rendements excédentaires
        var ex = mu - rf;

        // résoudre Sigma * x = ex  (x = Sigma^{-1} ex)
        Vector<double> x;
        try
        {
            x = cov.Solve(ex);
        }
        catch
        {
            // repli : régulariser légèrement
            var reg = cov + DenseMatrix.CreateIdentity(n) * 1e-8;
            x = reg.Solve(ex);
        }

        // normaliser pour somme = 1
        var xs = x.ToArray();
        double sum = xs.Sum();
        if (Math.Abs(sum) < 1e-15)
        {
            // repli : utiliser des poids égaux
            return Enumerable.Repeat(1.0 / n, n).ToList();
        }

        var w = xs.Select(v => v / sum).ToList();

        if (enforceNonNegative)
        {
            return ProjectToSimplex(w);
        }

        return w;
    }

    // projection euclidienne sur le simplexe {w | sum(w)=1, w_i >= 0}
    private static List<double> ProjectToSimplex(IList<double> v)
    {
        int n = v.Count;
        var u = v.Select(x => x).ToArray();
    Array.Sort(u);
    Array.Reverse(u); // tri décroissant

        double cumsum = 0.0;
        int rho = -1;
        for (int j = 0; j < n; j++)
        {
            cumsum += u[j];
            var t = (cumsum - 1.0) / (j + 1);
            if (u[j] - t > 0)
                rho = j;
        }

        if (rho == -1)
        {
            // fallback: uniforme
            return Enumerable.Repeat(1.0 / n, n).ToList();
        }

        // calculer theta
        double sumR = 0.0;
        for (int i = 0; i <= rho; i++) sumR += u[i];
        double theta = (sumR - 1.0) / (rho + 1);

        var w = new double[n];
        for (int i = 0; i < n; i++) w[i] = Math.Max(0.0, v[i] - theta);
        // renormaliser pour somme = 1 (pour être sûr)
        double s = w.Sum();
        if (s <= 0) return Enumerable.Repeat(1.0 / n, n).ToList();
        return w.Select(val => val / s).ToList();
    }

    private static double[][] BuildAlignedSeries(List<Asset> assets, int N)
    {
        int n = assets.Count;
        var series = new double[n][];
        for (int i = 0; i < n; i++)
        {
            var ret = assets[i].Returns;
            series[i] = new double[N];
            int offset = ret.Count - N;
            for (int j = 0; j < N; j++) series[i][j] = ret[offset + j];
        }
        return series;
    }

    private static DenseMatrix ComputeCovarianceMatrix(double[][] series, double[] means, int N)
    {
        int n = series.Length;
        var cov = DenseMatrix.Create(n, n, 0.0);
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                double acc = 0.0;
                for (int t = 0; t < N; t++) acc += (series[i][t] - means[i]) * (series[j][t] - means[j]);
                acc /= N;
                cov[i, j] = acc;
                cov[j, i] = acc;
            }
        return cov;
    }

}
