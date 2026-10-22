# Controle Patrimonial e Logística

Sistema web para gestão de patrimônio e logística. Ele permite controlar inventário, transferências, anotações (privadas ou enviadas a outros usuários), mural de anotações, calendário de prazos e auditoria.

---

## 1. Instalar os programas necessários

Instale pelos sites oficiais:

* .NET SDK: <https://dotnet.microsoft.com/download>
* Node.js: <https://nodejs.org>

No .NET, baixe o **SDK**.  
No Node.js, baixe a versão **LTS**.

Se você já tem esses programas instalados, pode ir direto para o próximo passo.

---

## 2. Baixar e executar o projeto

1. Abra esse repositório no GitHub. `https://github.com/Eberty/controle-de-logistica`
2. Clique no botão verde **Code**.
3. Clique em **Download ZIP**.
4. Depois que baixar, extraia o arquivo ZIP.
5. Coloque a pasta extraída em um lugar fácil de encontrar, por exemplo, em `Documentos`.
6. Entre na pasta do projeto pelo terminal. No Windows, basta digitar `cmd` na barra de endereços do explorador de arquivos.
7. Se for a **primeira vez** a executar o projeto, instale as dependências digitando o seguinte comando no terminal: `npm --prefix frontend install`

### Iniciar o sistema

Estando na pasta principal do projeto, rode este comando:

```bash
npx --yes concurrently "dotnet run --project backend" "npm --prefix frontend run dev"
```

Deixe esse terminal aberto enquanto estiver usando o sistema.

### Abrir o sistema

Quando o comando estiver rodando, abra no navegador:

```text
http://localhost:5173
```

No primeiro acesso, o sistema vai pedir a criação da senha do administrador.

---

## Erros comuns

### Erro no Windows PowerShell ao rodar `npm`

Se aparecer uma mensagem dizendo que `npm.ps1` não pode ser carregado porque a execução de scripts está desabilitada, rode:

```powershell
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### A tela abre, mas o login não funciona

Confira se o terminal do sistema continua aberto e rodando.

Se ele foi fechado, rode o comando de iniciar o sistema novamente.

### A porta já está em uso

Feche terminais antigos do projeto e rode o comando de iniciar o sistema novamente.

---

## Funcionalidades

* Login com usuário e senha
* Cadastro, edição e exclusão de itens de inventário
* Controle de quantidade, patrimônio, natureza, localização e conservação
* Transferência de material
* Anotações privadas por usuário, com etiquetas e número do SEI
* Mural privado com anotações enviadas e recebidas entre usuários
* Calendário de prazos com número do SEI e assunto
* Histórico individual por item
* Auditoria administrativa

---

## Banco de dados

Os dados ficam salvos mesmo depois de fechar e abrir o sistema novamente.

No uso local, o arquivo com os dados é criado automaticamente aqui:

```text
backend/Data/asset-management.db
```

As imagens dos itens ficam salvas localmente aqui:

```text
backend/Data/images
```

---

## Onde o sistema está no ar

| Site | API | Como publicar |
| --- | --- | --- |
| <https://slt18bbm.com.br> | <https://api.slt18bbm.com.br> | Manual, pelo GitHub Actions (abaixo) ou com uma tag `v*` |

O endereço `www.slt18bbm.com.br` redireciona para `slt18bbm.com.br`.

Não há ambiente de teste (staging): teste as mudanças localmente, como descrito no início deste README, antes de publicar em produção.

### Publicar em produção

1. No GitHub, abra **Actions → Deploy Production**.
2. Clique em **Run workflow**. Em algumas versões do GitHub, esse botão fica no menu **⋯**, ao lado do título.
3. Deixe **Branch: main** e confirme.

Pelo terminal, com o GitHub CLI:

```bash
gh workflow run deploy-production.yml --repo Eberty/controle-de-logistica --ref main
```

Quando o workflow terminar, a versão nova já está no ar. Como as sessões ficam na memória da API, todos os usuários precisam entrar de novo. Os detalhes da infraestrutura (EC2, RDS, GHCR e secrets) estão em `docs/DEPLOYMENT.md`.

---

## Como a produção funciona

```text
Navegador ── HTTPS ──▶ Cloudflare ── HTTP ──▶ EC2 (Docker)
                                               └─ nginx ─┬─ slt18bbm.com.br      ──▶ frontend
                                                         └─ api.slt18bbm.com.br  ──▶ backend ──▶ RDS (PostgreSQL)
                                                                                           └──▶ S3 (fotos)
```

| Parte | O que é | Onde roda |
| --- | --- | --- |
| Domínio e HTTPS | `slt18bbm.com.br` e `api.slt18bbm.com.br` | Cloudflare, que termina o HTTPS e encaminha para a EC2 |
| Proxy | nginx, que separa o site e a API pelo endereço | Contêiner na EC2, na porta 80 (a única aberta para o Cloudflare) |
| Frontend | Site em React, servido como arquivos estáticos | Contêiner na EC2 |
| Backend | API em .NET | Contêiner na EC2 |
| Banco | PostgreSQL | Amazon RDS (fora da EC2) |
| Fotos | Imagens dos itens | Bucket S3 privado (o navegador só acessa pela API) |
| Imagens Docker | Backend e frontend já montados | GitHub Actions monta e guarda no GitHub Container Registry (GHCR) |

### O que acontece num deploy

1. O GitHub Actions monta as imagens do backend e do frontend e envia ao GHCR.
2. Entra na EC2 por SSH, copia o `docker-compose.prod.yml` e os arquivos do nginx, gera o `.env` a partir dos secrets do GitHub e baixa as imagens novas.
3. Recria os contêineres com `docker compose up -d`. Ao subir, o backend atualiza sozinho as tabelas do banco (não há migrations para rodar à mão).

### Configuração

As senhas e endereços **não** ficam no código nem são digitados no servidor. Ficam nos **Environments** do GitHub (`Settings → Environments → production`) e chegam à EC2 a cada deploy:

* **Secrets:** acesso à EC2 (`EC2_HOST`, `EC2_USER`, `EC2_SSH_KEY`), banco (`DB_CONNECTION_STRING`), acesso ao GHCR (`GHCR_USERNAME`, `GHCR_PAT`) e S3 (`AWS_REGION`, `AWS_S3_BUCKET_NAME`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`).
* **Variables:** endereços públicos (`API_BASE_URL`, `APP_ORIGIN`, `FRONTEND_HOST`, `API_HOST`) e o prefixo opcional das fotos (`AWS_S3_KEY_PREFIX`).

Depois de alterar um secret ou uma variable, rode o deploy de novo para o valor chegar ao servidor. O `API_BASE_URL` é embutido no frontend na hora de montar a imagem, então mudar esse valor também exige um deploy completo.

Para testar a configuração de produção na própria máquina, copie `.env.example` para `.env`, ajuste os valores e rode o comando abaixo. As imagens do GHCR são privadas: faça `docker login ghcr.io` antes, ou monte as imagens localmente (veja `DOCKER.md`) e use os mesmos nomes e tags do `.env`.

```bash
docker compose -f docker-compose.prod.yml --env-file .env up -d
```

### Guias detalhados

* [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md): GitHub Actions, secrets e variables, preparação da EC2, RDS, rede e checklist do primeiro deploy.
* [`docs/NGINX_HTTPS.md`](docs/NGINX_HTTPS.md): nginx, Cloudflare e HTTPS.
* [`docs/AWS_S3_SETUP.md`](docs/AWS_S3_SETUP.md): bucket S3 das fotos e usuário IAM.
