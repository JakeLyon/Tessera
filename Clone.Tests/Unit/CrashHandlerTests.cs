using Clone.UI;
using Xunit;

namespace Clone.Tests.Unit;

public class CrashHandlerTests
{
    [Fact]
    public void Describe_PlainException_NamesTypeAndMessage()
    {
        string text = CrashHandler.Describe(new InvalidOperationException("the tree was replaced"));

        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("the tree was replaced", text);
    }

    [Fact]
    public void Describe_NestedException_AddsTheInnermostCause()
    {
        var ex = new InvalidOperationException("outer",
            new IOException("middle", new UnauthorizedAccessException("the real reason")));

        string text = CrashHandler.Describe(ex);

        Assert.Contains("outer", text);
        Assert.Contains("UnauthorizedAccessException", text);
        Assert.Contains("the real reason", text);
        // The intermediate layer is noise — only the outer and the root cause are shown.
        Assert.DoesNotContain("middle", text);
    }

    [Fact]
    public void Describe_SameMessageInnerException_DoesNotRepeatItself()
    {
        var ex = new InvalidOperationException("identical", new IOException("identical"));

        string text = CrashHandler.Describe(ex);

        Assert.DoesNotContain("Caused by", text);
    }

    [Fact]
    public void Describe_SingleWrappedAggregate_UnwrapsToTheRealException()
    {
        // Task.WaitAll's message is "One or more errors occurred." — useless on its own.
        var ex = new AggregateException(new UnauthorizedAccessException("access to D:\\ denied"));

        string text = CrashHandler.Describe(ex);

        Assert.Contains("UnauthorizedAccessException", text);
        Assert.Contains("access to D:\\ denied", text);
        Assert.DoesNotContain("One or more errors", text);
    }

    [Fact]
    public void Describe_MultiErrorAggregate_ListsEachOne()
    {
        var ex = new AggregateException(
            new IOException("drive vanished"),
            new UnauthorizedAccessException("denied"));

        string text = CrashHandler.Describe(ex);

        Assert.Contains("2 errors occurred", text);
        Assert.Contains("drive vanished", text);
        Assert.Contains("denied", text);
    }

    [Fact]
    public void Describe_NestedAggregate_IsFlattened()
    {
        var ex = new AggregateException(new AggregateException(new IOException("innermost")));

        string text = CrashHandler.Describe(ex);

        Assert.Contains("IOException", text);
        Assert.Contains("innermost", text);
        Assert.DoesNotContain("AggregateException", text);
    }
}
