# Docker — Guia de Uso

## Arquitetura

| Serviço    | Imagem / Build                                         | Porta (host) | Descrição                                        |
| ---------- | ------------------------------------------------------ | ------------ | ------------------------------------------------ |
| `db`       | `postgres:17-alpine`                                   | `5432`       | PostgreSQL com volume nomeado para persistência  |
| `backend`  | `backend/Dockerfile` (multi-stage)                     | `5256`       | API .NET 10 (interna na 8080, non-root)          |
| `frontend` | `node:22-alpine` (dev) / `frontend/Dockerfile` (prod)  | `5173`       | Vite dev server com hot reload                   |

> Os dois Dockerfiles usam a **raiz do repositório como contexto de build**, pois backend e frontend dependem de `shared/catalog.json`.

## Desenvolvimento local

Subir todo o ambiente (build + start):

```bash
docker compose up --build
```

- Frontend: <http://localhost:5173> (hot reload — edite `frontend/src` e o navegador atualiza)
- API: <http://localhost:5256>
- PostgreSQL: `localhost:5432` (`asset` / `asset_local_dev`, database `asset_management`)

Subir em segundo plano:

```bash
docker compose up --build -d
```

Acompanhar logs:

```bash
docker compose logs -f backend
```

Se o hot reload não disparar (limitação de eventos de arquivo em bind mounts no macOS/Windows), adicione `CHOKIDAR_USEPOLLING: "true"` ao `environment` do serviço `frontend`.

## Parar e limpar

Parar os contêineres (mantendo os dados):

```bash
docker compose down
```

Parar e **apagar os volumes** (banco de dados e fotos — irreversível):

```bash
docker compose down -v
```

Rebuild forçado sem cache:

```bash
docker compose build --no-cache
```

## Builds de produção

Backend (imagem final Alpine, non-root, porta 8080):

```bash
docker build -f backend/Dockerfile -t asset-management-backend .
```

Frontend (a URL da API é embutida no bundle em tempo de build):

```bash
docker build -f frontend/Dockerfile --build-arg VITE_API_BASE_URL=https://api.seudominio.com -t asset-management-frontend .
```

Ambos os comandos devem ser executados a partir da **raiz do repositório**.

### Variáveis de ambiente relevantes em produção (backend)

| Variável                                 | Exemplo                                            |
| ---------------------------------------- | -------------------------------------------------- |
| `Database__Provider`                     | `PostgreSQL`                                       |
| `ConnectionStrings__DefaultConnection`   | `Host=...;Database=...;Username=...;Password=...`  |
| `Storage__Provider`                      | `S3` (ou `Local` com volume em `/app/Data/images`) |
| `AllowedOrigins__0`                      | `https://app.seudominio.com`                       |
| `Proxy__TrustForwardedHeaders`           | `true` (se atrás de reverse proxy)                 |

Em produção o TLS deve ser terminado por um reverse proxy (Nginx, ALB, etc.) na frente dos contêineres — a API escuta apenas HTTP na porta 8080. Como não há porta HTTPS configurada no contêiner, o `UseHttpsRedirection` não redireciona; configure `AllowedOrigins` para liberar o CORS do frontend.
