using Lajur.Application.Finance;

namespace Lajur.UnitTests;

public sealed class SettingsInputTests
{
    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1e5")]
    [InlineData(" 10")]
    [InlineData("9223372036854775808")]
    public void Invalid_integer_text_is_rejected(string text)
        => Assert.Throws<ArgumentException>(() => SettingsInput.Amount(text));

    [Fact]
    public void Empty_zero_and_large_integer_remain_distinct_and_exact()
    {
        Assert.Null(SettingsInput.Amount(null));
        Assert.Equal(0, SettingsInput.Amount("0"));
        Assert.Equal(9007199254740993, SettingsInput.Amount("9007199254740993"));
    }
}
