using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public sealed class CancellationTokenScopeTests
{
    [Fact]
    public void Token_IsInitiallyNotCancelled()
    {
        using var scope = new CancellationTokenScope();

        scope.Token.IsCancellationRequested.Must().BeFalse();
        scope.Token.CanBeCanceled.Must().BeTrue();
    }

    [Fact]
    public void Renew_CancelsPreviousToken()
    {
        using var scope = new CancellationTokenScope();
        var first = scope.Token;

        var second = scope.Renew();

        first.IsCancellationRequested.Must().BeTrue();
        second.IsCancellationRequested.Must().BeFalse();
    }

    [Fact]
    public void Renew_ReturnsDistinctToken()
    {
        using var scope = new CancellationTokenScope();
        var first = scope.Token;

        var second = scope.Renew();

        second.Equals(first).Must().BeFalse();
    }

    [Fact]
    public void Cancel_CancelsCurrentToken()
    {
        using var scope = new CancellationTokenScope();
        var token = scope.Token;

        scope.Cancel();

        token.IsCancellationRequested.Must().BeTrue();
    }

    [Fact]
    public void RenewAfterCancel_ReturnsActiveToken()
    {
        using var scope = new CancellationTokenScope();
        scope.Cancel();

        var token = scope.Renew();

        token.IsCancellationRequested.Must().BeFalse();
    }

    [Fact]
    public void Dispose_CancelsCurrentToken()
    {
        var scope = new CancellationTokenScope();
        var token = scope.Token;

        scope.Dispose();

        token.IsCancellationRequested.Must().BeTrue();
    }

    [Fact]
    public void Dispose_PreventsRenew()
    {
        var scope = new CancellationTokenScope();
        scope.Dispose();

        Xunit.Assert.Throws<ObjectDisposedException>(() => scope.Renew());
    }

    [Fact]
    public void Dispose_PreventsTokenAccess()
    {
        var scope = new CancellationTokenScope();
        scope.Dispose();

        Xunit.Assert.Throws<ObjectDisposedException>(() => scope.Token);
    }

    [Fact]
    public void Renew_OnlyCancelsLatestGeneration()
    {
        using var scope = new CancellationTokenScope();
        var first = scope.Token;
        var second = scope.Renew();

        first.IsCancellationRequested.Must().BeTrue();
        second.IsCancellationRequested.Must().BeFalse();
    }
}
