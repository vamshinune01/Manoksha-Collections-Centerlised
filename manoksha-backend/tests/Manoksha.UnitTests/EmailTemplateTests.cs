using Manoksha.Modules.Notifications.Application;

namespace Manoksha.UnitTests;

public class EmailTemplateTests
{
    [Fact]
    public void Dynamic_values_are_html_encoded_and_amounts_use_indian_grouping()
    {
        var email = EmailTemplates.Build("Manoksha", "Order <1>", "Hi <script>", [new Para("A & B"), new Items([("Saree \"silk\"", 2, 123456.5m)])], "Open", "https://x.test/?a=1&b=2");

        email.Html.Should().NotContain("<script>").And.Contain("Hi &lt;script&gt;").And.Contain("A &amp; B").And.Contain("https://x.test/?a=1&amp;b=2");
        email.Html.Should().Contain("₹1,23,456.50");
        email.Text.Should().Contain("Hi <script>").And.Contain("Open: https://x.test/?a=1&b=2");
    }

    [Theory]
    [InlineData(0, "₹0.00")]
    [InlineData(999.5, "₹999.50")]
    [InlineData(1000, "₹1,000.00")]
    [InlineData(123456.5, "₹1,23,456.50")]
    [InlineData(12345678.9, "₹1,23,45,678.90")]
    [InlineData(-2500, "-₹2,500.00")]
    public void Rupees_use_indian_digit_grouping(decimal amount, string expected) => EmailTemplates.Rs(amount).Should().Be(expected);
}
