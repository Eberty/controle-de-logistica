# Guia de Deploy — CI/CD com GitHub Actions, GHCR e EC2

A aplicação é buildada pelo GitHub Actions, publicada no GitHub Container Registry (GHCR) e implantada em uma instância EC2 via SSH usando `docker-compose.prod.yml`. O banco de dados roda no Amazon RDS (PostgreSQL) e **não** faz parte do compose de produção.

| Ambiente | Trigger | Workflow |
| --- | --- | --- |
| Staging | `push` na branch `develop` | `.github/workflows/deploy-staging.yml` |
| Production | Manual (`workflow_dispatch`) ou tag `v*` | `.github/workflows/deploy-production.yml` |

Fluxo: **Build and Push** (imagens `backend` e `frontend` → GHCR) → **Deploy** (SSH na EC2: login no GHCR, gera `.env` com a connection string do RDS, copia o compose e roda `docker compose up -d --remove-orphans`).

---

## 1. GitHub Secrets e Variables

Use **Environments** do GitHub (`Settings → Environments`): crie `staging` e `production`. Os dois workflows referenciam esses ambientes, então cada um lê seu próprio conjunto de secrets/variables com os mesmos nomes. No ambiente `production`, ative **Required reviewers** para exigir aprovação antes do deploy.

### Secrets (por ambiente)

| Secret | Descrição | Exemplo |
| --- | --- | --- |
| `EC2_HOST` | IP público ou DNS da instância EC2 | `54.203.10.25` |
| `EC2_USER` | Usuário SSH da instância | `ec2-user` |
| `EC2_SSH_KEY` | Chave **privada** SSH (conteúdo completo do arquivo, incluindo `-----BEGIN...` e `-----END...`) | conteúdo de `github-actions-deploy` |
| `DB_CONNECTION_STRING` | Connection string completa do RDS | ver abaixo |
| `GHCR_USERNAME` | Usuário GitHub dono do PAT usado pela EC2 para puxar imagens | `eberty` |
| `GHCR_PAT` | Personal Access Token com escopo `read:packages` | `ghp_xxx...` |

### Variables (por ambiente)

| Variable | Descrição | Exemplo staging | Exemplo production |
| --- | --- | --- | --- |
| `API_BASE_URL` | URL pública da API, embutida no build do frontend (`VITE_API_BASE_URL`) | `https://api-staging.exemplo.com` | `https://api.exemplo.com` |
| `APP_ORIGIN` | Origin do frontend, usado no CORS do backend (`AllowedOrigins__0`) | `https://staging.exemplo.com` | `https://app.exemplo.com` |

> Sem domínio/HTTPS ainda? Use `http://<IP-da-EC2>:8080` como `API_BASE_URL` e `http://<IP-da-EC2>` como `APP_ORIGIN`.

### Formato da `DB_CONNECTION_STRING` (Npgsql + RDS)

```text
Host=meu-banco.abc123xyz.us-east-2.rds.amazonaws.com;Port=5432;Database=asset_management;Username=asset;Password=SENHA_FORTE_AQUI;Ssl Mode=Require;Trust Server Certificate=true
```

Pontos de atenção:

- A string é um conjunto de pares `chave=valor` separados por `;` e **precisa começar com `Host=`**. Sem o `Host=`, o Npgsql trata o endpoint como nome de parâmetro e o backend crasha na inicialização com `KeyNotFoundException`.
- **TLS com RDS:** o certificado do RDS é assinado pela CA própria da Amazon, que não existe no trust store da imagem Alpine. Como o Npgsql (6+) valida o certificado com `Ssl Mode=Require`, é obrigatório o `Trust Server Certificate=true` — a conexão continua criptografada, apenas sem validar o emissor (risco aceitável dentro da VPC). Para validação completa, use `Ssl Mode=VerifyFull;Root Certificate=/app/rds-bundle.pem` e adicione ao Dockerfile do backend o download do bundle: `https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem`.
- **Banco dedicado:** o RDS cria por padrão apenas o banco administrativo `postgres`. Usá-lo funciona (o backend cria as tabelas automaticamente no primeiro boot), mas o ideal é um banco próprio, criado uma única vez a partir da EC2:

  ```bash
  sudo dnf install -y postgresql16
  psql -h ENDPOINT_DO_RDS -U USUARIO_MASTER -d postgres -c "CREATE DATABASE asset_management;"
  ```

- A senha **nunca** aparece no código nem nos logs: ela vive apenas na Secret e no `.env` gerado na EC2 (com permissão `600`).
- O `.env` é interpolado pelo Docker Compose. Se a senha contiver o caractere `$`, escape-o como `$$` dentro da Secret, ou evite `$` na senha. O caractere `#` é seguro.
- Mudou a Secret? O valor só chega na EC2 no próximo deploy — re-execute o workflow após qualquer alteração.

---

## 2. Configuração da EC2 (Amazon Linux 2023)

### 2.1 Instalar Docker e o plugin Compose

```bash
sudo dnf install -y docker
sudo systemctl enable --now docker
sudo usermod -aG docker ec2-user
newgrp docker
```

O Amazon Linux 2023 não distribui o plugin do Compose via `dnf`. Instale o binário oficial, que resolve a arquitetura da instância (`x86_64` ou `aarch64`/Graviton) automaticamente:

```bash
mkdir -p ~/.docker/cli-plugins
curl -SL "https://github.com/docker/compose/releases/latest/download/docker-compose-linux-$(uname -m)" \
  -o ~/.docker/cli-plugins/docker-compose
chmod +x ~/.docker/cli-plugins/docker-compose
docker compose version
```

> Se `docker compose` reclamar de `exec format error`, o binário baixado não corresponde à arquitetura da instância — remova `~/.docker/cli-plugins/docker-compose` e repita o download com o comando acima.

### 2.2 Criar o diretório da aplicação

```bash
mkdir -p ~/app
```

O pipeline copia o `docker-compose.prod.yml` para `~/app/` e gera o `~/app/.env` a cada deploy.

### 2.3 Par de chaves SSH dedicado para o GitHub Actions

Não reutilize o `.pem` da AWS. Crie um par exclusivo para o pipeline (fácil de revogar sem perder seu próprio acesso):

Na sua máquina local:

```bash
ssh-keygen -t ed25519 -C "github-actions-deploy" -f ~/.ssh/github-actions-deploy -N ""
```

Na EC2, autorize a chave pública:

```bash
echo "CONTEUDO_DE_github-actions-deploy.pub" >> ~/.ssh/authorized_keys
chmod 600 ~/.ssh/authorized_keys
```

Teste da sua máquina e depois cadastre a chave **privada** na Secret `EC2_SSH_KEY`:

```bash
ssh -i ~/.ssh/github-actions-deploy ec2-user@SEU_IP_EC2 "docker ps"
cat ~/.ssh/github-actions-deploy
```

Para revogar o acesso do pipeline, basta remover a linha correspondente do `authorized_keys`.

### 2.4 Personal Access Token (PAT) para o GHCR

A EC2 precisa se autenticar no GHCR para puxar as imagens privadas:

1. GitHub → **Settings → Developer settings → Personal access tokens → Tokens (classic)** → *Generate new token (classic)*.
2. Nome: `ghcr-pull-ec2`. Escopo: marque **apenas `read:packages`**. Defina uma expiração e anote a data para renovação.
3. Salve o token na Secret `GHCR_PAT` e seu usuário GitHub em `GHCR_USERNAME`.

O `docker login` é executado pelo próprio pipeline a cada deploy — nada de token hardcoded na EC2. O Docker guarda a credencial em `~/.docker/config.json` (em base64, não criptografado): mantenha o acesso SSH à instância restrito.

> Alternativa mais segura: fine-grained PAT ou um **GitHub App**; para pull de pacotes, o token classic com `read:packages` é o caminho suportado e mais simples.

---

## 3. Rede e Security Groups (VPC)

Crie dois Security Groups na mesma VPC:

### SG da aplicação (`sg-app`, anexado à EC2)

| Regra | Porta | Origem | Motivo |
| --- | --- | --- | --- |
| Inbound | 80 (e 443 se tiver TLS) | `0.0.0.0/0` | Frontend público |
| Inbound | 8080 | `0.0.0.0/0` | API (só enquanto o navegador acessar a API direto pela porta; atrás de um reverse proxy/ALB, feche) |
| Inbound | 22 | **seu IP** (`x.x.x.x/32`) | Administração SSH |
| Outbound | tudo | `0.0.0.0/0` | Pull de imagens, RDS, updates |

Sobre o SSH do GitHub Actions: os runners não têm IP fixo. Opções, da mais simples à mais robusta:

- Liberar a porta 22 temporariamente e restringir depois;
- Atualizar a regra com os ranges publicados em `https://api.github.com/meta` (chave `actions`) — são muitos e mudam;
- **Recomendado a médio prazo:** um step no pipeline que usa a AWS CLI para adicionar o IP do runner ao SG antes do deploy e removê-lo depois, ou usar AWS SSM Session Manager e eliminar o SSH público.

### SG do banco (`sg-db`, anexado ao RDS)

| Regra | Porta | Origem | Motivo |
| --- | --- | --- | --- |
| Inbound | 5432 | **`sg-app`** (o Security Group, não um IP) | Apenas a EC2 alcança o banco |

Usar o *ID do Security Group* como origem é o ponto-chave: qualquer instância com o `sg-app` anexado acessa o banco, e nada mais — nem mesmo outros hosts da VPC.

### Configuração do RDS

- **Public accessibility: No.** O banco não deve ter IP público; a EC2 acessa pelo endpoint interno da VPC.
- Mesma VPC da EC2 (ou VPC peering, se separadas).
- Crie o banco `asset_management` e um usuário próprio da aplicação (evite usar o usuário master na connection string).
- Habilite backups automáticos e, em produção, considere Multi-AZ.

---

## 4. Checklist do primeiro deploy

1. RDS criado, `sg-db` liberando 5432 apenas para `sg-app`, banco e usuário criados.
2. EC2 com Docker instalado, `~/app` criado, chave pública do pipeline no `authorized_keys`.
3. Environments `staging` e `production` no GitHub com todas as Secrets/Variables da seção 1.
4. Branch `develop` criada — o push nela dispara o deploy de staging.
5. Production: rode manualmente em **Actions → Deploy Production → Run workflow** (branch `main`) ou crie uma tag `v1.0.0`.
6. Validação na EC2: `docker compose -f ~/app/docker-compose.prod.yml --env-file ~/app/.env ps` e `docker logs` dos serviços.
