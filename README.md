# Cold Mail Sender

A local-only campaign workspace for manually sending Gmail outreach and threaded follow-ups. The backend is ASP.NET Core on .NET 10, the frontend is React, and durable state is stored in local JSON files. Nothing is hosted and no database is required.

The application never schedules or sends email by itself. You explicitly preview, select recipients, type a confirmation phrase, and click send.

For a complete first-run walkthrough, see [USER_GUIDE.md](USER_GUIDE.md). It covers prerequisites, Google Cloud Gmail OAuth, a tour of the dashboard, campaign preparation, safe sending, reply checks, follow-ups, troubleshooting, backups, and the repository security checklist.

While a batch is running, the dashboard shows progress and the current recipient. **Cancel batch** requests cooperative cancellation; the provider call already in progress is allowed to finish, then remaining recipients are skipped.

## Current Workflow

1. Start the localhost application and connect Gmail using local Desktop OAuth.
2. Create or select a campaign.
3. Add recipients manually, edit them in the table, or import a CSV.
4. Upload attachments into the managed local attachment library.
5. Select recipients and preview the original batch.
6. Use the recipient selection controls to select uncontacted rows, the first 25/50/100 visible rows, invert the visible selection, or show selected rows only. Preview the batch and review the pre-send checklist for eligibility, previous sends, invalid addresses, missing attachments, duplicates, replied recipients, remaining capacity, and Gmail authorization. Enable resend approval only when needed.
7. Type `SEND ORIGINAL EMAILS` and send the selected originals.
8. Later, click **Check replies** to classify each Gmail thread.
9. Open a matched reply from the reply table when you need to inspect its sender, subject, body, or attachment metadata.
10. Preview follow-ups, filter by elapsed days, organization, and reply status, then select eligible recipients.
11. Type `SEND FOLLOW-UP EMAILS` and send the selected messages as replies in the original Gmail threads.
12. Open **Summary** to review campaign/company totals or download a Markdown report, full CSV, or deduplicated valid-contacts CSV.
13. Open **Workspace > Organizations** to review activity across every campaign, record an explicit organization outcome and notes, inspect the underlying contacts/replies, or export the filtered tracker to CSV.
14. Use message presets to populate reusable original/follow-up subjects, bodies, and attachment selections; review and preview normally before sending.

Reply checks and previews are read-only. Immediately before each follow-up, the backend checks the Gmail thread again and suppresses the send if a new reply, automated response, bounce, or uncertain message is found.

Opening a matched reply is also read-only and happens only on demand. Gmail HTML is sanitized on the backend, remote image sources are removed, and the frontend renders it inside a sandboxed frame. Attachment metadata is shown without downloading attachments.

The **Help & Guide** tab contains the maintained operational reference with searchable workflow, CSV, attachment, reply-status, filter, safety, troubleshooting, and placeholder sections. Its visible update date comes from `frontend/src/guide.ts`, which is the source to edit when application guidance changes.

The **Summary** tab is read-only and aggregates the current local campaign state. The full CSV includes recipient delivery/reply/follow-up fields. The valid-contacts CSV includes only syntactically valid, non-bounced, case-insensitively deduplicated contacts. Markdown and CSV exports are generated locally and do not query Gmail or include credentials.

Message presets are applied from compact controls in the Original email and Follow-ups forms. Manage them from **Workspace > Presets** in the sidebar, where presets can be created, edited/renamed, or deleted. Built-in presets cover general software, Backend/.NET, and recruiter outreach. Presets are stored in versioned browser-local storage on this computer. Applying or saving a preset only changes form fields; it never sends email or bypasses preview and confirmation.

The sidebar separates campaign work from workspace-level tools. Campaign tabs contain Original email, Reply check, Follow-ups, Summary, and Activity. Organizations, Presets, Templates, Settings, and Help & Guide are available under the sidebar's **Workspace** section.

The **Organizations** tab merges case-insensitive organization names across all local campaigns. It shows contacts, campaigns, original sends, follow-ups, reply classifications, bounces, recent outbound/reply timestamps, and the last persisted reply-check time. **Check all replies** runs the existing read-only Gmail classification for every campaign under one concurrency lock, updates every processed recipient's `CheckedAt` value, and refreshes the organization aggregates when complete. Expand an organization to review its contacts and open any matched Gmail reply directly inside that organization's expanded row. Set a manual outcome of **Unreviewed**, **No response**, **Interested**, **Not interested**, **Follow up**, or **Other**, add notes, then save the row. The app deliberately does not infer these business outcomes from reply text. Manual tracking is stored atomically in ignored `resources/app-data/organization-tracking.json`; CSV export includes the last-checked timestamp and is generated locally from the current filters.

Selecting any campaign from the sidebar returns directly to that campaign's Original email page, including when you were previously viewing a workspace tool.

The Original email page shows warning-only checks for selected duplicate emails, contacts present in another campaign, sends within the last 7 days, replied or bounced recipients, and repeated organizations. These warnings never block preview or sending.

## Requirements

- .NET 10 SDK
- A Gmail account
- A Google Cloud Desktop OAuth client with the Gmail API enabled
- Node.js and npm are only needed when rebuilding or developing the React frontend; the repository includes the production bundle for ordinary users.

## Run Locally

For first-time setup, create the ignored local configuration from the safe example and fill in only your local paths/account values:

```powershell
Copy-Item EmailSender\appsettings.exmaple.json EmailSender\appsettings.json
```

Never commit `EmailSender/appsettings.json`, files under `resources/app-data/`, OAuth client JSON, or Gmail token JSON.

Build the React application into ASP.NET's static files, then run the backend from the repository root:

```powershell
cd frontend
npm.cmd install
npm.cmd run build

cd ..
dotnet restore EmailSender.sln
dotnet run --project EmailSender\EmailSender.csproj --urls http://localhost:5195
```

Open `http://localhost:5195`.

For later runs, when dependencies and the frontend bundle are already current, this is sufficient:

```powershell
dotnet run --project EmailSender\EmailSender.csproj --no-build --urls http://localhost:5195
```

For frontend hot reload, keep the backend on port `5195`, run `npm.cmd run dev` from `frontend`, and open `http://localhost:5173`.

## Local Configuration

The application reads `EmailSender/appsettings.json`. This file is ignored by Git. Use `EmailSender/appsettings.exmaple.json` as the reference.

Important paths:

| Setting | Purpose |
| --- | --- |
| `FilePaths:Templates` | HTML email templates |
| `FilePaths:Attachments` | Managed attachment directory, normally `resources/attachments` |
| `FilePaths:CampaignStore` | Versioned local campaign JSON |
| `FilePaths:SentMailList` | Legacy address history retained for duplicate compatibility |
| `FilePaths:DeliveryLedger` | Append-only successful delivery events used for accurate daily counts |
| `FilePaths:OrganizationTracking` | Manual organization outcomes and notes |
| `FilePaths:ApplicationSettings` | UI-editable local defaults, normally `resources/app-data/settings.json` |
| `Gmail:CredentialsPath` | Downloaded Google Desktop OAuth client JSON |
| `Gmail:TokenDirectory` | Local OAuth token storage |
| `Gmail:Account` | Gmail address used to send and identify outgoing thread messages |

OAuth credentials, tokens, campaign state, and send history remain local and must not be committed.

Before every commit or push, stage the intended files and run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\check-staged-secrets.ps1
```

The script rejects staged local configuration, OAuth/token files, `resources/app-data`, and common JSON secret fields. It supplements review and `.gitignore`; it does not remove secrets from older Git history.

Security note: an early repository revision tracked `EmailSender/appsettings.json` with an SMTP password field. Revoke that old Gmail app password in the Google Account security settings. The current application uses Gmail OAuth and does not require an SMTP app password.

The React Settings tab safely edits the sender display name, default subject, default template name, and daily limit. It also shows read-only successful-delivery counts for the rolling last 24 hours, the current backend-local calendar date, and the current UTC calendar date. These values include original emails and follow-ups recorded in the delivery ledger. Editable settings are atomically stored in the ignored application-settings JSON and apply to future previews and sends without a restart. Gmail identity, OAuth locations, and storage paths remain read-only in the UI.

## Google Gmail OAuth

1. Create a project in Google Cloud Console.
2. Enable the Gmail API.
3. Configure the OAuth consent screen. Testing mode is sufficient for local use.
4. Add your Gmail address under **Audience > Test users**.
5. Create an OAuth Client ID with application type **Desktop app**.
6. Download its JSON to the configured `Gmail:CredentialsPath`.
7. Start the application and click **Connect Gmail**.
8. Complete Google's browser authorization flow.

The application requests Gmail send and read-only scopes. Tokens are stored only in the configured local token directory.

The top bar always includes **Connect Gmail** or **Reconnect Gmail**, so you can manually re-run the OAuth verification whenever needed. The local authorization marker is valid for two hours; reconnecting refreshes that window. The localhost backend must be allowed outbound network access to `oauth2.googleapis.com` and Gmail API endpoints for this flow to complete.

## Recipients and CSV

Campaign recipients can be added manually, edited or deleted in React, or imported from CSV. CSV import upserts recipients by case-insensitive email and preserves existing delivery, reply, and follow-up history.

The selected campaign can also be renamed, duplicated, exported as JSON, or deleted from the header controls. Duplication copies campaign defaults and recipient configuration into a clean campaign without original-message, reply, or follow-up history. Deleting a campaign removes only its local state; it does not alter Gmail messages.

Supported headers:

```csv
Name,Organization,Email,Subject,Attachment,AttachmentDisplayName,Template,HtmlBody,PresetName
Jane Doe,"Example, Inc.",jane@example.com,Software Engineer Application,company-a-resume.pdf,Divyansh_Resume.pdf,,"<html><body>Hello {name}</body></html>",
```

`Name`, `Organization`, and `Email` are required. `Subject`, `Attachment`, `AttachmentDisplayName`, `Template`, `HtmlBody` (or `Body`), and `PresetName` are optional. `Attachment` is the unique local managed filename; `AttachmentDisplayName` is the filename shown in the received email and may be identical for every recipient. `HtmlBody` supplies complete per-recipient HTML without a preset and supports the normal name and organization placeholders. `PresetName` must exactly match a browser-local message preset when used. Quoted commas are supported.

For your company-specific workflow, put each HTML file in `FilePaths:Templates` and set `Template` to its filename, such as `company-a.html`. Leave `HtmlBody` and `PresetName` blank. Body priority is: `PresetName` override (when selected in the browser), recipient `HtmlBody`, recipient `Template`, batch HTML body, organization template, then the campaign default template.

Use the **Sample CSV** button beside **Import CSV**, or open `resources/sample_recipients.csv`.

## Attachments

All attachments are managed under `resources/attachments`. Upload files through the attachment library and select them as common original attachments, recipient-specific attachments, or follow-up attachments.

The CSV `Attachment` column accepts one filename or multiple pipe-separated filenames, for example `resume.pdf|cover-letter.pdf`. Preview validates and displays the final attachment list before sending.

CSV and API values must be filenames only, for example `resume.pdf`. Absolute paths, relative paths, path separators, and `..` are rejected. Duplicate uploads require explicit replacement, and a file referenced by campaign state cannot be deleted.

Before the first provider call, the backend validates every attachment for every selected recipient. If any file is invalid or missing, the complete batch is rejected and no email in that batch is sent.

## Templates and Placeholders

Templates are HTML files in `FilePaths:Templates`. A recipient's custom template is used first; otherwise the application tries the organization template and then the configured default template.

You can also paste HTML directly into **HTML body override** for an original-email batch. If you apply a message preset and then edit this field, the edited HTML is used by the next preview (and the previously rendered row preview is cleared until refreshed). The body precedence is:

1. Explicit recipient template
2. Manual batch HTML body
3. Organization template
4. Default template

Each preview row can open the final rendered HTML for that recipient. The preview body is the exact personalized body supplied to Gmail when the batch is sent.

Original manual bodies, original templates, and follow-up bodies support:

| Placeholder | Value |
| --- | --- |
| `{name}` or `{{name}}` | Recipient's first name |
| `{organization}` or `{{organization}}` | Recipient's organization |

Original templates currently use the existing template formatting service. Preview every batch to verify the final subject, template, and attachment selection before sending.

## Reply Statuses

Manual Gmail checks persist one of these classifications:

- `replied`: a human reply matching the recipient was found
- `no-reply`: no qualifying reply was found
- `automated`: an automated response was found
- `bounced`: a delivery failure was found
- `uncertain`: the thread could not be classified confidently
- `missing-thread`: the original Gmail thread is unavailable

The campaign summary shows persisted counts for originals sent, human replies, awaiting reply, bounced recipients, and follow-ups sent. These counts reload from local campaign state; running **Check replies** is only required to query Gmail for newer classifications.

The Reply Check table can be filtered by classification. Select **Replied** to isolate human replies, or use the bounced, awaiting reply, automated, uncertain, missing-thread, and not-checked filters when reviewing larger campaigns.

Only recipients with a persisted `no-reply` status are eligible for follow-up preview. The table shows elapsed days and hours since the most recent outbound message and supports minimum-age, organization, and reply-status filters. Use the 2d, 3d, 5d, 7d, and 14d age shortcuts or enter a custom number of days. The default reply-status view is **Eligible / no reply**, while **All statuses** and the individual classifications can be used for diagnosis.

Follow-ups use the same review flow as original emails: click **Preview candidates**, select recipients, click **Preview batch**, inspect each personalized rendered message, type `SEND FOLLOW-UP EMAILS`, and send. **Select all visible** adds eligible rows from the current filters to the existing selection; **Deselect all visible** removes only the currently filtered rows, so selections can be composed across organizations or statuses.

Follow-up HTML bodies, subject overrides, attachments, subject-change approval, and follow-up filters are saved as browser-local drafts per campaign. They return when you switch campaigns or reload. Recipient selections and confirmation text are intentionally not persisted.

## Resends and Safety Gates

Previously sent recipients remain visible. They are blocked by default and show their prior send state. To send again, explicitly enable resend approval, preview again, select the intended recipients, and use the normal typed confirmation.

Original and follow-up sends use non-blocking operation locks, so concurrent batches are rejected. Successful Gmail message IDs, thread IDs, RFC message IDs, and campaign history are saved locally after each delivery.

## Storage

- Campaigns: versioned local JSON with locking, atomic replacement, and backup
- Delivery ledger: append-only local JSON containing every successful original and follow-up delivery
- OAuth: local credential JSON and token directory
- Attachments: `resources/attachments`
- React production build: `EmailSender/wwwroot`

There is no hosted service, background scheduler, or database.

## Development

### Milestone Documentation

After every meaningful feature or verification milestone, update `PROJECT_CONTEXT.md` with the current implementation state, decisions, verification results, risks, and exact next step. Update this README in the same milestone whenever user-facing workflow, setup, commands, or behavior changed. Keep these files current so a new Codex or Claude session can resume without reconstructing recent work. Read `AGENTS.md`, `PROJECT_CONTEXT.md`, and this README at session startup. Do not document secrets, tokens, passwords, or full recipient lists.

After verification, stage all intended milestone files and review the staged diff. The agent should then ask the user to commit and push; committing and pushing remain explicit user actions.

```powershell
dotnet test EmailSender.sln --no-restore
dotnet build EmailSender.sln --no-restore

cd frontend
npm.cmd run build
npm.cmd run test:e2e
```

Use `npm.cmd run test:e2e:chrome` or `npm.cmd run test:e2e:edge` to run one browser only.

Backend tests use temporary workspaces and fake mail/Gmail implementations. They do not send live email.

The suite also starts the real ASP.NET application in memory for HTTP integration tests. Gmail dispatch is replaced with a recording fake while campaign, preview, confirmation, batch-status, persistence, and ledger endpoints are exercised together.

Playwright uses the installed Google Chrome and Microsoft Edge browsers against the production React bundle with mocked API responses. It covers workspace loading/navigation, bulk selection, local settings, campaign rename/duplication, recipient creation and CSV import, recipient attachment selection and persistence, campaign summary/export controls, message preset application and management, original preview and confirmation gating, follow-up filtering, cumulative filtered selection, and follow-up draft persistence. The current suite runs 24 executions across Chrome and Edge. Browser tests never connect to Gmail, invoke a send route, or modify the live local campaign store.

The ASP.NET host uses `GmailEmailDispatchService` exclusively. SMTP passwords and the old console batch runner are not part of the project.

## Remaining Work

- Complete the deferred manual end-to-end UI review

See `PROJECT_CONTEXT.md` for the resumable engineering checkpoint and the exact next step.
