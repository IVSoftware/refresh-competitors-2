I have updated this answer from your comments:

> The score is actually not saved in the competitor, but in a separate Dictionary in which every Competitor Id is bound to a Score. That's what I meant by "the data in my ObservableRangeCollection aren't actually changing.
___

> I don't have to store the Scores at several places, I can just save them in one object and access it globally.

___
> On a previous View the User gets to pick a stage, and on this View the user gets to pick a competitor. 

With that clarification, the answer to _**OnPropertyChanged on an ObservableCollection doesn't refresh a CollectionView content**_ has two explanations at the first approximation. The first, like you say, _the data in my ObservableRangeCollection aren't actually changing_ (I know, that's what you're trying to "fix" with `ItemsChangedObservableRangeCollection`) but also the `PropertyChanged` event does _very little_ for an `ObservableCollection<T>` because the visible list is updated by the `CollectionChanged` event, not the `PropertyChanged` event.  It responds to Add, Remove, Replace, Move, and Reset only.

When you put items into the list, their properties can be observable too. It's just that this _has no bearing on the list_. None. That is, it doesn't make it longer or shorter. But that shouldn't bother you or make you want to write a custom class, because the refresh that you seek happens in the `DataTemplate` which is bound to an instance of `Competitor`. It has almost nothing to do with the list other than being contained by it.

___

#### Is better binding the way forward here?

Based on everything you said (and this is more a perspective than a deterministic answer) - bindings might take you only so far. There is another way and since I'm dealing with similar issues in my own app I've been doing proof of concept for driving the data with persistent SQLite in place of an ephemeral `Dictionary` in memory. 

Looking at your code I do have a concern about what happens if the app is suspended, or closed (as often happens inadvertently on mobile devices). In order to avoid losing all the Competitor data when that happens you probably already have (or will want to have) an SQLite database to persist the info. It follows that it might be a good fit to make this "global object" _be_ the `SQLiteAsyncConnection` and declare it in `App` perhaps. 

```csharp
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new AppShell();
    }

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

    static bool DEBUG_START_FRESH = false;
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

                if (DEBUG_START_FRESH && File.Exists(DatabasePath))
                {
                    File.Delete(DatabasePath);
                }

                using (var cnx = new SQLiteConnection(DatabasePath))
                {
                    cnx.CreateTable<CompetitorRecord>();
                    cnx.CreateTable<ScoreRecord>();
                    if (DEBUG_START_FRESH)
                    {
                        cnx.InsertAll(
                            new object[]
                            {
                                new CompetitorRecord { FullName = "Alex Johnson", Id = "AJ-001234", Number = 7 },
                                new ScoreRecord { Id = "AJ-001234", Score = "85", Phase = Phase.PreliminaryRound },
                                new ScoreRecord { Id = "AJ-001234", Score = "85.1", Phase = Phase.GroupStage },
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
}
```

___

### Refresh a CollectionView (with Filters)

**Your question states an intent to _Refresh a CollectionView_.** But if the backing store of your Competitor data is now the SQLite database, that puts a different spin on the `Refresh()` that occurs in response to `OnAppearing()`. Suppose you were to requery the database in this method, where the justification would include the added benefit of being able to apply filters such as FullName and Stage.

[![effect of name filter][1]][1]

___

Selecting a Stage in the picker could make a leader board by sorting the scores of competitors who have one in that phase.


[![effect-of-stage-filter][2]][2]

___

##### Database Tables

If the database has a table for the `Competitor` basic info, and a relational table of `ScoreRecords` where a given `Competitor` might have 0 to N score records, then for MainForm.OnAppearing():

##### View
```
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

```
##### Model with Refresh()


```
class MainPageBindingContext : INotifyPropertyChanged
{
    public MainPageBindingContext()
    {
        EditCompetitorCommand = new Command<CompetitorRecord>((competitor) =>
        {
            ActiveCompetitor = competitor;
            _ = Shell.Current.GoToAsync("StagesPage");
        });
        ...
    }
    // Yes good to use ObservableRangeCollection here
    // Will be more performant for the UI updates.
    public ObservableRangeCollection<CompetitorRecord> CompetitorQueryFilteredResult { get; } = 
        new ObservableRangeCollection<CompetitorRecord>();

    private CompetitorRecord[] _competitorQueryResult;
    public ICommand EditCompetitorCommand { get; }

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
 }
 ```

___

#### Access to common data from other views

```
<Shell
    ...
    Shell.FlyoutBehavior="Disabled">

    <ShellContent
        Title="Home"
        ContentTemplate="{DataTemplate local:MainPage}"
        Route="MainPage" />

    <ShellContent
        Title="Phases"
        ContentTemplate="{DataTemplate local:StagesPage}"
        Route="StagesPage" />
</Shell>
```

Swiping a card left in the `MainPage` opens the `StagesPage` view. The `Refresh` here is much simpler, showing the competitor's score for a stage if they have one, or a placeholder if they don't. A stage card can then be swiped left to open an editing overlay for the score.

[![workflow main card swiped left][3]][3]

```
class StagesPageBindingContext : INotifyPropertyChanged
{
    ...

    public async Task Refresh()
    {
        var existingScores = await
            ACnx
            .Table<ScoreRecord>()
            .Where(_ => _.Id == ActiveCompetitor.Id)
            .ToArrayAsync();
        var stagedScores = new List<ScoreRecord>();
        await Task.Run(() =>
        {
            foreach (Stage phase in Enum.GetValues<Stage>())
            {
                if (existingScores.FirstOrDefault(_ => _.Stage == phase) is ScoreRecord existing)
                {
                    stagedScores.Add(existing);
                }
                else
                {
                    stagedScores.Add(new ScoreRecord { Id = ActiveCompetitor.Id, Stage = phase });
                };
            }
        });
        Busy = true;
        Stages.Clear();
        await Task.Delay(250);
        Stages.AddRange(stagedScores);
        Busy = false;
    }

    // Controls can be bound to the static member of App (they
    // are one-way in this context) instead of IValueConverter
    public CompetitorRecord? ActiveCompetitor => 
        App.ActiveCompetitor;

    public ObservableRangeCollection<ScoreRecord> Stages { get; } =
        new ObservableRangeCollection<ScoreRecord>();

    public ICommand EditScoreCommand { get; }
    private void OnEditScore(ScoreRecord score)
    {
        EditTarget = score;
        IsEditingScore = true;
        // Work with a copy so that Cancel can leave original unchanged.
        ScorePreview = score.Score;
    }

    ScoreRecord EditTarget {  get; set; }

    public ICommand ApplyCommand { get; private set; }
    private async void OnApply(object o)
    {
        EditTarget.Score = ScorePreview;
        IsEditingScore = false;
        if (string.IsNullOrWhiteSpace(EditTarget.Score))
        {
            await ACnx.DeleteAsync(EditTarget);
        }
        else
        {
            await ACnx.InsertOrReplaceAsync(EditTarget);
        }
        await Refresh();
    }
    ...

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    public event PropertyChangedEventHandler? PropertyChanged;
}
```

___

When the [Apply] button is clicked, it returns to an visually updated `StagesPage`. When the `MainPage` is navigated to, it will show the updated values, including (for example) an updated and re-sorted leader board for the selected phase.

[![effect of apply][4]][4]


  [1]: https://i.sstatic.net/TspQhtJj.png
  [2]: https://i.sstatic.net/gVX6n8Iz.png
  [3]: https://i.sstatic.net/WxnI5cGw.png
  [4]: https://i.sstatic.net/QsN2ZSZn.png