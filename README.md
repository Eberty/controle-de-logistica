# Controle Patrimonial e Logística

Sistema web para gestão de patrimônio e logística. Ele permite controlar inventário, transferências, anotações privadas, mural compartilhado, calendário de prazos e auditoria.

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
* Controle de quantidade, tombo, natureza, localização e conservação
* Transferência de material
* Anotações privadas por usuário
* Mural compartilhado de recados
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

## Deploy em produção (AWS)

O sistema tem quatro partes que ficam em lugares diferentes na nuvem:

| Parte | O que é | Onde fica na AWS |
| --- | --- | --- |
| Frontend | Site em React (arquivos estáticos) | S3 + CloudFront (HTTPS) |
| Backend | API em .NET | Servidor de aplicação + HTTPS |
| Banco | PostgreSQL | RDS (gerenciado) |
| Fotos | Imagens dos itens | Bucket S3 |

### Ordem sugerida dos passos

1. **Criar a conta AWS** e ativar a autenticação em duas etapas (MFA) no usuário raiz.
2. **Banco (RDS PostgreSQL):** criar uma instância pequena. Anotar host, porta, banco, usuário e senha.
3. **Fotos (S3):** criar um bucket privado para as imagens e uma chave de acesso (IAM) com permissão só nesse bucket.
4. **Backend:** publicar a API com as variáveis de ambiente abaixo e um endereço HTTPS.
5. **Frontend:** gerar os arquivos com o comando da seção *Build do frontend* (abaixo) e publicá-los em outro bucket S3 servido pelo CloudFront.
6. **Domínio e HTTPS:** apontar o domínio (Route 53 ou seu registrador) para o CloudFront (frontend) e para o backend, com certificado (ACM).

### Variáveis de ambiente do backend (produção)

Defina todas no servidor onde a API roda:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080
AllowedOrigins__0=https://SEU_DOMINIO_DO_FRONTEND

Database__Provider=PostgreSQL
ConnectionStrings__DefaultConnection=Host=SEU_HOST;Port=5432;Database=SEU_BANCO;Username=SEU_USUARIO;Password=SUA_SENHA;SSL Mode=Require;Trust Server Certificate=true

Storage__Provider=S3
Storage__S3__Bucket=SEU_BUCKET
Storage__S3__Region=us-east-1
Storage__S3__AccessKey=SUA_ACCESS_KEY
Storage__S3__SecretKey=SUA_SECRET_KEY
Storage__S3__KeyPrefix=items
```

* `ASPNETCORE_URLS=http://0.0.0.0:8080` define a porta em que a API escuta. Sem isso, ela sobe só em `localhost` e o proxy/load balancer não consegue alcançá-la.
* `AllowedOrigins__0` deve ser o endereço exato do frontend (senão o navegador bloqueia as chamadas por CORS). Para mais de um endereço, use `AllowedOrigins__1`, `AllowedOrigins__2`, etc.

Se usar um serviço compatível com S3 que não seja AWS, adicione:

```text
Storage__S3__ServiceUrl=https://endpoint-do-servico
Storage__S3__ForcePathStyle=true
```

Se o backend ficar atrás de um proxy reverso (nginx, load balancer da AWS), configure também para o limite de tentativas de login funcionar por IP:

```text
Proxy__TrustForwardedHeaders=true
Proxy__KnownNetworks__0=172.31.0.0/16
```

### Build do frontend

O frontend precisa saber o endereço do backend **no momento do build**:

```bash
VITE_API_BASE_URL=https://SEU_DOMINIO_DO_BACKEND
npm --prefix frontend run build
```

Os arquivos gerados ficam em `frontend/dist` — é esse conteúdo que vai para o bucket S3 do frontend.
