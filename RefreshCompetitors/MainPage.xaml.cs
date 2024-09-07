using IVSoftware.Portable;
using MvvmHelpers;
using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using static RefreshCompetitors.App;

namespace RefreshCompetitors
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            searchBar.TextChanged += (sender, e) =>
            {
                var capture = searchBar.Text;
                _wdtSearch.StartOrRestart(action: () =>
                {
                    BindingContext.RefreshSearch(searchText: capture);
                });
                _wdtClose.StartOrRestart(action: () =>
                {
                    searchBar.Unfocus();
                });
            };
        }
        // <PackageReference Include="IVSoftware.Portable.WatchdogTimer" Version="1.2.1" />
        WatchdogTimer _wdtSearch = new WatchdogTimer{ Interval=TimeSpan.FromSeconds(0.4) };
        WatchdogTimer _wdtClose = new WatchdogTimer{ Interval=TimeSpan.FromSeconds(1.0) };
        protected override void OnAppearing()
        {
            base.OnAppearing();
            BindingContext.Refresh();
        }
        new MainPageBindingContext BindingContext => (MainPageBindingContext)base.BindingContext;
    }

    class MainPageBindingContext : INotifyPropertyChanged
    {
        public MainPageBindingContext()
        {
            EditCompetitorCommand = new Command<CompetitorRecord>((competitor) =>
            {
                ActiveCompetitor = competitor;
                _ = Shell.Current.GoToAsync("StagesPage");
            });
            ClearStageFilterCommand = new Command((o) => SelectedStage = null);
        }
        public ObservableRangeCollection<CompetitorRecord> CompetitorQueryFilteredResult { get; } = 
            new ObservableRangeCollection<CompetitorRecord>();
        private CompetitorRecord[] _competitorQueryResult;
        public ICommand EditCompetitorCommand { get; }
        public ICommand ClearStageFilterCommand { get; }
        public async void Refresh()
        {
            _competitorQueryResult = await
                ACnx
                .Table<CompetitorRecord>()
                .ToArrayAsync();
            Busy = true;
            var stageRange = new List<CompetitorRecord>();
            foreach (var record in _competitorQueryResult)
            { 
                stageRange.Add(record);
                record.ScoresQueryResult = await 
                    ACnx.Table<ScoreRecord>()
                    .Where(_ => _.Id == record.Id)
                    .ToArrayAsync();
            }
            RefreshSearch(_prevSearchText);
            Busy = false;
        }
        private string? _prevSearchText = null;
        internal async void RefreshSearch(string? searchText)
        {
            CompetitorQueryFilteredResult.Clear();
            List<CompetitorRecord> preview;
            if (string.IsNullOrEmpty(searchText))
            {
                preview = _competitorQueryResult.ToList();
            }
            else
            {
                preview = new List<CompetitorRecord>();
                foreach (
                    var record in
                    _competitorQueryResult
                    .Where(_ => _.FullName.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
                {
                    preview.Add(record);
                }
            }
            localRefreshStages();
            CompetitorQueryFilteredResult.AddRange(preview);
            _prevSearchText = searchText;

            void localRefreshStages()
            {
                foreach (var record in preview)
                {
                    record.FilteredScores.Clear();
                    if (SelectedStage == null)
                    {
                        record.FilteredScores.AddRange(record.ScoresQueryResult);
                    }
                    else
                    {
                        record.FilteredScores.AddRange(record.ScoresQueryResult.Where(_ => _.Stage == SelectedStage));
                    }
                }
                if (SelectedStage is Stage notNullStage)
                {
                    preview.Sort((a, b) =>
                    {
                        var scoreA = a.GetStageScore(notNullStage);
                        var scoreB = b.GetStageScore(notNullStage);
                        double dValA, dValB;
                        if(double.TryParse(scoreA, out dValA))
                        {
                            if (double.TryParse(scoreB, out dValB))
                            {
                                // Highest to Lowest
                                return dValB.CompareTo(dValA);
                            }
                            else return -1; // Parsable before unparsable
                        }
                        if (double.TryParse(scoreB, out dValB))
                        {
                            return 1; // Parsable before unparsable
                        }
                        if(string.IsNullOrWhiteSpace(scoreA))
                        {
                            if (string.IsNullOrWhiteSpace(scoreB))
                            {
                                // Neither record has a score for this stage
                                return a.FullName.CompareTo(b.FullName);
                            }
                            else return 1; // Something before nothing
                        }
                        if (string.IsNullOrWhiteSpace(scoreB))
                        {
                            return -1; // Something before nothing.
                        }
                        return scoreA.CompareTo(scoreB);
                    });
                }
                else
                {
                    preview.Sort((a, b) => a.FullName.CompareTo(b.FullName));
                }
            }
        }

        public bool Busy
        {
            get => _busy;
            set
            {
                if (!Equals(_busy, value))
                {
                    _busy = value;
                    OnPropertyChanged();
                }
            }
        }
        bool _busy = default; 
        public Stage? SelectedStage
        {
            get => _selectedStage;
            set
            {
                _selectedStage = value;
                OnPropertyChanged(nameof(SelectedStage));
                // You can do this in place of an IValueConverter:
                OnPropertyChanged(nameof(IsStageFiltered));
                RefreshSearch(_prevSearchText);
            }
        }

        private Stage? _selectedStage;

        public bool IsStageFiltered => SelectedStage != null;
        public Stage[] Stages { get; } = Enum.GetValues<Stage>();


        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Table("Competitors")]
    public class CompetitorRecord
    {
        [PrimaryKey]
        public string Guid { get; set; } = System.Guid.NewGuid().ToString().ToUpper();
        /// <summary>
        /// A string, to allow something like "RP-002916"
        /// </summary>
        [Unique]
        public string? Id { get; set; }
        public string? FullName { get; set; }
        [Unique]
        public int Number { get; set; }

        [Ignore]
        public ScoreRecord[]? ScoresQueryResult { get; set; }
        public ObservableRangeCollection<ScoreRecord> FilteredScores { get; } = new ObservableRangeCollection<ScoreRecord>();

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        internal string? GetStageScore(Stage selectedStage) =>
            ScoresQueryResult?
            .FirstOrDefault(_ => _.Stage == selectedStage)?.Score;


        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Table("Scores")]
    public class ScoreRecord
    {
        [PrimaryKey]
        public string Guid { get; set; } = System.Guid.NewGuid().ToString().ToUpper();
        public string? Id { get; set; }
        /// <summary>
        /// A string, for expediency for testing, allow direct formatted entry
        /// </summary>
        public string? Score { get; set; }

        public Stage Stage { get; set; }

        public string StageDescription => $"{Stage}".CamelCaseToSpaces();

        public bool IsScoreVisible => !string.IsNullOrWhiteSpace(Score);

        public Color BorderColor
        {
            get
            {
                if(Score == null) return Colors.DimGray;
                switch (Stage)
                {
                    case Stage.PreliminaryRound: return Colors.Blue;
                    case Stage.GroupStage: return Colors.LightBlue;
                    case Stage.KnockoutStage: return Colors.LightSalmon;
                    case Stage.Semifinals: return Colors.LightGreen;
                    case Stage.Finals: return Colors.Green;
                    default: return Colors.Black;
                }
            }
        }
    }
}
