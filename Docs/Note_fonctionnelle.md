Note fonctionnelle - PortfolioOptimizer
======================================

1. But et public
----------------
Application destinée à des étudiants/analystes pour construire, optimiser et comparer des portefeuilles d'actifs simples.

2. Écran principal (MainWindow)
------------------------------
La fenêtre principale contient trois onglets principaux :
- Frontière efficiente
- Performance avancée
- Comparaison

Panneau gauche (saisie & commandes) :
- `TickerTextBox` : saisir une liste de tickers séparés par virgule (ex. AAPL,MSFT,NVDA)
- `StartDatePicker` / `EndDatePicker` : période souhaitée
- `DownloadButton` : télécharger les séries (Yahoo) et calculer la frontière
- `OptimizeButton` : lancer l'optimisation (modes: MinVariance, MaxSharpe, Use Strategy Weights)
- `StrategyComboBox` / `OptimizeModeComboBox` : sélection des stratégies (Value, Momentum, Carry, Equal)
- `ExportButton` : exporter le portefeuille chargé (JSON/CSV)
- `SaveToDbButton` : sauvegarder le portefeuille dans la base (nom via ComboBox editable)
- `UpdateMetricsButton` : recalculer et sauvegarder les métriques pour les portefeuilles sélectionnés
- `DeletePortfolioButton` : supprimer un portefeuille sélectionné de la base

Panneau central / droit :
- `PlotView` : affiche la frontière efficiente
- `ComparePlotView` : affiche les courbes cumulées (onglet Comparaison)
- `CompositionListView` : composition du portefeuille courant (ticker: poids)
- `MetricsDataGrid` : tableau récapitulatif des métriques (Sharpe, Treynor, Information, Alpha, Beta)

3. Usage pas-à-pas
-------------------
1. Entrer les tickers et la période -> `Download`.
2. Une fois les séries chargées, la frontière efficiente est calculée automatiquement.
3. Cliquer `Optimize` et choisir un mode :
   - Minimize Variance : renvoie portefeuille min variance
   - Maximize Sharpe : renvoie portefeuille maximisant Sharpe
   - Use Strategy Weights : calcule poids via la stratégie choisie et applique ces poids
4. Visualiser la composition (list view) et sauvegarder si souhaité.
5. Aller dans l'onglet `Comparaison`, sélectionner (Ctrl+clic) plusieurs portefeuilles sauvegardés pour afficher leurs courbes cumulées et le tableau des métriques.

4. Comportements attendus et messages
-------------------------------------
- Si une série manque pour un portefeuille chargé depuis la base, l'UI tente de re-télécharger automatiquement les séries manquantes avant la comparaison.
- Pendant les opérations longues (download / calcul frontière / calcul métriques), afficher un message dans la zone de log (`OutputTextBox`) et désactiver les boutons pertinents.
- Si `UpdateMetricsButton` est exécuté, la base est mise à jour avec les métriques calculées.

5. Scénarios d'erreur courants
-----------------------------
- Pas de données retournées par Yahoo -> message d'erreur et la sauvegarde se fera sans métriques.
- Sélection multiple rapide -> l'application doit ignorer les événements de sélection concurrents (protection déjà implémentée).
- Données insuffisantes pour calculs statistiques -> afficher "NaN" ou message explicite.

6. Glossaire utilisateur
------------------------
- Frontière efficiente : ensemble de portefeuilles offrant le meilleur rendement attendu pour une volatilité donnée.
- Sharpe (ann.) : rentabilité ajustée du risque.
- Alpha/Beta : alpha = surperformance par rapport au benchmark; beta = sensibilité au benchmark.



