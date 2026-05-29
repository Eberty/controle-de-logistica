# Controle Patrimonial e Logística

Sistema web para gestão de patrimônio e logística. Ele permite controlar inventário, transferências, anotações privadas e auditoria.

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

Para produção, o backend também aceita PostgreSQL externo e S3. Configure por variáveis de ambiente no servidor:

```text
Database__Provider=PostgreSQL
ConnectionStrings__DefaultConnection=Host=SEU_HOST;Port=5432;Database=SEU_BANCO;Username=SEU_USUARIO;Password=SUA_SENHA;SSL Mode=Require;Trust Server Certificate=true

Storage__Provider=S3
Storage__S3__Bucket=SEU_BUCKET
Storage__S3__Region=us-east-1
Storage__S3__AccessKey=SUA_ACCESS_KEY
Storage__S3__SecretKey=SUA_SECRET_KEY
Storage__S3__KeyPrefix=items
```

Se usar um serviço compatível com S3 que não seja AWS, configure também:

```text
Storage__S3__ServiceUrl=https://endpoint-do-servico
Storage__S3__ForcePathStyle=true
```
