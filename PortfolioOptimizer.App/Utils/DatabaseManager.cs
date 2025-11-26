using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Data.SQLite;
using PortfolioOptimizer.App.Models;

namespace PortfolioOptimizer.App.Utils
{
    /// <summary>
    /// Gestion simple d'une base SQLite pour persister des portefeuilles et leurs positions.
    /// Tables:
    /// - Portfolios(id INTEGER PK, name TEXT UNIQUE, created_at TEXT)
    /// - Positions(id INTEGER PK, portfolio_id INTEGER, ticker TEXT, weight REAL, expected_return REAL, volatility REAL)
    /// </summary>
    public static class DatabaseManager
    {
        private static readonly string DbFile = Path.Combine(AppContext.BaseDirectory, "portfolio.db");
        private static readonly string ConnectionString = $"Data Source={DbFile};Version=3;";

        /// <summary>
        /// Initialise la base: crée le fichier (si nécessaire) et les tables.
        /// Appelle CREATE TABLE IF NOT EXISTS pour être idempotent.
        /// </summary>
        public static void InitDatabase()
        {
            // Créer le répertoire si nécessaire
            try
            {
                var dir = Path.GetDirectoryName(DbFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Si le fichier n'existe pas, le créer
                using var conn = new SQLiteConnection(ConnectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Portfolios (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Positions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    portfolio_id INTEGER NOT NULL,
    ticker TEXT NOT NULL,
    weight REAL NOT NULL,
    expected_return REAL,
    volatility REAL,
    FOREIGN KEY(portfolio_id) REFERENCES Portfolios(id) ON DELETE CASCADE
);
 
 CREATE TABLE IF NOT EXISTS PerformanceMetrics (
     id INTEGER PRIMARY KEY AUTOINCREMENT,
     portfolio_id INTEGER NOT NULL,
     metric_name TEXT NOT NULL,
     metric_value REAL,
     created_at TEXT NOT NULL,
     FOREIGN KEY(portfolio_id) REFERENCES Portfolios(id) ON DELETE CASCADE
 );
";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // ignorer les erreurs d'initialisation
                throw new Exception("Erreur lors de l'initialisation de la base SQLite: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Sauvegarde un portefeuille dans la base sous le nom fourni.
        /// Si un portefeuille du même nom existe, il est remplacé.
        /// </summary>
        public static void SavePortfolio(Portfolio p, string name, System.Collections.Generic.IDictionary<string, double?>? metrics = null)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Le nom du portefeuille est requis.", nameof(name));

            InitDatabase();

            using var conn = new SQLiteConnection(ConnectionString);
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                // Supprimer un portefeuille existant avec le même nom
                using (var cmdDel = conn.CreateCommand())
                {
                    cmdDel.CommandText = "SELECT id FROM Portfolios WHERE name = @name";
                    cmdDel.Parameters.AddWithValue("@name", name);
                    var existing = cmdDel.ExecuteScalar();
                    if (existing != null && existing != DBNull.Value)
                    {
                        long existingId = (long)existing;
                        using var cmdRemPos = conn.CreateCommand();
                        cmdRemPos.CommandText = "DELETE FROM Positions WHERE portfolio_id = @id";
                        cmdRemPos.Parameters.AddWithValue("@id", existingId);
                        cmdRemPos.ExecuteNonQuery();

                        using var cmdRemPort = conn.CreateCommand();
                        cmdRemPort.CommandText = "DELETE FROM Portfolios WHERE id = @id";
                        cmdRemPort.Parameters.AddWithValue("@id", existingId);
                        cmdRemPort.ExecuteNonQuery();
                    }
                }

                // Insérer nouveau portefeuille
                long portfolioId;
                using (var cmdIns = conn.CreateCommand())
                {
                    cmdIns.CommandText = "INSERT INTO Portfolios(name, created_at) VALUES(@name, @created)";
                    cmdIns.Parameters.AddWithValue("@name", name);
                    cmdIns.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    cmdIns.ExecuteNonQuery();
                    cmdIns.CommandText = "SELECT last_insert_rowid()";
                    portfolioId = (long)cmdIns.ExecuteScalar();
                }

                // Insérer positions
                for (int i = 0; i < p.Assets.Count; i++)
                {
                    var a = p.Assets[i];
                    var w = p.Weights[i];
                    using var cmdPos = conn.CreateCommand();
                    cmdPos.CommandText = "INSERT INTO Positions(portfolio_id, ticker, weight, expected_return, volatility) VALUES(@pid, @ticker, @weight, @er, @vol)";
                    cmdPos.Parameters.AddWithValue("@pid", portfolioId);
                    cmdPos.Parameters.AddWithValue("@ticker", a.Ticker ?? string.Empty);
                    cmdPos.Parameters.AddWithValue("@weight", w);
                    cmdPos.Parameters.AddWithValue("@er", double.IsNaN(a.ExpectedReturn) ? (object)DBNull.Value : a.ExpectedReturn);
                    cmdPos.Parameters.AddWithValue("@vol", double.IsNaN(a.Volatility) ? (object)DBNull.Value : a.Volatility);
                    cmdPos.ExecuteNonQuery();
                }

                // Insérer métriques de performance si fournies
                if (metrics != null)
                {
                    foreach (var kv in metrics)
                    {
                        using var cmdMet = conn.CreateCommand();
                        cmdMet.CommandText = "INSERT INTO PerformanceMetrics(portfolio_id, metric_name, metric_value, created_at) VALUES(@pid, @name, @value, @created)";
                        cmdMet.Parameters.AddWithValue("@pid", portfolioId);
                        cmdMet.Parameters.AddWithValue("@name", kv.Key ?? string.Empty);
                        cmdMet.Parameters.AddWithValue("@value", kv.Value.HasValue ? (object)kv.Value.Value : DBNull.Value);
                        cmdMet.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        cmdMet.ExecuteNonQuery();
                    }
                }

                tran.Commit();
            }
            catch (Exception ex)
            {
                try { tran.Rollback(); } catch { }
                throw new Exception("Erreur lors de la sauvegarde du portefeuille: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Charge un portefeuille par son nom. Retourne un objet Portfolio si trouvé, sinon null.
        /// Les actifs sont construits avec Ticker, ExpectedReturn et Volatility (les séries historiques ne sont pas restaurées ici).
        /// </summary>
        public static Portfolio? LoadPortfolio(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Le nom du portefeuille est requis.", nameof(name));

            InitDatabase();

            using var conn = new SQLiteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM Portfolios WHERE name = @name";
            cmd.Parameters.AddWithValue("@name", name);
            var idObj = cmd.ExecuteScalar();
            if (idObj == null || idObj == DBNull.Value) return null;
            long pid = (long)idObj;

            var assets = new List<Asset>();
            var weights = new List<double>();

            using var cmdPos = conn.CreateCommand();
            cmdPos.CommandText = "SELECT ticker, weight, expected_return, volatility FROM Positions WHERE portfolio_id = @pid ORDER BY id";
            cmdPos.Parameters.AddWithValue("@pid", pid);
            using var rdr = cmdPos.ExecuteReader();
            while (rdr.Read())
            {
                string ticker = rdr.GetString(0);
                double weight = rdr.GetDouble(1);
                double? er = rdr.IsDBNull(2) ? (double?)null : rdr.GetDouble(2);
                double? vol = rdr.IsDBNull(3) ? (double?)null : rdr.GetDouble(3);

                Asset a;
                if (er.HasValue || vol.HasValue)
                {
                    // utiliser le constructeur qui accepte ER/vol si disponible
                    a = new Asset(ticker, er ?? 0.0, vol ?? 0.0);
                }
                else
                {
                    a = new Asset(ticker);
                }

                assets.Add(a);
                weights.Add(weight);
            }

            if (assets.Count == 0) return null;

            // Normaliser les poids si nécessaire
            var sum = weights.Sum();
            if (Math.Abs(sum - 1.0) > 1e-9 && sum > 0)
            {
                for (int i = 0; i < weights.Count; i++) weights[i] = weights[i] / sum;
            }

            return new Portfolio(assets, weights);
        }

        /// <summary>
        /// Récupère les métriques de performance enregistrées pour un portefeuille donné (par nom).
        /// Retourne un dictionnaire metricName -> metricValue (nullable si valeur NULL en base).
        /// Si le portefeuille n'existe pas ou n'a pas de métriques, retourne un dictionnaire vide.
        /// </summary>
        public static IDictionary<string, double?> GetPortfolioMetrics(string name)
        {
            var res = new Dictionary<string, double?>();
            if (string.IsNullOrWhiteSpace(name)) return res;

            InitDatabase();
            using var conn = new SQLiteConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM Portfolios WHERE name = @name";
            cmd.Parameters.AddWithValue("@name", name);
            var idObj = cmd.ExecuteScalar();
            if (idObj == null || idObj == DBNull.Value) return res;
            long pid = (long)idObj;

            using var cmdMet = conn.CreateCommand();
            cmdMet.CommandText = "SELECT metric_name, metric_value FROM PerformanceMetrics WHERE portfolio_id = @pid"
                + " ORDER BY id";
            cmdMet.Parameters.AddWithValue("@pid", pid);
            using var rdr = cmdMet.ExecuteReader();
            while (rdr.Read())
            {
                var key = rdr.IsDBNull(0) ? string.Empty : rdr.GetString(0);
                double? val = rdr.IsDBNull(1) ? (double?)null : rdr.GetDouble(1);
                if (!string.IsNullOrEmpty(key) && !res.ContainsKey(key)) res[key] = val;
            }

            return res;
        }

        /// <summary>
        /// Liste les noms de portefeuilles enregistrés.
        /// </summary>
        public static List<string> ListPortfolios()
        {
            InitDatabase();
            var res = new List<string>();
            using var conn = new SQLiteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM Portfolios ORDER BY created_at DESC";
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read()) res.Add(rdr.GetString(0));
            return res;
        }

        /// <summary>
        /// Supprime un portefeuille (et ses positions/métriques) par nom.
        /// </summary>
        public static void DeletePortfolio(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Le nom du portefeuille est requis.", nameof(name));
            InitDatabase();
            using var conn = new SQLiteConnection(ConnectionString);
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id FROM Portfolios WHERE name = @name";
                cmd.Parameters.AddWithValue("@name", name);
                var idObj = cmd.ExecuteScalar();
                if (idObj == null || idObj == DBNull.Value)
                {
                    tran.Commit();
                    return;
                }
                long pid = (long)idObj;

                using var cmdDelPos = conn.CreateCommand();
                cmdDelPos.CommandText = "DELETE FROM Positions WHERE portfolio_id = @pid";
                cmdDelPos.Parameters.AddWithValue("@pid", pid);
                cmdDelPos.ExecuteNonQuery();

                using var cmdDelMet = conn.CreateCommand();
                cmdDelMet.CommandText = "DELETE FROM PerformanceMetrics WHERE portfolio_id = @pid";
                cmdDelMet.Parameters.AddWithValue("@pid", pid);
                cmdDelMet.ExecuteNonQuery();

                using var cmdDelPort = conn.CreateCommand();
                cmdDelPort.CommandText = "DELETE FROM Portfolios WHERE id = @pid";
                cmdDelPort.Parameters.AddWithValue("@pid", pid);
                cmdDelPort.ExecuteNonQuery();

                tran.Commit();
            }
            catch (Exception ex)
            {
                try { tran.Rollback(); } catch { }
                throw new Exception("Erreur lors de la suppression du portefeuille: " + ex.Message, ex);
            }
        }
    }
}
