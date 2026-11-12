using NorthspanPolicy;

namespace NorthspanPolicy.Tests;

public class NotifierTests
{
    [Fact]
    public void OwnerNotice_ListsTheSignUpDetails()
    {
        var mail = SignupNotifier.BuildOwnerNotice("owner@example.com", "jane@example.com", "Demo Bank", "fix", true,
            new DateTime(2026, 11, 12, 15, 30, 0, DateTimeKind.Utc));

        Assert.Equal("owner@example.com", mail.To);
        Assert.Equal("New PolicyKit sign-up: the updated policy", mail.Subject);
        Assert.Contains("jane@example.com", mail.Body);
        Assert.Contains("Demo Bank", mail.Body);
        Assert.Contains("Yes, agreed", mail.Body);
        Assert.Contains("2026-11-12 15:30", mail.Body);
    }

    [Fact]
    public void OwnerNotice_HandlesMissingOrganization()
    {
        var mail = SignupNotifier.BuildOwnerNotice("o@example.com", "j@example.com", null, "create", false, DateTime.UtcNow);
        Assert.Contains("(not given)", mail.Body);
        Assert.Contains("Marketing emails: No", mail.Body);
    }

    [Fact]
    public void Confirmation_GoesToTheVisitor()
    {
        var mail = SignupNotifier.BuildConfirmation("jane@example.com", "report");
        Assert.Equal("jane@example.com", mail.To);
        Assert.Contains("the full gap report", mail.Body);
    }
}
