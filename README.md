# healthspan-api

````
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Passw0rd" -p 1433:1433 --name sqlserver -d mcr.microsoft.com/mssql/server:latest 
docker run -e "MYSQL_ROOT_PASSWORD=Passw0rd" -p 3306:3306 --name mysql -d mysql:latest
````

TODOS:
- [ ] Return json with id on posts and puts
- [ ] Allow to add workout relation on post exercises
- https://barbend.com/how-to-use-rpe-scale-strength-training/

## Google Sign-In wiring

- Configure secrets via the flat keys `GoogleClientId`, `GoogleClientSecret`, `GoogleCallbackUri`, `GoogleDefaultReturnUrl`, and optional `GoogleAllowedReturnUrls` (array) in `appsettings.*`, user-secrets, or environment variables.
- `POST /auth/google/exchange` accepts `{ "idToken": "<Google ID token>" }`, validates with Google, provisions the user locally, then returns the same payload as `/login` (`tokenType`, `accessToken`, `expiresIn`, `refreshToken`).
- `GET /auth/google/challenge?returnUrl=` starts the PKCE authorization code flow. After Google redirects back to `/auth/google/callback` the API issues local tokens, then redirects to the sanitized return URL with `tokenType`, `accessToken`, `refreshToken`, `expiresIn` query parameters.
- `POST /refresh` (existing Identity endpoint) works for Google sessions because they use the same bearer-token issuance path as email/password accounts.
- Claim mapping: `/manage/info` still returns `email`/`isEmailConfirmed`, and the issued access tokens add `ClaimTypes.Name` + `picture` so the SPA can hydrate display name/photo from the same response cycle.
- Calling `/Logout` signs the current user out and attempts to revoke any stored Google refresh token (`https://oauth2.googleapis.com/revoke`) before clearing the local session.