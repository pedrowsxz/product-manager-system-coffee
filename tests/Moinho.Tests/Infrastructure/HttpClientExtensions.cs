using System.Net;
using System.Text.RegularExpressions;

namespace Moinho.Tests.Infrastructure;

public static partial class HttpClientExtensions
{
    [GeneratedRegex("<input[^>]*type=\"hidden\"[^>]*>")]
    private static partial Regex HiddenInput();

    [GeneratedRegex("name=\"([^\"]+)\"")]
    private static partial Regex NameAttribute();

    [GeneratedRegex("value=\"([^\"]*)\"")]
    private static partial Regex ValueAttribute();

    // Behaves like a browser: GETs the page holding the form, keeps its hidden fields (antiforgery
    // token, reset code, ...), adds the given fields and POSTs. Only the first form's fields are used
    // when a name repeats.
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string formPage, Dictionary<string, string> fields, string? postUrl = null)
    {
        var page = await client.GetAsync(formPage);
        var html = await page.Content.ReadAsStringAsync();

        var form = new Dictionary<string, string>();
        foreach (Match input in HiddenInput().Matches(html))
        {
            var name = NameAttribute().Match(input.Value);
            if (name.Success && !form.ContainsKey(name.Groups[1].Value))
            {
                form[name.Groups[1].Value] = WebUtility.HtmlDecode(ValueAttribute().Match(input.Value).Groups[1].Value);
            }
        }
        Assert.True(form.ContainsKey("__RequestVerificationToken"), $"Sem token antiforgery em {formPage} (status {(int)page.StatusCode})");

        foreach (var (key, value) in fields)
        {
            form[key] = value;
        }
        return await client.PostAsync(postUrl ?? formPage, new FormUrlEncodedContent(form));
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
