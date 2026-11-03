using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VSModManager.Services;

namespace VSModManager.ViewModels;

/// <summary>
/// The Browse tab: every mod on the ModDB, searchable and sortable. Results are
/// shown a page at a time so 9,000+ rows (and their icons) never load at once.
/// </summary>
public sealed partial class BrowseViewModel(MainViewModel main, ModDbClient client, IconCache icons) : ObservableObject
{
    private const int PageSize = 50;
    private const string AllCategories = "All categories";

    private List<ModDbListEntry> _all = [];
    private List<ModDbListEntry> _matches = [];
    private Task? _loading;

    public ObservableCollection<BrowseItemViewModel> Results { get; } = [];
    public ObservableCollection<string> Categories { get; } = [AllCategories];
    public static IReadOnlyList<string> SortOptions { get; } = ["Trending", "Most downloaded", "Recently updated", "Name"];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _sortIndex;
    [ObservableProperty] private string _category = AllCategories;
    [ObservableProperty] private bool _hideInstalled;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _resultText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowMoreCommand))]
    private bool _hasMore;

    partial void OnSearchTextChanged(string value) => ApplyQuery();
    partial void OnSortIndexChanged(int value) => ApplyQuery();
    partial void OnCategoryChanged(string value) => ApplyQuery();
    partial void OnHideInstalledChanged(bool value) => ApplyQuery();

    /// <summary>Loads the ModDB listing the first time the tab is opened.</summary>
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        ResultText = "Loading the ModDB…";
        try
        {
            // External tools and "other" entries don't go in the Mods folder.
            _all = (await client.GetModListAsync()).Where(e => e.Type == "mod").ToList();

            foreach (var tag in _all.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order())
                Categories.Add(tag);

            ApplyQuery();
        }
        catch (Exception ex)
        {
            ResultText = "Couldn't load the ModDB: " + ex.Message;
            _loading = null; // retry next time the tab is opened
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyQuery()
    {
        if (_all.Count == 0) return;

        var terms = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<ModDbListEntry> q = _all.Where(e =>
            terms.All(t =>
                (e.Name?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Author?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Summary?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                e.ModIdStrs.Any(id => id.Contains(t, StringComparison.OrdinalIgnoreCase))));

        if (Category != AllCategories)
            q = q.Where(e => e.Tags.Contains(Category, StringComparer.OrdinalIgnoreCase));

        if (HideInstalled)
            q = q.Where(e => !e.ModIdStrs.Any(id => main.FindMod(id) is not null));

        q = SortIndex switch
        {
            1 => q.OrderByDescending(e => e.Downloads),
            // "yyyy-MM-dd HH:mm:ss" sorts correctly as text.
            2 => q.OrderByDescending(e => e.LastReleased, StringComparer.Ordinal),
            3 => q.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            _ => q.OrderByDescending(e => e.TrendingPoints).ThenByDescending(e => e.Downloads),
        };

        _matches = q.ToList();
        Results.Clear();
        ShowMore();
    }

    [RelayCommand(CanExecute = nameof(HasMore))]
    private void ShowMore()
    {
        foreach (var entry in _matches.Skip(Results.Count).Take(PageSize))
        {
            var item = new BrowseItemViewModel(main, entry);
            Results.Add(item);
            _ = item.LoadIconAsync(icons);
        }

        HasMore = Results.Count < _matches.Count;
        ResultText = _matches.Count == 1 ? "1 mod" : $"{_matches.Count:N0} mods";
    }

    /// <summary>Re-checks Installed badges, e.g. after mods change on the Installed tab.</summary>
    public void RefreshInstalled()
    {
        foreach (var item in Results)
            item.RefreshInstalled();
    }
}
