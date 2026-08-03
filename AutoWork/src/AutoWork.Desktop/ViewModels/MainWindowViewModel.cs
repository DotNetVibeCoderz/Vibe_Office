using AutoWork.Core.Agents;
using AutoWork.Desktop.Localization;
using AutoWork.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWork.Desktop.ViewModels;

public enum Section
{
    Work,
    Jobs,
    History,
    Recycle,
    Activity,
    Knowledge,
    Skills,
    Mcp,
    Integrations,
    Settings,
}

/// <summary>
/// The shell. Owns the section views and the organ rail in the header.
///
/// The organ rail is the piece of chrome that earns its place: three segments that light in
/// their own hue as the Brain, Eyes or Hands take over. It tells you at a glance whether
/// AutoWork is reasoning, looking at your screen, or touching your files — which is the single
/// thing a person most wants to know about an agent running on their own machine.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(AppServices services)
    {
        Services = services;

        Work = new WorkViewModel(services);
        JobsPage = new JobsViewModel(services);
        History = new HistoryViewModel(services, Work, () => Section = Section.Work);

        // The scheduler decides when a job is due; running it goes through the same Work view a
        // person uses, on the UI thread, so a scheduled run raises its consent cards where they
        // can be answered.
        services.RunJob = async (job, reason, _) =>
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                Section = Section.Work;
                await Work.RunJobAsync(job, reason);
            });

        services.Scheduler.Start();
        Recycle = new RecycleViewModel(services);
        Activity = new ActivityViewModel(services);
        Knowledge = new KnowledgeViewModel(services);
        Skills = new SkillsViewModel(services);
        Mcp = new McpViewModel(services);
        Integrations = new IntegrationsViewModel(services);
        SettingsPage = new SettingsViewModel(services);

        SettingsPage.Applied += () =>
        {
            Work.RefreshModelAvailability();

            // The MCP capability switch lives in Permissions, so the gallery has to be told
            // when it changes or it keeps claiming servers are blocked.
            Mcp.Refresh();

            // Turning history off — or shortening how long it is kept — has to show up here,
            // otherwise the page keeps promising to record runs that are no longer being kept.
            History.Reload();

            OnPropertyChanged(nameof(L));
        };

        Work.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkViewModel.ActiveOrgan)) RaiseOrganFlags();
        };

        // Land on Settings when there is nothing to run with — the first thing a new install
        // needs is a model, and hiding that behind a nav click helps nobody.
        _section = services.Config.Current.ResolveExecutorModel() is null ? Section.Settings : Section.Work;
    }

    public AppServices Services { get; }
    public Strings L => Services.Strings;

    public WorkViewModel Work { get; }
    public JobsViewModel JobsPage { get; }
    public HistoryViewModel History { get; }
    public RecycleViewModel Recycle { get; }
    public ActivityViewModel Activity { get; }
    public KnowledgeViewModel Knowledge { get; }
    public SkillsViewModel Skills { get; }
    public McpViewModel Mcp { get; }
    public IntegrationsViewModel Integrations { get; }
    public SettingsViewModel SettingsPage { get; }

    [ObservableProperty] private Section _section;

    public bool IsWork => Section == Section.Work;
    public bool IsJobs => Section == Section.Jobs;
    public bool IsHistory => Section == Section.History;
    public bool IsRecycle => Section == Section.Recycle;
    public bool IsActivity => Section == Section.Activity;
    public bool IsKnowledge => Section == Section.Knowledge;
    public bool IsSkills => Section == Section.Skills;
    public bool IsMcp => Section == Section.Mcp;
    public bool IsIntegrations => Section == Section.Integrations;
    public bool IsSettings => Section == Section.Settings;

    public bool ThinkActive => Work.ActiveOrgan == AgentOrgan.Brain;
    public bool SeeActive => Work.ActiveOrgan == AgentOrgan.Eyes;
    public bool ActActive => Work.ActiveOrgan == AgentOrgan.Hands;

    [RelayCommand]
    private void Navigate(Section section) => Section = section;

    partial void OnSectionChanged(Section value)
    {
        OnPropertyChanged(nameof(IsWork));
        OnPropertyChanged(nameof(IsJobs));
        OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(IsRecycle));
        OnPropertyChanged(nameof(IsActivity));
        OnPropertyChanged(nameof(IsKnowledge));
        OnPropertyChanged(nameof(IsSkills));
        OnPropertyChanged(nameof(IsMcp));
        OnPropertyChanged(nameof(IsIntegrations));
        OnPropertyChanged(nameof(IsSettings));

        if (value == Section.Knowledge) Knowledge.ReloadCommand.Execute(null);

        // Reads from disk, so it refreshes on arrival — a run that just finished has to be
        // there without the user thinking to press anything.
        if (value == Section.History) History.Reload();
        if (value == Section.Jobs) JobsPage.Reload();

        // Same reason: a file deleted a moment ago has to be there without a manual refresh.
        if (value == Section.Recycle) Recycle.Reload();

        // Not a browse: that costs network requests and stays a deliberate click.
        if (value == Section.Mcp) Mcp.Refresh();
    }

    private void RaiseOrganFlags()
    {
        OnPropertyChanged(nameof(ThinkActive));
        OnPropertyChanged(nameof(SeeActive));
        OnPropertyChanged(nameof(ActActive));
    }
}
