# Deploy

Toda a configuração vem de **variáveis de ambiente** (e da connection string), então o mesmo código roda no seu PC, numa VPS sozinho ou numa VPS dividida com outros projetos.

> ⚠️ **Atenção: mudança de esquema = banco novo.** O banco é criado com `EnsureCreated` (exigência do desafio). Ele **não altera** um banco existente. Se uma atualização mudar o modelo do EF (nova coluna, novo tamanho), é preciso apagar o banco e deixar o app recriá-lo e popular de novo. Veja [Atualizações](#atualizações) e a [ADR 0002](adr/0002-ensurecreated-instead-of-migrations.md).

## Alvo principal: uma VPS Linux pequena

Requisitos: Ubuntu 24.04 LTS, **4 GB de RAM** para o layout A (o SQL Server sozinho pede uns 2 GB), um domínio ou subdomínio e uma conta num provedor de email transacional.

### Layout A: tudo neste projeto (`compose.prod.yaml`)

```
Internet ──80/443──▶ caddy ──▶ web:8080 ──▶ sqlserver:1433 (Express, 1,5 GB)
                     (HTTPS)    (app)        (sem porta exposta)
```

- O Caddy obtém e renova o certificado HTTPS sozinho (Let's Encrypt).
- Só as portas 80 e 443 ficam expostas. O SQL Server e o app só existem dentro da rede do Docker.
- A rede tem sub-rede fixa (`172.30.0.0/24`) e o app só aceita cabeçalhos `X-Forwarded-*` dessa rede. É isso que faz os links nos emails usarem `https://seu-dominio` em vez de `http://web:8080`.
- As chaves do Data Protection ficam no volume `keys`. **Sem isso, cada redeploy derrubaria todos os logins, os tokens antiforgery e todos os links de confirmação e redefinição de senha já enviados por email.**

### Layout B: VPS compartilhada (`compose.shared.yaml`)

Para quando a mesma VPS já tem **um** SQL Server e **um** Caddy servindo vários projetos de portfólio, numa rede Docker externa chamada `proxy`:

```
Internet ──▶ caddy (compartilhado) ──moinho.seudominio──▶ moinho-web:8080 ──▶ sqlserver (compartilhado)
                                    ──outro.seudominio──▶ outro-app
```

1. **Banco e login só deste app** (no SQL Server compartilhado, como `sa`):
   ```sql
   CREATE DATABASE Moinho;
   CREATE LOGIN moinho_app WITH PASSWORD = 'uma-senha-forte-1!';
   USE Moinho;
   CREATE USER moinho_app FOR LOGIN moinho_app;
   ALTER ROLE db_owner ADD MEMBER moinho_app;  -- dono só deste banco, sem acesso aos outros
   ```
   O `EnsureCreated` cria as tabelas num banco existente e vazio, então o app não precisa da permissão `dbcreator`. O reset diário da demo apaga linhas, não o banco ([ADR 0006](adr/0006-demo-accounts-and-nightly-reset.md)), pelo mesmo motivo.
2. **`.env`:** `CONNECTION_STRING=Server=sqlserver,1433;Database=Moinho;User Id=moinho_app;Password=...;TrustServerCertificate=True` e `PROXY_NETWORK_CIDR` com a sub-rede da rede `proxy` (`docker network inspect proxy`, campo `IPAM.Config.Subnet`).
3. **Site no Caddy compartilhado:**
   ```
   moinho.seudominio.com.br {
       encode zstd gzip
       reverse_proxy moinho-web:8080
   }
   ```
   `moinho-web` é um alias de rede único definido em `compose.shared.yaml`, porque outros apps podem ter um serviço chamado `web`.
4. `docker compose -f compose.shared.yaml up -d --build` e `caddy reload` no container do Caddy.

## Servidor Ubuntu do zero

```bash
# 1. Docker Engine + Compose v2 (repositório oficial da Docker)
sudo apt update && sudo apt install -y ca-certificates curl git
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER && newgrp docker

# 2. Firewall: só SSH, HTTP e HTTPS
sudo ufw allow OpenSSH && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw allow 443/udp
sudo ufw enable
# Atenção: portas publicadas pelo Docker ignoram o ufw. Por isso os arquivos de produção só publicam 80/443.

# 3. Código e configuração
sudo mkdir -p /opt/moinho && sudo chown $USER /opt/moinho
git clone <repo> /opt/moinho && cd /opt/moinho
cp deploy/.env.production.example .env && nano .env      # domínio, senhas, SMTP, admin

# 4. DNS: crie um registro A (e AAAA, se tiver IPv6) de moinho.seudominio.com.br para o IP da VPS.

# 5. Subir
docker compose -f compose.prod.yaml up -d --build
docker compose -f compose.prod.yaml logs -f web          # espere "Application started"
```

Abra `https://moinho.seudominio.com.br`. O primeiro acesso pode levar alguns segundos enquanto o Caddy emite o certificado.

## Email em produção

O Mailpit só existe no desenvolvimento. Em produção use um provedor transacional por SMTP (porta 587 com STARTTLS):

| | **Brevo** | **Resend** |
|---|---|---|
| Plano grátis | 300 emails/dia | 3.000/mês, no máximo 100/dia |
| SMTP | `smtp-relay.brevo.com:587`, usuário e chave SMTP do painel | `smtp.resend.com:587`, usuário `resend`, senha = API key |
| Antes de enviar | a conta passa por aprovação da Brevo; verifique o remetente (ou o domínio) | exige **um domínio verificado** |

`.env`: `SMTP_HOST`, `SMTP_PORT=587`, `SMTP_USER`, `SMTP_PASSWORD`, `EMAIL_FROM="Moinho <nao-responda@seudominio.com.br>"`.

**SPF e DKIM (sem eles, o email cai no spam):** o provedor mostra os registros DNS para criar no seu domínio.

- **SPF** (registro TXT `v=spf1 include:... ~all`) diz quais servidores podem enviar em nome do domínio.
- **DKIM** (registro TXT ou CNAME com uma chave pública) permite que o destinatário confira a assinatura de cada mensagem.
- **DMARC** (`_dmarc`, TXT `v=DMARC1; p=none; rua=mailto:...`) diz o que fazer quando SPF ou DKIM falham. Gmail e Yahoo exigem DMARC para quem envia em volume.

Sem esses registros, o Gmail não consegue provar que a mensagem veio mesmo do seu domínio e a trata como suspeita.

**Cota:** cadastro, "esqueci minha senha", "reenviar confirmação", troca de email e convites têm limite de 5 POSTs por IP a cada 15 minutos. Isso protege a cota diária contra abuso de um mesmo visitante.

## Atualizações

```bash
cd /opt/moinho
git pull
docker compose -f compose.prod.yaml up -d --build
```

Ou pela action **Deploy** (`.github/workflows/deploy.yml`, disparo manual), que faz o mesmo por SSH. Configure os secrets `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY` e `DEPLOY_KNOWN_HOSTS` (saída de `ssh-keyscan seu-host`, conferida uma vez).

> ⚠️ **Se a atualização mudou o modelo do banco** (entidades ou `AppDbContext`), o `EnsureCreated` **não** vai aplicar a mudança e o app pode falhar ao ler ou gravar. É preciso recriar o banco:
> - Layout A: `docker compose -f compose.prod.yaml down` → `docker volume rm moinho_sqldata` → `up -d --build`.
> - Layout B: `DROP DATABASE Moinho; CREATE DATABASE Moinho;` (e recriar o usuário, como acima).
>
> Isso apaga os dados. Aqui tudo é dado de demonstração; num sistema real, use migrations ([ADR 0002](adr/0002-ensurecreated-instead-of-migrations.md)).

## Logs

- **Console** (tudo de Information para cima): `docker compose -f compose.prod.yaml logs -f web`
- **Erros** (Error e Fatal): tabela `ErrorLogs`, visível em **Admin → Logs**, com filtro por TraceId (o código que aparece na página de erro).

## Backups

```bash
# Backup do banco (layout A) para dentro do volume e depois para fora do servidor
docker compose -f compose.prod.yaml exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -Q "BACKUP DATABASE Moinho TO DISK = '/var/opt/mssql/data/moinho.bak' WITH INIT"
docker compose -f compose.prod.yaml cp sqlserver:/var/opt/mssql/data/moinho.bak ./moinho-$(date +%F).bak
# copie o .bak para outro lugar (outro servidor, storage)

# Chaves do Data Protection (sem elas, os links já enviados param de funcionar)
docker run --rm -v moinho_keys:/keys -v "$PWD":/out alpine tar czf /out/keys-$(date +%F).tgz -C /keys .
```

Com a demo pública e o reset diário, o backup do banco importa pouco. Já o backup das **chaves** e do `.env` importa sempre.

## Azure (alternativa)

- **App Service Linux, plano B1, com "Always On"** + **Azure SQL Database, camada Basic** (5 DTU). Custo baixo e previsível.
- **Por que não os planos grátis:**
  - o App Service **F1** "dorme" depois de alguns minutos sem acesso e tem cota diária de CPU, então a primeira visita do recrutador fica lenta, ou o app para no meio do dia, e o `BackgroundService` do reset não roda;
  - o **Azure SQL serverless grátis** pausa quando ocioso, e a primeira requisição depois da pausa leva dezenas de segundos (ou falha) enquanto o banco "acorda".
- Configuração em **App Settings**, com os mesmos nomes: `ConnectionStrings__Default`, `Email__Host`, `Seed__AdminEmail`, ...
- `DataProtection__KeysPath=/home/keys`: `/home` é persistente no App Service.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, porque o App Service fica atrás do proxy da própria Azure e os IPs dele variam.
- O login do Azure SQL precisa ser dono do banco (`db_owner`) para o `EnsureCreated` criar as tabelas.
