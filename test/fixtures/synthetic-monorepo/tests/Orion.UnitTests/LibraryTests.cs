using System;
using Orion.Auth;
using Orion.Core.Data;
using Orion.Storage;
using Xunit;

namespace Orion.UnitTests;

public class ParserTests
{
    [Fact]
    public void Parse_ValidInput_ReturnsFoo()
    {
        var foo = Parser.Parse("42:widget");
        Assert.Equal(42, foo.Id);
        Assert.Equal("widget", foo.Name);
        Assert.True(foo.IsValid);
    }

    [Theory]
    [InlineData("1:a")]
    [InlineData("2:b")]
    [InlineData("3:c")]
    public void Parse_ManyRows_AllValid(string input)
    {
        var foo = Parser.Parse(input);
        Assert.True(foo.IsValid);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        Assert.False(Parser.TryParse("not-a-foo", out _));
    }

    [Fact]
    public void Parse_EmptyInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => Parser.Parse(""));
    }
}

public class NestedAuthTests
{
    public class Inner
    {
        [Fact]
        public void ValidateCredentials_AcceptsStrongPair()
        {
            Assert.True(AuthService.ValidateCredentials("alice", "supersecret"));
        }
    }

    [Fact]
    public void IssueToken_ReturnsTokenWithUsernameLength()
    {
        var token = AuthService.IssueToken("alice");
        Assert.StartsWith("tok-alice-", token);
    }
}

public class RepositoryTests
{
    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var repo = new Repository();
        repo.Save("k", "v");
        Assert.Equal("v", repo.Load("k"));
        Assert.Equal(1, repo.Count);
    }

    [Fact]
    public void Load_MissingKey_ReturnsNull()
    {
        var repo = new Repository();
        Assert.Null(repo.Load("nope"));
    }
}
