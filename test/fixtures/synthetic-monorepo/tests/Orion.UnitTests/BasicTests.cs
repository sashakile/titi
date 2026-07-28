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

public class GenericTestClass<T>
    where T : new()
{
    [Fact]
    public void GenericTest() => Assert.NotNull(new T());
}
