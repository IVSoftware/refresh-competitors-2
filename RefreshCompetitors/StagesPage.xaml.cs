using MvvmHelpers;
using SQLite;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using static RefreshCompetitors.App;

namespace RefreshCompetitors;

public partial class StagesPage : ContentPage
{
	public StagesPage() => InitializeComponent();
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = BindingContext.Refresh();
    }
    new StagesPageBindingContext BindingContext => (StagesPageBindingContext)base.BindingContext;
}

public enum Stage
{
    PreliminaryRound,
    GroupStage,
    KnockoutStage,
    Semifinals,
    Finals,
}
class StagesPageBindingContext : INotifyPropertyChanged
{
    public StagesPageBindingContext()
    {
        BackNavCommand = new Command((o) =>
                _ = Shell.Current.GoToAsync("//MainPage")); 
        EditScoreCommand = new Command<ScoreRecord>(OnEditScore);
        ApplyCommand = new Command(OnApply);
        CancelCommand = new Command(OnCancel);
    }
    public ObservableRangeCollection<ScoreRecord> Stages { get; } =
        new ObservableRangeCollection<ScoreRecord>();
    public ICommand BackNavCommand { get; }
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

    public ICommand CancelCommand { get; private set; }
    private void OnCancel(object o) => IsEditingScore = false;

    // Controls can be bound to the static member of App (they
    // are one-way in this context) instead of IValueConverter
    public CompetitorRecord? ActiveCompetitor => 
        App.ActiveCompetitor;
    public bool IsEditingScore
    {
        get => _isEditingScore;
        set
        {
            if (!Equals(_isEditingScore, value))
            {
                _isEditingScore = value;
                OnPropertyChanged();
                // You can do this in place of an IValueConverter:
                OnPropertyChanged(nameof(IsNotEditingScore));
            }
        }
    }
    bool _isEditingScore = default;
    public bool IsNotEditingScore => !IsEditingScore;


    /// <summary>
    /// Editing value for score that can be reverted on cancel.
    /// </summary>
    public string? ScorePreview
    {
        get => _scorePreview;
        set
        {
            if (!Equals(_scorePreview, value))
            {
                _scorePreview = value;
                OnPropertyChanged();
            }
        }
    }
    string? _scorePreview = string.Empty;

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
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class Extensions
{
    public static string CamelCaseToSpaces(this string @string)
    {
        // This pattern looks for a capital letter that is either followed by a lowercase letter
        // or is not directly preceded by another capital letter, excluding the start of the string.
        string pattern = "(?<![A-Z])([A-Z][a-z]|(?<=[a-z])[A-Z])";
        string replacement = " $1"; // $1 refers to the first captured group in the pattern
        return Regex.Replace(@string, pattern, replacement).Trim();
    }
}