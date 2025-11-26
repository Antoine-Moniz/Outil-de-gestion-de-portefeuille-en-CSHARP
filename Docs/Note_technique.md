Note technique - PortfolioOptimizer
===================================

1. Objectif technique
---------------------
Ce document décrit l'architecture du projet, les classes principales, les responsabilités, et les formules mathématiques utilisées pour le calcul des rendements, de la frontière efficiente et des métriques de performance.

2. Structure générale
---------------------
- PortfolioOptimizer.App (WPF application)
  - MainWindow.xaml / MainWindow.xaml.cs : UI et logique d'orchestration
  - Views: PlotView (OxyPlot) pour frontière et comparaison
- PortfolioOptimizer.App.Models
  - Asset : encapsule ticker, HistoricalPrices, HistoricalDates, Returns, ExpectedReturn, Volatility
  - Portfolio : Assets, Weights, méthodes ComputePortfolioReturn(), ComputePortfolioVolatility(), etc.
- PortfolioOptimizer.App.Services
  - DataProvider : téléchargement des séries (Yahoo), méthodes sync/async
  - Optimizer : méthodes EfficientFrontier(), OptimizeMaxSharpe(), OptimizeMinVariance() (grid-search)
  - PortfolioComparer : Compare(List<Portfolio>) -> ComparisonResult (labels, periodic & cumulative returns, Sharpe/Alpha/Beta/IR/Treynor)
  - PerformanceAnalyzer : fonctions statiques pour Sharpe, Sortino, Alpha/Beta, TrackingError, MaxDrawdown...
  - Strategies : InvestmentStrategy (abstrait), MomentumStrategy, ValueStrategy, CarryStrategy
- PortfolioOptimizer.App.Utils
  - DatabaseManager : SQLite persistence (Portfolios, Positions, PerformanceMetrics)
  - FileManager : import/export JSON/CSV

3. Diagramme (textuel)
----------------------
MainWindow -> DataProvider: GetHistoricalPricesWithTimestampsAsync(ticker)
MainWindow -> Optimizer: EfficientFrontier(_loadedAssets)
MainWindow -> DatabaseManager: SavePortfolio / LoadPortfolio / DeletePortfolio
MainWindow -> PortfolioComparer: Compare(selectedPortfolios)
PortfolioComparer -> PerformanceAnalyzer: ComputeSharpe / ComputeAlphaBeta / ComputeInformationRatio

4. Principales classes et méthodes
----------------------------------
- Asset
  - string Ticker
  - List<double> HistoricalPrices
  - List<DateTime> HistoricalDates
  - List<double> Returns
  - double ExpectedReturn, Volatility
  - Constructeurs: Asset(ticker), Asset(ticker, prices), Asset(ticker, prices, dates)

- Portfolio
  - List<Asset> Assets
  - List<double> Weights
  - ComputePortfolioReturn(): annualized return (from asset expectations or periodic series)
  - ComputePortfolioVolatility(): annualized volatility

- DataProvider
  - GetHistoricalPricesWithTimestampsAsync(string ticker, string range="1y", string interval="1d", DateTime? from=null, DateTime? to=null)
  - GetReturns(List<double> prices)

- Optimizer
  - EfficientFrontier(List<Asset> assets, double step=0.01)
  - OptimizeMaxSharpe(List<Asset> assets, double rf, double step)
  - OptimizeMinVariance(List<Asset> assets, double step)
  - (implémentation: grid-search sur poids (somme 1), simple mais robuste pour M petits)

- PortfolioComparer
  - Compare(List<Portfolio> portfolios) -> ComparisonResult
  - Aligne séries en prenant la plus courte longueur commune, calcule rendements périodiques et séries cumulées

- DatabaseManager
  - InitDatabase(), SavePortfolio(Portfolio p, string name, IDictionary<string,double?>? metrics), LoadPortfolio(string name), ListPortfolios(), DeletePortfolio(string name)

5. Formules mathématiques (détaillées)
-------------------------------------
- Rendements simples : r_{t} = P_t / P_{t-1} - 1
- Rendement périodique moyen : \bar{r} = (1/N) \sum_{t=1}^N r_t
- Volatilité périodique : σ = sqrt( (1/(N-1)) \sum (r_t - \bar{r})^2 )
- Annualisation (journalière approximative) :
  - AnnualReturn ≈ (\prod_{t=1}^N (1 + r_t))^{252/N} - 1
  - AnnualVol ≈ σ * sqrt(252)

- Sharpe (annualisé) :
  Sharpe = (AnnualReturn - r_f) / AnnualVol

- Alpha/Beta : régression linéaire simple entre rendements portefeuilles r_p,t et rendements benchmark r_b,t
  r_p,t = α + β r_b,t + ε_t
  β = Cov(r_p, r_b) / Var(r_b)
  α = mean(r_p) - β mean(r_b)

- Information Ratio:
  IR = mean(r_p - r_b) / std(r_p - r_b)

- Treynor:
  Treynor = (AnnualReturn - r_f) / β

- Max Drawdown:
  D_t = (peak_t - trough_t) / peak_t, maximum over time

6. Limitations et améliorations futures
--------------------------------------
- Optimizer actuel: grid-search simple (suffisant pour petits univers). À remplacer par solveur convexe (quadprog) pour grandes dimensions.
- Séries non persistées: l'application télécharge les séries à la volée; persister en base (table Prices) améliorerait reproductibilité et performance.
- Gestion de la période: alignement naïf sur la plus courte série; ajouter interpolation / calendrier commun pourrait aider.

7. Tests importants
-------------------
- Tests unitaires : PerformanceAnalyzer, Optimizer (cas trivial), Strategies
- Tests d'intégration : Save/Load Portfolio + stub DataProvider (vérifie que Compare reçoit des Asset avec Returns peuplés)


Annexe: fichiers et points d'entrée
----------------------------------
- `PortfolioOptimizer.App` : `MainWindow.xaml.cs` (UI) — point d'entrée WPF
- `PortfolioOptimizer.Tests` : tests NUnit

