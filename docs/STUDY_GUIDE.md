# Guia de estudo

O objetivo é que você consiga explicar cada linha numa entrevista. Leia o código na ordem abaixo, com este guia ao lado.

## 1. Ordem de leitura

1. **`src/Moinho.Web/Program.cs`**: o mapa do app inteiro. Leia de cima para baixo: serviços (DI) primeiro, pipeline de middlewares depois. A ordem dos middlewares importa (veja a seção 6).
2. **`Models/`**: `ApplicationUser`, `Product`, `ErrorLog`, `Roles`, `Policies`, `Options`.
3. **`Data/AppDbContext.cs`** → **`Data/DbInitializer.cs`** → **`Data/Seeder.cs`**: como o banco nasce (`EnsureCreated`) e é populado.
4. **`Controllers/ProductsController.cs`** + **`Services/ProductService.cs`** + **`ViewModels/ProductViewModels.cs`**: o CRUD, o preço pt-BR e o SKU único.
5. **`Areas/Identity/Pages/Account/`**: Register, ConfirmEmail, Login, ForgotPassword, ResetPassword e Manage/*. Depois **`Services/AccountEmails.cs`**, **`SmtpEmailSender.cs`** e **`EmailTemplates.cs`**.
6. **`Areas/Admin/Controllers/AdminControllerBase.cs`** → **`StaffController.cs`** → **`Services/StaffService.cs`** (o coração das regras).
7. **`Controllers/ErrorController.cs`** → **`Data/ErrorLogSink.cs`** → **`Areas/Admin/Controllers/LogsController.cs`**.
8. **`Controllers/DemoController.cs`**, **`Data/DemoReset.cs`** e **`Services/DemoResetService.cs`**.
9. **`tests/Moinho.Tests/`**: comece por `Infrastructure/` (como o app sobe nos testes) e depois `StaffRulesTests.cs`.

## 2. Cookie, perfis, claims e security stamp

- **Login** (`SignInManager.PasswordSignInAsync`): confere a senha (hash PBKDF2 no banco), monta um `ClaimsPrincipal` com claims (id, email, **perfis** e o **security stamp**) e grava tudo num **cookie criptografado** (`.AspNetCore.Identity.Application`).
- **A cada requisição**, o middleware de autenticação descriptografa o cookie e preenche `HttpContext.User`. Por padrão, os perfis vêm **do cookie**, sem consultar o banco.
- **Autorização:** `[Authorize(Policy = "RequireAdmin")]` olha `User.IsInRole("Admin")`, ou seja, a claim de perfil dentro do cookie.
- **O problema:** se um admin rebaixa alguém, o cookie dessa pessoa ainda diz "Admin".
- **Security stamp:** um valor aleatório guardado no usuário (`AspNetUsers.SecurityStamp`) e copiado no cookie. O `SecurityStampValidator` compara os dois. Se forem diferentes, o cookie é rejeitado e a pessoa é deslogada. Por padrão essa conferência acontece a cada **30 minutos**; aqui é a **cada requisição** (`ValidationInterval = TimeSpan.Zero`, em `Program.cs`). Na prática, a cada requisição o validador carrega o usuário do banco, confere o stamp, **remonta o `ClaimsPrincipal` com os perfis atuais** e reemite o cookie. É mais trabalho por requisição (algumas consultas e um novo `Set-Cookie`), aceitável num back-office pequeno.
- **Quem muda o stamp:** `UpdateSecurityStampAsync` (chamado pelo `StaffService` em toda alteração), `ChangePasswordAsync`, `ResetPasswordAsync` e `SetEmailAsync`.
- **Por que `RefreshSignInAsync` no ChangePassword?** Trocar a senha muda o stamp, o que derrubaria a própria sessão. O refresh emite um cookie novo, com o stamp novo, só para esse navegador.

## 3. Tokens de confirmação e redefinição (Data Protection)

- `GenerateEmailConfirmationTokenAsync` e `GeneratePasswordResetTokenAsync` não gravam nada no banco. O token é um pacote **criptografado e assinado** pelo **Data Protection** com: id do usuário, finalidade ("ResetPassword"), data de criação e security stamp.
- Ao receber o token de volta, o Identity descriptografa e confere a finalidade, a validade (2 h para redefinição, 24 h para confirmação: dois provedores, veja `EmailConfirmationTokenProvider.cs`) e se o **stamp ainda é o mesmo**. Por isso o link de redefinição só funciona uma vez: redefinir muda o stamp.
- **Por que as chaves precisam persistir:** as chaves do Data Protection criptografam o cookie de login, o token antiforgery e esses tokens de email. Se o container for recriado sem guardar as chaves (volume `keys`, `DataProtection:KeysPath`), todo mundo é deslogado, os formulários abertos falham e **todos os links já enviados por email deixam de funcionar**.
- O token vai na URL codificado em Base64Url (`WebEncoders.Base64UrlEncode`), porque o token cru tem `+` e `/`.

## 4. O caminho de uma exceção não tratada até a tela de Logs

1. Um controller lança uma exceção (ex.: `LogsController.SimulateError`).
2. O `ExceptionHandlerMiddleware` (`app.UseExceptionHandler("/Erro")`) captura, limpa a resposta e **re-executa** a requisição no caminho `/Erro`, dentro da **mesma** requisição (mesmo `Activity`, mesmo TraceId).
3. `ErrorController.Error` lê a exceção em `IExceptionHandlerPathFeature`, registra `LogError(ex, "Erro não tratado em {Path} (usuário {UserId})")` e devolve status 500.
4. O Serilog recebe o evento. Vai para o console e, por ser **Error** vindo de `Moinho.*`, para o sub-logger do banco (`ErrorLogSink`). O framework também registra a mesma exceção (ExceptionHandlerMiddleware), mas esse evento não é do namespace `Moinho`, então fica só no console: **uma linha por falha**.
5. O sink do SQL Server junta eventos e grava em **lote** (a cada ~5 s) na tabela `ErrorLogs`: mensagem, exceção, nível, data em UTC, JSON com as propriedades e o **TraceId** (`Activity.Current.TraceId`).
6. A página `/Erro` mostra o mesmo TraceId. Em Admin → Logs, o filtro por TraceId encontra exatamente a linha.
7. Ao desligar, o host descarta o logger, o que força a gravação do último lote (`Log.CloseAndFlushAsync()` cuida do logger de inicialização).

O que **não** entra: 404, 403 e 429 (são respostas esperadas, tratadas por `UseStatusCodePagesWithReExecute`) e qualquer coisa abaixo de Error.

## 5. A armadilha do decimal em pt-BR

- O app usa a cultura **pt-BR** (`UseRequestLocalization`), então o model binding lê números do jeito brasileiro: **vírgula = decimal, ponto = milhar**.
- `"12,50"` → 12,50 ✔. Mas `"12.50"` → o ponto é ignorado como separador de milhar → **1250**, sem erro nenhum.
- `<input type="number">` envia `12.50` (com ponto), qualquer que seja o idioma do navegador. E a regra `number` do jquery-validation rejeita vírgula.
- **Solução aqui:** o preço é um `string` no view model, com `[RegularExpression]` que só aceita o formato brasileiro (`12`, `12,5`, `12,50`, `1.234,56`). O mesmo atributo vira regra do jquery-validation no navegador (unobtrusive), sem JavaScript extra. O servidor converte com `decimal.Parse(..., pt-BR)` só depois de validar. `"12.50"` recebe "Use o formato brasileiro, com vírgula nos centavos".
- `ParsePrice()` é **método**, não propriedade: a validação do MVC lê todas as propriedades, e uma propriedade que converte quebraria com `"abc"`.

## 6. Ordem do pipeline (`Program.cs`)

`UseForwardedHeaders` → `UseExceptionHandler` → `UseStatusCodePagesWithReExecute` → `UseHsts` → `UseSerilogRequestLogging` → `UseRouting` → `UseRequestLocalization` → `UseRateLimiter` → `UseAuthentication` → `UseAuthorization` → endpoints.

- Forwarded headers primeiro: tudo depois (links nos emails, IP do rate limit, logs) precisa ver o cliente real, e não o Caddy.
- Exception handler logo no início, para capturar erros de tudo o que vem depois.
- Rate limiter depois do roteamento, porque precisa saber qual endpoint (e qual política) foi escolhido.
- Autenticação antes de autorização: primeiro "quem é", depois "pode?".

## 7. Lógica central, linha a linha

### (a) `StaffService`: regras da equipe

**`InviteAsync`**
1. Valida o perfil contra `Roles.All` (o formulário pode ser adulterado).
2. Se já existe conta com o email, devolve erro **no campo** Email. Aqui revelar que existe não tem problema: só admin vê.
3. Cria o `ApplicationUser` com `EmailConfirmed = true` (o admin garante o endereço) e chama `CreateAsync(user)` **sem senha**.
4. Adiciona ao perfil. `Check(...)` lança exceção se algo que "não pode falhar" falhar.
5. `SendInvitationAsync` gera um **token de redefinição de senha** e manda o link para `ResetPassword?invite=true`. Se o email falhar, a conta existe mesmo assim e o admin vê o aviso.

**`UpdateAsync`**
1. Valida o perfil.
2. Abre uma **transação serializável**. Sem isso, dois admins poderiam, ao mesmo tempo, rebaixar um ao outro: cada um veria "existe outro admin ativo" e os dois seriam rebaixados. Com `Serializable`, a leitura da contagem e a escrita formam uma unidade.
3. Carrega o usuário e calcula: `isSelf`, `demoting` (era Admin e deixa de ser), `deactivating` (fica inativo e ainda não estava), `emailChanged`.
4. **Regra 1:** ninguém rebaixa nem desativa a si mesmo.
   - Também não troca o próprio email por aqui (a conta ficaria sem confirmação e a sessão cairia na hora); para isso existe Minha conta → Email.
5. **Regra 2:** contas protegidas (`IsProtectedDemo` ou o admin real de `Seed:AdminEmail`) não mudam de **perfil** (nem para cima: senão o botão "Equipe (demo)" viraria Admin), de **email** (quem controla o email consegue redefinir a senha) nem de **situação**.
6. **Regra 3:** se a operação tira um Admin de ação (rebaixar, desativar ou trocar o email, que exige nova confirmação), `CountOtherActiveAdminsAsync` precisa achar **pelo menos um outro** admin ativo (perfil Admin, email confirmado, sem bloqueio).
7. Aplica: nome (`UpdateAsync`); email (`SetEmailAsync`, que já zera `EmailConfirmed`, + `SetUserNameAsync`); perfil (remove o antigo, adiciona o novo); situação (`SetLockoutEndDateAsync(MaxValue)` = inativo, `null` = ativo).
8. `UpdateSecurityStampAsync`: a sessão aberta da pessoa cai **na próxima requisição** (seção 2).
9. `Commit`. Só **depois** envia o email de confirmação, para o link apontar para o dado salvo.
   - Qualquer `return` antes do commit desfaz tudo, porque o `await using` da transação faz rollback.

**`DeleteAsync`**: mesma transação e a mesma ordem de regras (si mesmo → protegida → último admin), depois `UserManager.DeleteAsync` (exclusão real; produtos não referenciam usuários).

### (b) Autorização da área Admin

1. `AdminControllerBase` tem `[Area("Admin")]` e `[Authorize(Policy = Policies.RequireAdmin)]`. Atributos de autorização são **herdados**: todo controller que herda dela está protegido.
2. `Program.cs` define as políticas: `RequireAdmin` = perfil Admin; `RequireStaff` = Admin ou Staff; `FallbackPolicy` = estar logado.
3. Quem não tem o perfil: o cookie handler devolveria um redirect para AccessDenied, mas `OnRedirectToAccessDenied` troca isso por **403**, e `UseStatusCodePagesWithReExecute` mostra a página amigável.
4. `AdminAuthorizationTests.Every_Admin_area_controller_requires_the_Admin_policy` acha por **reflexão** todo controller da área e confere: herda da base, tem a política, tem a área, e **não** tem `[AllowAnonymous]` (que anularia a política).
5. Os testes HTTP confirmam o comportamento: Staff → 403, Admin → 200, anônimo → login.

### (c) Pipeline de erros

Veja a seção 4. Arquivos: `ErrorController.cs` (registra e mostra o TraceId), `ErrorLogSink.cs` (filtro `Moinho.*`, nível Error, colunas), `AppDbContext.cs` (entidade `ErrorLog` com os mesmos nomes e tipos), `LogsController.cs` (lista, filtros e detalhe).

## 8. Roteiro de demo (5 minutos)

1. **Login** (0:30): mostre os botões de demo e a frase "você receberá um email de verdade".
2. **Equipe (demo)** (0:45): liste produtos, busque "moedor", ordene por preço, crie um produto com preço `12.50` → erro claro; troque para `12,50` → salvo. Digite `/Admin` na barra → **403**.
3. **Sair → Admin (demo)** (1:30): Painel; Equipe → filtre "Não confirmados"; **convide** alguém (seu email, se o SMTP real estiver configurado; no dev, abra o Mailpit em :8025) e mostre o link "Definir sua senha"; tente rebaixar a si mesmo → mensagem da regra.
4. **Simular erro** (1:00): Logs → "Simular erro" → página amigável com o TraceId → "Ver no log" → a linha exata, com exceção e propriedades.
5. **Cadastro** (1:00): numa janela anônima, cadastre-se → Mailpit → confirmar → entrar. Mostre "Esqueci minha senha" com um email inexistente: mesma resposta.
6. **Feche com o código** (0:15): `AdminControllerBase` + o teste de reflexão.

## 9. Perguntas prováveis de entrevista

1. **Autenticação × autorização?** Autenticação descobre *quem* é (cookie → `User`). Autorização decide *o que* pode (políticas, perfis). Aqui: `UseAuthentication` e depois `UseAuthorization`.
2. **Roles × policies?** Role é um dado do usuário (está no cookie). Policy é uma regra nomeada que pode usar roles, claims ou código. `[Authorize(Policy = "RequireAdmin")]` deixa a regra num lugar só e pode mudar sem tocar nos controllers.
3. **Por que um admin rebaixado ainda conseguiria entrar?** Porque os perfis estão no cookie, que só é revalidado (security stamp) a cada 30 min por padrão. Resolvido com intervalo zero + `UpdateSecurityStampAsync`.
4. **Como impedir alguém de acessar `/Admin` digitando a URL?** Esconder o link não basta: a política no servidor (`AdminControllerBase`) devolve 403. E um teste garante que nenhum controller da área ficou sem ela.
5. **EnsureCreated × migrations?** `EnsureCreated` cria o esquema do zero se o banco não existe e não guarda histórico; não evolui um banco existente. Migrations são passos versionados que alteram o esquema sem perder dados. Em produção, migrations aplicadas no deploy (bundle ou script).
6. **Por que não revelar se um email existe?** Para ninguém usar o formulário para descobrir quem tem conta (enumeração), o que facilita phishing e ataques de senha. "Esqueci minha senha", "reenviar confirmação" e o cadastro respondem igual.
7. **O que é over-posting?** Enviar campos extras no POST (ex.: `DateAdded`, `Id`) que o model binding preencheria na entidade. Evitado com view models que só têm os campos editáveis; `DateAdded` é definido pelo servidor. Há teste.
8. **Por que os links do email ficam errados atrás de um proxy?** O app monta o link com o esquema e o host da requisição que *ele* recebeu (`http://web:8080`). O proxy informa os originais em `X-Forwarded-Proto/Host`. `UseForwardedHeaders` aplica esses valores, mas só se vierem de uma rede confiável.
9. **Como evitar logar segredos?** Mensagens estruturadas só com o que precisa (caminho, id do usuário, tipo de email); nunca o formulário, o link ou o token. Logs do ASP.NET em Warning, para a URL com query string (que tem o token) não aparecer. Há teste que confere que a linha de erro não contém o link.
10. **O que é o token antiforgery?** Um valor secreto no formulário + cookie. Um site malicioso não consegue lê-lo, então não consegue forjar um POST em nome do usuário logado (CSRF). `AutoValidateAntiforgeryToken` está ligado globalmente.
11. **Por que o preço é string no formulário?** Por causa da armadilha da cultura pt-BR (seção 5).
12. **Como você garante que o SKU é único?** Índice único no banco. O serviço tenta salvar e transforma o erro do SQL Server (2627/2601) em erro de campo. Checar antes com `Any()` não basta: duas pessoas podem salvar ao mesmo tempo.
13. **Por que o admin não define a senha de quem ele cadastra?** Para ele nunca conhecer a senha de outra pessoa nem mandá-la por um canal inseguro. A pessoa recebe um convite e escolhe a própria senha.
14. **Para que serve o rate limiter aqui?** O cadastro e o "esqueci minha senha" mandam email para qualquer endereço digitado. Sem limite, um robô usaria o site para mandar spam e esgotaria a cota do provedor.
15. **Por que os testes usam SQL Server de verdade (Testcontainers) e não um banco em memória?** O índice único, a transação serializável, `ExecuteDeleteAsync` e o sink do Serilog dependem do SQL Server real; um banco em memória daria falsos positivos.
16. **O que acontece se o envio de email falhar no cadastro?** A conta é criada, o erro vai para o log (sem o link) e a tela pede para usar "Reenviar confirmação".
17. **Por que deletar linhas no reset em vez de apagar o banco?** `DROP DATABASE` exige permissão `dbcreator` e falha com conexões abertas; num SQL Server compartilhado, o app nem deveria ter essa permissão.

## 10. Comandos úteis

```bash
docker compose up --build                       # tudo
docker compose up -d sqlserver mailpit          # só a infra, para rodar pela IDE
dotnet test                                     # testes (precisa do Docker)
docker compose down -v                          # apaga o banco local
docker compose logs -f web                      # logs do app
```
