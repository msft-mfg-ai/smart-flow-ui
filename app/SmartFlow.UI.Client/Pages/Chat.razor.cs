// Copyright (c) Microsoft. All rights reserved.

using System.Net;

namespace SmartFlow.UI.Client.Pages;

public sealed partial class Chat
{
    //private const long MaxIndividualFileSize = 1_024L * 1_024;

    private MudForm _form = null!;

    // User input and selections
    private string _userQuestion = "";
    private List<FileSummary> _files = new();
    private List<DocumentSummary> _userDocuments = new();
    private string _selectedDocument = "";
    private UserQuestion _currentQuestion;

    private bool _filtersSelected = false;

    private string _selectedProfile = "";
    private List<ProfileSummary> _profiles = new();
    private ProfileSummary? _selectedProfileSummary = null;
    private ProfileSummary? _userUploadProfileSummary = null;
    private UserSelectionModel? _userSelectionModel = null;

    private string _lastReferenceQuestion = "";
    private bool _isReceivingResponse = false;
    private bool _supportsFileUpload = false;

    private readonly Dictionary<UserQuestion, ApproachResponse?> _questionAndAnswerMap = [];

    private bool _gPT4ON = false;
    private Guid _chatId = Guid.NewGuid();
    private string? _agentThreadId = null;

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] public required HttpClient HttpClient { get; set; }
    [Inject] public required ApiClient ApiClient { get; set; }
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required NavigationManager Navigation { get; set; }
    [Inject] public required ISnackbar Snackbar { get; set; }

    [CascadingParameter(Name = nameof(Settings))] public required RequestSettingsOverrides Settings { get; set; }
    [CascadingParameter(Name = nameof(IsReversed))] public required bool IsReversed { get; set; }

    public bool _showProfiles { get; set; }
    public bool _errorLoadingProfiles { get; set; }
    public string _errorLoadingMessage { get; set; } = string.Empty;

    public bool _showDocumentUpload { get; set; }
    public bool _showPictureUpload { get; set; }
    [SupplyParameterFromQuery(Name = "cid")] public string? ArchivedChatId { get; set; }
    [SupplyParameterFromQuery(Name = "profile")] public string? QueryProfileName { get; set; }
    [SupplyParameterFromQuery(Name = "message")] public string? QueryInitialMessage { get; set; }

    private HashSet<DocumentSummary> _selectedDocuments = new HashSet<DocumentSummary>();

    private HashSet<DocumentSummary> SelectedDocuments
    {
        get => _selectedDocuments;
        set
        {
            _selectedDocuments = value;
            OnSelectedDocumentsChanged();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var user = await ApiClient.GetUserAsync();
        _profiles = user.Profiles.Where(x => x.Approach != ProfileApproach.UserDocumentChat).ToList();
        _userUploadProfileSummary = user.Profiles.FirstOrDefault(x => x.Approach == ProfileApproach.UserDocumentChat);

        if (!string.IsNullOrEmpty(QueryProfileName) && !string.IsNullOrEmpty(QueryInitialMessage))
        {
            var profileToSelect = _profiles.FirstOrDefault(p => p.Name.Equals(QueryProfileName, StringComparison.OrdinalIgnoreCase));
            if (profileToSelect != null)
            {
                await SetSelectedProfileAsync(profileToSelect);
                _userQuestion = QueryInitialMessage; 
                _ = OnAskClickedAsync(); 
            }
            else
            {
                showWarning($"Profile '{QueryProfileName}' not found. Defaulting to standard behavior.");
                await LoadDefaultProfileOrArchivedChatAsync();
            }
        }
        else
        {
            await LoadDefaultProfileOrArchivedChatAsync();
        }
        
        _errorLoadingMessage = _profiles.Count > 0 ? string.Empty : $" Error loading profiles...! {user.SessionId}";
        EvaluateOptions();
        StateHasChanged();
    }

    private async Task LoadDefaultProfileOrArchivedChatAsync()
    {
        if (!string.IsNullOrEmpty(ArchivedChatId))
        {
            showInfo("Loading chat history...");
            await LoadArchivedChatAsync(_cancellationTokenSource.Token, ArchivedChatId);
        }
        else if (_profiles.Count > 0 && _selectedProfileSummary == null) // Only set default if no profile is selected yet
        {
            await SetSelectedProfileAsync(_profiles.First());
        }
    }

    private async Task OnProfileClickAsync(string selection)
    {
        await SetSelectedProfileAsync(_profiles.Single(x => x.Name == selection));
        OnClearChat();
    }
    private async Task SetSelectedProfileAsync(ProfileSummary profile)
    {
        _selectedProfile = profile.Name;
        _selectedProfileSummary = profile;
        _supportsFileUpload = _selectedProfileSummary.SupportsFileUpload;
        if (_userUploadProfileSummary != null)
        {
            var userDocuments = await ApiClient.GetUserDocumentsAsync();
            _userDocuments = userDocuments.ToList();
        }
        if (profile.SupportsUserSelectionOptions)
        {
            _userSelectionModel = await ApiClient.GetProfileUserSelectionModelAsync(profile.Id);
        }
    }
    private void OnFileUpload(FileSummary fileSummary)
    {
        _files.Add(fileSummary);
        Console.WriteLine($"OnFileUpload - {_files.Count()}");
    }
    private void OnModelSelection(bool isPremium)
    {
        _gPT4ON = isPremium;
    }

    private Task OnAskQuestionAsync(string userInput)
    {
        _userQuestion = userInput;
        return OnAskClickedAsync();
    }

    private Task OnPromptTemplateClickedAsync(string promptTemplate)
    {
        _userQuestion = promptTemplate;
        return OnAskClickedAsync();
    }


    private async Task OnRetryQuestionAsync()
    {
        _questionAndAnswerMap.Remove(_currentQuestion);
        await OnAskClickedAsync();
    }

    private async Task OnAskClickedAsync()
    {
        Console.WriteLine($"OnAskClickedAsync: {_userQuestion}");

        if (string.IsNullOrWhiteSpace(_userQuestion))
        {
            return;
        }

        if (_userSelectionModel != null)
        {
            foreach (var option in _userSelectionModel.Options)
            {
                _userQuestion = _userQuestion.Replace($"${option.Name}", option.SelectedValue);
            }
        }

        _isReceivingResponse = true;
        _lastReferenceQuestion = _userQuestion;
        _currentQuestion = new(_userQuestion, DateTime.Now);
        _questionAndAnswerMap[_currentQuestion] = null;

        try
        {
            var history = _questionAndAnswerMap.Where(x => x.Value is not null).Select(x => new ChatTurn(x.Key.Question, x.Value.Answer)).ToList();
            history.Add(new ChatTurn(_userQuestion.Trim()));

            var options = new Dictionary<string, string>
            {
                ["GPT4ENABLED"] = _gPT4ON.ToString(),
                ["PROFILE"] = _selectedProfile
            };

            if (_userUploadProfileSummary != null && SelectedDocuments.Any())
            {
                options["PROFILE"] = _userUploadProfileSummary.Name;
            }

            var request = new ChatRequest(
                _chatId,
                Guid.NewGuid(),
                history.ToArray(),
                SelectedDocuments.Select(x => x.Name),
                _files,
                options,
                _userSelectionModel,
                _agentThreadId);

            var responseBuffer = new StringBuilder();
            await foreach (var chunk in ApiClient.StreamChatAsync(request))
            {
                if (chunk == null)
                {
                    continue;
                }

                responseBuffer.Append(chunk.Text);
                var responseText = responseBuffer.ToString();

                if (chunk.FinalResult != null)
                {
                    _questionAndAnswerMap[_currentQuestion] = new ApproachResponse(chunk.FinalResult.Answer, chunk.FinalResult.CitationBaseUrl, chunk.FinalResult.Context);
                    _isReceivingResponse = false;
                    _userQuestion = "";
                    _currentQuestion = default;

                    if(chunk.FinalResult.Context?.ThreadId != null)
                        _agentThreadId = chunk.FinalResult.Context.ThreadId.ToString();
                }
                else
                {
                    _questionAndAnswerMap[_currentQuestion] = new ApproachResponse(responseText, null, null);
                    _isReceivingResponse = true;
                }

                await Task.Delay(1);
                StateHasChanged();
            }
        }
        catch (HttpRequestException ex)
        {
            string msg;
            if (ex.StatusCode.HasValue)
            {
                msg = ex.StatusCode.Value switch
                {
                    HttpStatusCode.NotFound => "Error: API Defined Incorrectly!",
                    HttpStatusCode.TooManyRequests => "Error: Rate Limit exceeded!",
                    _ => "Error: Unable to get a response from the server."
                };
            }
            else
            {
                msg = "Error: Unable to get a response from the server. Status code not available.";
            }
            _questionAndAnswerMap[_currentQuestion] = new ApproachResponse(string.Empty, null, null, msg);
        }
        catch (JsonException)
        {
            _questionAndAnswerMap[_currentQuestion] = new ApproachResponse(string.Empty, null, null, "Error: Failed to parse the server response.");
        }
        finally
        {
            _isReceivingResponse = false;
            _files.Clear();
            StateHasChanged();
        }
    }



    private void OnSelectedDocumentsChanged()
    {
        Console.WriteLine($"SelectedDocuments: {SelectedDocuments.Count()}");
        if (SelectedDocuments.Any())
        {
            if (SelectedDocuments.Count() == 1)
            {
                _selectedDocument = $"{SelectedDocuments.First().Name}";
            }
            else
            {
                _selectedDocument = $"{SelectedDocuments.Count()} - Documents selected";
            }
        }
        else
        {
            _selectedDocument = string.Empty;
        }

        OnClearChatDocuumentSelection();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        Console.WriteLine($"OnAfterRenderAsync: _isReceivingResponse - {_isReceivingResponse}");
        await JS.InvokeVoidAsync("scrollToBottom", "answerSection");
        await JS.InvokeVoidAsync("highlight");
        //if (!_isReceivingResponse)
        //{
        //    await JS.InvokeVoidAsync("renderMathJax");
        //}
    }

    private void OnClearChatDocuumentSelection()
    {
        _userQuestion = _lastReferenceQuestion = "";
        _currentQuestion = default;
        _questionAndAnswerMap.Clear();
        _chatId = Guid.NewGuid();
        _agentThreadId = null;
        EvaluateOptions();
    }

    private void OnClearChat()
    {
        _userQuestion = _lastReferenceQuestion = "";
        _currentQuestion = default;
        _questionAndAnswerMap.Clear();
        _selectedDocument = "";
        SelectedDocuments.Clear();
        _chatId = Guid.NewGuid();
        _agentThreadId = null;
        _files.Clear();

        EvaluateOptions();
    }

    private void EvaluateOptions()
    {
        _errorLoadingProfiles = _selectedProfileSummary == null;

        // hide options when no profiles are loaded ?
        // what about the case where the user is uploading a document?
        if (_errorLoadingProfiles)
        {
            _showProfiles = false;
            _showDocumentUpload = false;
            _showPictureUpload = false;
            return;
        }

        // show profiles if there are multiple profiles or if there's a document selected
        _showProfiles = _profiles.Count > 1 || !string.IsNullOrEmpty(_selectedDocument);

        // show document upload if there are no profiles that support it
        _showDocumentUpload = _profiles.Any(p => p.Approach == ProfileApproach.UserDocumentChat);

        // show picture upload when approach is chat and document is not already selected
        _showPictureUpload = _selectedProfileSummary?.Approach == ProfileApproach.Chat && string.IsNullOrEmpty(_selectedDocument);
    }

    private async Task LoadArchivedChatAsync(CancellationToken cancellationToken, string chatId)
    {
        var chatMessages = await System.Linq.AsyncEnumerable.ToListAsync(ApiClient.GetChatHistorySessionAsync(cancellationToken, chatId), cancellationToken);
        var profile = chatMessages.First().Profile;
        _selectedProfile = profile;
        _selectedProfileSummary = _profiles.FirstOrDefault(x => x.Name == profile);
        _chatId = Guid.Parse(chatId);

        foreach (var chatMessage in chatMessages.OrderBy(x => x.Timestamp))
        {
            var ar = new ApproachResponse(chatMessage.Answer, chatMessage.ProfileId, new ResponseContext(chatMessage.Profile, chatMessage.DataPoints, Array.Empty<ThoughtRecord>(), Guid.Empty, Guid.Empty, string.Empty, null));
            _questionAndAnswerMap[new UserQuestion(chatMessage.Prompt, chatMessage.Timestamp.UtcDateTime)] = ar;
        }
        Navigation.NavigateTo(string.Empty, forceLoad: false);
    }
    private void showInfo(string message)
    {
        showMessage(message, Severity.Info);
    }
    private void showWarning(string message)
    {
        showMessage(message, Severity.Warning);
    }
    private void showMessage(string message, Severity severity)
    {
        Snackbar.Add(
            message,
            severity,
            static options =>
            {
                options.ShowCloseIcon = true;
                options.VisibleStateDuration = 10_000;
            });
    }
}
