using System.Net;
using System.Net.Mail;

namespace NorthspanPolicy;

public sealed record OutgoingEmail(string To, string Subject, string Body);

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(OutgoingEmail email, CancellationToken ct = default);
}

/// <summary>
/// Sends plain-text email through Zoho (or any SMTP server) on port 587 with STARTTLS.
/// Settings come from the server's .env: Smtp__Host, Smtp__Port, Smtp__User, Smtp__Password, Smtp__From.
/// With no host or password set, nothing is sent and sign-ups still work.
/// </summary>
public sealed class SmtpEmailSender(IConfiguration config) : IEmailSender
{
    private string? Host => config["Smtp:Host"];
    private string? User => config["Smtp:User"];
    private string? Password => config["Smtp:Password"];
    private int Port => config.GetValue("Smtp:Port", 587);
    private string From => config["Smtp:From"] ?? User ?? "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(User) &&
        !string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(From);

    public async Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        if (!IsConfigured) return;
        using var msg = new MailMessage(new MailAddress(From, "Northspan PolicyKit"), new MailAddress(email.To))
        {
            Subject = email.Subject,
            Body = email.Body,
            IsBodyHtml = false,
        };
        using var smtp = new SmtpClient(Host, Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(User, Password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 20_000,
        };
        await smtp.SendMailAsync(msg, ct);
    }
}

/// <summary>Emails sent when someone joins the launch list.</summary>
public sealed class SignupNotifier(IEmailSender sender, IConfiguration config, ILogger<SignupNotifier> log)
{
    internal static readonly IReadOnlyDictionary<string, string> Products = new Dictionary<string, string>
    {
        ["create"] = "the tailored AI policy",
        ["report"] = "the full gap report",
        ["fix"] = "the updated policy",
    };

    private string OwnerEmail => config["Notify:OwnerEmail"] ?? "info@northspanltd.com";
    private bool SendConfirmation => config.GetValue("Notify:SendConfirmation", false);

    /// <summary>Sends the notice in the background so the sign-up answers at once. Failures are logged, never shown to the visitor.</summary>
    public void QueueSignup(string email, string? organization, string interest, bool marketingOk)
    {
        if (!sender.IsConfigured)
        {
            log.LogInformation("Launch sign-up saved; email not configured, so no notice sent");
            return;
        }
        log.LogInformation("Launch sign-up saved; sending email notice");
        _ = Task.Run(async () =>
        {
            try
            {
                await sender.SendAsync(BuildOwnerNotice(OwnerEmail, email, organization, interest, marketingOk, DateTime.UtcNow));
                log.LogInformation("Sign-up email sent to the owner address");
                if (SendConfirmation)
                {
                    await sender.SendAsync(BuildConfirmation(email, interest));
                    log.LogInformation("Sign-up confirmation email sent to the visitor");
                }
            }
            catch (Exception ex)
            {
                // Never log the visitor's address or the SMTP password; the type is enough to diagnose.
                log.LogWarning("Sign-up email could not be sent: {Type}: {Message}", ex.GetType().Name, ex.Message);
            }
        });
    }

    internal static OutgoingEmail BuildOwnerNotice(string ownerEmail, string email, string? organization, string interest,
        bool marketingOk, DateTime utc)
    {
        var product = Products.GetValueOrDefault(interest, interest);
        var body =
            $"""
            New PolicyKit launch sign-up

            Email:            {email}
            Organization:     {(string.IsNullOrWhiteSpace(organization) ? "(not given)" : organization.Trim())}
            Interested in:    {product}
            Marketing emails: {(marketingOk ? "Yes, agreed" : "No")}
            Time (UTC):       {utc:yyyy-MM-dd HH:mm}

            Sent automatically by policy.northspanltd.com.
            """;
        return new OutgoingEmail(ownerEmail, $"New PolicyKit sign-up: {product}", body);
    }

    internal static OutgoingEmail BuildConfirmation(string email, string interest)
    {
        var product = Products.GetValueOrDefault(interest, "PolicyKit");
        var body =
            $"""
            Thank you for your interest in PolicyKit.

            You're on the list for {product}. We'll email you on the day it becomes available to buy.

            In the meantime, the free AI policy check is open at https://policy.northspanltd.com/#check

            Northspan Ltd
            info@northspanltd.com

            You received this email because you asked to be notified at policy.northspanltd.com.
            To be removed from the list, reply to this email.
            """;
        return new OutgoingEmail(email, "You're on the PolicyKit launch list", body);
    }
}
