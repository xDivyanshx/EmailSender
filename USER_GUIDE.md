# Cold Mail Sender User Guide

Updated: 2026-09-18

This guide is for someone using Cold Mail Sender for a real, manual job-search campaign. Read the safety sections before connecting Gmail or importing a contact list.

## Contents

- [What the application does](#what-the-application-does)
- [Safety first](#safety-first)
- [1. Prerequisites](#1-prerequisites)
- [2. Get a clean copy](#2-get-a-clean-copy)
- [3. Create local configuration](#3-create-local-configuration)
- [4. Set up Gmail OAuth in Google Cloud](#4-set-up-gmail-oauth-in-google-cloud)
- [5. Start the application](#5-start-the-application)
- [6. Find your way around the dashboard](#6-find-your-way-around-the-dashboard)
- [7. First-time workspace setup](#7-first-time-workspace-setup)
- [8. Add recipients](#8-add-recipients)
- [9. Send original emails](#9-send-original-emails)
- [10. Check replies](#10-check-replies)
- [11. Send threaded follow-ups](#11-send-threaded-follow-ups)
- [12. Track organizations](#12-track-organizations)
- [13. Use presets, templates, and settings](#13-use-presets-templates-and-settings)
- [14. Review history and export reports](#14-review-history-and-export-reports)
- [15. Local files and backups](#15-local-files-and-backups)
- [16. Troubleshooting](#16-troubleshooting)
- [17. Owner sharing and credential audit](#17-owner-sharing-and-credential-audit)
- [18. Developer-only verification](#18-developer-only-verification)
- [Words used in this guide](#words-used-in-this-guide)
- [Quick first-send checklist](#quick-first-send-checklist)

## What the application does

Cold Mail Sender is a local workspace for personalized Gmail outreach:

- You create campaigns and keep their recipients, templates, attachments, delivery history, reply classifications, and follow-up history in local files.
- Gmail is connected through Google's Desktop OAuth flow. The app sends one Gmail message per selected recipient.
- You choose every batch, preview the rendered messages, type an exact confirmation phrase, and click Send.
- Reply checks and previews are read-only. There is no scheduler, background sender, hosted service, database, open tracking, or link tracking.
- The app does not delete Gmail messages. Deleting a campaign only removes its local campaign record.

Keep the application terminal open while using the dashboard. Stop it with Ctrl+C when finished.

## Safety first

Use a small test campaign and one controlled recipient before sending a real batch. Confirm the sender account, subject, body, recipient, and attachment in the preview.

The daily limit in Settings is a local guard, not a promise that Gmail will accept that many messages. Gmail quotas, spam detection, organizational policy, and applicable email laws still apply. Send relevant, personalized applications to people and organizations you are allowed to contact.

Never put any of these in Git:

- EmailSender/appsettings.json
- Files under resources/app-data
- The downloaded Google OAuth client JSON
- Files in the Gmail token directory
- Unreviewed recipient lists, resumes, cover letters, or private campaign exports

The repository has a staged-file secret check, but it is not a replacement for reviewing the files you are about to share.

## 1. Prerequisites

### Required for normal use

- Windows, macOS, or Linux with the .NET 10 SDK
- A modern browser
- A Gmail account that can authorize the Gmail API
- A Google Cloud project with the Gmail API enabled
- Permission to make outbound HTTPS requests to Google OAuth and Gmail API endpoints

The backend targets net10.0. Check the installed SDK before starting:

~~~powershell
dotnet --version
dotnet --list-sdks
~~~

The first command must report version 10.x or later. On Windows, these commands install common prerequisites when winget is available:

~~~powershell
winget install --id Microsoft.DotNet.SDK.10 -e
winget install --id Git.Git -e
~~~

If a winget package is not found, install the .NET 10 SDK and Git from their official download pages, then open a new terminal and repeat the version checks.

### Only needed for frontend development

The checked-in EmailSender/wwwroot bundle means a normal user does not need Node.js or npm. Install Node.js LTS only if you will change frontend source or rebuild the bundle:

~~~powershell
winget install --id OpenJS.NodeJS.LTS -e
node --version
npm.cmd --version
~~~

## 2. Get a clean copy

Use a fresh clone for a friend or a new campaign. Do not copy the sender's ignored appsettings, OAuth tokens, app-data, or personal resources into it.

~~~powershell
git clone <repository-url> cold-mail-sender
cd cold-mail-sender
git switch development
git status --short
~~~

If the repository's default branch is already development, the git switch command can be skipped. A clean checkout should not show a local appsettings file or resources/app-data in git status.

This checkout currently contains example and historical resources. Before using it, inspect the resources directory and replace any sender-specific templates, resumes, and CSV files with your own materials. Do not assume a cloned campaign is safe to send without reviewing it.

## 3. Create local configuration

The reference file is named EmailSender/appsettings.exmaple.json in the current repository. The spelling is part of the existing filename.

~~~powershell
Copy-Item EmailSender\appsettings.exmaple.json EmailSender\appsettings.json
~~~

Open EmailSender/appsettings.json and change the values in the Mail and Gmail sections:

~~~json
{
  "FilePaths": {
    "MasterCsv": "../../../../resources/master.csv",
    "MailTemplate": "default_mail_body",
    "Templates": "../../../../resources/",
    "Attachments": "../../../../resources/attachments/",
    "SentMailList": "../../../../resources/sentMailList.json",
    "DeliveryLedger": "../../../../resources/app-data/delivery-ledger.json",
    "CampaignStore": "../../../../resources/app-data/campaigns.json",
    "ApplicationSettings": "../../../../resources/app-data/settings.json",
    "OrganizationTracking": "../../../../resources/app-data/organization-tracking.json"
  },
  "Mail": {
    "SenderName": "Your Name",
    "Subject": "Application for Software Engineer | Your Name",
    "DailyLimit": 25
  },
  "Gmail": {
    "Account": "you@gmail.com",
    "CredentialsPath": "../../../../resources/app-data/google-oauth-client.json",
    "TokenDirectory": "../../../../resources/app-data/google-tokens"
  }
}
~~~

Keep the relative FilePaths values unless you deliberately move the resources directory. The application resolves them from its own build directory, so the commands in this guide work from the repository root.

Important settings:

| Setting | What to enter |
| --- | --- |
| Mail:SenderName | The display name people should see |
| Mail:Subject | The default original-email subject |
| Mail:DailyLimit | A conservative local maximum for successful messages per day |
| Gmail:Account | The exact Gmail address that will authorize and send |
| Gmail:CredentialsPath | The local path of the downloaded Desktop OAuth client JSON |
| Gmail:TokenDirectory | A private local directory for OAuth tokens |
| FilePaths:MailTemplate | A template filename without .html, normally default_mail_body |

Do not add an SMTP password. The active application uses Gmail OAuth. If an old local configuration has an Smtp section, remove it or treat any password that was ever used there as compromised and revoke it.

## 4. Set up Gmail OAuth in Google Cloud

Complete this once for the Gmail account that will send the job-search messages.

1. Open Google Cloud Console and create or select a project dedicated to this local tool.
2. Open APIs & Services > Library, search for Gmail API, and enable it.
3. Configure the OAuth consent screen. For a personal Gmail account, External plus Testing is sufficient for local use.
4. Enter an app name and the requested support/developer contact details.
5. In Audience or Test users, add the exact Gmail address from Gmail:Account.
6. Create an OAuth client with application type Desktop app.
7. Download the client JSON. Do not commit or upload it to the repository.
8. Place or copy it at the configured Gmail:CredentialsPath. The simplest Windows layout is:

~~~powershell
New-Item -ItemType Directory -Force resources\app-data | Out-Null
Copy-Item <downloaded-client-json> resources\app-data\google-oauth-client.json
~~~

9. Start the application and click Connect Gmail.
10. In the browser, choose the same Gmail account, review the requested Gmail send and read-only permissions, and approve.
11. Return to the dashboard and confirm the top bar shows the connected account. If Google shows a testing-mode warning, use the advanced continuation only when you trust this local app and the client you created.

The app requests Gmail send and Gmail read-only scopes. OAuth tokens stay in the configured token directory. Cold Mail Sender also uses a local two-hour authorization marker; click Reconnect Gmail when that marker expires. Google can impose additional testing-mode expiration or consent requirements, so reauthorize whenever Google or the dashboard asks.

If the authorization browser window cannot complete, confirm that the terminal has outbound access to oauth2.googleapis.com and Gmail API endpoints, that the system clock is correct, and that the Gmail address is listed as a test user.

## 5. Start the application

From the repository root, restore the .NET packages and launch the localhost server:

~~~powershell
dotnet restore EmailSender.sln
dotnet run --project EmailSender\EmailSender.csproj --urls http://localhost:5195
~~~

Open http://localhost:5195 in the browser. The production React interface is served by the ASP.NET host.

If port 5195 is already in use, choose another local port:

~~~powershell
dotnet run --project EmailSender\EmailSender.csproj --urls http://localhost:5196
~~~

Use the URL printed for that run. For later runs, when packages and the checked-in frontend bundle are already current:

~~~powershell
dotnet run --project EmailSender\EmailSender.csproj --no-build --urls http://localhost:5195
~~~

The Connect Gmail button is always available in the top bar. Refresh the workspace after changing local files outside the UI.

## 6. Find your way around the dashboard

The dashboard is a single local page with a sidebar on the left and the active screen on the right. Almost everything is driven by the campaign you select in the sidebar.

### Sidebar

The sidebar has two groups.

**Campaigns** lists every local campaign by name. Clicking one opens it and always returns you to that campaign's Original email tab, even if you were previously viewing a workspace tool. **New campaign** reveals a name field; **Create** adds the campaign and selects it.

**Workspace** holds the tools that are not tied to a single campaign:

| Tool | What it is for |
| --- | --- |
| Organizations | Cross-campaign activity, manual outcomes, and reply checks |
| Presets | Reusable message content stored in this browser |
| Templates | The shared HTML template files |
| Settings | Local defaults and workspace-wide exports |
| Help & Guide | The built-in, searchable version of this reference |

The **Help & Guide** tab is the short in-app version of this document. It has a search box, a list of sections covering the workflow, CSV format, attachment rules, reply statuses, and filters, and a placeholder reference panel showing each supported placeholder with an example. Its update date is shown in the heading. Use this guide for setup and troubleshooting, and the tab for quick lookups while working.

### Top bar

- The title shows the active campaign name, or the name of the workspace tool you opened. The line beneath it shows when the campaign was last updated.
- Four icon buttons appear on campaign screens: rename the campaign, duplicate it without history, export it as local JSON, and delete the local campaign.
- The connection chip shows the connected Gmail address, **Gmail disconnected**, or **Gmail login expired**.
- **Connect Gmail** or **Reconnect Gmail** starts the Google authorization flow at any time.
- **Refresh workspace** reloads campaigns, attachments, templates, settings, and organization data from the local files. Use it after changing files outside the app.

### Campaign screens

A campaign has five tabs: **Original email**, **Reply check**, **Follow-ups**, **Summary**, and **Activity**. Above the tabs, a strip shows that campaign's originals sent, replies, awaiting reply, bounced, and follow-ups sent.

### Progress and messages

While a batch is running, a progress bar shows how many recipients have been processed, the address currently being sent to, and the sent, failed, and skipped counts, with a **Cancel batch** button. Once the batch finishes, **Dismiss** clears the panel.

Errors appear in a red banner and confirmations in a green banner. Both have a **Dismiss** button. The banner is the first place to look when an action does not do what you expected.

## 7. First-time workspace setup

### Connect and verify Gmail

Click Connect Gmail before preparing a batch. The status should show the configured account and a connected state. Reconnect if the status says authorization-required, credentials-required, or Gmail login expired.

### Set local defaults

Open Workspace > Settings and set:

- Sender display name
- Default original subject
- Default template name
- Daily send limit

Save settings and confirm the values remain after a browser refresh. These settings affect future previews, new campaigns, and outgoing messages. Existing campaign defaults are not rewritten automatically. The Gmail identity and filesystem/OAuth paths are intentionally read-only in this tab.

### Create a campaign

Click New campaign in the sidebar, enter a name such as Backend applications - September, and click Create. Campaign state is isolated from other campaigns.

Campaign header actions allow you to:

- Rename the selected campaign
- Duplicate its defaults and recipient configuration without delivery/reply/follow-up history
- Export a local JSON snapshot
- Delete only local campaign state after typing DELETE CAMPAIGN

Deleting a campaign never deletes Gmail messages. Keep exported snapshots private because they can contain recipient data and message history.

## 8. Add recipients

Open the campaign's Original email tab. You can add one recipient at a time with **Add recipient**, or import a CSV. CSV import is usually faster and preserves delivery, reply, and follow-up history when an email already exists in the campaign.

### Adding one recipient at a time

**Add recipient** opens a small form:

| Field | Notes |
| --- | --- |
| Name | Required |
| Email | Required, and must look like an address |
| Organization | Required, and used for organization template matching |
| Custom subject | Optional; overrides the campaign subject for this person |
| Custom template | Optional template filename for this person |
| Recipient attachments | Optional; click filenames from the attachment library to select them |

**Save recipient** stays disabled until name, email, and organization are filled in. The pencil icon on a row reopens the same form for editing, and the trash icon deletes that recipient after a confirmation prompt.

### CSV columns

Required headers, with exact capitalization:

~~~csv
Name,Organization,Email
~~~

Optional headers:

~~~csv
Name,Organization,Email,Subject,Attachment,AttachmentDisplayName,Template,HtmlBody,PresetName
~~~

Field behavior:

| Column | Behavior |
| --- | --- |
| Name | Required. The first word becomes the name used by placeholders. |
| Organization | Required. Also used to find an organization template. |
| Email | Required. Validated before preview and again before sending. |
| Subject | Optional recipient-specific subject. |
| Attachment | Optional managed filename or pipe-separated filenames such as resume.pdf\|cover-letter.pdf. |
| AttachmentDisplayName | Optional filename shown to the recipient for that row's attachments. |
| Template | Optional HTML filename such as Acme.html. |
| HtmlBody or Body | Optional complete per-recipient HTML body. |
| PresetName | Optional exact name of a browser-local message preset. |

Quoted commas are supported. For example:

~~~csv
Name,Organization,Email,Subject,Attachment,AttachmentDisplayName,Template,HtmlBody,PresetName
Priya Sharma,"Example, Inc.",priya@example.com,Backend Engineer Application,resume.pdf,Resume.pdf,Acme.html,,
~~~

Use the Sample CSV button in the app for a starting file. The sample demonstrates the schema; its example attachment filenames are not automatically uploaded, so replace them or upload matching files before previewing.

Campaign imports upsert by case-insensitive email. Updating a row does not erase the recipient's successful-delivery, reply-check, or follow-up history.

### Templates and body selection

Templates are HTML files in the configured Templates directory, normally resources. You can upload them from Workspace > Templates or place them there before starting the app.

For each recipient, the body source is selected in this order:

1. A browser-local recipient preset override, when one is assigned
2. Recipient HtmlBody or Body
3. A valid recipient Template
4. The HTML body override entered on the Original email form
5. An organization template
6. The campaign or application default template

Organization matching uses the first word of Organization, case-insensitively. For an organization value such as Example Technologies, a file named Example.html can be selected automatically.

The default template in the example configuration is default_mail_body.html. A template name can be entered with or without the .html extension.

Supported placeholders in original and follow-up HTML:

| Placeholder | Replaced with |
| --- | --- |
| {name} or {{name}} | Recipient first name |
| {organization} or {{organization}} | Recipient organization |

Preview each selected row. The rendered HTML viewer is the best way to catch an incorrect name, organization, link, or template before sending.

### Attachments

Open the Attachment library on the Original email tab and upload files. Attachments are stored in the selected campaign's managed directory under resources/attachments. Select files as common original attachments, recipient-specific attachments, or follow-up attachments.

Use filenames only in CSV and form fields. Do not use an absolute path, a relative path, a slash, a backslash, or .. . Multiple CSV attachments are separated with a pipe.

Uploading a filename that already exists asks for explicit overwrite approval. A file referenced by campaign state cannot be deleted. The backend validates every selected attachment before the first original-email provider call; a missing or invalid attachment blocks the original batch instead of sending a partial batch.

## 9. Send original emails

Follow this sequence for every batch:

1. Select the intended campaign recipients. The table has a search box and these controls: **Selected only** shows just the checked rows, **Select uncontacted** adds every visible recipient with no original send, the **Select first...** dropdown offers First 25, First 50, and First 100, and **Invert visible**, **Select all visible**, and **Clear selection** work on the rows currently shown. The checkbox in the table header selects or clears every visible row.
2. Choose a message preset if useful, then review the subject override, HTML body override, and common attachments.
3. Enable Allow selected recipients that were previously sent to only when an intentional resend is required.
4. Click Preview batch.
5. Read the pre-send review. It reports Selected, Eligible, Previously sent, Invalid email, Missing attachments, Duplicates, Replied selected, Remaining today, and Gmail authorization, and labels the batch either Ready to review or with a count of blocked rows.
6. Open the eye icon for individual rendered HTML and verify the exact subject, body, and attachment filenames. Editing the body, subject, attachments, or resend approval closes the rendered viewer, so reopen it after any change.
7. Correct any blocked rows and preview again. A previous send is blocked by default; enable resend approval and preview again when it is truly intended.
8. Type SEND ORIGINAL EMAILS exactly, including capitalization and spaces.
9. Click Send originals.

The backend recomputes the preview and performs attachment and eligibility checks again immediately before sending. It sends one Gmail message at a time. Progress shows the current recipient and counts; Cancel batch requests cooperative cancellation, so the Gmail call already in progress may finish before the remaining rows are skipped.

Successful Gmail message IDs, thread IDs, RFC message IDs, local campaign history, and the delivery ledger are saved after each successful delivery. A failed provider call is reported per recipient. Use Activity and Summary to review what happened.

### Review warnings

Whenever at least one recipient is selected, the Original email tab shows a **Review warnings** panel counting:

| Warning | Meaning |
| --- | --- |
| Duplicate emails | The same address is selected more than once |
| Seen in another campaign | The address also exists in a different local campaign |
| Sent within 7 days | An original was sent to this address in the last seven days |
| Already replied | The stored reply classification is replied |
| Bounced | The stored reply classification is bounced |
| Repeated organizations | More than one selected recipient shares an organization |

These are warnings only. They never block preview or sending, and the panel says so. They exist to help you notice a contact you already approached before you write again.

### The local daily limit

The pre-send review shows how many sends remain today under Mail:DailyLimit, counted from the delivery ledger using the local date of the machine running the backend. The limit is enforced while sending, not before: once it is reached, the remaining recipients in that batch are reported as failed with `daily-limit-reached` instead of being sent. Sending is possible again on the next local day.

The limit is a local guard in this application, not a Gmail quota. Keep it well below what your Gmail account and your own judgement consider appropriate.

## 10. Check replies

Open Reply check and click Check replies. This is read-only and queries only the persisted Gmail thread for each recipient with an original message. It does not send or modify Gmail messages.

Reply classifications, with the label the app displays for each:

| Stored value | Shown as | Meaning |
| --- | --- | --- |
| `replied` | Replied | A human reply matching the recipient was found |
| `no-reply` | Awaiting reply | No qualifying reply was found; the recipient may become eligible for a follow-up |
| `automated` | Automated | An automated response was detected |
| `bounced` | Bounced | A delivery failure was detected |
| `uncertain` | Uncertain | The content could not be classified confidently |
| `missing-thread` | Missing thread | The original Gmail thread is unavailable |
| `not-checked` | Not checked | No local check has been recorded yet |

The checker ignores the original outbound message and later messages sent from the configured sender account, so your own Gmail follow-ups are not misclassified as recipient replies.

The **Classification** dropdown filters the table to All classifications, Replied, Awaiting reply, Bounced, Automated, Uncertain, Missing thread, or Not checked, and reports how many rows are shown. The table lists each recipient with the original send time, the last check time, the classification, and any notes.

When a human reply is matched, the eye icon opens a read-only viewer showing From, To, subject, received time, the sanitized HTML or plain text body, and attachment names with sizes. Attachment contents are not downloaded by this viewer.

Workspace > Organizations has Check all replies for a sequential read-only check across every local campaign. It persists each recipient's classification and checked timestamp.

## 11. Send threaded follow-ups

1. Run Check replies first. Only a recipient with a stored original Gmail thread and an Awaiting reply classification becomes eligible.
2. Open Follow-ups. The message form is always visible and can be filled in before or after the preview: HTML body, Subject override, follow-up attachments, and an optional message preset.
3. Write the follow-up HTML body. `{name}` or `{{name}}` and `{organization}` or `{{organization}}` are replaced for each recipient.
4. Leave Subject override blank to use Re: followed by the original subject. A custom subject can break Gmail threading, so it requires Allow subject changes that may break Gmail threading.
5. Select follow-up attachments from the managed library. Upload them from the Original email tab if the library is empty.
6. Click Preview candidates. This builds the candidate list and initially selects the eligible rows matching the filters as they currently stand.
7. Adjust the filters above the table: Show older than (default three days, with 2d/3d/5d/7d/14d shortcuts), Organization, and Reply status (default Eligible / no reply).
8. Select the rows you want. Only rows marked Eligible have a checkbox you can tick.
9. Click Preview batch and inspect each personalized rendered message with the eye icon, exactly as with originals.
10. Type SEND FOLLOW-UP EMAILS exactly.
11. Click Send follow-ups.

### Selecting follow-up rows

Three buttons manage the selection and they are not interchangeable:

| Button | Effect |
| --- | --- |
| Select visible | Replaces the selection with the currently visible eligible rows |
| Select all visible | Adds the currently visible eligible rows to whatever is already selected |
| Deselect all visible | Removes only the currently visible rows from the selection |

Use **Select all visible** to build a batch across several organizations or statuses: filter to one group, select it, change the filter, then select the next group. Use **Select visible** when you want to discard earlier choices and work only with what is on screen now.

Changing the age, organization, or reply-status filter does not change the selection by itself, so a row selected under an earlier filter stays selected after the filter changes. The bar under the table shows how many rows are selected out of the visible eligible count, which is the quickest way to confirm the batch you are about to send.

The table columns are Recipient, Organization, Waiting (elapsed time since the last outbound message), Reply status, Follow-up subject, Eligibility, and a preview eye icon. An ineligible row shows the blocking reason instead of Eligible.

Follow-ups are sent as replies in the original Gmail thread with the original thread ID, In-Reply-To, and References headers. Immediately before each provider call, Gmail is read again. If a new human reply, automated response, bounce, uncertainty, or missing thread is found, that recipient is suppressed.

Elapsed time is measured from the latest outbound message, so a previous follow-up resets the waiting period. Attachment preflight happens before provider calls. Other newly ineligible rows are skipped and reported individually.

Follow-up body, subject, attachment, subject-change approval, age, organization, and reply-status filters are saved as browser-local drafts per campaign. Recipient selections and confirmation text are intentionally not restored.

## 12. Track organizations

Open Workspace > Organizations to see case-insensitive organization totals across campaigns:

- Contacts and campaigns
- Original sends and follow-ups
- Human replies, awaiting replies, automated messages, and bounces
- Latest outbound, reply, and reply-check timestamps

Expand an organization to inspect its contacts and open an already matched reply. Record a manual outcome (Unreviewed, No response, Interested, Not interested, Follow up, or Other) and notes, then save. The app never infers a business outcome from reply text.

Search or filter by outcome, then use Export CSV. The export is generated locally and may contain private contact data.

## 13. Use presets, templates, and settings

### Message presets

Workspace > Presets stores reusable message content in this browser's local storage. Each preset holds a name, an original subject and HTML body, a common attachment list, and a follow-up subject, HTML body, and attachment list. Three built-in presets are provided: **General software application**, **Backend / .NET application**, and **Recruiter outreach**.

Use **New preset** to start one, or the pencil icon to edit an existing preset. **Save preset** is disabled until the preset has a name. The trash icon deletes a preset after a confirmation prompt, and **Clear** empties the editor without saving.

Applying a preset only fills form fields; it never sends email and never bypasses preview or confirmation. Applying one sets both the original and the follow-up fields, and a banner reminds you to review the populated fields before previewing.

A CSV PresetName must exactly match a preset in the browser you are using. If it does not, preview is blocked with a message naming the missing presets rather than silently sending a different message. Presets live in this browser only, so they are not shared between computers or browsers.

### HTML templates

Workspace > Templates lists the shared HTML files available to campaign CSV rows. **Upload template** adds an HTML file and reloads the list, and **Reload** rescans the templates directory after you change files on disk. Keep templates free of passwords, tokens, private keys, or unintended personal data.

### Settings

Workspace > Settings controls the sender display name, default original subject, default template name, and local daily limit. The daily limit accepts a whole number from 1 to 2000. The connected Gmail account is shown read-only, and OAuth credentials and storage paths stay in the ignored appsettings.json file.

Saved values apply to future previews and newly created campaigns without restarting the localhost backend. Existing campaign defaults are not rewritten when application defaults change.

The tab also reports successful deliveries in the last 24 hours, the current local date, and the current UTC date. These counts include originals and follow-ups and come from the delivery ledger; if they show as unavailable, restart the localhost backend so the ledger is loaded.

Below Settings is the Workspace exports panel, described in the next section.

## 14. Review history and export reports

**Campaign > Activity** lists locally stored original and follow-up deliveries, newest first, each with the message type, recipient, subject, and date. It is built from local campaign state, so it loads without contacting Gmail.

**Campaign > Summary** is a read-only report generated from local campaign state. It shows recipient, company, sent, not sent, reply, awaiting, bounced, follow-up, and valid-contact totals, plus a per-company table of contacts, sent, replied, awaiting, and bounced. Its downloads are:

- Markdown report
- Full CSV with delivery and reply fields
- Valid contacts CSV, deduplicated by email and excluding invalid or bounced rows

**Workspace exports** are on **Workspace > Settings**, in the Workspace exports panel below Local settings. Tick the campaigns to include (use the search box, **Select all matching**, and **Clear selection** to manage a long list), then download Markdown, Full CSV, or Valid contacts CSV across all selected campaigns at once.

Exports are generated in your browser from local data. They do not query Gmail and do not include OAuth credentials, but they can contain private recipient information. Store them securely.

## 15. Local files and backups

The important local state is:

| Location | Purpose |
| --- | --- |
| EmailSender/appsettings.json | Ignored machine/account configuration |
| resources/app-data | Ignored campaign JSON, delivery ledger, settings, organization tracking, OAuth client, and tokens |
| resources/attachments | Managed campaign attachment files |
| resources/*.html | Shared HTML templates |
| EmailSender/wwwroot | Checked-in production frontend bundle |
| Browser local storage | Message presets and follow-up drafts |

Stop the app before copying a backup. Back up campaign JSON, the delivery ledger, attachments, and templates to a private location. Treat OAuth client and token files as credentials; do not place them in a shared drive or commit them.

To force a fresh Gmail authorization after stopping the app, remove only the token directory and reconnect. This signs the local copy out; it does not revoke access in the Google Account:

~~~powershell
Remove-Item -LiteralPath resources\app-data\google-tokens -Recurse -Force
~~~

Use Google Account security settings to revoke the app if the computer or OAuth grant is no longer trusted.

## 16. Troubleshooting

### The app says appsettings.json is missing

Copy the reference file again, then edit the local copy:

~~~powershell
Copy-Item EmailSender\appsettings.exmaple.json EmailSender\appsettings.json
~~~

### Default template not found

Confirm that resources/default_mail_body.html exists and that FilePaths:MailTemplate is default_mail_body. Template names are case-insensitive and may omit .html.

### Gmail says credentials-required

Confirm the OAuth client JSON exists exactly at Gmail:CredentialsPath. Do not point the setting at a directory or at the token directory.

### The Gmail chip says Gmail login expired

The local two-hour authorization marker has lapsed. Click **Reconnect Gmail** and complete the browser flow with the configured Gmail account. Check that the address is listed under Audience > Test users in Google Cloud and that the terminal can reach oauth2.googleapis.com and the Gmail API endpoints.

**Gmail disconnected** instead of an address means no usable token was found. Confirm the OAuth client JSON exists at the configured path, reconnect, and if it still fails, check that the token directory is writable.

### The browser cannot reach localhost

Confirm the terminal is still running, use the exact URL printed by dotnet run, and check whether another process already owns port 5195. Start on 5196 or another free local port.

### Preview reports missing attachments

Upload the exact filename into the selected campaign's Attachment library. CSV values must be filenames only and are case-insensitive for matching, but spelling and extension still matter.

### Preview reports a missing preset

Create or import the exact PresetName in the same browser under Workspace > Presets, or clear the CSV PresetName field and provide a normal template/body.

### Previously sent recipients are blocked

This is intentional duplicate protection. Enable Allow selected recipients that were previously sent to, preview again, inspect the prior timestamp, and send only when the resend is deliberate.

### No follow-up candidates appear

Run Check replies, make sure the recipient has an original Gmail thread and a no-reply status, and lower the older-than filter if the message is not old enough. Replied, automated, bounced, uncertain, and missing-thread rows are not eligible.

### A batch is already running

Wait for it to finish or use Cancel batch. A second original or follow-up operation is rejected while the first is active. The current provider call may finish after cancellation is requested.

### The send button stays disabled

Select at least one row, preview the batch, and type the exact required phrase. Original: SEND ORIGINAL EMAILS. Follow-up: SEND FOLLOW-UP EMAILS. For follow-ups, the selection must also contain at least one eligible row, so check the filter and the selected count.

### Recipients are reported as daily-limit-reached

The batch reached Mail:DailyLimit for the current local day. The delivery ledger counts successful originals and follow-ups, so batches sent earlier today are already included. Send a smaller batch, raise the limit deliberately if the account allows it, or wait for the next local day.

### The app stops with "Mail:DailyLimit must be a positive integer."

Mail:DailyLimit in appsettings.json is missing, zero, negative, or not a number. Set it to a positive whole number such as 25 and start the app again.

### Import CSV asks you to create or select a campaign

CSV import always targets one campaign. Select a campaign in the sidebar, or create one, and then import. There is no workspace-wide import.

### An attachment cannot be deleted

A file that campaign state still references cannot be removed. Clear it from the campaign's common attachments, from any recipient that selects it, and from the follow-up attachment selection, then delete it.

## 17. Owner sharing and credential audit

Before handing this project to another person:

1. Prefer a fresh clone or a clean branch.
2. Review tracked and untracked files; do not use git add . in a workspace containing personal campaign files.
3. Remove or replace sender-specific resumes, templates, and recipient CSVs before sharing.
4. Keep EmailSender/appsettings.json, resources/app-data, OAuth JSON, and token directories ignored.
5. Stage only intended files and run:

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts\check-staged-secrets.ps1
~~~

6. Review the staged diff and the staged file names before pushing.

The audit performed for this guide found no current tracked appsettings.json, OAuth client JSON, token file, or resources/app-data. The initial remote history does contain an old EmailSender/appsettings.json, but its Password field was empty and no non-empty OAuth/token secret was found in the audited Git history. The current machine's ignored appsettings.json does contain a legacy SMTP password value; it was not tracked or staged, and the current application does not use SMTP. Remove that legacy value and revoke or rotate it if it was ever valid.

This repository also contains tracked sender-specific resumes, templates, and historical CSV contact lists. They are not credentials, but they are private-data exposure risks. Clean those files and, when necessary, their published history before sharing the repository with a friend.

The secret-check script protects staged changes going forward. It does not rewrite old Git history. History cleanup and force-pushing are destructive operations that require a deliberate backup and coordination with anyone else using the repository.

## 18. Developer-only verification

Run these checks after changing backend or frontend code. They use fake mail/Gmail implementations in automated tests; they do not send live email:

~~~powershell
dotnet test EmailSender.sln --no-restore
dotnet build EmailSender.sln --no-restore

cd frontend
npm.cmd install
npm.cmd run build
npm.cmd run test:e2e
~~~

The Playwright suite uses mocked API responses and installed Chrome/Edge browsers. It does not connect to Gmail, invoke a send route, or modify the live campaign store.

## Words used in this guide

| Word | What it means here |
| --- | --- |
| Campaign | A named local workspace holding one group of recipients and their sending, reply, and follow-up history |
| Recipient | One contact inside a campaign, identified by email address |
| Template | An HTML file in the shared templates directory, used as the body of a message |
| Preset | Reusable message content stored in this browser and applied to fill the form fields |
| Body override | HTML typed directly into the batch form instead of using a template file |
| Batch | The set of recipients you preview and send together in one confirmed action |
| Preview | A read-only rehearsal that renders each selected message and checks it, without sending anything |
| Reply classification | The stored result of the most recent reply check for a recipient |
| Follow-up | A later message sent as a reply inside the same Gmail thread as the original |
| Delivery ledger | The append-only local record of successful sends, used for the daily counts |
| Managed attachment | A file uploaded into the campaign's local attachment folder and referenced by filename only |
| Activity | The local list of messages this application has sent for a campaign |

## Quick first-send checklist

- Install and verify .NET 10.
- Clone a clean copy and inspect the resources directory.
- Copy EmailSender/appsettings.exmaple.json to EmailSender/appsettings.json.
- Set your name, subject, daily limit, Gmail account, and OAuth paths.
- Enable Gmail API, create a Desktop OAuth client, add the account as a test user, and copy the client JSON locally.
- Start localhost and connect Gmail.
- Create a campaign and import a reviewed CSV.
- Upload and select the intended attachments.
- Preview originals and inspect rendered HTML for every message type.
- Read the pre-send review and clear every blocked row before sending.
- Send one controlled test message with SEND ORIGINAL EMAILS.
- Check replies before considering follow-ups.
- Confirm the selected count matches the batch you intend, then use the exact follow-up preview, recheck, and confirmation workflow.
- Back up local state privately and run the staged secret check before any push.
