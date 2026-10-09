using FocusApp.Core;
using Xunit;

namespace FocusApp.Tests.Core;

public sealed class TaskCompletionPolicyTests
{
    [Theory]
    [InlineData(false, new bool[] { }, false)]
    [InlineData(true, new bool[] { }, true)]
    [InlineData(false, new[] { true, true, true }, false)]
    [InlineData(true, new[] { true, true, true }, true)]
    [InlineData(true, new[] { false, false, false }, false)]
    [InlineData(true, new[] { true, false, true }, false)]
    public void CompletionRequiresTheParentAndEveryChild(bool parent, bool[] children, bool expected)
    {
        Assert.Equal(expected, TaskCompletionPolicy.IsCompleted(parent, children));
    }
}
