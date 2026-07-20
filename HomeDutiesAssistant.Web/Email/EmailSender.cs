using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace HomeDutiesAssistant.Web.Email;

public sealed class EmailSender(IOptions<SmtpOptions> options)
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(string toEmail, string subject, string body)
    {
        using var message = new MailMessage(_options.From, toEmail, subject, body);
        using var client = new SmtpClient(_options.Host, _options.Port);
        client.EnableSsl = true;
        client.Credentials = string.IsNullOrEmpty(_options.User)
            ? CredentialCache.DefaultNetworkCredentials
            : new NetworkCredential(_options.User, _options.Password);
        await client.SendMailAsync(message);
    }
}