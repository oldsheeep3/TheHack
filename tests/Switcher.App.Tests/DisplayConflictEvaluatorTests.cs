using Switcher.App.Display;

namespace Switcher.App.Tests;

public sealed class DisplayConflictEvaluatorTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    public void ConflictsWithOperator_ComparesIndices(int operatorIndex, int targetId, bool expected) =>
        Assert.Equal(expected, DisplayConflictEvaluator.ConflictsWithOperator(operatorIndex, targetId));

    [Fact]
    public void ConflictsWithOperator_NullTarget_NeverConflicts() =>
        Assert.False(DisplayConflictEvaluator.ConflictsWithOperator(0, null));

    [Fact]
    public void BuildWarningMessage_MentionsDisplayAndContinue()
    {
        var message = DisplayConflictEvaluator.BuildWarningMessage(2);

        Assert.Contains("Display 2", message);
        Assert.Contains("Continue", message);
    }
}
