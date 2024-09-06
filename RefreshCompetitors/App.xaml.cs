using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using SQLite;

namespace RefreshCompetitors
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new AppShell();
        }
        static bool DEBUG_START_WITH_SIM_DATA = false;  
        private static string DatabasePath
        {
            get
            {
                if (_pathSingleton == null)
                {
                    _pathSingleton =
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            Assembly.GetExecutingAssembly().GetName().Name,
                            "database.db"
                        );
                    Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));

                    if(File.Exists(DatabasePath))
                    { 
                        if (DEBUG_START_WITH_SIM_DATA)
                        {
                            File.Delete(DatabasePath);
                        }                        
                    }
                    else DEBUG_START_WITH_SIM_DATA = true;

                    using (var cnx = new SQLiteConnection(DatabasePath))
                    {
                        cnx.CreateTable<CompetitorRecord>();
                        cnx.CreateTable<ScoreRecord>();
                        if (DEBUG_START_WITH_SIM_DATA)
                        {
                            cnx.InsertAll(
                                new object[]
                                {
                                    new CompetitorRecord { FullName = "Alex Johnson", Id = "AJ-001234", Number = 7 },
                                    new ScoreRecord { Id = "AJ-001234", Score = "85", Stage = Stage.PreliminaryRound },
                                    new ScoreRecord { Id = "AJ-001234", Score = "85.1", Stage = Stage.GroupStage },
                                    new CompetitorRecord { FullName = "Jamie Lee", Id = "JL-001235", Number = 34 },
                                    new CompetitorRecord { FullName = "Morgan Smith", Id = "MS-001236", Number = 23 },
                                    new CompetitorRecord { FullName = "Casey Taylor", Id = "CT-001237", Number = 88 },
                                    new CompetitorRecord { FullName = "Jordan Brown", Id = "JB-001238", Number = 12 }
                                });
                        }
                    }
                }
                return _pathSingleton;
            }
        }
        static string? _pathSingleton = null;

        public static SQLiteAsyncConnection ACnx
        {
            get
            {
                if(_connectionSingleton == null)
                {
                    _connectionSingleton = new SQLiteAsyncConnection(DatabasePath);
                }
                return _connectionSingleton;
            }
        }
        static SQLiteAsyncConnection? _connectionSingleton = null;

        public static CompetitorRecord? ActiveCompetitor { get; set; }
    }
}
