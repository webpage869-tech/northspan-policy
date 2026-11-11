# Northspan AI Policy

The web app behind policy.northspanltd.com: create a tailored AI acceptable-use policy, or check an existing one against ISO/IEC 42001 and the NIST AI RMF.

**Soft launch:** the policy builder preview and the free AI check are live. Buy buttons collect launch sign-ups. Checkout is switched on later with `Checkout__Enabled=true` once the payment link is ready.

## How it works
- ASP.NET Core (.NET 9) with a static front end in `src/NorthspanPolicy/wwwroot`.
- `POST /api/check` reads an uploaded .docx/.txt (or pasted text) **in memory**, sends it to the Claude API (Haiku) with the 15-item checklist, and returns a score plus the first two gaps. Uploads are never written to disk or the database.
- `POST /api/waitlist` stores launch sign-ups in SQLite (Docker volume `policy-data`).
- Limits: 5 checks per IP per hour, 200 checks per day overall (`Checks__DailyLimit`), 2 MB uploads.
- The checklist in `Checklist.cs` must stay in step with Appendix C of the master policy template.

## First-time server setup (Owner)
1. DNS: at Squarespace add an **A** record, host `policy`, pointing to the server's IP.
2. On the server:
   ```bash
   sudo mkdir -p /opt/northspan-policy && sudo chown $USER /opt/northspan-policy
   cd /opt/northspan-policy
   nano .env          # add the line ANTHROPIC_API_KEY=... (see .env.example)
   chmod 600 .env
   ```
3. Nginx: copy `deploy/nginx-policy.conf` to `/etc/nginx/sites-available/policy.northspanltd.com`, enable it, reload, then run `sudo certbot --nginx -d policy.northspanltd.com`.
4. GitHub repo settings:
   - Secrets: `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY` (the same server and key used for Vault deploys work).
   - Variable: `DEPLOY_ENABLED` = `true` (until this is set, pushes only build and test).

After that, every push to `main` builds, runs the tests, copies the code to `/opt/northspan-policy`, runs `deploy/deploy.sh`, and checks `/health`.

## Never commit
`.env`, the API key, databases, or business documents. `.gitignore` covers these.
