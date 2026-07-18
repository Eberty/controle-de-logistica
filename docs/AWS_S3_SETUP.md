# Guia de Configuração — Amazon S3 para Imagens

O backend já possui suporte nativo a S3 ([S3PhotoStorage.cs](../backend/Services/S3PhotoStorage.cs)), ativado via `Storage__Provider=S3`. Este guia cobre a criação do bucket, políticas de acesso, CORS e o usuário IAM usado pelo backend.

## Arquitetura de acesso às imagens

**Ponto crítico:** nesta aplicação o navegador **nunca acessa o S3 diretamente**. O frontend carrega imagens via API (`GET /api/items/{id}/photo`) e o backend faz o streaming do objeto do S3 usando credenciais IAM. Consequências:

1. O bucket pode (e deve) permanecer **100% privado** — Block Public Access totalmente ativado.
2. **Não é necessária** Bucket Policy pública baseada em `aws:Referer`, nem configuração de CORS no bucket.
3. A restrição "imagens só acessíveis pela aplicação" é garantida de forma **muito mais forte** que por `Referer`: o único caminho até o objeto é a API, que já valida autenticação. O header `Referer` é trivialmente falsificável (`curl -H "Referer: https://slt18bbm.com.br/"`) — a própria documentação da AWS desaconselha usá-lo como mecanismo primário de segurança.

O modelo recomendado (proxy via backend) é a **Opção A** abaixo. A **Opção B** (acesso direto por URL pública + firewall de `Referer`), solicitada como alternativa, está documentada ao final com seus riscos.

---

## Opção A (recomendada) — Bucket privado, acesso só via backend

### 1. Criar o bucket

Console AWS → **S3 → Create bucket**:

| Campo | Valor |
| --- | --- |
| Bucket name | `slt18bbm-app-images` (nomes de bucket são globais e imutáveis) |
| Region | a mesma da EC2/RDS (ex.: `us-east-2`) |
| Object Ownership | **ACLs disabled (Bucket owner enforced)** |
| Block Public Access | **Block *all* public access ✅ (as 4 opções marcadas)** |
| Bucket Versioning | Opcional (recomendado: Enabled, protege contra deleção acidental) |
| Default encryption | SSE-S3 (padrão) |

Nenhuma Bucket Policy e nenhum CORS são necessários nesta opção.

Para separar ambientes, use um bucket por ambiente (`slt18bbm-app-images-staging` / `slt18bbm-app-images-prod`) **ou** um único bucket com prefixos, configurando a GitHub Variable `AWS_S3_KEY_PREFIX` (`staging` / `production`) — o backend já aplica o prefixo via `Storage__S3__KeyPrefix`.

### 2. Criar a política IAM do backend

Console AWS → **IAM → Policies → Create policy → JSON**:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "BackendObjectAccess",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:DeleteObject"
      ],
      "Resource": "arn:aws:s3:::slt18bbm-app-images/*"
    },
    {
      "Sid": "BackendBucketList",
      "Effect": "Allow",
      "Action": "s3:ListBucket",
      "Resource": "arn:aws:s3:::slt18bbm-app-images"
    }
  ]
}
```

Nome sugerido: `slt18bbm-backend-s3-images`.

Por que mais que `PutObject`/`DeleteObject`:

- **`s3:GetObject`** — o backend lê o objeto para servir a imagem ao frontend (`OpenReadAsync`) e é a permissão de origem exigida pelo `CopyObject` (usado ao promover fotos temporárias).
- **`s3:ListBucket`** — sem ela, um `GetObject` de chave inexistente retorna `403 AccessDenied` em vez de `404 NoSuchKey`; o tratamento de "foto não encontrada" do `S3PhotoStorage` depende do 404.

### 3. Criar o usuário IAM

Console AWS → **IAM → Users → Create user**:

1. Nome: `slt18bbm-backend` — **não** marque "Provide user access to the AWS Management Console".
2. Permissions: **Attach policies directly** → selecione `slt18bbm-backend-s3-images`.
3. Após criar: **Security credentials → Create access key → Application running outside AWS** (ou "Other").
4. Copie `Access key ID` e `Secret access key` — o secret só é exibido uma vez. Guarde-os direto nas GitHub Secrets (`AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY`).

> **Evolução recomendada:** como o backend roda em EC2, o ideal é anexar uma **IAM Role à instância** (Instance Profile) com essa mesma política e remover as chaves estáticas. O código já suporta isso: se `Storage__S3__AccessKey` ficar vazio, o SDK usa a cadeia de credenciais padrão (que inclui o metadata da instância). Basta deixar `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY` vazios no `.env`.

### 4. GitHub Secrets/Variables

Em **Settings → Environments** (`staging` e `production`), adicionar:

| Tipo | Nome | Exemplo |
| --- | --- | --- |
| Secret | `AWS_ACCESS_KEY_ID` | `AKIA...` |
| Secret | `AWS_SECRET_ACCESS_KEY` | `wJalr...` |
| Secret | `AWS_REGION` | `us-east-2` |
| Secret | `AWS_S3_BUCKET_NAME` | `slt18bbm-app-images` |
| Variable (opcional) | `AWS_S3_KEY_PREFIX` | `staging` / `production` |

Os workflows de deploy injetam esses valores no `.env` da EC2, e o `docker-compose.prod.yml` os repassa ao backend como `Storage__S3__*`.

---

## Opção B (alternativa) — Leitura pública restrita por `aws:Referer`

Use apenas se o frontend passar a montar `<img src="https://slt18bbm-app-images.s3...">` apontando direto para o bucket. **Riscos:** o `Referer` é falsificável por qualquer cliente HTTP (a proteção vale só contra hotlinking casual de outros sites, não contra download direto deliberado) e exige liberar parcialmente o Block Public Access.

### 1. Ajustar o Block Public Access

No bucket → **Permissions → Block public access → Edit**: desmarque `Block public access to buckets and objects granted through new public bucket policies` e `Block public and cross-account access...` (as duas de *policies*). Mantenha as duas de **ACLs** marcadas.

### 2. Bucket Policy com `aws:Referer`

**Permissions → Bucket policy**:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AllowGetFromOfficialDomainOnly",
      "Effect": "Allow",
      "Principal": "*",
      "Action": "s3:GetObject",
      "Resource": "arn:aws:s3:::slt18bbm-app-images/*",
      "Condition": {
        "StringLike": {
          "aws:Referer": [
            "https://slt18bbm.com.br/*",
            "https://www.slt18bbm.com.br/*"
          ]
        }
      }
    }
  ]
}
```

**Não adicione** um statement `Deny` com `StringNotLike` no `Referer`: o SDK da AWS não envia `Referer`, então um Deny explícito **bloquearia o próprio backend** (Deny explícito vence qualquer Allow do IAM). Sem o Deny, o comportamento é o desejado: requisições sem `Referer` válido caem no *default deny* (bucket privado), e o backend continua acessando via IAM.

Se quiser um Deny explícito mesmo assim, exclua o principal do backend:

```json
{
  "Sid": "DenyHotlinking",
  "Effect": "Deny",
  "Principal": "*",
  "Action": "s3:GetObject",
  "Resource": "arn:aws:s3:::slt18bbm-app-images/*",
  "Condition": {
    "StringNotLike": {
      "aws:Referer": ["https://slt18bbm.com.br/*", "https://www.slt18bbm.com.br/*"]
    },
    "ArnNotEquals": {
      "aws:PrincipalArn": "arn:aws:iam::SEU_ACCOUNT_ID:user/slt18bbm-backend"
    }
  }
}
```

### 3. CORS do bucket

Só é necessário se o navegador fizer `fetch`/`XMLHttpRequest` ou upload direto (presigned PUT) contra o S3 — tags `<img>` simples não exigem CORS. **Permissions → Cross-origin resource sharing (CORS)**:

```json
[
  {
    "AllowedHeaders": ["*"],
    "AllowedMethods": ["GET", "PUT"],
    "AllowedOrigins": [
      "https://slt18bbm.com.br",
      "https://www.slt18bbm.com.br"
    ],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 3000
  }
]
```

### 4. Frontend — garantir o envio do `Referer`

A política padrão dos navegadores (`strict-origin-when-cross-origin`) envia apenas a origem (`https://slt18bbm.com.br/`) em requisições cross-origin — o que **casa** com o padrão `https://slt18bbm.com.br/*`. Para não depender do padrão do navegador:

```html
<img src="https://slt18bbm-app-images.s3.us-east-2.amazonaws.com/foto.jpg"
     referrerpolicy="strict-origin-when-cross-origin" />
```

Nunca use `referrerpolicy="no-referrer"` nem meta tag global `no-referrer` — o S3 negaria todas as imagens.

### Alternativa robusta à Opção B

Se a necessidade real for servir imagens públicas em escala sem passar pelo backend, o padrão de mercado é **CloudFront + Origin Access Control (OAC)**: o bucket continua 100% privado, o CloudFront assina as requisições à origem, e o hotlinking é bloqueado por WAF/comportamento de cache — sem depender de `Referer` e sem abrir o Block Public Access. Presigned URLs geradas pelo backend são outra opção (URL expira, sem política pública).

---

## Checklist de verificação

1. `aws s3 ls s3://slt18bbm-app-images` com as credenciais do usuário → deve falhar apenas se `ListBucket` não tiver sido incluída; com a política acima, funciona.
2. Suba a stack, cadastre um item com foto e confirme o objeto no console S3.
3. Acesse a URL pública do objeto num navegador anônimo → deve retornar `403 AccessDenied` (Opção A) ou só funcionar via aplicação (Opção B).
4. Delete o item e confirme que o objeto sumiu do bucket.
