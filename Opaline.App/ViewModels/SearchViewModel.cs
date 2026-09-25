using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.Core.Models;
using Opaline.Core.Services;

namespace Opaline.App.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly IYouTubeService _yt;
    private string? _continuation;
    private string _lastQuery = string.Empty;

    public ObservableCollection<Video> Results { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string query = string.Empty;

    public SearchViewModel(IYouTubeService yt)
    {
        _yt = yt;
    }

    [RelayCommand]
    public async Task SearchAsync(string? q = null)
    {
        var term = (q ?? Query)?.Trim();
        if (string.IsNullOrEmpty(term)) return;

        Query = term;
        _lastQuery = term;
        _continuation = null;
        IsLoading = true;
        ErrorMessage = null;
        Results.Clear();

        try
        {
            var page = await _yt.SearchAsync(term);
            foreach (var item in page.Items)
            {
                if (item is VideoSearchItem v)
                    Results.Add(v.Video);
            }
            _continuation = page.ContinuationToken;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsLoading || string.IsNullOrEmpty(_continuation)) return;
        IsLoading = true;
        try
        {
            var page = await _yt.SearchAsync(_lastQuery, _continuation);
            foreach (var item in page.Items)
            {
                if (item is VideoSearchItem v)
                    Results.Add(v.Video);
            }
            _continuation = page.ContinuationToken;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Load more failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
