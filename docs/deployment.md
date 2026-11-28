# Deployment (Dokploy)

Demo stack (Angular + BFF on synthetic data) deployed on Dokploy at `https://dokploy.yvadev.com` (#99).
**Dokploy never builds**: GitHub Actions builds, scans and publishes the image, Dokploy only pulls it.

```text
push main ──► release.yml: docker build ─► Trivy (blocking) ─► push GHCR ─► provenance attestation
                                                                    │
                                     deploy job (environment "production")
                                                                    │
                                    POST /api/compose.deploy ──► Dokploy pulls ghcr.io/...:main
```

- Image: `ghcr.io/yvahajatiana/counterparty-risk-bff`, tags `sha-<12 chars>` (immutable) and `main` (moving).
- Stack file: [`compose.dokploy.yaml`](../compose.dokploy.yaml) (image only, no `build:`, hardening of `compose.yaml` kept).
- Workflow: [`.github/workflows/release.yml`](../.github/workflows/release.yml), on push to `main` when the
  BFF, the web app or the image inputs change, or manually (`workflow_dispatch`).

## One-time setup

### Dokploy
1. **Registry** (only while the GHCR package is private): Settings → Registry → add `ghcr.io`, user
   `Yvahajatiana`, password = a fine-grained or classic PAT with `read:packages` **only**.
   Once the package is public (Package settings → Change visibility), no credential is needed.
2. **Compose service**: Project → Create Service → Compose.
   - Provider: GitHub, repo `counterparty-risk`, branch `main`, compose path `./compose.dokploy.yaml`.
   - **Autodeploy: off** (the release workflow triggers deployments after the scan).
   - Advanced → Isolated Deployments: on.
3. **Domains**: service `bff`, container port `8080`, HTTPS on, certificate Let's Encrypt.
4. **Environment**: empty by default. `BFF_IMAGE_TAG` is only set for a rollback (see below).
5. **API key**: Profile → API/CLI → generate a key, ideally from a dedicated Dokploy user restricted
   to this project.
6. Copy the compose ID (visible in the service URL, `.../compose/<composeId>`).

### GitHub
Settings → Environments → `production`:
- Secret `DOKPLOY_API_KEY`: the Dokploy API key.
- Variable `DOKPLOY_COMPOSE_ID`: the compose ID.
- Recommended: Deployment branches = `main` only; add yourself as required reviewer to approve each deploy.

## Day-to-day
- Merge to `main` → image published → Dokploy redeploys. Follow the build in Actions and the rollout in
  the Dokploy Deployments tab (the API call only queues the deployment).
- Manual redeploy: Actions → Release → Run workflow.

## Rollback
1. Find the previous good tag in the GHCR package page (`sha-<commit>`).
2. Dokploy → Environment: `BFF_IMAGE_TAG=sha-<commit>` → Deploy.
3. Fix forward on `main`, then remove `BFF_IMAGE_TAG` so the service follows `main` again.

## Verify an image
```bash
gh attestation verify oci://ghcr.io/yvahajatiana/counterparty-risk-bff:main --repo Yvahajatiana/counterparty-risk
```

## Security notes
- The demo has **no authentication** yet (Keycloak lands later): it is public once a domain is set.
  Until then restrict access (Traefik basic auth or IP allow-list middleware, or a VPN).
- Health endpoints are not exposed outside Development (BFF is the only public entry point).
- Secrets for the Api/PostgreSQL/Keycloak stages go in the Dokploy Environment tab, never in the repo.
