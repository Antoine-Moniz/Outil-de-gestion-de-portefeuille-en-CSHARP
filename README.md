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
Notation : N = nombre de périodes (périodicité journalière), m = nombre d'actifs.

- Rendement simple d'une période : `r_t = P_t / P_{t-1} - 1`
- Rendement attendu (moyenne simple périodique) : `E[R] = mean(r_t)`
- Volatilité périodique (écart-type) : `σ_periodic = stddev(r_t)`
- Annualisation (approx. journalière, 252 jours de trading) :
  - `AnnualReturn ≈ (∏(1+r_t))^(252/N) - 1` (ou approximation `mean(r_t) * 252`)
  - `AnnualVol ≈ σ_periodic * sqrt(252)`

Indicateurs dérivés :
- `Sharpe (annualisé) = (AnnualReturn - r_f) / AnnualVol`  
  (où `r_f` est le taux sans risque annualisé)
- `Alpha / Beta` : issus d'une régression linéaire entre les rendements périodiques du portefeuille `r_p` et du benchmark `r_b` : `r_p = α + β * r_b + ε`
- `Information Ratio (IR) = mean(r_p - r_b) / std(r_p - r_b)`
- `Treynor = (AnnualReturn - r_f) / β`
- `Max Drawdown` : maximum drawdown calculé sur la série cumulative de la valeur du portefeuille

Notes pratiques :
- Utiliser des rendements simples ou log selon cohérence du pipeline (mais rester cohérent pour annualisation).  
- Pour petits échantillons, préférer l'estimateur sans biais (`N-1`) pour l'écart-type.  
- L'annualisation géométrique (produit) est plus précise que la multiplication simple quand les rendements sont volatils.

- Rendement simple d'une période :
  \[
    r_t = \frac{P_t}{P_{t-1}} - 1
  \]

- Rendement attendu (moyenne simple périodique) :
  \[
    \mathbb{E}[R] = \frac{1}{N}\sum_{t=1}^{N} r_t
  \]

- Volatilité périodique (écart-type) :
  \[
    \sigma_{\text{periodic}} = \sqrt{\frac{1}{N-1}\sum_{t=1}^{N} (r_t - \mathbb{E}[R])^2}
  \]

- Annualisation (approx. journalière, \(252\) jours de trading) :
  \[
    \text{AnnualReturn} \approx \left(\prod_{t=1}^{N} (1 + r_t)\right)^{\frac{252}{N}} - 1
    \quad\text{(ou approx. } \mathbb{E}[R]\times 252\text{)}
  \]
  \[
    \text{AnnualVol} \approx \sigma_{\text{periodic}}\sqrt{252}
  \]

Indicateurs dérivés :
- Sharpe (annualisé) :
  \[
    \text{Sharpe} = \frac{\text{AnnualReturn} - r_f}{\text{AnnualVol}}
  \]
  où \(r_f\) est le taux sans risque annualisé.

- Alpha / Beta : issus d'une régression linéaire entre rendements périodiques du portefeuille \(r_p\) et du benchmark \(r_b\) :
  \[
    r_{p,t} = \alpha + \beta\, r_{b,t} + \varepsilon_t
  \]

- Information Ratio (IR) :
  \[
    \text{IR} = \frac{\overline{r_p - r_b}}{\sigma(r_p - r_b)}
  \]
  (on peut annualiser la moyenne et l'écart‑type si nécessaire).

- Treynor :
  \[
    \text{Treynor} = \frac{\text{AnnualReturn} - r_f}{\beta}
  \]

- Max Drawdown : défini sur la série cumulative de la valeur du portefeuille \(V_t\) :
  \[
    \text{MaxDrawdown} = \max_{t} \left( \frac{\max_{s \le t} V_s - V_t}{\max_{s \le t} V_s} \right)
  \]

Notes pratiques :
- Pour les rendements périodiques, utilise des rendements simples (log ou simple selon préférence), mais reste cohérent lors des annualisations et des calculs de volatilité.
- Pour des petits échantillons, préfère l'estimateur sans biais (\(N-1\)) pour l'écart‑type.
- L'annualisation par produit (géométrique) est plus précise que la simple multiplication quand les rendements sont volatils.
Installation et exécution
-------------------------
Pré-requis :
- Windows (pour WPF), .NET 8 SDK installé

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
