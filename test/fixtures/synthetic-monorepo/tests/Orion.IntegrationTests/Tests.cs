using NUnit.Framework;
using Orion.Auth;
using Orion.Core.Data;

namespace Orion.IntegrationTests;

public class Tests
{
    [SetUp]
    public void Setup() { }

    [Test]
    public void Test1() => Assert.Pass();

    [TestCase(1)]
    [TestCase(2)]
    public void ParameterizedTest(int data) => Assert.That(data, Is.GreaterThan(0));

    [Test]
    public void Parse_ProducesValidFoo()
    {
        var foo = Parser.Parse("7:gadget");
        Assert.Multiple(() =>
        {
            Assert.That(foo.Id, Is.EqualTo(7));
            Assert.That(foo.Name, Is.EqualTo("gadget"));
            Assert.That(foo.IsValid, Is.True);
        });
    }

    [Test]
    public void ValidateCredentials_RejectsShortPassword()
    {
        Assert.That(AuthService.ValidateCredentials("bob", "x"), Is.False);
    }

    [TestCase("alice", "supersecret", true)]
    [TestCase("al", "supersecret", false)]  // username too short
    public void ValidateCredentials_Cases(string user, string pass, bool expected)
    {
        Assert.That(AuthService.ValidateCredentials(user, pass), Is.EqualTo(expected));
    }
}
