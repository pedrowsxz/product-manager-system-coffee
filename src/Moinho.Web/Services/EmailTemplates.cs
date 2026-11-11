using System.Net;

namespace Moinho.Web.Services;

public record EmailContent(string Subject, string Html);

// Small pt-BR HTML emails. Every dynamic value is HTML-encoded.
public static class EmailTemplates
{
    public static EmailContent Confirmation(string name, string link) => new(
        "Confirme seu cadastro no Moinho",
        Layout($"Olá, {name}!",
            "Recebemos seu cadastro no back-office do Moinho. Confirme seu email para poder entrar:",
            "Confirmar email", link,
            "O link vale por 24 horas. Se você não se cadastrou, ignore este email."));

    public static EmailContent EmailChange(string name, string link) => new(
        "Confirme seu novo email no Moinho",
        Layout($"Olá, {name}!",
            "Este endereço foi informado como o novo email da sua conta no Moinho. Confirme para concluir a troca:",
            "Confirmar novo email", link,
            "O link vale por 24 horas. Se você não pediu essa troca, ignore este email."));

    public static EmailContent PasswordReset(string name, string link) => new(
        "Redefinição de senha do Moinho",
        Layout($"Olá, {name}!",
            "Recebemos um pedido para redefinir a senha da sua conta. Escolha uma nova senha:",
            "Redefinir senha", link,
            "O link vale por 2 horas. Se você não pediu, ignore este email: sua senha continua a mesma."));

    public static EmailContent Invitation(string name, string link) => new(
        "Você foi convidado para o Moinho",
        Layout($"Olá, {name}!",
            "Um administrador criou sua conta no back-office do Moinho. Para começar, defina sua senha:",
            "Definir minha senha", link,
            "O link vale por 2 horas. Se expirar, use \"Esqueci minha senha\" na tela de login com este email."));

    private static string Layout(string greeting, string intro, string buttonText, string link, string footnote)
    {
        var e = (string s) => WebUtility.HtmlEncode(s);
        return $$"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head><meta charset="utf-8"><style>a.btn { background:#a0522d; color:#ffffff; padding:12px 20px; border-radius:6px; text-decoration:none; display:inline-block; }</style></head>
            <body style="margin:0; background:#f6f3f0; font-family:Segoe UI, Arial, sans-serif; color:#2a1e17;">
            <div style="max-width:520px; margin:24px auto; background:#ffffff; border-radius:10px; padding:28px;">
            <div style="font-weight:700; font-size:20px; color:#a0522d; margin-bottom:16px;">Moinho</div>
            <h1 style="font-size:18px; margin:0 0 12px;">{{e(greeting)}}</h1>
            <p>{{e(intro)}}</p>
            <p style="margin:24px 0;"><a class="btn" href="{{e(link)}}">{{e(buttonText)}}</a></p>
            <p style="font-size:13px; color:#6c5a4e; word-break:break-all;">Se o botão não funcionar, copie este endereço no navegador:<br>{{e(link)}}</p>
            <p style="font-size:13px; color:#6c5a4e;">{{e(footnote)}}</p>
            </div>
            </body>
            </html>
            """;
    }
}
