using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_ReportsSuccessAndCarriesValue()
    {
        var result = Result<int, string>.Success(42);

        result.IsSuccess.Must().BeTrue();
        result.IsFailure.Must().BeFalse();
        result.Value.Must().Be(42);
    }

    [Fact]
    public void Failure_ReportsFailureAndCarriesError()
    {
        var result = Result<int, string>.Failure("boom");

        result.IsFailure.Must().BeTrue();
        result.IsSuccess.Must().BeFalse();
        result.Error.Must().Be("boom");
    }

    [Fact]
    public void Value_OnFailure_Throws()
    {
        var result = Result<int, string>.Failure("boom");

        Xunit.Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Error_OnSuccess_Throws()
    {
        var result = Result<int, string>.Success(1);

        Xunit.Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Match_RoutesBranches()
    {
        Result<int, string>.Success(3).Match(v => v * 2, _ => -1).Must().Be(6);
        Result<int, string>.Failure("x").Match(v => v, _ => -1).Must().Be(-1);
    }

    [Fact]
    public void Map_TransformsValueOnly()
    {
        Result<int, string>.Success(3).Map(v => v + 1).Value.Must().Be(4);
        Result<int, string>.Failure("x").Map(v => v + 1).IsFailure.Must().BeTrue();
    }

    [Fact]
    public void MapError_TransformsErrorOnly()
    {
        Result<int, string>.Failure("x").MapError(e => e.Length).Error.Must().Be(1);
        Result<int, string>.Success(3).MapError(e => e.Length).Value.Must().Be(3);
    }

    [Fact]
    public void TryGetValue_ReturnsDefaultOnFailure()
    {
        Result<string, int>.Failure(1).TryGetValue(out var value).Must().BeFalse();
        Xunit.Assert.Null(value);
    }

    [Fact]
    public void TryGetError_ReturnsDefaultOnSuccess()
    {
        Result<int, string>.Success(1).TryGetError(out var error).Must().BeFalse();
        Xunit.Assert.Null(error);
    }

    [Fact]
    public void FactoryHelpers_BuildCorrectStates()
    {
        Result.Success<int, string>(5).IsSuccess.Must().BeTrue();
        Result.Failure<int, string>("e").IsFailure.Must().BeTrue();
    }

    [Fact]
    public void Success_OfReferenceType_ReturnsViaTryGetValue()
    {
        var result = Result<string, int>.Success("value");

        result.TryGetValue(out var value).Must().BeTrue();
        value.Must().Be("value");
    }
}
