# Project Context Checkpoint

Last updated: 2026-09-18

## Purpose

This file is a resumable engineering checkpoint for work on Cold Mail Sender. Update it after meaningful investigation, decisions, implementation milestones, and verification so a later session can continue without reconstructing the project state.

Do not record passwords, app passwords, full recipient lists, or other secrets here.

## Current Understanding

- The repository contains a .NET 10 console application in `EmailSender`.
- The application reads recipients from a configured CSV file and sends personalized HTML email through SMTP.
- Organization-specific templates are loaded from `resources`; the default template is used when no organization template matches.
- Successful sends are stored in `resources/sentMailList.json` to prevent duplicates and enforce a per-day limit.
- Services are constructed manually in `Runner.Main`; there is no dependency-injection container.
- The active resource directory currently contains campaign-specific templates, a recipient CSV, attachments, and send history.
- `old_resources` is archival and is not part of the normal runtime flow.

## Runtime Flow

1. `Runner` finds and loads `EmailSender/appsettings.json`.
2. `TemplateService` loads all configured HTML templates into memory.
3. `SentMailTrackerService` loads the sent-address JSON dictionary.
4. `CsvReaderService` parses recipients from the master CSV.
5. `EmailApplication` skips duplicates, checks limits, selects content, and requests each send.
6. `EmailDispatchService` sends synchronously through SMTP.
7. Successful addresses are marked and the tracker is saved after the loop.

## Work Completed

- Inspected the solution, project file, application flow, model, and all services.
- Inspected the active CSV structure and template naming behavior.
- Confirmed `dotnet build EmailSender.sln --no-restore` succeeds.
- Build currently reports five nullable initialization warnings in `Recipient.cs`.
- Replaced the stale README with documentation for the current .NET 10 implementation.
- Verified the documentation with `git diff --check` and scanned it for accidental inclusion of the active SMTP credential; no secret was included.
- Converted `EmailSender` from a console SDK project to an ASP.NET Core Web SDK project.
- Added a localhost ASP.NET Core entry point in `Program.cs` with dependency injection for the existing services.
- Added read-only API endpoints: `GET /api/status`, `GET /api/recipients`, and `GET /api/templates`.
- Kept sending unavailable through the API at this milestone so starting or browsing the host cannot accidentally send email.
- Made configured relative paths deterministic by setting the process working directory to the application base directory at startup.
- Removed redundant configuration package references now supplied by the Web SDK.
- Initialized `Recipient` string properties and eliminated the five nullable build warnings.
- Verified the solution builds with zero warnings and zero errors.
- Live-tested the localhost status, templates, and recipients endpoints. The template endpoint returns 32 templates; the recipient endpoint correctly returns an empty list because the active CSV currently contains only its header.
- Added original-email preview models and `OriginalEmailService` as the manual command layer.
- Added `POST /api/original-emails/preview`; an empty email selection means preview all CSV recipients and never sends email.
- Added `POST /api/original-emails/send`; it requires at least one selected address, `confirm: true`, and the exact phrase `SEND ORIGINAL EMAILS`.
- Added a non-blocking send lock so a second original-email batch receives HTTP 409 instead of running concurrently.
- Added validation for email format, default template, per-recipient attachment existence, duplicate status, and the daily limit.
- Made sent-history access thread-safe and changed the ASP.NET send path to persist atomically after every successful message.
- Live-tested preview plus invalid-confirmation and empty-selection safety gates. Both unsafe send requests returned HTTP 400, and no SMTP operation was invoked.
- The current local tracker reports 13 messages sent today; this is existing local state and was not changed during endpoint testing.
- Added versioned local campaign models for campaign defaults, recipients, original messages, provider message/thread IDs, reply checks, and follow-up history.
- Added `CampaignStoreService`, which persists campaign JSON with locking, atomic replacement, schema-version validation, and a previous-version backup.
- Added `GET /api/campaigns`, `GET /api/campaigns/{id}`, and `POST /api/campaigns`.
- Added request-level and per-recipient custom subject overrides to original-email preview and send contracts.
- Added multiple additive attachments at request and recipient levels while retaining the existing CSV attachment.
- Subject precedence is recipient override, CSV value, request override, then configured default.
- Attachment paths are normalized, deduplicated, and validated during preview.
- Updated SMTP dispatch to accept multiple custom attachments.
- Added `FilePaths:CampaignStore` to example configuration and ignored `resources/app-data/` in Git.
- Verified campaign creation and reload using an isolated temporary JSON store. The temporary test state was removed afterward and no email was sent.
- Added the `EmailSender.Tests` xUnit project to the solution.
- Added an `IEmailDispatchService` abstraction; production resolves it to SMTP while tests use a recording fake. This is also the intended seam for Gmail API delivery.
- Added six isolated automated tests covering campaign persistence, schema rejection, subject precedence, combined attachments, missing-attachment rejection, immediate successful-send persistence, and duplicate skipping.
- The schema-version test exposed that camelCase JSON could bypass version validation; campaign deserialization is now case-insensitive and still rejects unsupported versions.
- Verified all six tests pass. Tests use temporary directories and never invoke SMTP.
- Changed duplicate handling to an explicit resend workflow. Preview exposes previous-send state and timestamp; duplicates remain ineligible unless `allowResend` is true.
- Successful approved resends refresh the local sent timestamp and persist immediately.
- Added a seventh automated test proving a duplicate is resent only when explicitly approved; all seven tests pass.
- Installed official `Google.Apis.Gmail.v1` and `Google.Apis.Auth` packages.
- Added `GmailConnectionService` with local Desktop OAuth, Gmail send/read-only scopes, serialized authorization, local token storage, and an authenticated profile check.
- Added `GET /api/gmail/status` and manual `POST /api/gmail/connect` endpoints.
- Added Gmail account, OAuth client path, and token-directory settings to the example configuration; credential/token paths are ignored by Git.
- Live-tested Gmail status only. It correctly returned `credentials-required`; no browser was opened, no token was stored, and no Gmail call or email send occurred.
- The user created a Google Desktop OAuth client, configured the app as External/Testing, and added the sending Gmail account as a test user.
- Added the local `Gmail` section to ignored `EmailSender/appsettings.json` with the account, OAuth client path, and token directory.
- Completed the manual Google OAuth flow successfully. Google returned and stored a local token, and an authenticated Gmail profile call verified it.
- `GET /api/gmail/status` now reports `connected-locally` for the configured account.
- The localhost backend was left running at `http://localhost:5195` after OAuth verification. No email was sent.
- Installed MimeKit for standards-compliant HTML, attachment, and threading MIME generation.
- Replaced the production dispatch registration with `GmailEmailDispatchService`; SMTP remains only as legacy code and is no longer the web host's active provider.
- Changed dispatch results from a boolean to structured success/error plus provider message and thread IDs.
- Added `GmailMimeMessageFactory` with custom subjects, HTML bodies, Unicode mailbox fields, multiple attachments, `In-Reply-To`, `References`, and Gmail base64url encoding.
- Added optional `campaignId` to original send requests. A supplied campaign is validated before dispatch, and successful Gmail IDs are persisted immediately to the matching campaign recipient.
- Unknown campaign IDs are rejected before any provider call.
- Added four tests for MIME structure/base64url and campaign provider-ID persistence/pre-dispatch campaign validation.
- All eleven automated tests pass, and the complete solution builds with zero warnings and zero errors.
- No live Gmail message was sent during this milestone.
- Added `IGmailThreadReader` and a Gmail API implementation that retrieves thread metadata and selected reply-classification headers.
- Added `ReplyCheckService` and manual `POST /api/campaigns/{id}/replies/check`; it never sends email and rejects concurrent checks.
- Reply statuses are `replied`, `no-reply`, `automated`, `bounced`, `uncertain`, and `missing-thread`.
- Reply checking ignores the original Gmail message and any later messages from the configured sender account, preventing outgoing messages from being classified as replies.
- Reply classifications, timestamps, matched message IDs, and notes are atomically persisted into campaign state.
- Added fake Gmail thread-reader tests covering human replies, automated replies, delivery failures, no reply with later outgoing mail, missing thread IDs, and persistence.
- All thirteen automated tests pass; the solution builds with zero warnings and zero errors.
- No live Gmail thread was queried because no controlled live original-email test has been approved/sent yet.
- The user explicitly approved a single live Gmail test recipient: `divyanshdevang@gmail.com`.
- Preview correctly detected that this address had an older local send record, so the live test used the explicit `allowResend: true` path.
- The first attempt was blocked by the restricted network sandbox and returned no provider IDs; it did not update local sent/campaign state.
- Restarted the localhost backend with approved network access and sent exactly one test message with subject `[Cold Mail Sender Test] Gmail thread integration` and no attachments.
- The Gmail API send succeeded and returned message ID/thread ID `19fff706d65d02f8`.
- Verified both IDs were immediately persisted in local campaign `02283d58-917c-4c10-b25f-41b72d23995e`.
- Restored the active `resources/master.csv` to its prior header-only state after the controlled test.
- Ran the manual read-only reply check against the real Gmail thread. It returned `no-reply` with no bounce, automated response, uncertainty, or missing-thread condition.
- The localhost backend is running at `http://localhost:5195` with approved network access.
- Added RFC `Message-ID` capture for original and follow-up Gmail sends and thread metadata reads.
- Added threaded provider dispatch using Gmail `threadId`, `In-Reply-To`, and `References`.
- Added manual `POST /api/campaigns/{id}/follow-ups/preview` and `POST /api/campaigns/{id}/follow-ups/send` endpoints.
- Follow-up candidates require a persisted `no-reply` status, stored Gmail thread, non-empty body, valid attachments, and a thread-safe subject by default.
- Custom follow-up subjects require explicit `allowSubjectChange` approval because they can break conversation threading.
- Follow-up sending requires selected recipients, `confirm: true`, and the exact phrase `SEND FOLLOW-UP EMAILS`.
- Every selected thread is read and reclassified immediately before dispatch; newly replied, automated, bounced, uncertain, or otherwise ineligible recipients are suppressed.
- Successful follow-ups and their provider/RFC IDs are atomically persisted in recipient follow-up history.
- Added tests for subject-change warnings, threaded header inputs, follow-up persistence, and the preview/send race where a reply arrives before dispatch.
- All sixteen automated tests pass; the solution builds with zero warnings and zero errors.
- Ran a live read-only reply check and follow-up preview for the controlled test campaign. Status remained `no-reply`, default subject was `Re: [Cold Mail Sender Test] Gmail thread integration`, and the candidate was eligible. No follow-up was sent.
- Added a Vite React 19 + TypeScript frontend in `frontend` with Lucide icons.
- Campaign recipients are now first-class local state: React supports manual add/edit/delete and campaign-scoped CSV upsert import.
- Original preview/send uses campaign-owned recipients when a campaign ID is supplied; it no longer depends on the global CSV for campaign sends.
- Added campaign recipient APIs for create, update, delete, and CSV import.
- CSV imports now use CsvHelper and support quoted commas plus optional `Subject`, `Attachment`, and `Template` columns.
- Campaign imports upsert by case-insensitive email and preserve original delivery, reply-check, and follow-up history.
- Added tests for campaign recipient CRUD, duplicate protection, history preservation, CSV quoted commas, and optional template fields.
- The test suite now contains 22 passing tests.
- Added an operational localhost dashboard for Gmail status, campaign selection/creation, CSV import, recipient selection, original preview/send, resend approval, reply checks, follow-up preview/send, and activity history.
- Both original and follow-up sends require their exact typed confirmation phrases in the UI.
- Added `POST /api/recipients/import` with required-header validation and atomic CSV replacement; invalid uploads preserve the existing recipient file.
- Added follow-up `{{name}}` replacement using each recipient's first name.
- Configured Vite development proxy to `http://localhost:5195` and production output to `EmailSender/wwwroot`.
- Configured ASP.NET Core to serve the React build and route non-API paths to `index.html`.
- The React production build completes successfully with no npm vulnerability findings.
- Added two CSV-import tests; the first run exposed a Windows file-lock issue during replacement, which was fixed by closing the validation reader before the atomic move.
- All eighteen automated backend tests pass; the full solution builds with zero warnings and zero errors.
- Follow-up preview now returns organization, most recent outbound timestamp, and elapsed hours per recipient.
- Elapsed time is based on the latest follow-up when one exists, otherwise the original email send time.
- The React follow-up table displays elapsed days/hours and supports a configurable `older than N days` filter, defaulting to three days.
- Added an organization filter and `Select visible` command; hidden rows are removed from selection so sends apply only to the filtered candidates.
- Expanded follow-up review with a reply-status filter, defaulting to `Eligible / no reply`, while retaining all classifications for diagnosis.
- Added cumulative `Select all visible` and filter-scoped `Deselect all visible` controls so users can compose a batch across organizations/statuses without clearing prior selections.
- Matched follow-up review to the original-email workflow with an explicit `Preview batch` action and per-recipient rendered HTML previews.
- Added per-campaign browser-local follow-up drafts for body, subject, attachments, subject-change approval, and filters; selections and confirmation text are never restored.
- Follow-up bodies now replace both `{{name}}` and `{name}` with the recipient's first name.
- Added `{{organization}}` and `{organization}` placeholder replacement.
- React production build succeeds, all eighteen backend tests pass, and the solution builds with zero warnings and zero errors after these changes.
- Completed the managed attachment workflow: React upload/list/delete/select controls, filename-only CSV/API values, explicit duplicate overwrite, and campaign-reference-protected deletion.
- Attachment paths are now resolved only at the mail-dispatch boundary. Preview responses and campaign history retain filenames and do not expose arbitrary local paths.
- Original and follow-up sends perform whole-batch attachment preflight before the first provider call. Invalid or missing files reject the entire batch with a consolidated error list.
- Added structured API exception responses for validation/preflight (400), missing state (404), file conflicts (409), and unexpected failures (500).
- Added four attachment regression tests. The backend suite now has 26 passing tests; the solution builds with zero warnings and the React production build succeeds.
- Replaced the stale console/SMTP README with documentation for the current localhost React, Gmail OAuth, campaign, attachment, reply-check, and follow-up workflows.
- Added an on-demand read-only Gmail reply viewer endpoint scoped through campaign and recipient state. It only fetches the persisted matched Gmail message ID.
- Full Gmail messages now expose sender, recipients, subject, received time, decoded plain text, sanitized HTML, and attachment metadata without downloading attachment contents.
- Reply HTML strips active content, event handlers, and remote/script URL attributes, then React renders it in a sandboxed iframe. Plain text is used when HTML is unavailable.
- Added the Reply table open/close viewer UI and a Gmail multipart decoding/sanitization regression test. The backend suite now has 27 passing tests.
- Added a visible searchable Help & Guide tab backed by the single typed source `frontend/src/guide.ts`.
- The guide covers the complete workflow, CSV format, attachment rules, confirmation phrases, reply statuses, follow-up filters, resend and preflight safety, troubleshooting, supported placeholders, examples, and a visible updated date.
- React production build succeeds after the guide addition; the backend remains at 27 passing tests with zero build warnings.
- Removed the obsolete `Runner`, `EmailApplication`, and SMTP `EmailDispatchService` files after confirming the ASP.NET host and tests do not reference them.
- Removed SMTP and legacy default-resume settings from the example configuration. Gmail account configuration is now required directly with no SMTP username fallback.
- `TemplateService` remains active because original emails use it to load and personalize HTML templates before Gmail MIME delivery.
- Added an optional original-email HTML body override to the API and React workflow, so original messages no longer require a template file.
- Original body precedence is explicit recipient template, manual batch HTML, organization template, then default template.
- Original preview now returns each recipient's body source and fully personalized rendered HTML. React provides a sandboxed per-recipient preview before sending.
- Original manual bodies and templates support `{name}`, `{{name}}`, `{organization}`, and `{{organization}}` consistently with follow-ups.
- Added tests proving manual HTML is personalized and dispatched exactly as previewed, and that an explicit recipient template takes precedence. The backend suite now has 29 passing tests.
- Completed a no-send readiness audit against the real local configuration: Gmail reports `connected-locally`, OAuth/token paths exist, the frontend returns HTTP 200, and template/attachment/campaign storage paths are present.
- Current local state contains one Gmail integration test campaign with two previously sent recipients, one loaded template, and one managed attachment. There is not yet a fresh production campaign prepared for tomorrow.
- The first real-state manual-body preview exposed legacy deliveries that had copied fallback template names into recipient configuration. Fixed delivery persistence so it no longer overwrites recipient template configuration and added backward-compatible fallback detection.
- Repeated the real local preview after the fix: both recipients used `manual-html`, no placeholders remained unresolved, both were eligible with explicit resend approval, and no send endpoint was called.
- Added regression coverage for legacy campaign fallback-template state. The backend suite now has 30 passing tests.
- Added `DeliveryLedgerService` with atomic local JSON persistence for every successful original and follow-up delivery, including provider message and thread IDs.
- Existing `sentMailList.json` entries are imported into an empty ledger exactly once as `legacy-import` events. The legacy map remains available for duplicate compatibility.
- Approved resends append new ledger entries instead of replacing the previous delivery timestamp. Daily-limit accounting now counts actual delivery events.
- Added tests for one-time legacy migration, two deliveries to the same address counting separately, and follow-up ledger persistence. The backend suite now has 32 passing tests with zero warnings.
- Manual test reminder pending: after the user is ready, verify the new campaign preview and one explicitly approved controlled send through the React UI.
- Added `BatchOperationService` with localhost status and cancellation endpoints. Original and follow-up sends report processed, sent, failed, skipped, current recipient, and cancellation state.
- React now polls batch status while sending and exposes a progress bar plus cooperative Cancel batch control. Cancellation is honored between recipients and never interrupts a provider call already in flight.
- Added batch-operation state tests. The backend suite remains at 34 passing tests with zero build warnings; the React production build passes.
- Fixed the New campaign input contrast by explicitly using a white background, dark text/caret, and visible placeholder color.
- Added explicit `Select all visible` and `Clear selection` buttons beside the selected-recipient count. The existing table-header checkbox remains available.
- React production build passes after these UI fixes. Manual test reminder: refresh the restarted application and verify campaign-name visibility plus selection controls.
- During the first 124-recipient real batch, the backend correctly recorded 124 processed, 124 sent, 0 failed, and 0 skipped, but the progress panel was not visible enough in the scrolled workflow and disappeared on completion.
- Made batch progress sticky below the top bar, visually prominent while running, loaded during workspace refresh, and retained as a completed summary until explicitly dismissed.
- React production build passes after the progress visibility fix. Manual test reminder: confirm the sticky progress/completed summary on the next batch.
- Added the official ASP.NET Core test host and isolated HTTP integration tests using temporary storage plus a fake email provider.
- Integration coverage now exercises campaign and recipient creation, manual HTML preview, unsafe confirmation rejection, idle cancellation rejection, confirmed fake-provider sending, completed batch status, campaign provider-ID persistence, and delivery-ledger persistence.
- The complete backend suite now has 37 passing tests with zero build warnings. Integration tests cannot call Gmail or modify live local campaign state.
- Manual test reminder remains: confirm sticky progress visibility and the retained completion summary during the next real batch.

## Confirmed Product Decisions

- The application will remain local-only and will not be hosted externally.
- The backend will use .NET and ASP.NET Core.
- The frontend will use React.
- The application will be accessed through localhost.
- No relational or embedded database will be introduced at this stage.
- Durable application state must therefore use structured local files with atomic writes, locking, backups, and schema/version handling.
- The primary future feature is automated follow-up email in the original conversation thread when no qualifying reply has been received.
- The preferred Gmail integration direction is the Gmail API with local OAuth credentials because SMTP alone cannot inspect replies or reliably manage Gmail threads.
- All email and mailbox operations must be explicitly initiated from the UI; there will be no automatic scheduler or unattended sending.
- The user manually starts the original-email send operation.
- The user manually starts a Gmail reply-status check when they want to prepare follow-ups.
- The application presents a reviewable follow-up candidate list and must not send during the check operation.
- The user selects/approves candidates and manually starts the follow-up send operation.
- Follow-ups must be sent in the original Gmail thread and re-check reply status immediately before sending to reduce race-condition mistakes.

## Known Risks and Improvement Candidates

Priority order is provisional and should be confirmed before implementation.

1. Rotate any SMTP credential that has been exposed outside its intended secret store.
2. Make path resolution deterministic and independent of the launch directory.
3. Replace naive comma splitting with a real CSV parser and validate malformed rows.
4. Persist successful sends incrementally or atomically to reduce duplicate-send risk after a crash.
5. Add a dry-run or preview mode before further live campaign use.
6. Correct batch-summary accounting; it currently mixes current recipients with historical send totals.
7. Clarify or correct the consecutive-error threshold (`> 5` currently permits six failures).
8. Initialize or require `Recipient` properties to remove nullable warnings.
9. Validate email addresses, template availability, and attachment paths before starting a batch.
10. Add focused unit tests for CSV parsing, template selection, tracking, and batch limits.
11. Consider asynchronous sending, throttling, cancellation, and a maintained SMTP library.
12. Define versioned JSON storage for campaigns, recipients, messages, thread IDs, follow-up eligibility/check results, replies, and application settings.

## Product Todo

Current status as of 2026-08-17:

- Completed: three recipient entry methods, campaign recipient persistence, sample CSV download, CsvHelper parsing, managed attachments, filename-only validation, whole-batch attachment preflight, follow-up age/organization filters, and placeholder replacement.
- Completed: read-only Gmail reply viewer with sanitized HTML and attachment metadata.
- Completed: in-app Help & Guide from a maintainable source.
- Completed: legacy console/SMTP orchestration removed from the active project.
- Completed: true delivery ledger and legacy history migration.
- Completed: batch progress and cooperative cancellation.
- Completed: initial API integration test coverage for safety-critical workflows.
- Completed: campaign rename, clean duplication, JSON export, and confirmed local deletion through the API and React header controls.
- Completed: validated local settings controls for sender display name, default subject, default template, and daily limit.
- Completed: browser-level UI smoke tests run against installed Google Chrome and Microsoft Edge.
- Next engineering step: prepare and execute the deferred manual end-to-end review.
- Pending user test: create/import the actual campaign, upload attachments, enter the final subject/body, inspect every preview row, and perform one explicitly approved controlled send.

- Replace CSV-only campaign recipient sourcing with three supported entry methods:
  - Add a single recipient manually in React.
  - Add, edit, and delete recipients directly in the campaign recipient table.
  - Import recipients in bulk from CSV.
- Add a `Download sample CSV` button directly beside the `Import CSV` button in the React original-email workflow.
- Serve the maintained sample file through the localhost application so users can download, edit, and re-import it.
- Persist editable recipients in campaign JSON and make original preview/send use campaign recipients rather than rereading the CSV.
- Support recipient fields for name, email, organization, custom subject, custom template, and multiple attachments.
- Add a read-only reply viewer in React so the user can open the actual received reply from a campaign recipient.
- Add a Gmail endpoint that fetches reply content only on demand using the already stored matched Gmail message ID.
- Return sender, recipients, subject, received time, plain-text body, sanitized HTML body, and attachment metadata.
- Sanitize received HTML before rendering it in React; do not execute remote scripts or unsafe markup.
- Keep reply viewing separate from reply checking: checking classifies messages, while opening a reply performs an explicit read-only fetch.
- A safe fictional example is available at `resources/sample_recipients.csv` using the currently supported `Name,Organization,Email,Subject,Attachment` headers.
- Add a visible `Help & Guide` section to the React frontend, accessible from the main navigation without leaving localhost.
- Document the complete workflow: connect Gmail, create a campaign, add/import recipients, manage attachments, preview originals, approve resends, send originals, check replies, filter follow-up candidates, preview follow-ups, and send selected follow-ups.
- Include copyable CSV examples, attachment rules, required confirmation phrases, reply-status meanings, timing/organization filter behavior, and troubleshooting steps.
- Include a searchable placeholder reference showing supported syntax and example output.
- Document current placeholders: `{{name}}`, `{name}`, `{{organization}}`, and `{organization}`; clarify that name resolves to the recipient's first name.
- Show which message types support each placeholder and provide a preview using sample recipient data.
- Keep the guide content in one maintainable source that can be rendered in React and referenced by repository documentation, avoiding duplicated instructions that drift apart.
- Add a visible application/version or guide-updated timestamp so users can tell whether help content matches the running build.
- Standardize all user-provided attachments under `resources/attachments/`.
- Add React file-upload controls for original-email common attachments, recipient-specific attachments, and follow-up attachments.
- Upload/copy selected files into `resources/attachments/` and display filename, size, upload status, and remove/replace controls.
- CSV `Attachment` values must contain filenames only, such as `DivyanshAgarwal_Resume.pdf`; relative and absolute paths must not be accepted.
- Resolve attachment filenames exclusively against `resources/attachments/` in the backend.
- Reject path separators, `..`, rooted paths, and any resolved path that escapes the attachments directory.
- Define duplicate-filename behavior before implementation, preferably prompting to rename or explicitly replace rather than silently overwriting.
- Add API endpoints for listing, uploading, and deleting locally managed attachment files, with deletion blocked or warned when a campaign still references the file.
- Update preview to report missing filenames and show resolved attachment metadata without exposing arbitrary filesystem paths.
- Add a mandatory attachment preflight immediately before both original and follow-up batch sends.
- Preflight must validate every common, campaign, and recipient-specific attachment for every selected recipient before the first provider call.
- If any attachment is missing, unreadable, outside `resources/attachments/`, or otherwise invalid, reject the entire batch and return one consolidated recipient/file error report.
- Do not begin a partially valid batch and discover attachment failures recipient by recipient during sending.
- Keep the final provider-level file check as defensive protection against a file being removed between preflight and dispatch, but treat that as an exceptional race rather than normal validation.

## Repository State at Checkpoint

- The worktree already had modified, deleted, and untracked files under `resources` before documentation work began.
- Those resource changes belong to the user and must not be reverted or overwritten without explicit direction.
- Documentation added/changed by this checkpoint: `README.md` and `PROJECT_CONTEXT.md`.
- Backend files changed by the first ASP.NET milestone: `EmailSender.csproj`, `Program.cs`, `Runner.cs`, `Recipient.cs`, and `TemplateService.cs`.
- Local `EmailSender/appsettings.json` and `resources/sentMailList.json` are ignored by Git.

## Latest Campaign Management Checkpoint

- Added rename, duplicate, JSON export, and delete endpoints plus compact React header actions.
- Campaign duplication copies defaults and recipient configuration but deliberately clears delivery, reply, and follow-up history and assigns fresh IDs.
- Campaign deletion requires typing `DELETE CAMPAIGN` in the UI and removes only local state; Gmail messages are untouched.
- Unit and HTTP integration coverage exercise the management lifecycle without calling Gmail or modifying live campaign state.
- Next engineering step: add editable local settings controls, then broader browser-level UI tests.
- Pending manual test reminder: verify rename, duplicate, export, and delete controls in React. The real campaign preview and controlled send test also remain pending when the user is ready.

## Latest Local Settings Checkpoint

- Added safe `GET /api/settings` and `PUT /api/settings` endpoints. They never expose OAuth credentials or filesystem paths.
- Added atomic ignored persistence under `resources/app-data/settings.json`, layered over the base local appsettings file with reload support.
- React now has a Settings tab for sender display name, default original subject, default template name, and daily limit; Gmail account is read-only.
- Updated MIME generation to read the current sender display name for every new message, so saved settings take effect without restarting localhost.
- Validation requires non-empty text values and a daily limit from 1 through 2000.
- Verification: React production build passed; all 43 backend tests passed; the solution built with zero warnings and zero errors.
- Next engineering step: broader browser-level UI tests for campaign management, settings, selection, preview, and confirmation workflows.
- Pending manual test reminder: manual UI testing remains deferred until the user is ready.

## Latest Browser Test Checkpoint

- Added Playwright as a frontend development dependency and configured projects for the already-installed Google Chrome and Microsoft Edge browsers.
- Browser tests serve the production React bundle and intercept all `/api` calls with deterministic fixtures, so they cannot send Gmail messages or alter live local files.
- Covered workspace startup, Gmail status display, bulk clear/select-visible behavior, settings edits, read-only Gmail identity, campaign rename/duplication, manual recipient creation, CSV import, original preview, exact confirmation gating, and follow-up age/organization filtering.
- Verification: all 12 browser executions pass, comprising 6 scenarios in Chrome and the same 6 in Edge. React build and lint both pass with no warnings or errors.
- Fixed the final React hook warning by explicitly tracking campaign switches. Selections and previews reset when the selected campaign changes, while normal refreshes preserve the current selection.
- Test command: `cd frontend` then `npm run test:e2e`.
- Next engineering step: consolidate and execute the deferred manual test checklist. No automated test should perform a real Gmail send.
- Pending manual test reminder: the user has chosen to perform manual testing at the end.

## Latest Credential Audit Checkpoint

- Current tracked files do not include `EmailSender/appsettings.json`, OAuth client JSON, Gmail token JSON, or `resources/app-data`.
- Git history contains an early tracked `EmailSender/appsettings.json` with an SMTP password field. Treat that old Gmail app password as exposed and revoke it; the current Gmail OAuth implementation does not use it.
- No tracked OAuth client secret or Gmail token file was found by the repository audit.
- Strengthened `.gitignore` for appsettings, OAuth, credential, and token JSON files.
- Added `scripts/check-staged-secrets.ps1` to reject blocked local files and common staged JSON credential fields before commit/push.
- Added explicit first-time and repeat localhost run commands to the README.
- Rewriting published Git history is intentionally not performed because it is destructive and requires coordinated force-pushing.

## Latest Gmail Reauthorization Checkpoint

- Added a persistent top-bar **Connect Gmail** / **Reconnect Gmail** button so the user can manually rerun OAuth verification at any time, even while the account is currently connected.
- Reduced the local Gmail authorization marker validity from 24 hours to two hours. This is a local authorization policy; it does not revoke Google's refresh token.
- Updated the Help & Guide and README with the two-hour behavior and reconnect workflow.
- A rebuilt server initially failed OAuth because it was started without outbound network access; the failure was `oauth2.googleapis.com:443` socket access denied. Restarting the localhost backend with network access resolved the issue and the reconnect flow completed successfully.
- The app is currently available at `http://localhost:5195` and Gmail reports `connected-locally`.
- The UI now surfaces provider-level send errors returned in the original-send response instead of showing only an aggregate failed count.

## Latest Reply Review Checkpoint

- Added a persisted **Bounced** counter to the campaign summary alongside originals sent, human replies, awaiting reply, and follow-ups sent.
- Confirmed reply classifications are loaded from campaign JSON on refresh/reload; a new Gmail check is only needed to discover newer provider state.
- Added Reply Check filtering for `replied`, `no-reply`, `bounced`, `automated`, `uncertain`, `missing-thread`, and `not-checked`, with a visible matching-recipient count.
- Added browser regression coverage proving persisted reply/bounced counters reload correctly and filtering isolates a replied recipient.
- Added browser coverage for follow-up reply-status filtering, cumulative select/deselect behavior, rendered preview workflow, and draft persistence across reloads.
- Verification: frontend production build passed; the focused Chrome and Edge Playwright scenarios passed.
- Current next step: commit and push the staged application changes; local `resources/` data and temporary build/test directories remain excluded.

## Checkpoint Practice

For future work, update the milestone handoff documentation after every meaningful milestone, before final verification. The required files are `PROJECT_CONTEXT.md` and, whenever user-facing behavior or instructions changed, `README.md`; update `AGENTS.md` only when agent workflow rules change. Do not allow either context or user documentation to remain two features behind the running application. Each checkpoint should capture:

- What was learned or changed
- Important decisions and their rationale
- Verification performed and its result
- Current blockers or risks
- The exact next step

The root `AGENTS.md` contains the full session-startup and handoff rule for Codex, Claude, and other coding agents.

The verified milestone must also be staged before handoff. Agents should review the staged diff, then ask the user to commit and push; agents must not commit or push unless explicitly requested.

## Latest Attachment Picker Checkpoint

- Added managed attachment selection controls for common original attachments, recipient-specific original attachments, and follow-up attachments.
- Recipient attachment buttons now toggle selection and visibly show a green selected state with a checkmark; the selected filenames are persisted in the recipient payload and shown in the recipient table.
- Added a Playwright regression assertion for recipient attachment selection, saved attachment persistence, and the visual selected state.
- Hardened the Gmail authorization boundary test fixture to use 119 minutes ago instead of exactly two hours ago, avoiding timing-dependent expiry.
- Verification: 49 backend tests passed, 18 Playwright executions passed across Chrome and Edge, frontend build and lint passed, and `git diff --check` passed.
- Exact next step: freeze the current workflow for the user's next bulk-email run; make future improvements as isolated milestones with this documentation update completed before handoff.

## Latest Follow-up Age Shortcuts Checkpoint

- Added 2d, 3d, 5d, 7d, and 14d shortcut buttons beside the existing follow-up age filter.
- Shortcuts update the existing `followOlderDays` value only; the active choice has an accessible pressed state and selected styling, while manual numeric entry remains available.
- Added browser coverage proving 7d and 3d update the filter and visible candidate count in both browser projects.
- Verification: 49 backend tests passed, 18 Playwright executions passed across Chrome and Edge, frontend build and lint passed.
- Exact next step: review the staged milestone and commit/push it before starting Milestone 2.

## Latest Bulk Selection Checkpoint

- Added original-email recipient selection controls for selected-only view, uncontacted-only selection, first 25/50/100 visible recipients, invert-visible selection, select-all-visible, and clear-selection.
- Selection remains local UI state and the existing preview/send confirmation gates and request payloads are unchanged.
- Selected-only view combines with the existing search field and resets when switching campaigns.
- Verification: 49 backend tests passed, 18 Playwright executions passed across Chrome and Edge, frontend build and lint passed.
- Exact next step: review the staged milestone and commit/push it before starting Milestone 3.

## Latest Campaign Summary and Export Checkpoint

- Added a read-only Summary tab with recipient, company, sent, unsent, reply, awaiting, bounced, follow-up, and valid-contact metrics.
- Added company-level aggregation showing contacts, sent, replied, awaiting, and bounced counts.
- Added Markdown report download, full Excel-compatible CSV download, and deduplicated valid-contacts CSV download.
- Valid-contact export accepts valid email syntax, excludes bounced recipients, and deduplicates case-insensitively. Exports are generated locally from campaign state and never call Gmail.
- Added browser coverage for Summary rendering and export actions in Chrome and Edge.
- Verification: 49 backend tests passed, 20 Playwright executions passed across Chrome and Edge, frontend build and lint passed.
- Exact next step: review the staged milestone and commit/push it before starting the next planned improvement.

## Latest Pre-send Review Checkpoint

- Added a read-only original-email pre-send review panel after batch preview.
- The panel shows selected, eligible, previously sent, invalid-email, missing-attachment, duplicate, replied-selected, remaining-daily-capacity, and Gmail authorization values.
- Values are derived from the existing preview response and locally loaded Gmail status; preview, confirmation phrase, and send endpoints are unchanged.
- Added browser assertions for the review panel and key counts in Chrome and Edge.
- Verification: 49 backend tests passed, 20 Playwright executions passed across Chrome and Edge, frontend build and lint passed.
- Exact next step: review the staged milestone and commit/push it before selecting the next incremental feature.

## Latest Message Presets Checkpoint

- Added local named message presets for original subject/body/common attachments and follow-up subject/body/attachments.
- Added built-in General software application, Backend/.NET application, and Recruiter outreach presets.
- Applying a preset only populates existing fields, clears stale previews/confirmation text, and never sends or changes recipients.
- Users can save the current message fields as a custom preset in browser-local storage; built-in presets remain available.
- Added browser coverage proving preset application populates the original subject/body without creating a send confirmation.
- Verification: 49 backend tests passed, 22 Playwright executions passed across Chrome and Edge, frontend build and lint passed.
- Exact next step: review the staged milestone and commit/push it before selecting the next incremental feature.

## Latest Workspace Navigation and Preset Management Checkpoint

- Replaced the passive Local workspace footer with real sidebar navigation for Presets, Settings, and Help & Guide.
- Removed Settings and Help from campaign tabs so the campaign workspace now focuses on Original email, Reply check, Follow-ups, Summary, and Activity.
- Added a dedicated Presets workspace with create, edit/rename, and delete operations for original/follow-up subject, body, and attachment fields.
- Campaign forms retain only compact preset application controls plus a Manage presets shortcut.
- Migrated preset persistence to versioned browser-local storage while preserving existing custom presets from the prior key.
- Workspace navigation remains accessible as a compact horizontal row on mobile layouts.
- Verification: 49 backend tests passed, 12 Chrome and 12 Edge Playwright scenarios passed, frontend build and lint passed.
- Exact next step: review the staged milestone and commit/push it before selecting the next incremental feature.

## Latest Workspace Navigation Return Fix

- Fixed sidebar campaign navigation so selecting a campaign always returns to the Original email campaign view, even when the user is currently in Presets, Settings, or Help & Guide.
- Added browser regression coverage for leaving Presets and returning to the campaign page.
- Verification: focused Chrome and Edge navigation tests passed; frontend build and lint passed.
- Exact next step: review the staged fix and commit/push it with the workspace navigation milestone.

## Latest Duplicate and Recent-contact Warnings Checkpoint

- Added warning-only recipient review on the Original email page for duplicate selected emails, contacts seen in another campaign, sends within the last 7 days, replied recipients, bounced recipients, and repeated organizations.
- Warnings recalculate from the current local campaign selection and never block preview or sending.
- Tightened an existing browser metric assertion after warning labels introduced intentionally overlapping text.
- Verification: 49 backend tests passed, 12 Chrome and 12 Edge Playwright scenarios passed, frontend build and lint passed.
- Exact next step: review the staged milestone and commit/push it before selecting the next incremental feature.

## Latest Preset HTML Editing Preview Fix

- Fixed the original rendered HTML viewer to clear whenever the original subject, HTML override, attachments, or resend approval changes. Editing HTML after applying a preset can no longer leave an old rendered preview visible.
- Added browser regression coverage that applies the Backend/.NET preset, previews it, edits the HTML override, previews again, and verifies the rendered iframe contains the edited HTML.
- Verification: frontend production build passed, lint passed, and all 26 Playwright executions passed across Chrome and Edge.
- Backend verification was attempted while the existing localhost process held the application binaries. The no-build test run reached 49 tests but one pre-existing Gmail authorization timing test reported `reauthorization-required` at the 119-minute boundary; the build was likewise blocked by the running process's file lock. No backend source changed in this milestone.
- Exact next step: review the staged milestone and commit/push it before selecting the next incremental feature.

## Latest Settings Delivery Counts

- Added three read-only successful-delivery counts to Settings: rolling last 24 hours, current backend-local calendar date, and current UTC calendar date.
- Counts come from the offset-aware delivery ledger and include both original emails and follow-ups.
- Kept the settings update contract unchanged; the counts are computed response data and are not written into application settings.
- Added deterministic ledger boundary coverage and browser coverage for displaying all three counts.
- Missing count properties from a still-running older backend now render as `Unavailable` with an explicit restart instruction instead of blank values. The current backend process must be restarted after deploying this milestone before numeric counts can be returned.
- Verification: 50 backend tests passed in Release configuration; frontend production build and lint passed; all 26 Playwright executions passed across Chrome and Edge.
- Exact next step: review the staged milestone and commit/push it before selecting the next incremental feature.

## Latest CSV Preset Assignment

## Latest Recipient HTML And Attachment Aliases

- Added optional CSV `HtmlBody`/`Body` content so each recipient can use complete HTML without a browser preset.
- Added optional `AttachmentDisplayName` so unique managed files can all appear under the same filename in received email.
- The local managed filename remains the value in `Attachment`; aliases affect only outgoing MIME metadata.
- Verification: `dotnet test EmailSender.sln --no-restore -c Release` passed all 52 tests. Release backend build succeeds; the ordinary Debug build was blocked by the currently running localhost process holding `EmailSender.exe`.
- CSV import is the supported setup path for these fields; the current recipient editor does not yet expose separate HtmlBody/display-name inputs.
- Updated both downloadable sample CSV copies with `AttachmentDisplayName`, `HtmlBody`, and `PresetName` examples, plus documented body precedence.
- Updated the sample rows to demonstrate the intended company-specific `Template` workflow with one HTML file per company.
- Exact next step: review the staged milestone and commit/push it before selecting the next incremental feature.

- Added optional `PresetName` to campaign recipient CSV imports and persisted it with recipient state.
- During original preview/send, the frontend resolves each selected recipient's exact browser-local preset name into recipient-specific subject, HTML body, and attachments.
- Recipient preset HTML is personalized by the backend and reported as `recipient-preset` in preview.
- Missing browser-local preset names block preview with a clear error instead of silently falling back to campaign defaults.
- Existing CSV files without `PresetName` remain compatible.
- Verification: 52 backend tests passed; frontend production build and lint passed; all 26 Playwright executions passed across Chrome and Edge.
- Exact next step: review the staged milestone and commit/push it before selecting the next incremental feature.

- Verification: frontend production build passed. Template reload is read-only and does not send email.

- Moved the shared template library into the workspace sidebar, added HTML upload, and automatically reload templates after upload. Frontend production build and Release backend build pass.

- Corrected template navigation: removed the inline sidebar template list and added a dedicated Workspace > Templates page, matching Presets. Campaign list space is restored. Frontend production build passed.
- Corrected the canonical `frontend/public/sample_recipients.csv`; Vite had been copying its stale version over the production download during builds.
- Hardened HTML template upload with temp-file streaming, atomic replacement, and retries for transient Windows file locks.
- Added root `.build-temp/` and `.test-temp/` to `.gitignore`; both are local verification artifacts and must never be committed.

- Cleaned up attachment library presentation based on provided screenshot: added structured header, aligned upload action, bordered file chips, wrapping, truncation, and spacing. Frontend build passed.

## Latest Organization Tracker Milestone

- Added a workspace-wide **Organizations** tab that merges normalized organization names across all campaigns and aggregates contacts, campaigns, original sends, follow-ups, replies, awaiting replies, automated responses, bounces, and latest activity timestamps.
- Organization rows expand to show their underlying campaign contacts and can open an already matched Gmail reply through the existing read-only reply viewer.
- Added explicit manual organization outcomes: `unreviewed`, `no-response`, `interested`, `not-interested`, `follow-up`, and `other`, plus free-form notes. Outcomes are never inferred from reply text.
- Added `OrganizationTrackerService` with schema-version validation and atomic persistence in ignored `resources/app-data/organization-tracking.json`.
- Added `GET /api/organizations` and `PUT /api/organizations/tracking`, filtered organization CSV export, search, and outcome filtering.
- Added workspace-wide `POST /api/organizations/replies/check`. It uses the existing reply-check concurrency lock, processes every campaign sequentially, persists each campaign recipient's classification and `CheckedAt`, and returns consolidated totals.
- The Organizations UI now has **Check all replies**, an overall latest persisted check time, per-organization last-checked values, contact-level checked times, and last-checked CSV export.
- Added focused tests for case/whitespace-insensitive cross-campaign aggregation and durable manual outcome/notes persistence.
- Added multi-campaign fake-Gmail coverage proving the global operation updates both campaigns and flows into the organization last-checked aggregate.
- Verification: all 55 backend tests passed; React production build passed; frontend lint passed; live `GET /api/organizations` returned 120 organizations with `lastCheckedAt`; production UI returned HTTP 200 at `http://localhost:5195`.
- The live all-campaign POST was intentionally not invoked during verification because it would query every real Gmail thread; automated coverage exercises the full operation with fake Gmail data.
- The localhost backend was restarted and is running at `http://localhost:5195` with the new API and production frontend.
- Exact next step: manually review several Organizations rows in the browser, save a non-sensitive test outcome/note if desired, then review and commit/push the staged milestone.

## Latest Inline Organization Reply Viewer Fix

- Moved the matched-reply viewer from the bottom of the Organizations page into the expanded organization detail row that launched it.
- Kept organization reply state separate from the campaign Reply Check viewer so replies do not carry across workspace tabs.
- Collapsing the organization or closing the viewer clears the inline reply state.
- Added Playwright coverage asserting the reply iframe is nested inside the expanded organization row and no page-level organization viewer remains.
- Verification: frontend lint and production build passed. The focused Chrome scenario reached the inline viewer assertion successfully; the Playwright preview-server process then required manual termination during Windows teardown.
- Exact next step: review the inline placement in the running Organizations UI, then commit/push it with the staged Organization Tracker milestone.

## Latest User Guide Milestone

- Added `USER_GUIDE.md`, a task-oriented guide for a non-developer using this application for a real job-search campaign: prerequisites, clean checkout, local configuration, Google Cloud OAuth setup, startup, campaign preparation, sending, reply checks, follow-ups, organization tracking, exports, backups, troubleshooting, and the owner-sharing/credential audit.
- `README.md` now links to the guide from its introduction, and its Requirements list clarifies that Node.js and npm are needed only when rebuilding or developing the frontend because the repository tracks the production bundle.
- Completed the guide with a table of contents and a new section 6, "Find your way around the dashboard", describing the sidebar groups, top bar, campaign tabs, metric strip, batch progress panel, and the in-app Help & Guide tab. A closing glossary defines the terms the guide uses.
- Corrected and expanded several sections against the running implementation: manual recipient form fields, the Review warnings panel and its six chips, the exact pre-send review fields, daily-limit behaviour (`daily-limit-reached` per remaining recipient, Settings accepting 1-2000), the three follow-up selection buttons and their different semantics, reply classification values alongside the labels the UI displays, the location of workspace exports under Settings, and additional troubleshooting entries.
- Documentation-only milestone: no application code, configuration, tests, or generated assets changed, so no build or test run was required and no Gmail call or email send occurred.
- Exact next step: review the rendered guide, then stage and commit the documentation together with the README change.

## Planned Gmail Conversation And Manual-Send Sync Features

Research and product decisions recorded for a future implementation session; no application code was changed for this item.

### Full Gmail Thread Viewer

- For a campaign recipient whose original message is already tracked, add a one-click full-thread viewer.
- Retrieve and display the complete Gmail conversation chronologically, including the original outbound email, recipient replies, replies manually sent from Gmail, later back-and-forth messages, timestamps, and attachment metadata.
- Extend the current single matched-reply viewer rather than treating the sender's manual Gmail replies as new recipient replies.
- Keep Gmail as the source of truth for conversation contents while retaining the campaign recipient's persisted Gmail thread ID as the link.

### Separate Gmail Sync Section

- Add a dedicated **Gmail Sync** workspace section for discovering new emails/threads sent manually from the connected Gmail account.
- The normal sync list must show only Gmail threads that are not linked to any campaign recipient. Already-linked threads must be excluded entirely from this list to prevent clutter.
- Each unlinked candidate should show enough context to review it safely, such as subject, recipient, latest activity time, last sender, and a short preview, with an option to open the full thread.
- The user explicitly chooses **Track this thread**, then selects the destination campaign and matching recipient. Do not automatically attach ambiguous conversations.
- After confirmation, persist the Gmail message/thread identifiers so existing reply checks, full-thread viewing, activity history, latest-outbound timing, and threaded follow-ups can use the manually initiated conversation.
- Prevent accidental duplicate linking of the same Gmail thread. Any future audit or reassignment workflow should be separate from the uncluttered unlinked-thread sync list.
- This feature tracks conversations, replies, and detectable bounces. It does not promise reliable email-open or link-click tracking.

- Verification: documentation-only product checkpoint; no build or tests were required and no Gmail API call or email send occurred.
- Exact next step: start with the Gmail API/read-model design for listing unlinked sent threads and loading complete thread contents, then implement the backend endpoints and separate Gmail Sync UI with explicit campaign/recipient linking.
