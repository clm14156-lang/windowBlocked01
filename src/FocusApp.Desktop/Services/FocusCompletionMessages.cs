namespace FocusApp.Desktop.Services;

internal static class FocusCompletionMessages
{
    private static readonly string[] Messages =
    [
        "你走过的每一步，都算数。",
        "微小的进步，也值得为自己喝彩。",
        "时间没有白白流逝，你又留下了新的足迹。",
        "愿这份小小的成就感，陪你走向下一程。",
        "一点点积累，就是改变开始的地方。",
        "每一个踏实的当下，都在积累未来的底气。",
        "一步一步来，远方终会变得清晰。",
        "时间见证着你，慢慢成为更好的自己。",
        "你刚刚完成的，是值得珍惜的一小步。",
        "你认真付出的时光，正在成为你的底气。"
    ];

    public static string Select(string previous)
    {
        var previousIndex = Array.IndexOf(Messages, previous);
        var index = Random.Shared.Next(Messages.Length - (previousIndex >= 0 ? 1 : 0));
        return Messages[previousIndex >= 0 && index >= previousIndex ? index + 1 : index];
    }
}
