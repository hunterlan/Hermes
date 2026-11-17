using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class RequestSettingsViewModelTests : IDisposable
{
    // Resolved inside each test, once the headless app is running.
    private SettingsService _appSettings = null!;
    private bool _originalFollowRedirects;
    private bool _originalVerifySsl;

    private RequestSettingsViewModel CreateSettings()
    {
        _appSettings = App.Services.GetRequiredService<SettingsService>();
        _originalFollowRedirects = _appSettings.Current.FollowRedirects;
        _originalVerifySsl = _appSettings.Current.VerifySsl;
        return new RequestSettingsViewModel(_appSettings);
    }

    /// <summary>Changes the app settings in place, the way the app does for a one-setting change.</summary>
    private void SetAppSettings(bool followRedirects, bool verifySsl)
    {
        _appSettings.Current.FollowRedirects = followRedirects;
        _appSettings.Current.VerifySsl = verifySsl;
        _appSettings.SaveCurrent();
    }

    public void Dispose()
    {
        if (_appSettings is not null)
            SetAppSettings(_originalFollowRedirects, _originalVerifySsl);
    }

    private static string Serialized(RequestSettings settings) =>
        ModelCloner.Serialize(new HttpRequestModel { Id = Guid.Empty, Settings = settings });

    public static TheoryData<string, RequestSettings> RoundTripSettings() => new()
    {
        { "defaults", new RequestSettings() },
        { "follow redirects off", new RequestSettings { FollowRedirects = false } },
        { "follow redirects on", new RequestSettings { FollowRedirects = true } },
        { "verify SSL off", new RequestSettings { VerifySsl = false } },
        { "verify SSL on", new RequestSettings { VerifySsl = true } },
        { "timeout", new RequestSettings { TimeoutMs = 2500 } },
        { "no timeout", new RequestSettings { TimeoutMs = 0 } },
        { "all", new RequestSettings { FollowRedirects = false, VerifySsl = true, TimeoutMs = 10 } },
    };

    [AvaloniaTheory]
    [MemberData(nameof(RoundTripSettings))]
    public void Loading_settings_and_reading_them_back_gives_the_same_settings(string name, RequestSettings settings)
    {
        var vm = CreateSettings();

        vm.Load(settings);

        Assert.Equal(Serialized(settings), Serialized(vm.ToModel()));
        Assert.NotNull(name);
    }

    [AvaloniaTheory]
    [InlineData(" 2500 ", 2500)]
    [InlineData("0", 0)]
    [InlineData("", null)]
    [InlineData("-1", null)]
    [InlineData("abc", null)]
    [InlineData("1.5", null)]
    public void Timeout_text_is_parsed_with_the_invariant_culture(string text, int? expected)
    {
        var vm = CreateSettings();

        vm.TimeoutText = text;

        Assert.Equal(expected, vm.ToModel().TimeoutMs);
    }

    [AvaloniaFact]
    public void Loading_a_timeout_shows_it_as_text()
    {
        var vm = CreateSettings();

        vm.Load(new RequestSettings { TimeoutMs = 0 });

        Assert.Equal("0", vm.TimeoutText);
    }

    [AvaloniaFact]
    public void Nulls_mean_no_override_and_follow_the_app_settings()
    {
        var vm = CreateSettings();

        vm.Load(new RequestSettings());

        Assert.Null(vm.FollowRedirectsOverride);
        Assert.Null(vm.VerifySslOverride);
        Assert.Equal(_appSettings.Current.FollowRedirects, vm.FollowRedirects);
        Assert.Equal(_appSettings.Current.VerifySsl, vm.VerifySsl);
        Assert.False(vm.HasSettingsOverrides);
    }

    [AvaloniaFact]
    public void Effective_values_follow_an_app_settings_change_without_an_override()
    {
        var vm = CreateSettings();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var followRedirects = !_originalFollowRedirects;
        var verifySsl = !_originalVerifySsl;

        SetAppSettings(followRedirects, verifySsl);
        vm.OnAppSettingsChanged();

        Assert.Equal(followRedirects, vm.FollowRedirects);
        Assert.Equal(verifySsl, vm.VerifySsl);
        Assert.Contains(nameof(RequestSettingsViewModel.FollowRedirects), changed);
        Assert.Contains(nameof(RequestSettingsViewModel.VerifySsl), changed);
    }

    [AvaloniaFact]
    public void An_override_wins_over_the_app_settings()
    {
        var vm = CreateSettings();
        vm.Load(new RequestSettings { FollowRedirects = false, VerifySsl = true });

        SetAppSettings(followRedirects: true, verifySsl: false);
        vm.OnAppSettingsChanged();

        Assert.False(vm.FollowRedirects);
        Assert.True(vm.VerifySsl);
    }

    [AvaloniaFact]
    public void Toggling_a_value_makes_it_an_override()
    {
        var vm = CreateSettings();
        var appVerifies = _appSettings.Current.VerifySsl;

        vm.VerifySsl = !appVerifies;

        Assert.Equal(!appVerifies, vm.VerifySslOverride);
        Assert.Equal(!appVerifies, vm.VerifySsl);
        Assert.True(vm.HasSettingsOverrides);
    }

    [AvaloniaFact]
    public void A_timeout_alone_is_an_override()
    {
        var vm = CreateSettings();

        vm.TimeoutText = "1500";

        Assert.True(vm.HasSettingsOverrides);
        vm.TimeoutText = "-1";
        Assert.False(vm.HasSettingsOverrides);
    }

    [AvaloniaFact]
    public void Use_default_settings_clears_every_override()
    {
        var vm = CreateSettings();
        vm.Load(new RequestSettings { FollowRedirects = true, VerifySsl = false, TimeoutMs = 300 });

        vm.UseDefaultSettingsCommand.Execute(null);

        Assert.Equal(Serialized(new RequestSettings()), Serialized(vm.ToModel()));
        Assert.False(vm.HasSettingsOverrides);
    }

    [AvaloniaFact]
    public void Changed_is_raised_for_edits_but_not_for_load()
    {
        var vm = CreateSettings();
        var changes = 0;
        vm.Changed += () => changes++;

        vm.Load(new RequestSettings { FollowRedirects = true, VerifySsl = false, TimeoutMs = 300 });
        Assert.Equal(0, changes);

        vm.FollowRedirects = false;
        Assert.Equal(1, changes);

        vm.TimeoutText = "";
        Assert.Equal(2, changes);
    }
}
