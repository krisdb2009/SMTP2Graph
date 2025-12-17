using Microsoft.Graph;
using Azure.Identity;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Graph.Models;
using System.Text;

Console.WriteLine("Initializing...");
var mail_listener = new TcpListener(IPAddress.Any, 25);
var scopes = new[] { "https://graph.microsoft.com/.default" };
var options = new ClientSecretCredentialOptions
{
    AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
};
var clientSecretCredential = new ClientSecretCredential(
    Environment.GetEnvironmentVariable("TENANT_ID"),
    Environment.GetEnvironmentVariable("CLIENT_ID"),
    Environment.GetEnvironmentVariable("CLIENT_SECRET"),
    options
);

Console.WriteLine("Setting up MS Graph Service Client...");
var graphClient = new GraphServiceClient(clientSecretCredential, scopes);

Console.WriteLine("Starting SMTP listener loop...");
mail_listener.Start();
while (true)
{
    var client = mail_listener.AcceptTcpClient();
    Console.WriteLine("Accepting connection, attempting to process...");
    _ = Task.Run(() => {
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var sw = new StreamWriter(stream);
            var sr = new StreamReader(stream);
            sw.AutoFlush = true;
            sw.Write($"220 Who are you...?\r\n");
            HandleConnection(client, stream, sw, sr);
        }
        catch (Exception e)
        {
            if (client.Connected) client.Close();
            Console.WriteLine("An error occured in the SMTP listener loop:");
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
        }
    });
}

void HandleConnection(TcpClient client, Stream stream, StreamWriter sw, StreamReader sr)
{
    string From = string.Empty;
    List<string> To = new();
    string Body = string.Empty;
    string Subject = string.Empty;
    BodyType BType = BodyType.Text;
    while (true)
    {
        var msg = sr.ReadLine();
        if (msg.StartsWith("HELO") || msg.StartsWith("EHLO"))
        {
            sw.Write("250 Whatchu want?\r\n");
            continue;
        }
        if (msg.StartsWith("MAIL FROM:"))
        {
            From = Regex.Match(msg, "<(.*)>").Groups[1].Value;
            sw.Write("250 You again?!\r\n");
            continue;
        }
        if (msg.StartsWith("RCPT TO:"))
        {
            To.Add(Regex.Match(msg, "<(.*)>").Groups[1].Value);
            sw.Write("250 Ok fine...\r\n");
            continue;
        }
        if (msg.StartsWith("DATA"))
        {
            sw.Write("354 And whats so important that you have to bother me...?\r\n");
            (BType, Subject, Body) = ReadData(sr);
            sw.Write("250 Oh yea, that was *very* important... SMH.\r\n");
            continue;
        }
        if (msg.StartsWith("QUIT"))
        {
            sw.Write("221 Bye, don't talk to me...\r\n");
            break;
        }
    }
    client.Close();
    Console.WriteLine("Sending to MS Graph...");
    SendMessage(From, To, Subject, Body, BType);
    Console.WriteLine("Done.");
}

(BodyType, string, string) ReadData(StreamReader sr)
{
    string Subject = string.Empty;
    string Body;
    BodyType BType = BodyType.Text;
    bool Base64 = false;
    while (true)
    {
        var msg = sr.ReadLine();
        if (msg.StartsWith("Subject:"))
        {
            Subject = Regex.Match(msg, "Subject: (.*)").Groups[1].Value;
            continue;
        }
        if (msg.StartsWith("Content-Type:"))
        {
            if (msg.Contains("html")) BType = BodyType.Html;
            continue;
        }
        if (msg == "Content-Transfer-Encoding: base64")
        {
            Base64 = true;
            continue;
        }
        if (msg == string.Empty) 
        {
            Body = ReadBody(sr, Base64);
            break;
        }
    }
    return (BType, Subject, Body);
}

string ReadBody(StreamReader sr, bool Base64)
{
    string Body = string.Empty;
    while (true)
    {
        var msg = sr.ReadLine();
        if (msg == ".") break;
        if (msg == "..")
        {
            msg = ".";
        }
        Body += msg;
    }
    if (Base64)
    {
        Body = Encoding.UTF8.GetString(Convert.FromBase64String(Body));
    }
    return Body;
}

void SendMessage(string From, List<string> To, string Subject, string Body, BodyType BType)
{
    List<Recipient> recipients = new();
    foreach (string email in To)
    {
        recipients.Add(new() { EmailAddress = new() { Address = email } });
    }
    _ = graphClient.Users[From].SendMail.PostAsync(new()
    {
        Message = new()
        {
            From = new() { EmailAddress = new() { Address = From } },
            ToRecipients = recipients,
            Subject = Subject,
            Body = new() { ContentType = BType, Content = Body }
        },
        SaveToSentItems = false
    });
}