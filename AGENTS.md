# AGENTS.md

Orientações para agentes de IA que trabalham neste repositório.

## Idioma

O projeto é todo em português (interface, mensagens de erro da API, documentação). Escreva textos para o usuário, mensagens de erro e documentação em português. Comentários de código e nomes de identificadores seguem o padrão já existente (em inglês).

## Execução de comandos

Rode comandos demorados (servidores, `dotnet run`, `npm run dev`, `docker compose up`, builds e instalações longas) em segundo plano, para não travar a conversa e o usuário poder continuar fazendo perguntas enquanto eles rodam.

## Documentação

Não escreva nos `.md` dados que envelhecem rápido: contagem de linhas, tempos, limites, quantidades e versões. Em vez do valor, diga onde ele é definido (constante, arquivo de configuração ou de dependências). Ao mudar o código, revise os `.md` que descrevem aquela parte.

## Git

Se o usuário tiver colocado arquivos em staged, não tire nada do staged: não use `git stash`, `git reset`, `git restore --staged`, `git checkout` nem outro comando que mexa no índice. Faça as edições por cima, deixando-as como alterações fora do staged, para o usuário revisar e adicionar.

## Visão geral

Sistema web de controle patrimonial e logística do 18º Batalhão de Bombeiros Militar: inventário, transferências de material, anotações (privadas ou enviadas a outros usuários), mural privado com Enviados e Recebidos, calendário de prazos (número do SEI) e auditoria administrativa.

- `backend/`: API ASP.NET Core com EF Core (versões em `backend/backend.csproj`). Usa SQLite em desenvolvimento e PostgreSQL em produção.
- `frontend/`: SPA em React + Vite (versões em `frontend/package.json`), em JavaScript puro (sem TypeScript e sem roteador).
- `shared/catalog.json`: listas fixas de natureza, estado de conservação e localizações padrão, usadas **pelos dois lados**.
- `nginx/` + `docker-compose.prod.yml`: proxy reverso de produção (atrás do Cloudflare).
- `docs/`: guias de deploy (EC2), S3 e nginx/HTTPS.

## Comandos

Rodar tudo localmente, a partir da raiz (API em `http://localhost:5256`, frontend em `http://localhost:5173`):

```bash
npm --prefix frontend install
npx --yes concurrently "dotnet run --project backend" "npm --prefix frontend run dev"
```

Separadamente:

```bash
dotnet build backend
dotnet run --project backend
npm --prefix frontend run dev
npm --prefix frontend run lint
npm --prefix frontend run build
```

Com Docker (PostgreSQL + API + Vite com hot reload): `docker compose up --build`. Veja `DOCKER.md`.

**Não há testes automatizados** no repositório (nenhum projeto de testes no backend nem test runner no frontend). Para validar uma mudança: `dotnet build backend`, `npm --prefix frontend run lint` e `npm --prefix frontend run build`, e depois exercite a API ou a interface.

Para testar sem mexer no banco local (`backend/Data/asset-management.db`), aponte o backend para um banco e uma pasta de imagens temporários:

```bash
ConnectionStrings__DefaultConnection="Data Source=/caminho/tmp/test.db" Storage__LocalPath="/caminho/tmp/images" dotnet run --project backend
```

No primeiro acesso, `GET /api/auth/setup` retorna `requiresInitialAdmin: true` e `POST /api/auth/initial-admin` cria o usuário `admin` (o tamanho mínimo da senha é `MinPasswordLength`, no `AuthController`).

### Teste de ponta a ponta no navegador

Depois de uma mudança, valide também pela interface: suba o backend e o frontend (em segundo plano), abra `http://localhost:5173` no navegador embutido, entre no sistema e exercite os fluxos afetados, tirando prints para que o usuário também acompanhe o backend e o frontend funcionando. Teste com dois perfis: um usuário administrador e um operador (usuário comum, sem permissão de administrador).

## Arquitetura do backend

- **Sem migrations do EF.** O schema é mantido à mão em `Services/DatabaseInitializer.cs`, que roda no início da aplicação: cria tabelas, adiciona colunas que faltam e cria índices, com SQL separado para SQLite e PostgreSQL. Ao adicionar uma coluna ou tabela, atualize o modelo em `Models/`, o `AppDbContext` **e** o `DatabaseInitializer` (as duas versões de SQL, e a coluna em `EnsuredColumns` quando o banco já existe).
- **Autenticação própria**, sem ASP.NET Identity nem JWT: o login gera um token opaco guardado em memória (`InMemoryAuthSessionStore`, com a validade definida em `SessionTtl`). Reiniciar a API derruba todas as sessões, e a solução não funciona com várias instâncias. Senhas usam `PasswordHasher`. Novos usuários só são registrados com as credenciais de um administrador no próprio pedido.
- **Todos os controllers herdam de `AuthenticatedControllerBase`** e chamam `TryGetCurrentUserAsync()` ou `TryGetAdminUserAsync()` no início de cada ação (não há `[Authorize]`). A classe base também concentra regras compartilhadas: limite de quantidade, comparação de usuário e localização sem diferenciar maiúsculas (`QueryUsersByUsername`, `QueryItemsByLocation`, que mudam conforme o provedor) e fusão de itens (`FindMatchingInventoryItemAsync`, `MergeItemAsync`).
- **Concorrência de estoque:** operações que alteram itens abrem uma transação e chamam `AcquireItemsWriteLockAsync()` (um `UPDATE` na tabela `AppLocks`) para serializar as escritas. Mantenha esse padrão em qualquer nova escrita em `Items`.
- **Modelo de inventário:** um item é uma linha por combinação de nome, patrimônio, natureza, localização, conservação, observações, baixa e responsável. Uma transferência (`MovementsController`) tira quantidade da linha de origem e soma em uma linha de destino que já exista com os mesmos atributos, ou cria uma nova. Linhas que ficam iguais são fundidas, e os movimentos e logs de auditoria são reapontados. O nome do item é salvo em maiúsculas e o patrimônio aceita só números.
- **Catálogo:** `CatalogRules` lê `shared/catalog.json` (copiado para a saída do build pelo `.csproj`) e resolve os valores sem diferenciar maiúsculas. Localizações extras cadastradas pelo usuário ficam na tabela `LocationOptions`.
- **Fotos:** são enviadas como data URL base64 dentro do JSON do item (JPEG, PNG ou WebP; o limite de tamanho e a checagem de assinatura ficam em `PhotoService`). Ficam guardadas por `IPhotoStorage`: `LocalPhotoStorage` (pasta `Data/images`) ou `S3PhotoStorage`, escolhido por `Storage:Provider`. Arquivos antigos só são apagados depois do commit da transação.
- **Anotações e Mural:** uma anotação sem destinatário é privada. O prazo (`DueDate`, `yyyy-MM-dd`) é opcional; vazio quer dizer sem prazo. Os destinatários ficam em `UserNoteRecipients`, com as datas de abertura (`ReadAt`), de "Pronto" (`CompletedAt`) e de quando o remetente viu o "Pronto" (`CompletionSeenAt`). O Mural (`MuralController`) é privado e se divide em Enviados e Recebidos. `GET /api/mural/summary` alimenta o contador do menu, consultado periodicamente pelo frontend (intervalo em `muralSummaryPollMs`, no `App.jsx`). As etiquetas vêm da lista compartilhada `NoteTags` (nome e cor) e ficam gravadas na anotação como nomes separados por vírgula. A coluna `UserNotes.IsPublic` é legado do antigo mural público e não pode ser removida, porque nos bancos SQLite antigos ela é `NOT NULL` sem valor padrão.
- **Patrimônio** (`Item.AssetTag`, antes chamado "Tombo"): só existe para itens de natureza Permanente e não pode se repetir ao cadastrar nem ao trocar o número de um item. As linhas criadas por transferência mantêm o mesmo Patrimônio do item de origem.
- **Auditoria:** as ações relevantes passam por `IAuditLogger`. Ao criar uma ação nova que altera dados, registre a auditoria do mesmo jeito.
- **Erros:** a API sempre responde `{ "message": "..." }` em português. Erros de validação (DataAnnotations) são convertidos para esse formato em `Program.cs`, e exceções não tratadas viram 500 com mensagem genérica.
- `Program.cs` também configura CORS (em Development aceita qualquer origem loopback; em produção só `AllowedOrigins`), headers de segurança, limite de tentativas de login por IP (política `login`) e forwarded headers (`Proxy:*`).

## Arquitetura do frontend

- **Quase tudo fica em `src/App.jsx`**, um arquivo grande: um único componente `App` com todo o estado em `useState` e as telas trocadas pelo estado `activeView` (`items`, `search`, `notes`, `mural`, `calendar`, `audit`). Os relatórios para impressão são gerados como HTML em um iframe (`printReport`). Há poucos componentes extraídos em `src/components/`.
- `src/api.js`: função `request()` que chama a API com o token Bearer (salvo no `localStorage` com a chave `ams_token`) e lança `Error` com a `message` vinda do backend. Também tem os helpers de data no formato pt-BR.
- `src/constants.js`: importa `../../shared/catalog.json` (liberado no `vite.config.js` em `server.fs.allow`) e define os formulários vazios.
- Números de processo ou ofício SEI são mostrados com `components/SeiLink.jsx`. O SEI Bahia recusa links para um processo montados fora da sessão do usuário ("Link sem assinatura"), exceto o "link de acesso direto", que só precisa do número interno do processo. Anotações e calendário têm um campo opcional para colar esse link: o backend (`Services/SeiLinks.cs`) só aceita endereços `https` do SEI Bahia e guarda apenas o número interno (`SeiProcessId`), e o frontend remonta o link (`src/sei.js`). Sem esse link, o número abre a entrada do SEI e é copiado, com um aviso para colar na pesquisa rápida.
- **Confirmações** usam `confirmAction({ eyebrow, title, text, confirmLabel })`, que abre a janela do próprio sistema e devolve uma `Promise<boolean>`. Não use `window.confirm`: no navegador embutido do Claude ele não aparece e responde "não" sozinho.
- `VITE_API_BASE_URL` é embutida **na hora do build** (o padrão é `http://localhost:5256`).
- No ESLint, a regra `no-unused-vars` ignora nomes que começam com letra maiúscula ou `_`.

## Configuração e deploy

- A configuração do backend segue o padrão do ASP.NET por variáveis de ambiente (`Database__Provider`, `ConnectionStrings__DefaultConnection`, `Storage__Provider`, `Storage__S3__*`, `AllowedOrigins__0`, `Proxy__TrustForwardedHeaders`, `Proxy__KnownNetworks__0`). Os valores padrão ficam em `backend/appsettings.json`; os de produção, em `.env.example` e `docs/DEPLOYMENT.md`.
- Os dois Dockerfiles usam **a raiz do repositório como contexto de build**, por causa de `shared/`.
- CI/CD (`.github/workflows/`): tag `v*` ou disparo manual (`Deploy Production`) publica em produção. **Não há ambiente de staging**: teste localmente e publique direto em produção. As imagens vão para o GHCR, e o deploy roda via SSH na EC2 com `docker-compose.prod.yml`, onde o `.env` é gerado a partir dos secrets.
- **Onde está hospedado:** produção em `https://slt18bbm.com.br` (o `www` redireciona para ele), com a API em `https://api.slt18bbm.com.br`.
- **Como publicar:** só quando o usuário pedir, e com as mudanças já commitadas e enviadas para a `main` (o workflow publica o que está no GitHub, não a cópia local). Pela interface: **Actions → Deploy Production → Run workflow** (o botão pode ficar no menu **⋯**), com a branch `main`. Pelo terminal: `gh workflow run deploy-production.yml --repo Eberty/controle-de-logistica --ref main`, e acompanhe com `gh run watch <id> --repo Eberty/controle-de-logistica`.
- **Depois de cada deploy**, diga ao usuário em que endereço a versão nova está no ar e confira, sem fazer login, que o site responde 200, que `GET /api/auth/setup` responde 200 e que as rotas novas existem (sem login, elas respondem 401, e não 404). Lembre o usuário que todos precisam entrar de novo, porque as sessões ficam na memória da API.
