using Astryx.SharedKernel.Results;

namespace Astryx.UnitTests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Success_carries_value()
    {
        Result<int> result = 76;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(76);
        result.Match(v => $"ok {v}", e => e.Code).ShouldBe("ok 76");
    }

    [Fact]
    public void Failure_carries_error_and_has_no_value()
    {
        Result<int> result = Error.Conflict("quote_stale", "Prices moved.");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("quote_stale");
        Should.Throw<InvalidOperationException>(() => result.Value);
        result.Match(v => "ok", e => e.Code).ShouldBe("quote_stale");
    }

    [Fact]
    public void Non_generic_result()
    {
        Result.Success().IsSuccess.ShouldBeTrue();
        Result failed = Error.NotFound();
        failed.Error!.Code.ShouldBe("not_found");
    }
}
