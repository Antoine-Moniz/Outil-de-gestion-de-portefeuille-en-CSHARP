# PortfolioOptimizer (WPF .NET 8)

Résumé
-------
Petite application WPF (.NET 8) pour optimiser et comparer portefeuilles selon Markowitz (mean-variance), avec ingestion de séries historiques, visualisations OxyPlot, sauvegarde SQLite et calcul de métriques avancées (Sharpe, Alpha/Beta, Information Ratio, Treynor, Max Drawdown).

Objectifs
---------
- Permettre d'importer séries historiques (Yahoo), calculer rendements et frontier efficiente.
- Optimiser portefeuilles (Min variance / Max Sharpe) ou appliquer stratégies (Value / Momentum / Carry).
- Visualiser frontière efficiente, rendements cumulés et comparer plusieurs portefeuilles.
- Persister portefeuilles et métriques dans SQLite.
- Fournir tests unitaires et d'intégration pour les parties critiques.

Librairies utilisées
--------------------
- .NET 8
- WPF (Windows Presentation Foundation)
- OxyPlot.Wpf pour les graphiques
- System.Data.SQLite (ou Microsoft.Data.Sqlite selon l'environnement) pour persistance
- NUnit + NUnit3TestAdapter pour les tests

Formules et indicateurs (rappel)
--------------------------------
Notation : N = nombre de périodes (périodicité journalière), m = nombre d'actifs
- Rendement simple d'une période : r_t = P_t / P_{t-1} - 1
- Rendement attendu (moyenne simple périodique) : E[R] = mean(r_t)
- Volatilité périodique (σ_periodic) = stddev(r_t)
- Annualisation (approx. journalière) :
  - AnnualReturn ≈ (∏(1+r_t))^{252/N} - 1  (ou mean*252 pour approximation)
  - AnnualVol ≈ σ_periodic * sqrt(252)

- Sharpe (annualisé) : Sharpe = (AnnualReturn - r_f) / AnnualVol
- Alpha/Beta : issues de la régression linéaire r_p = α + β r_b + ε entre rendements périodiques du portefeuille et du benchmark
- Information Ratio : IR = mean(r_p - r_b) / std(r_p - r_b)
- Treynor : Treynor = (AnnualReturn - r_f) / β
- Max Drawdown : maximum drawdown sur la série cumulative

Installation et exécution
-------------------------
Pré-requis :
- Windows (pour WPF), .NET 8 SDK installé
- (optionnel) pandoc pour convertir la documentation markdown en PDF

Build & run :
```powershell
cd "c:\Users\amoni\OneDrive\Documents\COURS\M2 Quant\C#\projet C#"
# build
dotnet build
# lancer depuis Visual Studio (recommandé pour UI WPF) ou
dotnet run --project PortfolioOptimizer.App
```

Tests :
```powershell
dotnet test
```

Conversion docs -> PDF (optionnel, nécessite pandoc + LaTeX pour qualité PDF) :
```powershell
# installer pandoc et une distribution LaTeX (TeX Live / MikTeX)
# convertir README
pandoc README.md -o README.pdf
# convertir notes
pandoc Docs/Note_technique.md -o Docs/Note_technique.pdf
pandoc Docs/Note_fonctionnelle.md -o Docs/Note_fonctionnelle.pdf
```

Usage rapide
------------
1. Saisir tickers séparés par virgule (AAPL,MSFT,NVDA) et choisir période.
2. Télécharger -> l'application récupère les séries (Yahoo) et calcule rendements / frontier.
3. Cliquer Optimize pour choisir MinVariance / MaxSharpe ou Use Strategy Weights (Value/Momentum/Carry).
4. Sauvegarder le portefeuille dans la base (bouton Save to DB). Les métriques avancées sont calculées et stockées si possible.
5. Onglet Comparaison : sélectionner plusieurs portefeuilles sauvegardés (Ctrl+clic) pour afficher leurs courbes cumulées et un tableau de métriques (Sharpe, Treynor, Information, Alpha, Beta).

Notes & améliorations souhaitées
--------------------------------
- Persister les séries de prix dans la BD (table `Prices`) pour éviter re-téléchargements.
- Ajouter une barre de progression / spinner pendant les téléchargements et le calcul des métriques.
- Ajouter tests supplémentaires pour UpdateMetrics and DeletePortfolio.

Licence
-------
Code éducatif / projet étudiant. Pas de licence explicite.
