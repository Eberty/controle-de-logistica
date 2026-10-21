# AGENTS.md

Orientações para agentes de IA que trabalham neste repositório.

## Idioma

O projeto é todo em português (interface, mensagens de erro da API, documentação). Escreva textos para o usuário, mensagens de erro e documentação em português. Comentários de código e nomes de identificadores seguem o padrão já existente (em inglês).

## Execução de comandos

Rode comandos demorados (servidores, `dotnet run`, `npm run dev`, `docker compose up`, builds e instalações longas) em segundo plano, para não travar a conversa e o usuário poder continuar fazendo perguntas enquanto eles rodam.

## Visão geral

Sistema web de controle patrimonial e logística do 18º Batalhão de Bombeiros Militar: inventário, transferências de material, anotações privadas, mural compartilhado, calendário de prazos (número do SEI) e auditoria administrativa.

- `backend/`: API ASP.NET Core (.NET 10) com EF Core. Usa SQLite em desenvolvimento e PostgreSQL em produção.
- `frontend/`: SPA em React 19 + Vite, em JavaScript puro (sem TypeScript e sem roteador).
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

No primeiro acesso, `GET /api/auth/setup` retorna `requiresInitialAdmin: true` e `POST /api/auth/initial-admin` cria o usuário `admin` (senha com no mínimo 8 caracteres).

### Teste de ponta a ponta no navegador

Depois de uma mudança, valide também pela interface: suba o backend e o frontend (em segundo plano), abra `http://localhost:5173` no navegador embutido, entre no sistema e exercite os fluxos afetados, tirando prints para que o usuário também acompanhe o backend e o frontend funcionando. Teste com dois perfis: um usuário administrador e um operador (usuário comum, sem permissão de administrador).

## Arquitetura do backend

- **Sem migrations do EF.** O schema é mantido à mão em `Services/DatabaseInitializer.cs`, que roda no início da aplicação: cria tabelas, adiciona colunas que faltam e cria índices, com SQL separado para SQLite e PostgreSQL. Ao adicionar uma coluna ou tabela, atualize o modelo em `Models/`, o `AppDbContext` **e** o `DatabaseInitializer` (as duas versões de SQL, e a coluna em `EnsuredColumns` quando o banco já existe).
- **Autenticação própria**, sem ASP.NET Identity nem JWT: o login gera um token opaco guardado em memória (`InMemoryAuthSessionStore`, validade de 12 h). Reiniciar a API derruba todas as sessões, e a solução não funciona com várias instâncias. Senhas usam `PasswordHasher`. Novos usuários só são registrados com as credenciais de um administrador no próprio pedido.
- **Todos os controllers herdam de `AuthenticatedControllerBase`** e chamam `TryGetCurrentUserAsync()` ou `TryGetAdminUserAsync()` no início de cada ação (não há `[Authorize]`). A classe base também concentra regras compartilhadas: limite de quantidade, comparação de usuário e localização sem diferenciar maiúsculas (`QueryUsersByUsername`, `QueryItemsByLocation`, que mudam conforme o provedor) e fusão de itens (`FindMatchingInventoryItemAsync`, `MergeItemAsync`).
- **Concorrência de estoque:** operações que alteram itens abrem uma transação e chamam `AcquireItemsWriteLockAsync()` (um `UPDATE` na tabela `AppLocks`) para serializar as escritas. Mantenha esse padrão em qualquer nova escrita em `Items`.
- **Modelo de inventário:** um item é uma linha por combinação de nome, tombo, natureza, localização, conservação, observações, baixa e responsável. Uma transferência (`MovementsController`) tira quantidade da linha de origem e soma em uma linha de destino que já exista com os mesmos atributos, ou cria uma nova. Linhas que ficam iguais são fundidas, e os movimentos e logs de auditoria são reapontados. O nome do item é salvo em maiúsculas e o tombo aceita só números.
- **Catálogo:** `CatalogRules` lê `shared/catalog.json` (copiado para a saída do build pelo `.csproj`) e resolve os valores sem diferenciar maiúsculas. Localizações extras cadastradas pelo usuário ficam na tabela `LocationOptions`.
- **Fotos:** são enviadas como data URL base64 dentro do JSON do item (JPEG, PNG ou WebP, até 1,5 MB, com checagem de assinatura em `PhotoService`). Ficam guardadas por `IPhotoStorage`: `LocalPhotoStorage` (pasta `Data/images`) ou `S3PhotoStorage`, escolhido por `Storage:Provider`. Arquivos antigos só são apagados depois do commit da transação.
- **Anotações e Mural:** uma anotação sem destinatário é privada. Os destinatários ficam em `UserNoteRecipients`, com as datas de abertura (`ReadAt`), de "Pronto" (`CompletedAt`) e de quando o remetente viu o "Pronto" (`CompletionSeenAt`). O Mural (`MuralController`) é privado e se divide em Enviados e Recebidos. `GET /api/mural/summary` alimenta o contador do menu, consultado a cada 30 s pelo frontend. As etiquetas vêm da lista compartilhada `NoteTags` (nome e cor) e ficam gravadas na anotação como nomes separados por vírgula. A coluna `UserNotes.IsPublic` é legado do antigo mural público e não pode ser removida, porque nos bancos SQLite antigos ela é `NOT NULL` sem valor padrão.
- **Patrimônio** (`Item.AssetTag`, antes chamado "Tombo"): só existe para itens de natureza Permanente e não pode se repetir ao cadastrar nem ao trocar o número de um item. As linhas criadas por transferência mantêm o mesmo Patrimônio do item de origem.
- **Auditoria:** as ações relevantes passam por `IAuditLogger`. Ao criar uma ação nova que altera dados, registre a auditoria do mesmo jeito.
- **Erros:** a API sempre responde `{ "message": "..." }` em português. Erros de validação (DataAnnotations) são convertidos para esse formato em `Program.cs`, e exceções não tratadas viram 500 com mensagem genérica.
- `Program.cs` também configura CORS (em Development aceita qualquer origem loopback; em produção só `AllowedOrigins`), headers de segurança, limite de tentativas de login (10 por minuto por IP) e forwarded headers (`Proxy:*`).

## Arquitetura do frontend

- **Quase tudo fica em `src/App.jsx`** (cerca de 4.200 linhas): um único componente `App` com todo o estado em `useState` e as telas trocadas pelo estado `activeView` (`items`, `search`, `notes`, `mural`, `calendar`, `audit`). Os relatórios para impressão são gerados como HTML em um iframe (`printReport`). Há poucos componentes extraídos em `src/components/`.
- `src/api.js`: função `request()` que chama a API com o token Bearer (salvo no `localStorage` com a chave `ams_token`) e lança `Error` com a `message` vinda do backend. Também tem os helpers de data no formato pt-BR.
- `src/constants.js`: importa `../../shared/catalog.json` (liberado no `vite.config.js` em `server.fs.allow`) e define os formulários vazios.
- Números de processo ou ofício SEI são mostrados com `components/SeiLink.jsx`. Como a Pesquisa Pública do SEI Bahia só aceita o número por POST, o link é um GET comum para `public/sei.html?numero=...`, que abre numa aba nova e envia o formulário de pesquisa ao SEI (`public/sei-redirect.js`). Assim funciona em qualquer navegador e também ao abrir com o botão do meio do mouse. O clique também copia o número, para quem precisar colar na pesquisa rápida do SEI já logado (processos restritos não aparecem na Pesquisa Pública).
- `VITE_API_BASE_URL` é embutida **na hora do build** (o padrão é `http://localhost:5256`).
- No ESLint, a regra `no-unused-vars` ignora nomes que começam com letra maiúscula ou `_`.

## Configuração e deploy

- A configuração do backend segue o padrão do ASP.NET por variáveis de ambiente (`Database__Provider`, `ConnectionStrings__DefaultConnection`, `Storage__Provider`, `Storage__S3__*`, `AllowedOrigins__0`, `Proxy__TrustForwardedHeaders`, `Proxy__KnownNetworks__0`). Veja `README.md` e `.env.example`.
- Os dois Dockerfiles usam **a raiz do repositório como contexto de build**, por causa de `shared/`.
- CI/CD (`.github/workflows/`): push em `develop` publica em staging e tag `v*` (ou disparo manual) publica em produção. As imagens vão para o GHCR, e o deploy roda via SSH na EC2 com `docker-compose.prod.yml`, onde o `.env` é gerado a partir dos secrets.
