using System.Net;
using System.Text.RegularExpressions;

namespace Moinho.Tests.Infrastructure;

public static partial class HttpClientExtensions
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    // GETs the page that holds the form (to get its antiforgery token + cookie), then POSTs the form.
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string formPage, Dictionary<string, string> fields, string? postUrl = null)
    {
        var page = await client.GetAsync(formPage);
        var html = await page.Content.ReadAsStringAsync();
        var token = AntiforgeryToken().Match(html);
        Assert.True(token.Success, $"Sem token antiforgery em {formPage} (status {(int)page.StatusCode})");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(postUrl ?? formPage, new FormUrlEncodedContent(fields));
    }

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password) =>
        client.PostFormAsync("/Identity/Account/Login", new() { ["Input.Email"] = email, ["Input.Password"] = password });

    public static async Task<HttpClient> LoggedInAsync(this HttpClient client, string email, string password = MoinhoFactory.Password)
    {
        var response = await client.LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
}
