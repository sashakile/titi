using NUnit.Framework;

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
}
