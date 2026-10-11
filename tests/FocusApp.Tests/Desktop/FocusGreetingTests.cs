using System.Text;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusGreetingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FocusApp.GreetingTests", Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_directory, "Config", "FocusGreetings.txt");

    [Fact]
    public void FirstLoadCreatesUtf8TemplateWithTenLinesPerStageWithoutOverwritingEdits()
    {
        var provider = new FocusGreetingProvider(ConfigPath);
        var template = File.ReadAllText(ConfigPath, new UTF8Encoding(false, true));
        Assert.Contains("清晨的安静最适合进入状态，慢慢来。", template);
        Assert.Contains("愿你不负理想，也不辜负每一个好梦。", template);
        foreach (var state in Enum.GetValues<FocusGreetingState>())
            Assert.Equal(10, provider.GetGreetings(state).Count);

        Write("[早上好]\n我的新文案\n");
        provider = new FocusGreetingProvider(ConfigPath);
        provider.Reload();
        Assert.Equal("我的新文案", Assert.Single(provider.GetGreetings(FocusGreetingState.Morning)));
        Assert.Equal("[早上好]\n我的新文案\n", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void MissingFileIsRecreatedAndUnreadableOrInvalidUtf8UsesDefaults()
    {
        var provider = new FocusGreetingProvider(ConfigPath);
        Write("[早上好]\n用户文案\n");
        provider.Reload();
        Assert.Single(provider.GetGreetings(FocusGreetingState.Morning));
        File.Delete(ConfigPath);
        provider.Reload();
        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(10, provider.GetGreetings(FocusGreetingState.Morning).Count);

        File.WriteAllBytes(ConfigPath, [0xC3, 0x28]);
        provider.Reload();
        Assert.Equal(10, provider.GetGreetings(FocusGreetingState.Morning).Count);
        var unreadable = new FocusGreetingProvider(_directory); // A directory cannot be read as a file.
        Assert.Equal(10, unreadable.GetGreetings(FocusGreetingState.Morning).Count);
        using var locked = new FileStream(ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        provider.Reload();
        Assert.Equal(10, provider.GetGreetings(FocusGreetingState.Morning).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n   \n\t\n")]
    [InlineData("不属于任何分组\n[未知]\n不应显示\n[早上好\n损坏标题下的文案")]
    public void EmptyOrMalformedFileFallsBackForAllStages(string content)
    {
        Write(content);
        var provider = new FocusGreetingProvider(ConfigPath);
        foreach (var state in Enum.GetValues<FocusGreetingState>())
            Assert.Equal(10, provider.GetGreetings(state).Count);
        Assert.Equal(content, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void ParserKeepsValidGroupsIgnoresBlankLinesAndFallsBackOnlyForEmptyGroups()
    {
        Write("\uFEFF\n [早上好] \n\n  清晨一  \n清晨二\n清晨二\n[下午好]\n\n" +
              "[未知]\n不能串组\n[晚上好]\n夜晚一\n[夜深了\n损坏标题下的内容\n[夜深了]\n深夜一\n坏\0句子");
        var provider = new FocusGreetingProvider(ConfigPath);
        Assert.Equal(new[] { "清晨一", "清晨二" }, provider.GetGreetings(FocusGreetingState.Morning));
        Assert.Equal(10, provider.GetGreetings(FocusGreetingState.Afternoon).Count);
        Assert.Equal("夜晚一", Assert.Single(provider.GetGreetings(FocusGreetingState.Evening)));
        Assert.Equal("深夜一", Assert.Single(provider.GetGreetings(FocusGreetingState.LateNight)));
    }

    [Fact]
    public void SelectionUsesActualCountAndAvoidsRepeatingThePreviousTextAfterReload()
    {
        var lines = Enumerable.Range(1, 17).Select(index => $"文案{index}").ToArray();
        Write("[下午好]\n" + string.Join('\n', lines));
        var provider = new FocusGreetingProvider(ConfigPath);
        Assert.Equal(17, provider.GetGreetings(FocusGreetingState.Afternoon).Count);
        string? previous = null;
        for (var index = 0; index < 100; index++)
        {
            provider.Reload();
            var selected = provider.Select(FocusGreetingState.Afternoon);
            Assert.Contains(selected, lines);
            Assert.NotEqual(previous, selected);
            previous = selected;
        }
        Write("[下午好]\n唯一一句\n唯一一句\n");
        provider.Reload();
        Assert.Equal("唯一一句", provider.Select(FocusGreetingState.Afternoon));
        Assert.Equal("唯一一句", provider.Select(FocusGreetingState.Afternoon));
    }

    [Fact]
    public void ActualEntryReloadsEvenIfFileWasEditedDuringPreparationAndReentryReadsLatest()
    {
        Write("[下午好]\n进入前\n");
        var provider = new FocusGreetingProvider(ConfigPath);
        var model = new FocusSessionViewModel(() => new DateTime(2026, 10, 11, 14, 0, 0), false, provider);
        model.Start(30);
        Write("[下午好]\n准备期间更新\n");
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        Assert.Equal("准备期间更新", model.GreetingSubtitle);

        Write("[下午好]\n第二次进入\n");
        for (var index = 0; index < 20; index++) model.AdvanceOneSecond();
        Assert.Equal("准备期间更新", model.GreetingSubtitle);
        Assert.False(model.Start(30));
        Assert.Equal("准备期间更新", model.GreetingSubtitle);
        model.RequestEndCommand.Execute(null);
        model.ContinueFocusCommand.Execute(null);
        Assert.Equal("准备期间更新", model.GreetingSubtitle);
        ReturnHome(model);
        model.Start(30);
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        Assert.Equal("第二次进入", model.GreetingSubtitle);
    }

    [Theory]
    [InlineData(4, FocusGreetingState.LateNight, FocusGreetingState.Morning, "夜深了", "早上好")]
    [InlineData(11, FocusGreetingState.Morning, FocusGreetingState.Afternoon, "早上好", "下午好")]
    [InlineData(17, FocusGreetingState.Afternoon, FocusGreetingState.Evening, "下午好", "晚上好")]
    [InlineData(22, FocusGreetingState.Evening, FocusGreetingState.LateNight, "晚上好", "夜深了")]
    public void TimerUpdatesAndFloatingWindowChangesKeepTextStableAndBoundaryUpdatesOnce(
        int hour, FocusGreetingState initialState, FocusGreetingState nextState, string initialTitle, string nextTitle)
    {
        Write($"[{initialTitle}]\n初始一\n初始二\n[{nextTitle}]\n下一\n下二\n");
        var now = new DateTime(2026, 10, 11, hour, 59, 0);
        var provider = new FocusGreetingProvider(ConfigPath);
        var model = new FocusSessionViewModel(() => now, false, provider);
        model.Start(30);
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        var first = model.GreetingSubtitle;
        var changed = 0;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(model.GreetingSubtitle)) return;
            changed++;
            Assert.Contains(model.GreetingSubtitle, provider.GetGreetings(model.GreetingState));
            Assert.Equal(model.GreetingState == initialState ? initialTitle : nextTitle, model.GreetingTitle);
        };
        for (var index = 0; index < 20; index++) model.AdvanceOneSecond();
        using (var floating = new FocusFloatingWindowViewModel(model))
        {
            floating.SetCompletionPresentationHeight(24, 24);
            model.AdvanceOneSecond();
        }
        Assert.Equal(first, model.GreetingSubtitle);
        Assert.Equal(0, changed);

        // The current visit uses its existing snapshot, including on a stage boundary.
        Write($"[{nextTitle}]\n下次才读取\n");
        now = now.AddMinutes(1);
        for (var index = 0; index < 20; index++) model.AdvanceOneSecond();
        Assert.Equal(nextState, model.GreetingState);
        Assert.Contains(model.GreetingSubtitle, new[] { "下一", "下二" });
        Assert.Equal(1, changed);
    }

    [Fact]
    public void ReenteringAndFocusAgainAvoidConsecutiveDuplicateText()
    {
        Write("[下午好]\n甲\n乙\n");
        var model = new FocusSessionViewModel(() => new DateTime(2026, 10, 11, 14, 0, 0), false,
            new FocusGreetingProvider(ConfigPath));
        model.Start(30);
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        var first = model.GreetingSubtitle;
        ReturnHome(model);
        model.Start(30);
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        Assert.NotEqual(first, model.GreetingSubtitle);
        var second = model.GreetingSubtitle;
        for (var index = 0; index < 300; index++) model.AdvanceOneSecond();
        model.RequestEndCommand.Execute(null);
        model.ConfirmEndCommand.Execute(null);
        Assert.True(model.IsCompleted);
        model.FocusAgainCommand.Execute(null);
        model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        Assert.True(model.IsFocusing);
        Assert.NotEqual(second, model.GreetingSubtitle);
    }

    [Fact]
    public void AuthoritativeStateReplaysDoNotReloadAndCrossingStageChangesTextOnce()
    {
        Write("[晚上好]\n晚一\n晚二\n[夜深了]\n夜一\n夜二\n");
        var now = new DateTime(2026, 10, 11, 22, 59, 0);
        var utc = new DateTimeOffset(2026, 10, 11, 14, 59, 0, TimeSpan.Zero);
        var provider = new FocusGreetingProvider(ConfigPath);
        var model = new FocusSessionViewModel(() => now, false, provider);
        var session = new LocalFocusSessionDto(
            Guid.NewGuid(), LocalFocusSessionStatusDto.Focusing, true, 1800, 0,
            utc.AddSeconds(-5), utc, utc.AddMinutes(30), null, null, null, null,
            false, null, null, []);
        model.ApplyAuthoritativeSession(session, nowUtc: utc);
        var first = model.GreetingSubtitle;
        Write("[晚上好]\n下次的新晚安\n[夜深了]\n下次的新深夜\n");
        for (var index = 0; index < 10; index++) model.ApplyAuthoritativeSession(session, nowUtc: utc);
        Assert.Equal(first, model.GreetingSubtitle);
        now = now.AddMinutes(1);
        var changed = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.GreetingSubtitle)) changed++; };
        for (var index = 0; index < 10; index++) model.ApplyAuthoritativeSession(session, nowUtc: utc.AddMinutes(1));
        Assert.Equal(FocusGreetingState.LateNight, model.GreetingState);
        Assert.Contains(model.GreetingSubtitle, new[] { "夜一", "夜二" });
        Assert.Equal(1, changed);
    }

    private void Write(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, content, new UTF8Encoding(false));
    }

    private static void ReturnHome(FocusSessionViewModel model)
    {
        model.RequestEndCommand.Execute(null);
        model.EndConfirmedFocusCommand.Execute(null);
        Assert.Equal(FocusFlowStage.Idle, model.Stage);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
