using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;
using System.Reflection;

namespace CA.Application.Commons.Services;

public abstract class EmailService(IConfiguration configuration)
{

    protected IConfiguration configuration = configuration;

    protected async Task SendEmailAsync(string to, string subject, string body)
    {
        var client = new SmtpClient(configuration["Email:Host"]!)
        {
            Port = int.Parse(configuration["Email:Port"]!),
            Credentials = new NetworkCredential(configuration["Email:Username"]!, configuration["Email:Password"]!),
            EnableSsl = true,
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(configuration["Email:From"]!),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };

        mailMessage.To.Add(to);

        await client.SendMailAsync(mailMessage);
    }

    protected static async Task<string> GetEmailBodyAsync(string templateName, Dictionary<string, string> placeholders)
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrEmpty(assemblyLocation))
        {
            throw new InvalidOperationException("Unable to determine the assembly location.");
        }

        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
        if (string.IsNullOrEmpty(assemblyDirectory))
        {
            throw new InvalidOperationException("Unable to determine the assembly directory.");
        }

        var templatePath = Path.Combine(assemblyDirectory, "Templates", templateName);

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Template file {templateName} not found.");
        }

        var body = await File.ReadAllTextAsync(templatePath);

        foreach (var placeholder in placeholders)
        {
            body = body.Replace("{{" + placeholder.Key + "}}", placeholder.Value);
        }

        return body;
    }
}
