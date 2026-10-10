using Lajur.Application.Finance;

namespace Lajur.UnitTests;

public sealed class MasterInputTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Foo*Bar")]
    [InlineData("Foo?Bar")]
    [InlineData("Foo~Bar")]
    [InlineData("Foo\nBar")]
    public void Names_that_break_master_or_excel_criteria_are_rejected(string value) => Assert.Throws<ArgumentException>(() => MasterInput.Name(value));
    [Fact]
    public void Names_trim_without_changing_case_or_internal_spacing()
    {
        Assert.Equal("Belanja Baru", MasterInput.Name(" Belanja Baru "));
        Assert.Equal("cash", MasterInput.DefaultIcon("Cash")); Assert.Equal("qr", MasterInput.DefaultIcon("QRIS"));
        Assert.Equal("shopping", MasterInput.DefaultIcon("Shopee Pay"));
    }
}
