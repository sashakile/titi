using Xunit;

namespace Orion.UnitTests;

public class BasicTests
{
    [Fact]
    public void Test1() => Assert.True(true);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ParameterizedTest(int data) => Assert.True(data > 0);

    [Theory]
    [InlineData("hello")]
    [InlineData("world")]
    public void StringParameterized(string s) => Assert.NotEmpty(s);
}

public class NestedTests
{
    public class InnerTests
    {
        [Fact]
        public void NestedTest() => Assert.True(true);
    }
}

// Generic *method* on a concrete class (not an open generic class, which
// xUnit cannot instantiate). Mirrors the spec's test-detection scenario for
// generic test methods (CreateInstance<Foo> in FactoryTests).
public class FactoryTests
{
    [Fact]
    public void CreateInstance_Foo() => Assert.NotNull(new Foo());
}

public class Foo { }
