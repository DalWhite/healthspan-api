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

## Phase 1 health analytics foundation

- New endpoints:
	- `GET /BloodTests`
	- `GET /BloodTests/pending-review`
	- `GET /BloodTests/{id}`
	- `POST /BloodTests/manual`
	- `POST /BloodTests/upload`
	- `POST /BloodTests/{id}/review`
	- `GET /BloodTests/markers/latest`
	- `GET /BloodTests/markers/{metricKey}/trends`
	- `GET /GalpinAssessments/catalog`
	- `GET /GalpinAssessments`
	- `GET /GalpinAssessments/latest`
	- `GET /GalpinAssessments/trends/{adaptation}`
	- `POST /GalpinAssessments`
	- `GET /Dashboard/summary`
	- Uploaded documents are archived to Azure Blob Storage when blob storage is enabled. If blob storage is disabled, the API falls back to filesystem storage under the configured base path or the App Service `HOME/data/blood-test-results` directory.
- Configure Azure OCR with flat settings: `AzureDocumentIntelligenceEnabled`, `AzureDocumentIntelligenceEndpoint`, `AzureDocumentIntelligenceApiKey`, and `AzureDocumentIntelligenceModelId`.
	- Configure Azure Blob Storage with flat settings: `AzureBlobStorageEnabled`, `AzureBlobStorageConnectionString`, and `AzureBlobStorageContainerName`.
	- Configure filesystem storage with flat settings: `FileSystemDocumentStorageEnabled` and optional `FileSystemDocumentStorageBasePath`.
	- Optional LLM post-processing can normalize OCR output before review using flat settings: `LlmMarkerNormalizationEnabled`, `LlmMarkerNormalizationProvider`, `LlmMarkerNormalizationApiKey`, `LlmMarkerNormalizationModel`, `LlmMarkerNormalizationBaseUrl`, `LlmMarkerNormalizationPromptVersion`, `LlmMarkerNormalizationSiteUrl`, `LlmMarkerNormalizationAppName`, `LlmMarkerNormalizationMaxOcrChars`, `LlmMarkerNormalizationMaxCandidateMarkers`, `LlmMarkerNormalizationChunkSize`, `LlmMarkerNormalizationTemperature`, `LlmMarkerNormalizationMaxOutputTokens`, `LlmMarkerNormalizationTimeoutSeconds`, `LlmMarkerNormalizationLogPayloads`, and `LlmMarkerNormalizationLogPayloadMaxChars`.
	- If a large normalization response is truncated, the OpenRouter provider automatically retries in chunks and recursively splits failing chunks into smaller batches.
	- OpenRouter is wired as the current provider behind `IMarkerNormalizationProvider`, so adding Ollama later only requires a new provider class plus one selector case.
	- Uploaded blood tests are persisted even when OCR is disabled, and remain in `PendingReview` for manual follow-up. OCR-extracted marker candidates are suggestions only and should be finalized through the review endpoint. If blob storage is disabled and filesystem storage is enabled, the original file is archived on the App Service file system instead.
	- When LLM normalization is enabled, deterministic OCR remains the fallback path and combined OCR + LLM audit data is stored in `RawExtractionPayload`.