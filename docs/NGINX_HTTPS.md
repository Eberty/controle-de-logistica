# Reverse Proxy (Nginx) atrás do Cloudflare

O HTTPS é terminado pelo **Cloudflare** (modo SSL **Flexible**). Na EC2, o Nginx é o único serviço que expõe porta no host (`80`) e roteia por hostname; frontend e backend vivem apenas na rede interna do Docker (`172.28.0.0/16`), alcançados como `frontend:8080` e `backend:8080`.

```text
Navegador ── HTTPS ──▶ Cloudflare ── HTTP :80 ──▶ Nginx ─┬─ slt18bbm.com.br      ──▶ frontend:8080
                                                         ├─ www.slt18bbm.com.br  ──▶ 301 para o apex
                                                         ├─ api.slt18bbm.com.br  ──▶ backend:8080
                                                         └─ qualquer outro host  ──▶ 444 (conexão descartada)
```

> **Limitação aceita do modo Flexible:** o trecho Cloudflare → EC2 trafega **sem criptografia**. Login e dados cruzam a internet em claro nesse trecho. O upgrade futuro é o modo **Full (Strict)** com certificado **Origin CA** do Cloudflare (gratuito, validade de 15 anos, sem renovação): basta adicionar um server block 443 no template apontando para o certificado e trocar o modo no painel. Não requer certbot.

## Arquivos

| Arquivo | Papel |
| --- | --- |
| [`nginx/nginx.conf`](../nginx/nginx.conf) | Config global: gzip, buffers, keep-alive, zonas de rate limit, access log JSON e **real_ip do Cloudflare** |
| [`nginx/templates/default.conf.template`](../nginx/templates/default.conf.template) | Server blocks. A imagem oficial do Nginx roda `envsubst` no boot e resolve `${FRONTEND_HOST}`/`${API_HOST}` a partir do `.env` — o mesmo template serve staging e production |
| [`nginx/snippets/proxy-headers.conf`](../nginx/snippets/proxy-headers.conf) | Headers repassados ao upstream (`X-Real-IP`, `X-Forwarded-For`, `X-Forwarded-Proto`, keep-alive HTTP/1.1) |

Decisões embutidas:

- **IP real do cliente:** atrás do Cloudflare, o `$remote_addr` seria o IP da borda deles — o rate limiting por IP puniria o Cloudflare (todos os usuários num mesmo balde). O `nginx.conf` usa o módulo `real_ip` com os ranges publicados em <https://www.cloudflare.com/ips/> e o header `CF-Connecting-IP`; a partir daí, logs, rate limit e o `X-Forwarded-For` enviado ao backend usam o IP verdadeiro do usuário. **Se o Cloudflare atualizar os ranges, o bloco `set_real_ip_from` precisa acompanhar.**
- **Backend .NET:** lê `X-Forwarded-For` via `ForwardedHeadersMiddleware` ([`Program.cs`](../backend/Program.cs)), ativado pelas envs `Proxy__TrustForwardedHeaders=true` + `Proxy__KnownNetworks__0=172.28.0.0/16` no compose.
- **`X-Forwarded-Proto`:** o Cloudflare envia `https`; o Nginx repassa o valor recebido (fallback para o esquema local), então o backend enxerga a requisição como HTTPS.
- **Headers de segurança:** HSTS, `X-Frame-Options`, `X-Content-Type-Options` e `Referrer-Policy` são emitidos pelo proxy; os equivalentes dos upstreams são suprimidos com `proxy_hide_header` para não duplicar. O HSTS chega ao navegador por HTTPS (via Cloudflare), portanto é honrado.
- **Rate limiting:** zona `api` (30 req/s por IP, burst 60 — folga para grades de fotos, que passam pela API) em todo o host da API, e zona `auth` (10 req/min por IP) exclusiva em `POST /api/auth/login`, alinhada ao rate limiter do backend.
- **Host desconhecido / acesso por IP:** `return 444` derruba a conexão sem resposta.
- **Logs:** `access.log` em JSON estruturado. Ex.: `docker compose exec proxy tail -f /var/log/nginx/access.log | jq .`

## Configuração no painel do Cloudflare

1. **DNS:** registros `A` de `slt18bbm.com.br`, `www`, `api` (e os de staging) apontando para o IP da EC2, todos com **proxy ativado** (nuvem laranja). No registro.br, os nameservers do domínio devem ser os do Cloudflare.
2. **SSL/TLS → Overview:** modo **Flexible**.
3. **SSL/TLS → Edge Certificates:** ative **Always Use HTTPS** (o redirect HTTP→HTTPS acontece na borda do Cloudflare — o Nginx da origem não redireciona, justamente para evitar o loop infinito clássico do modo Flexible).

## GitHub Variables (por environment)

| Variable | staging | production |
| --- | --- | --- |
| `FRONTEND_HOST` | `staging.slt18bbm.com.br` | `slt18bbm.com.br` |
| `API_HOST` | `api.staging.slt18bbm.com.br` | `api.slt18bbm.com.br` |
| `API_BASE_URL` (atualizar) | `https://api.staging.slt18bbm.com.br` | `https://api.slt18bbm.com.br` |
| `APP_ORIGIN` (atualizar) | `https://staging.slt18bbm.com.br` | `https://slt18bbm.com.br` |

O `API_BASE_URL` é embutido no build do frontend — rode o pipeline completo, não só o job de deploy.

## Security Group

Restrinja a porta 80 aos ranges do Cloudflare — sem isso, qualquer um acessa a EC2 direto pelo IP, contornando o HTTPS, o WAF e o rate limiting da borda:

1. **VPC → Managed Prefix Lists → Create prefix list:** nome `cloudflare-ipv4`, entradas de <https://www.cloudflare.com/ips-v4/> (15 ranges). Repita para IPv6 se a instância tiver.
2. No `sg-app`: regra inbound `80` com source = a prefix list (no lugar de `0.0.0.0/0`).
3. **Remova as regras `8080` e `443`** — nada mais escuta nelas.
4. Mantenha `22` restrita ao seu IP.

## Aplicação (runbook)

Nenhuma instalação na EC2 é necessária — sem certbot, sem certificado na origem.

1. Configure o Cloudflare (seção acima) e as GitHub Variables.
2. Dispare o pipeline (**Run workflow** em production; push em `develop` para staging). Ele copia `nginx/` junto com o compose, regenera o `.env` e roda `docker compose up -d`.
3. **Só no primeiro deploy:** a rede do compose ganhou subnet fixa, e o `up` pode falhar com "network needs to be recreated". Na EC2:

   ```bash
   cd ~/app
   docker compose -f docker-compose.prod.yml --env-file .env down
   docker compose -f docker-compose.prod.yml --env-file .env up -d --remove-orphans
   ```

   São poucos segundos de indisponibilidade (as imagens já foram baixadas pelo `pull` do pipeline). Deploys seguintes não precisam de intervenção.

## Verificação

```bash
curl -sI http://slt18bbm.com.br | head -3                  # 301 -> https (na borda do Cloudflare)
curl -sI https://www.slt18bbm.com.br | head -3             # 301 -> apex
curl -sI https://slt18bbm.com.br | grep -iE 'strict-transport|x-frame|server'
curl -s https://api.slt18bbm.com.br/api/items | head -1    # 401 JSON (esperado sem token)
curl -m 5 http://IP_DA_EC2/ -H "Host: slt18bbm.com.br"     # deve falhar (SG restrito ao Cloudflare)
for i in $(seq 1 15); do curl -s -o /dev/null -w '%{http_code} ' -X POST https://api.slt18bbm.com.br/api/auth/login; done   # deve terminar em 429
```

No app, após login, o log de auditoria do backend deve registrar o IP real do usuário — não `172.28.x.x` (rede interna) nem um IP do Cloudflare.
