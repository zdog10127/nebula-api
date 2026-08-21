# Setup da infraestrutura AWS — Nébula (backend + frontend + voz/vídeo)

Guia pra criar do zero a infra que as pipelines (`backend/.github/workflows/deploy.yml` e
`frontend/.github/workflows/deploy-web.yml`) esperam encontrar, mais a EC2 que roda Redis/coturn/LiveKit
(essas três não cabem atrás do Elastic Beanstalk — precisam de portas UDP públicas diretas).

As três distribuições CloudFront abaixo dão HTTPS automático no domínio compartilhado `*.cloudfront.net`,
sem precisar de DNS nenhum — é o que backend (CloudFront #2) e a sinalização do LiveKit (CloudFront #3)
usam aqui. Pro frontend você já comprou `nebula-novacode.com` (via Cloudflare Registrar, DNS já fica lá) —
o passo 5b abaixo mostra como apontar ele pro CloudFront #1 em vez de usar o hostname feio. Backend e
LiveKit continuam em `*.cloudfront.net` por enquanto; dá pra dar subdomínio bonito pra eles depois
(`api.nebula-novacode.com`, `media.nebula-novacode.com`) do mesmo jeito, é opcional porque ninguém visita
essas URLs diretamente no navegador.

Nomenclatura (troque `nebula`/`zdog10127` se fizer sentido — é só find & replace):

| Recurso | Nome |
|---|---|
| Região | `sa-east-1` (São Paulo) |
| ECR | `nebula-backend` |
| Elastic Beanstalk (app / env) | `nebula-backend` / `nebula-backend-env` |
| S3 frontend | `nebula-frontend-zdog10127` — **nome de bucket é único em toda a AWS**, se `BucketAlreadyExists` ao criar, acrescente um sufixo e ajuste os comandos seguintes |
| S3 anexos | `nebula-attachments-zdog10127` — mesma ressalva de unicidade |
| EC2 mídia | `nebula-media` |

Pré-requisito: AWS CLI v2 configurado com credenciais de admin (`aws configure`) — só pra rodar esse setup
uma vez. As credenciais que as pipelines usam depois são outras, mais restritas (passo 1).

## 1. Usuários IAM

Dois usuários com escopos bem separados: um pra CI (deploy), outro pra runtime do backend (só acesso à
bucket de anexos — essas chaves vão virar env var no Elastic Beanstalk, não secrets do GitHub).

```bash
aws iam create-user --user-name nebula-ci
aws iam create-user --user-name nebula-app
```

**Política de `nebula-ci`** — salve como `nebula-ci-policy.json`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    { "Sid": "EcrAuth", "Effect": "Allow", "Action": "ecr:GetAuthorizationToken", "Resource": "*" },
    {
      "Sid": "EcrPushPull",
      "Effect": "Allow",
      "Action": [
        "ecr:CreateRepository", "ecr:DescribeRepositories", "ecr:BatchCheckLayerAvailability",
        "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage", "ecr:InitiateLayerUpload",
        "ecr:UploadLayerPart", "ecr:CompleteLayerUpload", "ecr:PutImage"
      ],
      "Resource": "arn:aws:ecr:sa-east-1:*:repository/nebula-backend"
    },
    {
      "Sid": "ElasticBeanstalkDeploy",
      "Effect": "Allow",
      "Action": [
        "elasticbeanstalk:CreateApplicationVersion", "elasticbeanstalk:DescribeApplicationVersions",
        "elasticbeanstalk:UpdateEnvironment", "elasticbeanstalk:DescribeEnvironments",
        "elasticbeanstalk:DescribeEnvironmentResources", "elasticbeanstalk:DescribeEvents",
        "elasticbeanstalk:DescribeConfigurationSettings", "elasticbeanstalk:ListPlatformVersions"
      ],
      "Resource": "*"
    },
    {
      "Sid": "ElasticBeanstalkVersionsBucket",
      "Effect": "Allow",
      "Action": ["s3:PutObject", "s3:GetObject", "s3:ListBucket"],
      "Resource": ["arn:aws:s3:::elasticbeanstalk-sa-east-1-*", "arn:aws:s3:::elasticbeanstalk-sa-east-1-*/*"]
    },
    {
      "Sid": "FrontendBucketDeploy",
      "Effect": "Allow",
      "Action": ["s3:PutObject", "s3:DeleteObject", "s3:ListBucket"],
      "Resource": ["arn:aws:s3:::nebula-frontend-zdog10127", "arn:aws:s3:::nebula-frontend-zdog10127/*"]
    },
    {
      "Sid": "CloudFrontInvalidate",
      "Effect": "Allow",
      "Action": ["cloudfront:CreateInvalidation", "cloudfront:GetInvalidation"],
      "Resource": "*"
    }
  ]
}
```

**Política de `nebula-app`** (o backend usa isso em runtime pra ler/escrever anexos — o código já cria a
bucket e a policy pública sozinho no startup via `S3StorageService.EnsureBucketExistsAsync`) — salve como
`nebula-app-policy.json`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AttachmentsBucket",
      "Effect": "Allow",
      "Action": [
        "s3:CreateBucket", "s3:PutBucketPolicy", "s3:PutObject", "s3:GetObject",
        "s3:HeadBucket", "s3:GetBucketLocation", "s3:ListBucket"
      ],
      "Resource": ["arn:aws:s3:::nebula-attachments-zdog10127", "arn:aws:s3:::nebula-attachments-zdog10127/*"]
    }
  ]
}
```

```bash
aws iam put-user-policy --user-name nebula-ci --policy-name nebula-ci-deploy --policy-document file://nebula-ci-policy.json
aws iam put-user-policy --user-name nebula-app --policy-name nebula-app-storage --policy-document file://nebula-app-policy.json

aws iam create-access-key --user-name nebula-ci   # guarde — vira secret do GitHub (passo 9)
aws iam create-access-key --user-name nebula-app  # guarde — vira env var S3_ACCESS_KEY/S3_SECRET_KEY do EB (passo 3)
```

## 2. ECR

Criado automaticamente pela pipeline no primeiro deploy, mas se quiser criar antes:

```bash
aws ecr create-repository --repository-name nebula-backend --region sa-east-1
```

## 3. Elastic Beanstalk (backend)

Pelo **console** (https://console.aws.amazon.com/elasticbeanstalk) na primeira vez — ele cria sozinho as
IAM roles de serviço (`aws-elasticbeanstalk-service-role`, `aws-elasticbeanstalk-ec2-role`) que via CLI
puro você teria que montar manualmente antes.

1. **Create Application** → `nebula-backend`.
2. **Platform**: Docker → "Docker running on 64bit Amazon Linux 2023" (versão mais recente listada).
3. **Application code**: "Sample application" (a pipeline substitui no primeiro deploy real).
4. **Configure more options** → **Environment name**: `nebula-backend-env` → **Domain**: tenta
   `nebula-backend` (se já estiver em uso, escolhe outro nome e ajusta os passos seguintes).
5. Cria e espera "Health: Ok" (uns 5 minutos).

Depois que o ambiente estiver de pé, **anote o Security Group da instância EC2 do EB** — você vai precisar
dele no passo 7 (Redis só pode ser acessado por essa SG, não pelo mundo todo):

```bash
aws elasticbeanstalk describe-environment-resources \
  --environment-name nebula-backend-env \
  --query "EnvironmentResources.Instances[0].Id" --output text
# com o instance-id em mãos:
aws ec2 describe-instances --instance-ids <INSTANCE_ID> \
  --query "Reservations[0].Instances[0].SecurityGroups[0].GroupId" --output text
```

Guarde esse `sg-xxxxxxxx` — é o `EB_SECURITY_GROUP_ID` do passo 7.

Configure as variáveis de ambiente da aplicação (isso fica só na configuração do ambiente, nunca vai pro
git — inclui os secrets do Mongo/JWT/LiveKit/S3):

```bash
aws elasticbeanstalk update-environment \
  --environment-name nebula-backend-env \
  --option-settings \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=ASPNETCORE_ENVIRONMENT,Value=Production \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=MONGODB_CONNECTION_STRING,Value="<sua connection string do Atlas>" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=MONGODB_DATABASE,Value=discordclone \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=REDIS_CONNECTION_STRING,Value="<IP_PRIVADO_DA_EC2_NEBULA_MEDIA>:6379" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=JWT_SECRET,Value="<gere um valor aleatorio com pelo menos 32 caracteres>" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=JWT_ISSUER,Value=nebula-prod \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=JWT_EXPIRATION_MINUTES,Value=15 \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=JWT_REFRESH_EXPIRATION_DAYS,Value=30 \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=CORS_ORIGINS,Value="http://localhost:5173,http://127.0.0.1:47823,https://<dominio-cloudfront-1>" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=LIVEKIT_API_KEY,Value=devkey \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=LIVEKIT_API_SECRET,Value="<mesmo valor usado no .env da EC2 nebula-media>" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=LIVEKIT_URL,Value="wss://<dominio-cloudfront-3>" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=S3_ENDPOINT,Value="https://s3.sa-east-1.amazonaws.com" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=S3_PUBLIC_ENDPOINT,Value="https://s3.sa-east-1.amazonaws.com" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=S3_ACCESS_KEY,Value="<access key do usuario nebula-app>" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=S3_SECRET_KEY,Value="<secret key do usuario nebula-app>" \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=S3_BUCKET,Value=nebula-attachments-zdog10127 \
    Namespace=aws:elasticbeanstalk:application:environment,OptionName=TENOR_API_KEY,Value="" \
  --region sa-east-1
```

Os valores entre `<>` que dependem de recursos criados mais adiante (CloudFront #1/#3, EC2) — dá pra rodar
esse comando de novo depois só com o `OptionName` que mudou, não precisa repetir tudo.

O host do ambiente (pro passo 4) você vê na aba do ambiente no console, ou:

```bash
aws elasticbeanstalk describe-environments \
  --environment-names nebula-backend-env \
  --query "Environments[0].CNAME" --output text
```

## 4. CloudFront #2 — HTTPS na frente do backend (API + SignalR)

Salve como `cloudfront-backend-config.json` (troque `DomainName` pelo CNAME do passo 3):

```json
{
  "CallerReference": "nebula-backend-setup-2026",
  "Comment": "Nebula backend API + SignalR",
  "Enabled": true,
  "Origins": {
    "Quantity": 1,
    "Items": [
      {
        "Id": "EB-nebula-backend",
        "DomainName": "nebula-backend-env.xxxxxxx.sa-east-1.elasticbeanstalk.com",
        "CustomOriginConfig": {
          "HTTPPort": 80,
          "HTTPSPort": 443,
          "OriginProtocolPolicy": "http-only",
          "OriginSslProtocols": { "Quantity": 1, "Items": ["TLSv1.2"] },
          "OriginReadTimeout": 60,
          "OriginKeepaliveTimeout": 60
        }
      }
    ]
  },
  "DefaultCacheBehavior": {
    "TargetOriginId": "EB-nebula-backend",
    "ViewerProtocolPolicy": "redirect-to-https",
    "AllowedMethods": {
      "Quantity": 7,
      "Items": ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"],
      "CachedMethods": { "Quantity": 2, "Items": ["GET", "HEAD"] }
    },
    "ForwardedValues": {
      "QueryString": true,
      "Cookies": { "Forward": "all" },
      "Headers": { "Quantity": 1, "Items": ["*"] }
    },
    "MinTTL": 0,
    "DefaultTTL": 0,
    "MaxTTL": 0,
    "Compress": true
  }
}
```

```bash
aws cloudfront create-distribution --distribution-config file://cloudfront-backend-config.json
```

Guarde o `DomainName` retornado (ex: `d1111abcd.cloudfront.net`) — é o `<dominio-cloudfront-2>` usado no
`VITE_API_URL` do frontend (`https://d1111abcd.cloudfront.net/api`) e no `CORS_ORIGINS`... espera, não: o
CORS_ORIGINS do backend lista a origem do **frontend**, não a dele mesmo — o valor do backend usado no
CORS_ORIGINS é o domínio do CloudFront #1 (frontend), configurado no passo 5. Cache desativado (TTL 0) —
não precisa invalidar essa distribuição a cada deploy.

> SignalR usa `.withAutomaticReconnect()` no frontend (`ChatHubContext.tsx`) — se o CloudFront ocasionalmente
> derrubar uma conexão WebSocket ociosa por timeout, o cliente reconecta sozinho. Não deve ser perceptível,
> mas é a mitigação caso aconteça.

## 5. Frontend — S3 + CloudFront #1

```bash
aws s3 mb s3://nebula-frontend-zdog10127 --region sa-east-1

aws s3 website s3://nebula-frontend-zdog10127/ \
  --index-document index.html --error-document index.html

aws s3api put-public-access-block \
  --bucket nebula-frontend-zdog10127 \
  --public-access-block-configuration BlockPublicAcls=false,IgnorePublicAcls=false,BlockPublicPolicy=false,RestrictPublicBuckets=false
```

Salve como `bucket-policy-frontend.json`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "PublicReadGetObject",
      "Effect": "Allow",
      "Principal": "*",
      "Action": "s3:GetObject",
      "Resource": "arn:aws:s3:::nebula-frontend-zdog10127/*"
    }
  ]
}
```

```bash
aws s3api put-bucket-policy --bucket nebula-frontend-zdog10127 --policy file://bucket-policy-frontend.json
```

Salve como `cloudfront-frontend-config.json`:

```json
{
  "CallerReference": "nebula-frontend-setup-2026",
  "Comment": "Nebula frontend web",
  "Enabled": true,
  "DefaultRootObject": "index.html",
  "Origins": {
    "Quantity": 1,
    "Items": [
      {
        "Id": "S3-nebula-frontend",
        "DomainName": "nebula-frontend-zdog10127.s3-website-sa-east-1.amazonaws.com",
        "CustomOriginConfig": {
          "HTTPPort": 80,
          "HTTPSPort": 443,
          "OriginProtocolPolicy": "http-only",
          "OriginSslProtocols": { "Quantity": 1, "Items": ["TLSv1.2"] }
        }
      }
    ]
  },
  "DefaultCacheBehavior": {
    "TargetOriginId": "S3-nebula-frontend",
    "ViewerProtocolPolicy": "redirect-to-https",
    "AllowedMethods": {
      "Quantity": 2,
      "Items": ["GET", "HEAD"],
      "CachedMethods": { "Quantity": 2, "Items": ["GET", "HEAD"] }
    },
    "ForwardedValues": { "QueryString": false, "Cookies": { "Forward": "none" } },
    "MinTTL": 0,
    "DefaultTTL": 86400,
    "MaxTTL": 31536000,
    "Compress": true
  },
  "CustomErrorResponses": {
    "Quantity": 2,
    "Items": [
      { "ErrorCode": 403, "ResponseCode": "200", "ResponsePagePath": "/index.html", "ErrorCachingMinTTL": 10 },
      { "ErrorCode": 404, "ResponseCode": "200", "ResponsePagePath": "/index.html", "ErrorCachingMinTTL": 10 }
    ]
  }
}
```

```bash
aws cloudfront create-distribution --distribution-config file://cloudfront-frontend-config.json
```

`CustomErrorResponses` faz o CloudFront devolver `index.html` pra qualquer rota que o S3 não reconheça
como arquivo — necessário porque o app usa client-side routing (React Router). Guarde o `Id` (é o
`CLOUDFRONT_DISTRIBUTION_ID` do passo 9) e o `DomainName` (é o `<dominio-cloudfront-1>` usado no
`CORS_ORIGINS` do backend, passo 3) — a menos que você já vá direto pro passo 5b, aí o que entra no
`CORS_ORIGINS` é o domínio customizado (`https://nebula-novacode.com`), não o `.cloudfront.net`.

## 5b. Apontar `nebula-novacode.com` pro CloudFront #1

CloudFront só aceita certificado ACM emitido em **us-east-1**, mesmo a distribuição/app rodando em
`sa-east-1` — é uma exigência do próprio serviço, não erro de digitação.

```bash
# 1. Pede o certificado (inclui o www também, opcional mas recomendado)
aws acm request-certificate \
  --domain-name nebula-novacode.com \
  --subject-alternative-names www.nebula-novacode.com \
  --validation-method DNS \
  --region us-east-1 \
  --query "CertificateArn" --output text
# guarde o ARN retornado

# 2. Pega o registro de validação DNS que a ACM pede
aws acm describe-certificate --certificate-arn <ARN> --region us-east-1 \
  --query "Certificate.DomainValidationOptions[].ResourceRecord" --output table
```

Isso devolve um ou dois registros tipo `Name: _abc123.nebula-novacode.com`, `Type: CNAME`,
`Value: _xyz456.acm-validations.aws.` — adicione cada um no **DNS da Cloudflare** (dashboard do domínio →
DNS → Add record → tipo `CNAME`, deixe **Proxy status = DNS only** (nuvem cinza, não laranja) — se ficar
proxiado a validação da ACM não completa. Depois de adicionar, espere a emissão:

```bash
aws acm wait certificate-validated --certificate-arn <ARN> --region us-east-1
echo "certificado emitido"
```

Agora, **antes de criar** a distribuição do passo 5 (ou depois, com `update-distribution` se já criou),
acrescente no `cloudfront-frontend-config.json` os campos `Aliases` e `ViewerCertificate`:

```json
{
  "Aliases": { "Quantity": 1, "Items": ["nebula-novacode.com"] },
  "ViewerCertificate": {
    "ACMCertificateArn": "<ARN do certificado>",
    "SSLSupportMethod": "sni-only",
    "MinimumProtocolVersion": "TLSv1.2_2021"
  }
}
```

(Mescle esses dois campos no nível raiz do JSON do passo 5, junto de `CallerReference`/`Origins`/etc — não
é um arquivo separado.) Se a distribuição já existe, o fluxo de update é: `aws cloudfront
get-distribution-config --id <DIST_ID>` → editar o JSON retornado (adicionar `Aliases`/`ViewerCertificate`,
manter o `ETag`) → `aws cloudfront update-distribution --id <DIST_ID> --distribution-config file://novo.json
--if-match <ETag>`.

Por fim, o registro final na Cloudflare — DNS → Add record:

| Tipo | Nome | Valor | Proxy status |
|---|---|---|---|
| `CNAME` | `nebula-novacode.com` (raiz) | `<DomainName da distribuição, ex: d1111abcd.cloudfront.net>` | **DNS only** (nuvem cinza) |

A Cloudflare "achata" (flattening) CNAME na raiz do domínio automaticamente — não precisa de registro `A`
manual. Deixe **DNS only** mesmo (não proxiado): o CloudFront já faz o papel de CDN/TLS aqui, colocar o
proxy da Cloudflare na frente também só adicionaria uma camada redundante e pode complicar o
`CustomErrorResponses` (SPA fallback) e headers.

Depois de propagar (minutos, às vezes até ~1h), `https://nebula-novacode.com` deve servir o frontend
direto, com cadeado válido. Atualize o `CORS_ORIGINS` do backend (passo 3) pra incluir
`https://nebula-novacode.com` (e `https://www.nebula-novacode.com` se for usar o www também).

## 6. Anexos — S3 (substitui o MinIO)

```bash
aws s3 mb s3://nebula-attachments-zdog10127 --region sa-east-1

aws s3api put-public-access-block \
  --bucket nebula-attachments-zdog10127 \
  --public-access-block-configuration BlockPublicAcls=false,IgnorePublicAcls=false,BlockPublicPolicy=false,RestrictPublicBuckets=false
```

Não precisa criar a policy de leitura pública manualmente — o próprio backend faz isso sozinho no startup
(`S3StorageService.EnsureBucketExistsAsync`), desde que `nebula-app` tenha `s3:PutBucketPolicy` (já tem,
passo 1) e o Public Access Block esteja desligado (comando acima).

## 7. EC2 `nebula-media` — Redis + coturn + LiveKit

```bash
# Elastic IP — sem isso o IP público muda se a instância for reiniciada, e
# quebra o CloudFront #3 (que aponta pro DNS público derivado desse IP).
aws ec2 allocate-address --domain vpc --query "AllocationId" --output text
# guarde o AllocationId retornado

# Security group — troque vpc-xxxx pela VPC default (aws ec2 describe-vpcs
# --filters Name=isDefault,Values=true --query "Vpcs[0].VpcId")
aws ec2 create-security-group \
  --group-name nebula-media-sg \
  --description "Nebula Redis/coturn/LiveKit" \
  --vpc-id <VPC_ID> --query "GroupId" --output text
# guarde o GroupId retornado (SG_ID)

# Redis: só a SG do backend (Elastic Beanstalk) acessa — nunca 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id <SG_ID> \
  --protocol tcp --port 6379 --source-group <EB_SECURITY_GROUP_ID>

# coturn — precisa ser público
aws ec2 authorize-security-group-ingress --group-id <SG_ID> --protocol udp --port 3478 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id <SG_ID> --protocol tcp --port 3478 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id <SG_ID> --protocol udp --port 49152-49300 --cidr 0.0.0.0/0

# LiveKit — sinalização (proxiada pelo CloudFront #3, mas a porta em si
# também fica aberta) + mídia direta (essa não passa pelo CloudFront)
aws ec2 authorize-security-group-ingress --group-id <SG_ID> --protocol tcp --port 7880 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id <SG_ID> --protocol tcp --port 7881 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id <SG_ID> --protocol udp --port 50000-50100 --cidr 0.0.0.0/0

# SSH (opcional, restrinja ao seu IP em vez de 0.0.0.0/0 se puder)
aws ec2 authorize-security-group-ingress --group-id <SG_ID> --protocol tcp --port 22 --cidr <SEU_IP>/32

# Instância — t3.small é um ponto de partida razoável; suba se as chamadas de
# voz/vídeo engasgarem com mais gente conectada ao mesmo tempo
aws ec2 run-instances \
  --image-id <AMI_AMAZON_LINUX_2023_MAIS_RECENTE> \
  --instance-type t3.small \
  --key-name <SEU_KEY_PAIR> \
  --security-group-ids <SG_ID> \
  --query "Instances[0].InstanceId" --output text
# guarde o InstanceId

aws ec2 associate-address --instance-id <INSTANCE_ID> --allocation-id <ALLOCATION_ID>

aws ec2 describe-instances --instance-ids <INSTANCE_ID> \
  --query "Reservations[0].Instances[0].[PublicIpAddress,PublicDnsName,PrivateIpAddress]" --output table
```

Guarde os três valores: `PublicIpAddress` (vai no `external-ip` do `coturn.prod.conf`), `PublicDnsName`
(vai no `DomainName` do CloudFront #3, passo 8 — CloudFront exige um hostname, não aceita IP puro) e
`PrivateIpAddress` (vai no `REDIS_CONNECTION_STRING` do backend, passo 3 — o backend fala com o Redis pela
rede interna da VPC, não precisa expor isso publicamente).

Conecte na instância (`ssh -i <sua-chave>.pem ec2-user@<PublicIpAddress>`), instale o Docker, e copie pra
lá `infra/docker-compose.media.yml`, `infra/coturn.prod.conf` e `infra/livekit.prod.yaml` deste
repositório:

```bash
# na EC2
sudo dnf install -y docker
sudo systemctl enable --now docker
sudo usermod -aG docker ec2-user   # depois desloga/loga de novo pra valer

# copie os 3 arquivos (scp da sua máquina, ou git clone se preferir manter
# esses arquivos versionados num repo à parte)

# edite coturn.prod.conf: troque CHANGE_ME_USER/CHANGE_ME_PASSWORD por
# credenciais reais, e CHANGE_ME_PUBLIC_IP pelo PublicIpAddress acima

# crie um .env do lado do docker-compose.media.yml
echo "LIVEKIT_API_SECRET=<mesmo valor usado no LIVEKIT_API_SECRET do backend>" > .env

docker compose -f docker-compose.media.yml up -d
docker compose -f docker-compose.media.yml ps   # confirma os 3 containers "healthy"/"running"
```

## 8. CloudFront #3 — HTTPS na frente do sinal do LiveKit

Salve como `cloudfront-livekit-config.json` (troque `DomainName` pelo `PublicDnsName` do passo 7):

```json
{
  "CallerReference": "nebula-livekit-setup-2026",
  "Comment": "Nebula LiveKit signaling (WSS)",
  "Enabled": true,
  "Origins": {
    "Quantity": 1,
    "Items": [
      {
        "Id": "EC2-nebula-livekit",
        "DomainName": "ec2-x-x-x-x.sa-east-1.compute.amazonaws.com",
        "CustomOriginConfig": {
          "HTTPPort": 7880,
          "HTTPSPort": 443,
          "OriginProtocolPolicy": "http-only",
          "OriginSslProtocols": { "Quantity": 1, "Items": ["TLSv1.2"] }
        }
      }
    ]
  },
  "DefaultCacheBehavior": {
    "TargetOriginId": "EC2-nebula-livekit",
    "ViewerProtocolPolicy": "redirect-to-https",
    "AllowedMethods": {
      "Quantity": 7,
      "Items": ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"],
      "CachedMethods": { "Quantity": 2, "Items": ["GET", "HEAD"] }
    },
    "ForwardedValues": {
      "QueryString": true,
      "Cookies": { "Forward": "all" },
      "Headers": { "Quantity": 1, "Items": ["*"] }
    },
    "MinTTL": 0,
    "DefaultTTL": 0,
    "MaxTTL": 0
  }
}
```

```bash
aws cloudfront create-distribution --distribution-config file://cloudfront-livekit-config.json
```

Guarde o `DomainName` retornado — é o `<dominio-cloudfront-3>` do `LIVEKIT_URL` do backend (passo 3):
`wss://<dominio-cloudfront-3>` (`wss://`, não `https://` — é o mesmo host, só o esquema que muda pra
WebSocket seguro). A mídia (áudio/vídeo em si, WebRTC/UDP) **não** passa pelo CloudFront — vai direto do
navegador pro IP público da EC2 nas portas abertas no passo 7; o CloudFront aqui só carrega o canal de
sinalização (que troca metadados/tokens antes da chamada começar).

## 9. Secrets e Variables no GitHub

Nos dois repositórios (`nebula-api` e `nebula`), em **Settings → Secrets and variables → Actions**:

**Secrets** (iguais nos dois repos — do usuário `nebula-ci`, passo 1):

| Nome | Valor |
|---|---|
| `AWS_ACCESS_KEY_ID` | access key do `nebula-ci` |
| `AWS_SECRET_ACCESS_KEY` | secret key do `nebula-ci` |

**Variables** — `nebula-api` (backend):

| Nome | Valor |
|---|---|
| `AWS_REGION` | `sa-east-1` |
| `EB_APPLICATION_NAME` | `nebula-backend` |
| `EB_ENVIRONMENT_NAME` | `nebula-backend-env` |

**Variables** — `nebula` (frontend):

| Nome | Valor |
|---|---|
| `AWS_REGION` | `sa-east-1` |
| `S3_BUCKET_NAME` | `nebula-frontend-zdog10127` |
| `CLOUDFRONT_DISTRIBUTION_ID` | `Id` do CloudFront #1 (passo 5) |

Via `gh` CLI, dentro de cada repo:

```bash
gh secret set AWS_ACCESS_KEY_ID --body "<access-key-id>"
gh secret set AWS_SECRET_ACCESS_KEY --body "<secret-access-key>"

# no repo nebula-api
gh variable set AWS_REGION --body "sa-east-1"
gh variable set EB_APPLICATION_NAME --body "nebula-backend"
gh variable set EB_ENVIRONMENT_NAME --body "nebula-backend-env"

# no repo nebula
gh variable set AWS_REGION --body "sa-east-1"
gh variable set S3_BUCKET_NAME --body "nebula-frontend-zdog10127"
gh variable set CLOUDFRONT_DISTRIBUTION_ID --body "<id-da-distribuicao-1>"
```

## 10. Primeiro deploy

Antes de dar push: confirme que o `VITE_API_URL` em `frontend/.env.production` já foi trocado do
placeholder pro domínio real do CloudFront #2 (passo 4), e commite isso.

```bash
git push origin master   # nos dois repos (nebula-api e nebula)
```

Os workflows disparam sozinhos (`push` na `master`). Acompanhe na aba **Actions** de cada repo — o backend
demora mais (build Docker + deploy EB, uns 3-5 min), o frontend é rápido (~1-2 min). Os dois também têm
`workflow_dispatch:` pra disparar manualmente sem push.

## Verificação

- **Backend**: `curl https://<dominio-cloudfront-2>/api/health` (ou a rota de health check que existir)
  depois do primeiro deploy — confirma que o CloudFront está proxiando certo pro EB.
- **Frontend**: abrir `https://<dominio-cloudfront-1>`, logar, e checar no console do navegador que não
  tem erro de mixed content nem de conexão recusada no hub do SignalR.
- **Voz/vídeo**: entrar num canal de voz de verdade com duas contas (idealmente em redes diferentes, tipo
  uma no wifi e outra no 4G) e confirmar áudio nos dois sentidos — isso valida coturn (NAT traversal) e o
  canal de sinalização via CloudFront #3 juntos. Se conectar só entre duas abas na mesma rede local, pode
  mascarar um problema de NAT traversal que só aparece com redes diferentes.
- **Redis**: no log do ambiente EB (console → Logs → Request Logs, ou `eb logs` se tiver o EB CLI),
  confirmar que não há erro de conexão com o Redis na subida do backend. E checar
  `aws ec2 describe-security-groups --group-ids <SG_ID>` que a porta 6379 só libera o
  `EB_SECURITY_GROUP_ID`, nunca `0.0.0.0/0`.

## Pendências conhecidas

- **coturn vs. STUN**: o `livekit.prod.yaml` usa `use_external_ip: true` + STUN público do Google pra NAT
  traversal — resolve a maioria dos casos. O coturn fica de pé mas ainda não está formalmente integrado
  como TURN do LiveKit (o `turn.enabled` continua `false` no config do LiveKit); ele cobre o cenário mais
  raro de rede muito restritiva (NAT simétrico, firewall corporativo) só se o cliente tentar usá-lo
  diretamente. Vale testar numa rede assim antes de considerar 100% resolvido — não dá pra simular isso
  sem testar de verdade.
- **Domínio**: o frontend já usa `nebula-novacode.com` (passo 5b). Backend (CloudFront #2) e LiveKit
  (CloudFront #3) continuam em `*.cloudfront.net` por enquanto — dá pra dar subdomínio bonito
  (`api.nebula-novacode.com`, `media.nebula-novacode.com`) repetindo o mesmo processo do passo 5b quando
  quiser, sem recriar nada. O IP da EC2 do LiveKit continua precisando do Elastic IP (passo 7) pra não
  quebrar o CloudFront #3 se a instância reiniciar, independente de domínio.
- **Escala do backend**: o ambiente Elastic Beanstalk aqui é single-instance (sem load balancer). Se
  precisar de mais de uma instância depois, o Redis já está pronto pra isso (é o backplane do SignalR),
  mas precisa migrar o ambiente EB pra "load balanced" — mudança de configuração, não de código.
