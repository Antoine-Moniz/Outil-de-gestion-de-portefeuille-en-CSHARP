using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using PortfolioOptimizer.App.Models;
using PortfolioOptimizer.App.Services;
using PortfolioOptimizer.App.Services.Strategies;
using PortfolioOptimizer.App.Utils; 
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace PortfolioOptimizer.App;

/// <summary>
/// Logique d'interaction pour MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string PerfDialogTitle = "Performance avancée";
    private PlotModel _plotModel = new PlotModel { Title = "Frontière efficiente" };
    private PlotModel _comparePlotModel = new PlotModel { Title = "Comparaison des rendements cumulés" };
    private List<Asset> _loadedAssets = new();
    private List<(double Return, double Volatility, List<double> Weights)> _frontier = new();
    private Portfolio? _currentPortfolio = null;
    // garde contre l'exécution concurrente du gestionnaire de sélection (évite un crash si l'utilisateur clique vite)
    private bool _isProcessingSavedSelection = false;
    // token d'annulation pour débouncer les changements du champ benchmark
    private CancellationTokenSource? _benchCts = null;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;

    // calculer automatiquement la performance lorsque le texte du benchmark change
        BenchmarkTextBox.TextChanged += BenchmarkTextBox_TextChanged;

    // préparer le PlotModel basique
    _plotModel.IsLegendVisible = false; // masquer la légende textuelle
    _plotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Volatilité (σ)", Minimum = 0 });
    _plotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Rendement (E[R])" });
        PlotView.Model = _plotModel;
    ComparePlotView.Model = _comparePlotModel;
        // Dates par défaut : dernière année
        try
        {
            StartDatePicker.SelectedDate = DateTime.Today.AddYears(-1);
            EndDatePicker.SelectedDate = DateTime.Today;
        }
        catch { }

        // Initialiser la base SQLite et remplir la liste des portefeuilles
        try
        {
            DatabaseManager.InitDatabase();
            RefreshSavedPortfoliosList();
        }
        catch (Exception ex)
        {
            // Ne pas bloquer l'UI: afficher dans le journal
            OutputTextBox.AppendText("Attention: impossible d'initialiser la base SQLite: " + ex.Message + "\r\n");
        }
    }

    private async void RefreshValueFactorsButton_Click(object sender, RoutedEventArgs e)
    {
        // supprimé : la récupération est désormais automatique lors de l'optimisation ; ce gestionnaire est obsolète.
        await System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Calcule les poids dérivés d'une stratégie pour les actifs chargés.
    /// Retourne null pour "Equal" (indiquant l'absence de pénalité cible).
    /// </summary>
    private async System.Threading.Tasks.Task<List<double>?> GetStrategyWeights(string strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName)) return null;
        if (string.Equals(strategyName, "Equal", StringComparison.OrdinalIgnoreCase)) return null;

        InvestmentStrategy? strat = strategyName switch
        {
            "Momentum" => new MomentumStrategy(),
            "Value" => new ValueStrategy(),
            "Carry" => new CarryStrategy(),
            _ => null
        };

        if (strat == null) return null;
        try
        {
            var asOf = StartDatePicker.SelectedDate ?? DateTime.Today;
            var map = await strat.ComputeWeightsAsync(_loadedAssets!, asOf);
                    // journaliser les tickers manquants dans les données de la stratégie (ils recevront poids = 0)
                    try
                    {
                        var missingTickers = _loadedAssets!.Select(a => a.Ticker).Where(t => !string.IsNullOrWhiteSpace(t) && !map.ContainsKey(t)).ToList();
                        if (missingTickers.Count > 0)
                        {
                            OutputTextBox.AppendText($"Value: données manquantes pour {string.Join(',', missingTickers)} ; ces actifs auront poids = 0.\r\n");
                        }
                    }
                    catch { }
            var weights = new List<double>();
            foreach (var a in _loadedAssets)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Ticker)) weights.Add(0.0);
                else if (map.TryGetValue(a.Ticker, out var v)) weights.Add(v);
                else weights.Add(0.0);
            }

            double s = weights.Sum(x => Math.Abs(x));
            if (s <= 0)
            {
                // Equi-pondéré
                return Enumerable.Repeat(1.0 / _loadedAssets.Count, _loadedAssets.Count).ToList();
            }

            for (int i = 0; i < weights.Count; i++) weights[i] = weights[i] / s;
            return weights;
        }
        catch
        {
            return null;
        }
    }

    private void RefreshSavedPortfoliosList()
    {
        try
        {
            var list = DatabaseManager.ListPortfolios();
            SavedPortfoliosComboBox.Items.Clear();
            SavedPortfolioListBox.Items.Clear();
            foreach (var n in list)
            {
                SavedPortfoliosComboBox.Items.Add(n);
                SavedPortfolioListBox.Items.Add(n);
            }
        }
        catch (Exception ex)
        {
            OutputTextBox.AppendText("Erreur lors de la lecture des portefeuilles sauvegardés: " + ex.Message + "\r\n");
        }
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        // Exécute les vérifications de démarrage en arrière-plan pour garder l'UI réactive.
        await Task.Run(() => RunStartupChecks());
    }

    private void RunStartupChecks()
    {
        try
        {
            Debug.WriteLine("Démarrage des vérifications (Debug only)...");

            // ETAPE 2 : Asset pour AAPL (vérifications silencieuses)
            var asset = new Asset("AAPL");
            // ETAPE 3 : vérifications DataProvider pour MSFT (silencieuses)
            var dp = new DataProvider();
            var msftPrices = dp.GetHistoricalPrices("MSFT");
            var msftReturns = dp.GetReturns(msftPrices);

            Debug.WriteLine("Vérifications de démarrage terminées (Debug only). ");
        }
        catch
        {
            // ignorer les erreurs de vérification au démarrage pour ne pas polluer l'UI/tests
        }
    }

    private void ShowDataButton_Click(object sender, RoutedEventArgs e)
    {
    // Obsolète : utilisé par un ancien bouton. Conserve pour compatibilité mais appeler DownloadButton_Click.
        DownloadButton_Click(sender, e);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var tickersRaw = TickerTextBox.Text ?? string.Empty;
        var tickers = tickersRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToUpperInvariant()).Where(t => t.Length > 0).ToArray();

        if (tickers.Length == 0)
        {
            OutputTextBox.Text = "Veuillez saisir au moins un ticker (ex. AAPL, MSFT)";
            return;
        }

        // Validation des dates : si les deux sont fournies, Start <= End
        if (StartDatePicker.SelectedDate.HasValue && EndDatePicker.SelectedDate.HasValue && StartDatePicker.SelectedDate.Value > EndDatePicker.SelectedDate.Value)
        {
            MessageBox.Show("La date de début ne peut pas être postérieure à la date de fin.", "Erreur de date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DownloadButton.IsEnabled = false;
        OptimizeButton.IsEnabled = false;
        OutputTextBox.Text = $"Téléchargement des données pour : {string.Join(", ", tickers)}...\r\n";

        try
        {
            var assets = new List<Asset>();
            foreach (var t in tickers)
            {
                OutputTextBox.AppendText($"Chargement {t}...\r\n");
                // charger en tâche d'arrière-plan pour ne pas bloquer l'UI
                var dp = new DataProvider();
                // intervalle par défaut (quotidien). Nous laissons le choix uniquement par dates.
                var interval = "1d";

                // récupérer les dates si fournies
                DateTime? from = null;
                DateTime? to = null;
                if (StartDatePicker.SelectedDate.HasValue && EndDatePicker.SelectedDate.HasValue)
                {
                    from = StartDatePicker.SelectedDate.Value;
                    to = EndDatePicker.SelectedDate.Value;
                }

                // Si from/to fournis, GetHistoricalPricesWithTimestampsAsync utilisera period1/period2
                var result = await dp.GetHistoricalPricesWithTimestampsAsync(t, range: "1y", interval: interval, from: from, to: to);
                var prices = result.Prices;
                var timestamps = result.Timestamps;
                var asset = timestamps != null && timestamps.Count == prices.Count
                    ? new Asset(t, prices, timestamps)
                    : new Asset(t, prices);
                assets.Add(asset);

                string dateRangeStr = "N/A";
                if (timestamps != null && timestamps.Count > 0)
                {
                    var first = timestamps[0].ToLocalTime().ToString("yyyy-MM-dd");
                    var last = timestamps[timestamps.Count - 1].ToLocalTime().ToString("yyyy-MM-dd");
                    dateRangeStr = $"{first} → {last}";
                }

                OutputTextBox.AppendText($"  {t}: prix={asset.HistoricalPrices.Count}, dates={dateRangeStr}, rendements={asset.Returns.Count}, ER={asset.ExpectedReturn:F4}, σ={asset.Volatility:F4}\r\n");
            }

            _loadedAssets = assets;
            // reset de current portfolio (aucune optimisation appliquée encore)
            _currentPortfolio = null;
            // activer le bouton Export pour permettre la sauvegarde des séries chargées
            ExportButton.IsEnabled = true;
            // afficher composition égalitaire par défaut
            if (_loadedAssets != null && _loadedAssets.Count > 0)
            {
                var eq = Enumerable.Repeat(1.0 / _loadedAssets!.Count, _loadedAssets!.Count).ToList();
                var p = new Portfolio(_loadedAssets!, eq);
                UpdateCompositionView(p);
            }
            OutputTextBox.AppendText("Chargement terminé.\r\n");

            // Construire la frontière via Optimizer (grille rapide)
            var opt = new Optimizer();
            OutputTextBox.AppendText("Calcul de la frontière efficiente (grille step=0.02)...\r\n");
            var frontier = await Task.Run(() => opt.EfficientFrontier(_loadedAssets!, step: 0.02));
            _frontier = frontier;

            UpdatePlot();

            OptimizeButton.IsEnabled = true;
            // calculer la performance si un benchmark est fourni
            try
            {
                var bench = BenchmarkTextBox.Text?.Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(bench)) await ComputePerformanceAsync(bench);
            }
            catch { }
        }
        catch (Exception ex)
        {
            OutputTextBox.AppendText("Erreur lors du téléchargement : " + ex.Message + "\r\n");
        }
        finally
        {
            DownloadButton.IsEnabled = true;
        }
    }

    private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedAssets == null || _loadedAssets.Count == 0)
        {
            OutputTextBox.AppendText("Aucun actif chargé. Appuyez sur 'Télécharger' d'abord.\r\n");
            return;
        }
        OptimizeButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;

        var raw = (OptimizeModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Minimize Variance (Equal)";
        OutputTextBox.AppendText($"Optimisation (sélection: {raw}) démarrée...\r\n");

    // Déterminer le mode et la stratégie sélectionnés, puis exécuter en conséquence
        var mode = (OptimizeModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Minimize Variance";
        var selectedStrategy = (StrategyComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Equal";

        try
        {
            var opt = new Optimizer();
            if (mode == "Use Strategy Weights")
            {
                // calculer et appliquer les poids de la stratégie comme poids de début de période
                InvestmentStrategy? strat = selectedStrategy switch
                {
                    "Momentum" => new MomentumStrategy(),
                    "Value" => new ValueStrategy(),
                    "Carry" => new CarryStrategy(),
                    _ => null
                };

                if (strat == null)
                {
                    var eq = Enumerable.Repeat(1.0 / _loadedAssets!.Count, _loadedAssets!.Count).ToList();
                    _currentPortfolio = new Portfolio(_loadedAssets!, eq);
                    OutputTextBox.AppendText("Stratégie 'Equal' sélectionnée : poids égalitaires appliqués.\r\n");
                }
                else
                {
                    var asOf = StartDatePicker.SelectedDate ?? DateTime.Today;

                    // Diagnostic: si on est en Value, interroger et journaliser les P/B par ticker
                    if (string.Equals(selectedStrategy, "Value", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var fetcher = new ValueFactorFetcher();
                            OutputTextBox.AppendText($"Récupération P/B (date={asOf:yyyy-MM-dd}) pour chaque ticker...\r\n");
                            foreach (var a in _loadedAssets)
                            {
                                if (a == null || string.IsNullOrWhiteSpace(a.Ticker)) continue;
                                try
                                {
                                    var pb = await fetcher.FetchPbAtDateAsync(a.Ticker!, asOf);
                                    if (pb.HasValue)
                                        OutputTextBox.AppendText($"  {a.Ticker} -> P/B = {pb.Value:F4}\r\n");
                                    else
                                        OutputTextBox.AppendText($"  {a.Ticker} -> P/B non disponible\r\n");
                                }
                                catch (Exception ex)
                                {
                                    OutputTextBox.AppendText($"  {a.Ticker} -> erreur: {ex.Message}\r\n");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            OutputTextBox.AppendText("Erreur diagnostic P/B: " + ex.Message + "\r\n");
                        }
                    }

                    var map = await strat.ComputeWeightsAsync(_loadedAssets!, asOf);
                    // si la stratégie n'a pas de données utiles (aucune valeur retournée),
                    // ne pas appliquer - garder poids égalitaires et avertir l'utilisateur.
                    if (map == null || map.Count == 0)
                    {
                        var eq = Enumerable.Repeat(1.0 / _loadedAssets!.Count, _loadedAssets!.Count).ToList();
                        _currentPortfolio = new Portfolio(_loadedAssets!, eq);
                        OutputTextBox.AppendText($"Stratégie '{selectedStrategy}' n'a pas de données (fichier manquant ou vide) — poids égalitaires appliqués.\r\n");
                        UpdateCompositionView(_currentPortfolio);
                        if (!string.IsNullOrWhiteSpace(BenchmarkTextBox.Text))
                            await ComputePerformanceAsync(BenchmarkTextBox.Text.Trim().ToUpperInvariant());
                        OptimizeButton.IsEnabled = true;
                        DownloadButton.IsEnabled = true;
                        return;
                    }
                    var weights = new List<double>();
                    foreach (var a in _loadedAssets)
                    {
                        if (a == null || string.IsNullOrWhiteSpace(a.Ticker)) weights.Add(0.0);
                        else if (map.TryGetValue(a.Ticker, out var v)) weights.Add(v);
                        else weights.Add(0.0);
                    }
                    double s = weights.Sum(Math.Abs);
                    if (s <= 0) weights = Enumerable.Repeat(1.0 / _loadedAssets.Count, _loadedAssets.Count).ToList();
                    else for (int i = 0; i < weights.Count; i++) weights[i] = weights[i] / s;

                    _currentPortfolio = new Portfolio(_loadedAssets, weights);
                    OutputTextBox.AppendText($"Stratégie '{selectedStrategy}' appliquée comme poids du début de période.\r\n");
                }

                UpdateCompositionView(_currentPortfolio);

                if (!string.IsNullOrWhiteSpace(BenchmarkTextBox.Text))
                {
                    try
                    {
                        await ComputePerformanceAsync(BenchmarkTextBox.Text.Trim().ToUpperInvariant());
                    }
                    catch (Exception ex)
                    {
                        OutputTextBox.AppendText("Erreur recalcul performance : " + ex.Message + "\r\n");
                    }
                }
            }
            else if (mode == "Maximize Sharpe")
            {
                var res = await Task.Run(() => opt.OptimizeMaxSharpe(_loadedAssets!, rf: 0.0, step: 0.02));
                OutputTextBox.AppendText("Résultat optimisation (Max Sharpe) :\r\n");
                OutputTextBox.AppendText($"Sharpe optimisé: {res.Sharpe:F4}, Return: {res.Return:F4}, Vol: {res.Volatility:F4}\r\n");
                _currentPortfolio = new Portfolio(_loadedAssets, res.Weights);
                UpdateCompositionView(_currentPortfolio);
                if (!string.IsNullOrWhiteSpace(BenchmarkTextBox.Text))
                {
                    try
                    {
                        await ComputePerformanceAsync(BenchmarkTextBox.Text.Trim().ToUpperInvariant());
                    }
                    catch (Exception ex)
                    {
                        OutputTextBox.AppendText("Erreur recalcul performance : " + ex.Message + "\r\n");
                    }
                }


                UpdatePlot(optimal: (res.Return, res.Volatility), optimalColor: OxyPlot.OxyColors.Yellow);
            }
            else // Minimize Variance
            {
                var res = await Task.Run(() => opt.OptimizeMinVariance(_loadedAssets!, step: 0.02));
                OutputTextBox.AppendText("Résultat optimisation (Min Variance) :\r\n");
                OutputTextBox.AppendText($"Return: {res.Return:F4}, Vol: {res.Volatility:F4}\r\n");
                _currentPortfolio = new Portfolio(_loadedAssets, res.Weights);
                UpdateCompositionView(_currentPortfolio);
                if (!string.IsNullOrWhiteSpace(BenchmarkTextBox.Text))
                {
                    try
                    {
                        await ComputePerformanceAsync(BenchmarkTextBox.Text.Trim().ToUpperInvariant());
                    }
                    catch (Exception ex)
                    {
                        OutputTextBox.AppendText("Erreur recalcul performance : " + ex.Message + "\r\n");
                    }
                }
                UpdatePlot(optimal: (res.Return, res.Volatility), optimalColor: OxyPlot.OxyColors.Yellow);
            }
        }
        catch (Exception ex)
        {
            OutputTextBox.AppendText("Erreur lors de l'optimisation : " + ex.Message + "\r\n");
        }
        finally
        {
            OptimizeButton.IsEnabled = true;
            DownloadButton.IsEnabled = true;
        }
    }

    

    private void UpdatePlot((double Return, double Volatility)? optimal = null, OxyPlot.OxyColor? optimalColor = null)
    {
        _plotModel.Series.Clear();

        // frontiere efficiente
        if (_frontier != null && _frontier.Count > 0)
        {
            var line = new LineSeries { Title = "Frontière efficiente", StrokeThickness = 2 };
            foreach (var p in _frontier)
            {
                line.Points.Add(new DataPoint(p.Volatility, p.Return));
            }
            _plotModel.Series.Add(line);

            // Aucun point individuel de la frontière n'est ajouté — on conserve uniquement la ligne.
        }


        // optimal point (si fourni) ; couleur configurable (par défaut rouge)
        if (optimal.HasValue)
        {
            var color = optimalColor ?? OxyColors.Red;
            var s = new ScatterSeries { Title = "Optimal", MarkerType = MarkerType.Diamond, MarkerFill = color, MarkerSize = 10 };
            s.Points.Add(new ScatterPoint(optimal.Value.Volatility, optimal.Value.Return));
            _plotModel.Series.Add(s);

            // annotation du point optimal supprimée (graphique épuré)
        }

        _plotModel.InvalidatePlot(true);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedAssets == null || _loadedAssets.Count == 0)
        {
            MessageBox.Show("Aucun actif chargé à exporter. Téléchargez d'abord des séries de prix.", "Exporter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

    // construire un portefeuille à exporter : si on a un portefeuille optimisé, l'utiliser;
    // sinon exporter avec poids égaux.
        Portfolio toSave;
        if (_currentPortfolio != null)
            toSave = _currentPortfolio;
        else
        {
            var eq = new List<double>();
            int m = _loadedAssets.Count;
            for (int i = 0; i < m; i++) eq.Add(1.0 / m);
            toSave = new Portfolio(_loadedAssets, eq);
        }

        var dlg = new SaveFileDialog { Filter = "JSON files (*.json)|*.json|CSV files (*.csv)|*.csv|All files (*.*)|*.*", FileName = "portfolio.json" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                FileManager.SavePortfolio(toSave, dlg.FileName);
                OutputTextBox.AppendText($"Portefeuille sauvegardé : {dlg.FileName}\r\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors de la sauvegarde : " + ex.Message, "Exporter", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON files (*.json)|*.json|CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var p = FileManager.LoadPortfolio(dlg.FileName);
                _loadedAssets = p.Assets;
                _currentPortfolio = p;

                // afficher la composition chargée dans la ListView
                UpdateCompositionView(p);

                // activer boutons
                OptimizeButton.IsEnabled = true;
                ExportButton.IsEnabled = true;

                // recalculer frontière si possible
                var opt = new Optimizer();
                _frontier = opt.EfficientFrontier(_loadedAssets, step: 0.02);
                UpdatePlot();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur lors du chargement : " + ex.Message, "Importer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void UpdateCompositionView(Portfolio p)
    {
        // Met à jour la ListView CompositionListView avec des éléments "TICKER: POIDS"
        CompositionListView.Items.Clear();
        for (int i = 0; i < p.Assets.Count; i++)
        {
            var item = $"{p.Assets[i].Ticker}: {p.Weights[i]:P2}";
            CompositionListView.Items.Add(item);
        }
    }

    private async void SavedPortfolioListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
    // Empêcher le traitement concurrent (l'utilisateur peut cliquer rapidement)
        if (_isProcessingSavedSelection)
        {
            OutputTextBox.AppendText("Sélection en cours, merci d'attendre...\r\n");
            return;
        }

        _isProcessingSavedSelection = true;
        try
        {
            var selectedNames = SavedPortfolioListBox.SelectedItems.Cast<object?>().Select(x => x?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (selectedNames.Count == 0)
            {
                // aucune sélection : effacer le graphique et la grille
                _comparePlotModel.Series.Clear();
                _comparePlotModel.InvalidatePlot(true);
                MetricsDataGrid.ItemsSource = null;
                return;
            }

            var portfolios = new List<Portfolio>();
            var loadedNames = new List<string>();
            foreach (var name in selectedNames)
            {
                try
                {
                    var p = DatabaseManager.LoadPortfolio(name!);
                    if (p != null)
                    {
                        portfolios.Add(p);
                        loadedNames.Add(name!);
                    }
                }
                catch (Exception ex)
                {
                    OutputTextBox.AppendText($"Erreur en chargeant '{name}': {ex.Message}\r\n");
                }
            }

            // Vérifier les actifs manquants (sans séries) et les récupérer via DataProvider
            var tickersToFetch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in portfolios)
            {
                for (int i = 0; i < p.Assets.Count; i++)
                {
                    var a = p.Assets[i];
                    if (a.Returns == null || a.Returns.Count < 2)
                    {
                        if (!string.IsNullOrWhiteSpace(a.Ticker)) tickersToFetch.Add(a.Ticker!);
                    }
                }
            }

            if (tickersToFetch.Count > 0)
            {
                var dp = new DataProvider();
                var fetched = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in tickersToFetch)
                {
                    try
                    {
                        OutputTextBox.AppendText($"Récupération séries pour {t}...\r\n");
                        var res = await dp.GetHistoricalPricesWithTimestampsAsync(t, range: "1y", interval: "1d");
                        if (res.Prices != null && res.Prices.Count >= 2)
                        {
                            Asset aNew = (res.Timestamps != null && res.Timestamps.Count == res.Prices.Count)
                                ? new Asset(t, res.Prices, res.Timestamps)
                                : new Asset(t, res.Prices);
                            fetched[t] = aNew;
                        }
                        else
                        {
                            OutputTextBox.AppendText($"Aucune série disponible pour {t}.\r\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        OutputTextBox.AppendText($"Erreur en récupérant {t}: {ex.Message}\r\n");
                    }
                }

                // remplacer les actifs manquants dans les portefeuilles chargés
                foreach (var p in portfolios)
                {
                    for (int i = 0; i < p.Assets.Count; i++)
                    {
                        var a = p.Assets[i];
                        if (!string.IsNullOrWhiteSpace(a.Ticker) && fetched.TryGetValue(a.Ticker!, out var newA))
                        {
                            p.Assets[i] = newA;
                        }
                    }
                }
            }

            if (portfolios.Count == 0)
            {
                OutputTextBox.AppendText("Aucun portefeuille valide sélectionné.\r\n");
                return;
            }

            try
            {
                var comp = PortfolioComparer.Compare(portfolios);
                // Utiliser les noms de portefeuilles réellement chargés (depuis la BD) comme étiquettes
                // afin que les métriques correspondent aux éléments sélectionnés
                if (loadedNames.Count == comp.Labels.Count)
                    comp.Labels = new List<string>(loadedNames);
                else
                    comp.Labels = loadedNames.Count > 0 ? new List<string>(loadedNames) : comp.Labels;

                PlotComparison(comp);
                UpdateMetricsGrid(comp);
                OutputTextBox.AppendText($"Comparaison: {portfolios.Count} portefeuille(s) affichés.\r\n");
            }
            catch (Exception ex)
            {
                OutputTextBox.AppendText($"Erreur lors de la comparaison des portefeuilles: {ex.Message}\r\n");
            }
        }
        finally
        {
            _isProcessingSavedSelection = false;
        }
    }

    private void DeletePortfolioButton_Click(object sender, RoutedEventArgs e)
    {
        var name = SavedPortfoliosComboBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            // essayer la sélection dans la liste
            var sel = SavedPortfolioListBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(sel))
            {
                MessageBox.Show("Sélectionnez un portefeuille à supprimer (via la ComboBox ou la liste).", "Supprimer portefeuille", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            name = sel;
        }

        var ok = MessageBox.Show($"Supprimer le portefeuille '{name}' de la base ?", "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;

        try
        {
            DatabaseManager.DeletePortfolio(name);
            OutputTextBox.AppendText($"Portefeuille '{name}' supprimé de la base.\r\n");
            RefreshSavedPortfoliosList();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erreur lors de la suppression: " + ex.Message, "Supprimer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PlotComparison(ComparisonResult comp)
    {
        if (comp == null) return;
        _comparePlotModel.Series.Clear();
        _comparePlotModel.Axes.Clear();
    _comparePlotModel.IsLegendVisible = true;

    // Axe X : dates si disponibles, sinon index entier
        // choisir le type d'axe en fonction de la disponibilité des dates
        bool hasDates = comp != null && comp.Dates != null && comp.Dates.Count > 0;
        if (hasDates)
        {
            _comparePlotModel.Axes.Add(new OxyPlot.Axes.DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Date",
                StringFormat = "yyyy-MM-dd",
                IntervalType = OxyPlot.Axes.DateTimeIntervalType.Days,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            });
        }
        else
        {
            _comparePlotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Période (index)" });
        }
        _comparePlotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Valeur cumulée" });

        var colors = new[] { OxyPlot.OxyColors.Blue, OxyPlot.OxyColors.Red, OxyPlot.OxyColors.Green, OxyPlot.OxyColors.Orange, OxyPlot.OxyColors.Purple, OxyPlot.OxyColors.Brown };

        var cumLists = comp?.CumulativeReturns ?? new List<List<double>>();
        var labels = comp?.Labels ?? new List<string>();
        int seriesCount = cumLists.Count;
        var dates = comp?.Dates;
        for (int i = 0; i < seriesCount; i++)
        {
            var cum = cumLists[i];
            var ls = new LineSeries { Title = labels.ElementAtOrDefault(i) ?? $"P{i+1}", StrokeThickness = 2, MarkerType = MarkerType.None };
            var col = colors[i % colors.Length];
            ls.Color = col;
            if (dates != null && dates.Count == cum.Count)
            {
                for (int t = 0; t < cum.Count; t++)
                {
                    var dt = dates[t];
                    ls.Points.Add(new DataPoint(OxyPlot.Axes.DateTimeAxis.ToDouble(dt), cum[t]));
                }
            }
            else
            {
                // valeurs X entières ; s'assurer que l'axe est linéaire (géré ci-dessus)
                for (int t = 0; t < cum.Count; t++) ls.Points.Add(new DataPoint(t, cum[t]));
            }
            _comparePlotModel.Series.Add(ls);
        }

        ComparePlotView.Model = _comparePlotModel;
        _comparePlotModel.InvalidatePlot(true);
    }

    private void UpdateMetricsGrid(ComparisonResult comp)
    {
        if (comp == null) return;
        var dt = new System.Data.DataTable();
        dt.Columns.Add("Metric", typeof(string));
        for (int i = 0; i < comp.Labels.Count; i++) dt.Columns.Add(comp.Labels[i], typeof(string));

        /// Cas spécial : si un seul portefeuille est sélectionné et que nous avons des métriques sauvegardées dans la BD,
        /// préférer afficher les métriques sauvegardées (elles peuvent inclure des valeurs basées sur le benchmark) au lieu des
        /// valeurs calculées par la comparaison qui reposent sur un benchmark cross-sectionnel.
    IDictionary<string, double?>? savedMetrics = null;
        if (comp.Labels.Count == 1)
        {
            try
            {
                savedMetrics = PortfolioOptimizer.App.Utils.DatabaseManager.GetPortfolioMetrics(comp.Labels[0]);
            }
            catch { savedMetrics = null; }
        }

        void AddRow(string metricName, Func<int, double> getter)
        {
            var row = dt.NewRow();
            row[0] = metricName;
            for (int j = 0; j < comp.Labels.Count; j++)
            {
                var val = getter(j);
                row[j + 1] = double.IsNaN(val) ? "NaN" : val.ToString("F6");
            }
            dt.Rows.Add(row);
        }

        if (savedMetrics != null && savedMetrics.Count > 0)
        {
            // Display a fixed set of common metrics using saved values when available.
            string[] keys = new[] { "Sharpe", "Treynor", "Information", "Alpha", "Beta", "AnnualReturn", "AnnualVolatility" };
            foreach (var k in keys)
            {
                var row = dt.NewRow();
                row[0] = k;
                var val = savedMetrics.TryGetValue(k, out var v) && v.HasValue ? v.Value : double.NaN;
                row[1] = double.IsNaN(val) ? "NaN" : val.ToString("F6");
                dt.Rows.Add(row);
            }
        }
        else
        {
            AddRow("Sharpe", i => comp.Sharpe.ElementAtOrDefault(i));
            AddRow("Treynor", i => comp.Treynor.ElementAtOrDefault(i));
            AddRow("Information", i => comp.Information.ElementAtOrDefault(i));
            AddRow("Alpha", i => comp.Alpha.ElementAtOrDefault(i));
            AddRow("Beta", i => comp.Beta.ElementAtOrDefault(i));
        }

        MetricsDataGrid.ItemsSource = dt.DefaultView;

        // Afficher les métriques clés dans la barre latérale MetricsBar
        try
        {
            MetricsBar.Items.Clear();
            for (int j = 0; j < comp.Labels.Count; j++)
            {
                var panel = new System.Windows.Controls.StackPanel { Width = 220, Margin = new System.Windows.Thickness(6,4,6,4) };
                var header = new System.Windows.Controls.TextBlock { Text = comp.Labels[j], FontWeight = System.Windows.FontWeights.Bold, TextWrapping = System.Windows.TextWrapping.Wrap };
                panel.Children.Add(header);

                void addLine(string name, string value)
                {
                    var tb = new System.Windows.Controls.TextBlock { Text = $"{name}: {value}", FontSize = 12 };
                    panel.Children.Add(tb);
                }

                if (savedMetrics != null && savedMetrics.Count > 0 && j == 0)
                {
                    // utiliser les métriques sauvegardées pour le premier portefeuille
                    string[] keys = new[] { "Sharpe", "Treynor", "Information", "Alpha", "Beta", "AnnualReturn", "AnnualVolatility" };
                    foreach (var k in keys)
                    {
                        var v = savedMetrics.TryGetValue(k, out var vv) && vv.HasValue ? vv.Value : double.NaN;
                        addLine(k, double.IsNaN(v) ? "NaN" : v.ToString("F4"));
                    }
                }
                else
                {
                    // utiliser les métriques calculées
                    addLine("Sharpe", comp.Sharpe.ElementAtOrDefault(j).ToString("F4"));
                    addLine("Treynor", comp.Treynor.ElementAtOrDefault(j).ToString("F4"));
                    addLine("Information", comp.Information.ElementAtOrDefault(j).ToString("F4"));
                    addLine("Alpha", comp.Alpha.ElementAtOrDefault(j).ToString("F4"));
                    addLine("Beta", comp.Beta.ElementAtOrDefault(j).ToString("F4"));
                }

                MetricsBar.Items.Add(panel);
            }
        }
        catch
        {
            // ignorer les erreurs d'affichage de la barre latérale
        }
    }

    private async void SaveToDbButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedAssets == null || _loadedAssets.Count == 0)
        {
            MessageBox.Show("Aucun actif chargé. Téléchargez d'abord des séries de prix.", "Sauvegarde", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // choisir le portefeuille à sauvegarder: portefeuille optimisé courant si présent, sinon égalitaire
        Portfolio toSave;
        if (_currentPortfolio != null)
            toSave = _currentPortfolio;
        else
        {
            var eq = Enumerable.Repeat(1.0 / _loadedAssets.Count, _loadedAssets.Count).ToList();
            toSave = new Portfolio(_loadedAssets, eq);
        }

        // nom fourni par l'utilisateur via ComboBox (editable)
        var name = SavedPortfoliosComboBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) name = $"Portfolio_{DateTime.Now:yyyyMMdd_HHmmss}";

        try
        {
            // tenter de calculer les métriques avancées (si benchmark fourni, elles seront incluses)
            IDictionary<string, double?>? metrics = null;
            var bench = BenchmarkTextBox.Text?.Trim();
            try
            {
                metrics = await ComputeMetricsForCurrentPortfolioAsync(string.IsNullOrWhiteSpace(bench) ? null : bench.ToUpperInvariant());
            }
            catch (Exception ex)
            {
                // si le calcul échoue, on logge et on continue sans métriques
                OutputTextBox.AppendText("Calcul des métriques avancées échoué: " + ex.Message + "\r\n");
                metrics = null;
            }

            DatabaseManager.SavePortfolio(toSave, name, metrics);
            OutputTextBox.AppendText($"Portefeuille '{name}' sauvegardé dans la base.\r\n");
            RefreshSavedPortfoliosList();
            SavedPortfoliosComboBox.Text = name;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erreur lors de la sauvegarde en base: " + ex.Message, "Sauvegarde", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadFromDbButton_Click(object sender, RoutedEventArgs e)
    {
        var name = SavedPortfoliosComboBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Sélectionnez un portefeuille dans la liste ou saisissez son nom.", "Charger", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var p = DatabaseManager.LoadPortfolio(name);
            if (p == null)
            {
                MessageBox.Show($"Portefeuille '{name}' introuvable dans la base.", "Charger", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _loadedAssets = p.Assets;
            _currentPortfolio = p;
            UpdateCompositionView(p);
            ExportButton.IsEnabled = true;
            OptimizeButton.IsEnabled = true;

            // recalculer la frontière si possible
            var opt = new Optimizer();
            _frontier = opt.EfficientFrontier(_loadedAssets, step: 0.02);
            UpdatePlot();

            OutputTextBox.AppendText($"Portefeuille '{name}' chargé depuis la base.\r\n");

            // Calculer la performance si un benchmark est fourni
            try
            {
                var bench = BenchmarkTextBox.Text?.Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(bench) && _loadedAssets != null && _loadedAssets.Count > 0)
                    await ComputePerformanceAsync(bench);
            }
            catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erreur lors du chargement depuis la base: " + ex.Message, "Charger", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async System.Threading.Tasks.Task ComputePerformanceAsync(string benchmarkTicker)
    {
        var dp = new DataProvider();

        // déterminer la période commune : utiliser la plus petite longueur de rendements parmi les actifs
        int N = _loadedAssets.Min(a => a.Returns?.Count ?? 0);
        if (N <= 0)
        {
            MessageBox.Show("Les actifs chargés ne contiennent pas de rendements valides.", PerfDialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // récupérer le benchmark
        var result = await dp.GetHistoricalPricesWithTimestampsAsync(benchmarkTicker, range: "1y", interval: "1d");
        var benchPrices = result.Prices;
        if (benchPrices == null || benchPrices.Count < 2)
        {
            MessageBox.Show($"Impossible de récupérer des prix pour {benchmarkTicker}.", PerfDialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        int nAssets = _loadedAssets.Count;

    List<double>? portReturns = null;
    List<double>? benchReturns = null;
    // portefeuille / poids utilisés pour le calcul
    Portfolio? p = null;
    List<double>? weights = null;

        // Si l'utilisateur a fourni une période explicite, tenter d'extraire les sous-séries par dates
        if (StartDatePicker.SelectedDate.HasValue && EndDatePicker.SelectedDate.HasValue)
        {
            var from = StartDatePicker.SelectedDate.Value.Date;
            var to = EndDatePicker.SelectedDate.Value.Date;

            // extraire pour chaque actif les prix dans la fenêtre [from,to]
            var assetReturnsList = new List<List<double>>();
            foreach (var a in _loadedAssets)
            {
                if (a.HistoricalDates != null && a.HistoricalDates.Count == a.HistoricalPrices.Count)
                {
                    var subPrices = new List<double>();
                    for (int i = 0; i < a.HistoricalPrices.Count; i++)
                    {
                        var d = a.HistoricalDates[i].Date;
                        if (d >= from && d <= to) subPrices.Add(a.HistoricalPrices[i]);
                    }
                    if (subPrices.Count >= 2)
                    {
                        var rets = new List<double>();
                        for (int i = 1; i < subPrices.Count; i++) rets.Add(subPrices[i] / subPrices[i - 1] - 1.0);
                        assetReturnsList.Add(rets);
                    }
                }
            }

            if (assetReturnsList.Count == 0)
            {
                MessageBox.Show("Aucune donnée d'actif disponible sur la période demandée.", PerfDialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // aligner sur la plus courte longueur commune
            int minLen = assetReturnsList.Min(l => l.Count);
            for (int i = 0; i < assetReturnsList.Count; i++)
            {
                if (assetReturnsList[i].Count > minLen)
                    assetReturnsList[i] = assetReturnsList[i].Skip(assetReturnsList[i].Count - minLen).Take(minLen).ToList();
            }

            // poids: utiliser portefeuille courant si présent, sinon égalitaire
            if (_currentPortfolio != null)
            {
                p = _currentPortfolio;
                weights = p.Weights;
            }
            else
            {
                weights = Enumerable.Repeat(1.0 / nAssets, nAssets).ToList();
                p = new Portfolio(_loadedAssets, weights);
            }

            portReturns = new List<double>(minLen);
            for (int t = 0; t < minLen; t++)
            {
                double r = 0.0;
                for (int i = 0; i < assetReturnsList.Count; i++) r += weights[i] * assetReturnsList[i][t];
                portReturns.Add(r);
            }

            // benchmark : récupérer les prix sur la même période
            var benchResult = await dp.GetHistoricalPricesWithTimestampsAsync(benchmarkTicker, range: "1y", interval: "1d", from: from, to: to);
            var benchPricesRange = benchResult.Prices;
            var benchDatesRange = benchResult.Timestamps;
            if (benchPricesRange == null || benchPricesRange.Count < 2)
            {
                MessageBox.Show($"Le benchmark {benchmarkTicker} n'a pas suffisamment de données sur la période demandée.", PerfDialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var benchR = new List<double>();
            for (int i = 1; i < benchPricesRange.Count; i++) benchR.Add(benchPricesRange[i] / benchPricesRange[i - 1] - 1.0);

            // aligner benchmark sur minLen
            if (benchR.Count >= minLen)
                benchReturns = benchR.Skip(benchR.Count - minLen).Take(minLen).ToList();
            else
            {
                // si le benchmark est plus court, adapter portReturns
                int newN = benchR.Count;
                if (newN == 0)
                {
                    MessageBox.Show($"Le benchmark {benchmarkTicker} n'a pas suffisamment de données pour le calcul.", PerfDialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                benchReturns = benchR;
                portReturns = portReturns.Skip(portReturns.Count - newN).Take(newN).ToList();
            }
        }
        else
        {
            // construire vecteurs de rendements alignés (prendre les N derniers jours communs)
            var series = BuildAlignedSeries(_loadedAssets, N);

            // portefeuille déjà défini plus haut (p, weights)
            if (p == null || weights == null)
            {
                if (_currentPortfolio != null)
                {
                    p = _currentPortfolio;
                    weights = p.Weights;
                }
                else
                {
                    weights = Enumerable.Repeat(1.0 / nAssets, nAssets).ToList();
                    p = new Portfolio(_loadedAssets, weights);
                }
            }

            portReturns = ComputePortfolioReturnsFromSeries(weights, series);

            // benchmark returns : calculer les rendements simples à partir des prix
            var benchReturnsFull = new List<double>();
            for (int i = 1; i < benchPrices.Count; i++) benchReturnsFull.Add(benchPrices[i] / benchPrices[i - 1] - 1.0);

            if (benchReturnsFull.Count >= portReturns.Count)
            {
                benchReturns = benchReturnsFull.Skip(benchReturnsFull.Count - portReturns.Count).Take(portReturns.Count).ToList();
            }
            else
            {
                int newN = benchReturnsFull.Count;
                if (newN == 0)
                {
                    MessageBox.Show($"Le benchmark {benchmarkTicker} n'a pas suffisamment de données pour le calcul.", PerfDialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                benchReturns = benchReturnsFull;
                // adapter portReturns
                portReturns = portReturns.Skip(portReturns.Count - newN).Take(newN).ToList();
            }
        }

        // Alpha & Beta
        var (alpha, beta) = PerformanceAnalyzer.ComputeAlphaBeta(portReturns, benchReturns);

        // Treynor : utiliser rendement annualisé du portefeuille (ComputePortfolioReturn renvoie annualisé)
        double portfolioAnnualReturn = p.ComputePortfolioReturn();
        double rf = 0.0; // on laisse rf à 0 par défaut (on pourrait ajouter un champ UI)
        double treynor = PerformanceAnalyzer.ComputeTreynor(portfolioAnnualReturn, rf, beta);

        // Information Ratio : sur les excès journaliers (r_p - r_b)
        var excess = portReturns.Zip(benchReturns, (rp, rb) => rp - rb).ToList();
        double infoRatio = PerformanceAnalyzer.ComputeInformationRatio(excess);

        // Max drawdown : construire série cumulative du portefeuille
        var cumulative = new List<double>();
        double cum = 1.0;
        foreach (var r in portReturns)
        {
            cum *= (1.0 + r);
            cumulative.Add(cum);
        }
        double maxDd = PerformanceAnalyzer.ComputeMaxDrawdown(cumulative);

        // autres indicateurs : Sharpe, Sortino, annualisé, tracking error, Calmar
        double annualVol = p.ComputePortfolioVolatility();
        double sharpe = PerformanceAnalyzer.ComputeSharpe(portfolioAnnualReturn, rf, annualVol);
        double sortino = PerformanceAnalyzer.ComputeSortino(portReturns, rf);
        double tracking = PerformanceAnalyzer.ComputeTrackingError(excess);
        double calmar = PerformanceAnalyzer.ComputeCalmar(portfolioAnnualReturn, maxDd);

        // annualized return/vol from periodic as well (utile à afficher)
        double annualFromPeriodic = PerformanceAnalyzer.ComputeAnnualizedReturnFromPeriodic(portReturns);
        double annualVolFromPeriodic = PerformanceAnalyzer.ComputeAnnualizedVolatility(portReturns);

        // Afficher résultats dans l'UI
        AlphaText.Text = $"Alpha: {alpha:F6}";
        BetaText.Text = $"Beta: {beta:F6}";
        TreynorText.Text = double.IsNaN(treynor) ? "Treynor: NaN" : $"Treynor: {treynor:F6}";
        InfoRatioText.Text = double.IsNaN(infoRatio) ? "Information Ratio (annualisé): NaN" : $"Information Ratio (annualisé): {infoRatio:F6}";
        MaxDdText.Text = $"Max Drawdown: {maxDd:P2}";
        AnnualReturnText.Text = $"Rendement annualisé: {annualFromPeriodic:P2}";
        AnnualVolText.Text = $"Volatilité annualisée: {annualVolFromPeriodic:P2}";
        SharpeText.Text = double.IsNaN(sharpe) ? "Sharpe (ann.): NaN" : $"Sharpe (ann.): {sharpe:F4}";
        SortinoText.Text = double.IsNaN(sortino) ? "Sortino: NaN" : $"Sortino: {sortino:F4}";
        TrackingText.Text = double.IsNaN(tracking) ? "Tracking Error: NaN" : $"Tracking Error: {tracking:F4}";
        CalmarText.Text = double.IsNaN(calmar) ? "Calmar: NaN" : $"Calmar: {calmar:F4}";
    }

    /// <summary>
    /// Calcule et retourne un dictionnaire des métriques avancées pour le portefeuille courant.
    /// Si benchmarkTicker est null ou vide, seules les métriques indépendantes du benchmark sont calculées.
    /// </summary>
    private async System.Threading.Tasks.Task<IDictionary<string, double?>> ComputeMetricsForCurrentPortfolioAsync(string? benchmarkTicker)
    {
        var result = new Dictionary<string, double?>();

        if (_loadedAssets == null || _loadedAssets.Count == 0)
            throw new InvalidOperationException("Aucun actif chargé pour le calcul des métriques.");

        var dp = new DataProvider();

        // déterminer la période commune : utiliser la plus petite longueur de rendements parmi les actifs
        int N = _loadedAssets.Min(a => a.Returns?.Count ?? 0);
        if (N <= 0)
        {
            // pas de séries: retourner au moins les métriques basiques depuis Portfolio
            Portfolio ptmp;
            if (_currentPortfolio != null) ptmp = _currentPortfolio;
            else ptmp = new Portfolio(_loadedAssets, Enumerable.Repeat(1.0 / _loadedAssets.Count, _loadedAssets.Count).ToList());

            result["AnnualReturn"] = ptmp.ComputePortfolioReturn();
            result["AnnualVolatility"] = ptmp.ComputePortfolioVolatility();
            result["Sharpe"] = PerformanceAnalyzer.ComputeSharpe(result["AnnualReturn"].GetValueOrDefault(), 0.0, result["AnnualVolatility"].GetValueOrDefault());
            return result;
        }

        List<double> portReturns;
        List<double> benchReturns;
        Portfolio p = _currentPortfolio ?? new Portfolio(_loadedAssets, Enumerable.Repeat(1.0 / _loadedAssets.Count, _loadedAssets.Count).ToList());
        var weights = p.Weights;

        if (StartDatePicker.SelectedDate.HasValue && EndDatePicker.SelectedDate.HasValue)
        {
            var from = StartDatePicker.SelectedDate.Value.Date;
            var to = EndDatePicker.SelectedDate.Value.Date;

            var assetReturnsList = new List<List<double>>();
            foreach (var a in _loadedAssets)
            {
                if (a.HistoricalDates != null && a.HistoricalDates.Count == a.HistoricalPrices.Count)
                {
                    var subPrices = new List<double>();
                    for (int i = 0; i < a.HistoricalPrices.Count; i++)
                    {
                        var d = a.HistoricalDates[i].Date;
                        if (d >= from && d <= to) subPrices.Add(a.HistoricalPrices[i]);
                    }
                    if (subPrices.Count >= 2)
                    {
                        var rets = new List<double>();
                        for (int i = 1; i < subPrices.Count; i++) rets.Add(subPrices[i] / subPrices[i - 1] - 1.0);
                        assetReturnsList.Add(rets);
                    }
                }
            }

            if (assetReturnsList.Count == 0) throw new InvalidOperationException("Aucune donnée d'actif disponible sur la période demandée.");

            int minLen = assetReturnsList.Min(l => l.Count);
            for (int i = 0; i < assetReturnsList.Count; i++)
            {
                if (assetReturnsList[i].Count > minLen)
                    assetReturnsList[i] = assetReturnsList[i].Skip(assetReturnsList[i].Count - minLen).Take(minLen).ToList();
            }

            portReturns = new List<double>(minLen);
            for (int t = 0; t < minLen; t++)
            {
                double r = 0.0;
                for (int i = 0; i < assetReturnsList.Count; i++) r += weights[i] * assetReturnsList[i][t];
                portReturns.Add(r);
            }

            if (!string.IsNullOrWhiteSpace(benchmarkTicker))
            {
                var benchResult = await dp.GetHistoricalPricesWithTimestampsAsync(benchmarkTicker, range: "1y", interval: "1d", from: from, to: to);
                var benchPricesRange = benchResult.Prices;
                if (benchPricesRange == null || benchPricesRange.Count < 2)
                    throw new InvalidOperationException("Le benchmark n'a pas suffisamment de données sur la période demandée.");
                var benchR = new List<double>();
                for (int i = 1; i < benchPricesRange.Count; i++) benchR.Add(benchPricesRange[i] / benchPricesRange[i - 1] - 1.0);
                if (benchR.Count >= minLen) benchReturns = benchR.Skip(benchR.Count - minLen).Take(minLen).ToList();
                else
                {
                    int newN = benchR.Count;
                    benchReturns = benchR;
                    portReturns = portReturns.Skip(portReturns.Count - newN).Take(newN).ToList();
                }
            }
            else benchReturns = new List<double>();
        }
        else
        {
            var series = BuildAlignedSeries(_loadedAssets, N);
            portReturns = ComputePortfolioReturnsFromSeries(weights, series);
            if (!string.IsNullOrWhiteSpace(benchmarkTicker))
            {
                var benchResult = await dp.GetHistoricalPricesWithTimestampsAsync(benchmarkTicker, range: "1y", interval: "1d");
                var benchPrices = benchResult.Prices;
                if (benchPrices == null || benchPrices.Count < 2) throw new InvalidOperationException("Impossible de récupérer des prix pour le benchmark.");
                var benchReturnsFull = new List<double>();
                for (int i = 1; i < benchPrices.Count; i++) benchReturnsFull.Add(benchPrices[i] / benchPrices[i - 1] - 1.0);
                if (benchReturnsFull.Count >= portReturns.Count) benchReturns = benchReturnsFull.Skip(benchReturnsFull.Count - portReturns.Count).Take(portReturns.Count).ToList();
                else
                {
                    int newN = benchReturnsFull.Count;
                    benchReturns = benchReturnsFull;
                    portReturns = portReturns.Skip(portReturns.Count - newN).Take(newN).ToList();
                }
            }
            else benchReturns = new List<double>();
        }

        // calculs principaux
        double portfolioAnnualReturn = p.ComputePortfolioReturn();
        double annualVol = p.ComputePortfolioVolatility();
        result["AnnualReturn"] = portfolioAnnualReturn;
        result["AnnualVolatility"] = annualVol;
        result["Sharpe"] = PerformanceAnalyzer.ComputeSharpe(portfolioAnnualReturn, 0.0, annualVol);

        // compute periodic-derived metrics
        result["AnnualReturnFromPeriodic"] = PerformanceAnalyzer.ComputeAnnualizedReturnFromPeriodic(portReturns);
        result["AnnualVolFromPeriodic"] = PerformanceAnalyzer.ComputeAnnualizedVolatility(portReturns);

        // cumulative & max drawdown
        var cumulative = new List<double>(); double cum = 1.0; foreach (var r in portReturns) { cum *= (1.0 + r); cumulative.Add(cum); }
        result["MaxDrawdown"] = PerformanceAnalyzer.ComputeMaxDrawdown(cumulative);

        if (!string.IsNullOrWhiteSpace(benchmarkTicker) && benchReturns != null && benchReturns.Count > 1)
        {
            var (alpha, beta) = PerformanceAnalyzer.ComputeAlphaBeta(portReturns, benchReturns);
            result["Alpha"] = alpha;
            result["Beta"] = beta;
            result["Treynor"] = PerformanceAnalyzer.ComputeTreynor(portfolioAnnualReturn, 0.0, beta);
            var excess = portReturns.Zip(benchReturns, (rp, rb) => rp - rb).ToList();
            result["InformationRatio"] = PerformanceAnalyzer.ComputeInformationRatio(excess);
            result["TrackingError"] = PerformanceAnalyzer.ComputeTrackingError(excess);
            result["Sortino"] = PerformanceAnalyzer.ComputeSortino(portReturns, 0.0);
            result["Calmar"] = PerformanceAnalyzer.ComputeCalmar(portfolioAnnualReturn, result["MaxDrawdown"].GetValueOrDefault());
        }

        return result;
    }

    private List<double> ComputePortfolioReturnsFromSeries(List<double> weights, double[][] series)
    {
        int nAssets = series.Length;
        int N = series[0].Length;
        var port = new List<double>(N);
        for (int t = 0; t < N; t++)
        {
            double r = 0.0;
            for (int i = 0; i < nAssets; i++) r += weights[i] * series[i][t];
            port.Add(r);
        }
        return port;
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
    private async void BenchmarkTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
    // anti-rebond pour la saisie rapide
        try
        {
            _benchCts?.Cancel();
            _benchCts = new CancellationTokenSource();
            var token = _benchCts.Token;
            var text = BenchmarkTextBox.Text?.Trim().ToUpperInvariant();
            // attendre un court délai pour éviter de déclencher à chaque frappe
            await Task.Delay(600, token);
            if (token.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_loadedAssets == null || _loadedAssets.Count == 0) return;
            await ComputePerformanceAsync(text);
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            OutputTextBox.AppendText("Erreur calcul performance auto: " + ex.Message + "\r\n");
        }
    }

}